using System.Text;
using Messaging.OutboxInbox.AspNetCore.Options;
using Messaging.OutboxInbox.AspNetCore.Queues;
using Messaging.OutboxInbox.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Messaging.OutboxInbox.AspNetCore.MessageBroker;

internal sealed class RabbitMqSubscriber : IHostedService, IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly MessageSubscriberOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly IInboxMessageQueue _inboxQueue;
    private readonly ILogger<RabbitMqSubscriber> _logger;
    private IChannel? _channel;

    public RabbitMqSubscriber(
        IConnection connection,
        IOptions<MessageSubscriberOptions> options,
        IServiceProvider serviceProvider,
        IInboxMessageQueue inboxQueue,
        ILogger<RabbitMqSubscriber> logger)
    {
        _connection = connection;
        _options = options.Value;
        _serviceProvider = serviceProvider;
        _inboxQueue = inboxQueue;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await _channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            cancellationToken: cancellationToken);

        await _channel.QueueDeclareAsync(
            queue: _options.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await _channel.QueueBindAsync(
            queue: _options.QueueName,
            exchange: _options.ExchangeName,
            routingKey: _options.RoutingKey,
            cancellationToken: cancellationToken);

        await _channel.BasicQosAsync(0, _options.PrefetchCount, false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        await _channel.BasicConsumeAsync(
            queue: _options.QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        _logger.LogInformation("RabbitMQ Subscriber started - Queue: {QueueName}", _options.QueueName);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        try
        {
            var messageId = Guid.Parse(args.BasicProperties.MessageId!);
            var messageType = args.BasicProperties.Type!;
            var content = Encoding.UTF8.GetString(args.Body.ToArray());
            var occurredAt = DateTimeOffset.FromUnixTimeSeconds(args.BasicProperties.Timestamp.UnixTime).UtcDateTime; // Fixed: Use UtcDateTime

            await using var scope = _serviceProvider.CreateAsyncScope();
            var inboxService = scope.ServiceProvider.GetRequiredService<IInboxMessagesService>();

            // Try to insert - idempotency handled in service
            var inserted = await inboxService.TryInsertAsync(messageId, messageType, content, occurredAt);

            if (inserted)
            {
                // Get the inserted record and enqueue for processing
                var messages = await inboxService.GetUnprocessedListAsync();
                var message = messages.FirstOrDefault(m => m.Id == messageId);
                if (message is not null)
                {
                    _inboxQueue.Enqueue(message);
                }
            }

            await _channel!.BasicAckAsync(args.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from RabbitMQ");
            await _channel!.BasicNackAsync(args.DeliveryTag, false, true);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("RabbitMQ Subscriber stopping");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync();
            await _channel.DisposeAsync();
        }
    }
}