using Messaging.OutboxInbox.AspNetCore.Extensions;
using Messaging.OutboxInbox.AspNetCore.Options;
using Messaging.OutboxInbox.AspNetCore.Queues;
using Messaging.OutboxInbox.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

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
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _inboxQueue = inboxQueue ?? throw new ArgumentNullException(nameof(inboxQueue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Entering {Method}", nameof(StartAsync));

        try
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

            _logger.RabbitMqSubscriberStarted(_options.QueueName);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "CRITICAL: Failed to start RabbitMQ Subscriber - Queue: {QueueName}", _options.QueueName);
            throw;
        }
        finally
        {
            _logger.LogDebug("Exiting {Method}", nameof(StartAsync));
        }
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        Guid? messageId = null;
        string? messageType = null;

        try
        {
            if (string.IsNullOrEmpty(args.BasicProperties?.MessageId))
            {
                _logger.LogError("Received message without MessageId - rejecting");
                await _channel!.BasicNackAsync(args.DeliveryTag, false, false);
                return;
            }

            if (string.IsNullOrEmpty(args.BasicProperties?.Type))
            {
                _logger.LogError("Received message without Type - MessageId: {MessageId}, rejecting", args.BasicProperties!.MessageId);
                await _channel!.BasicNackAsync(args.DeliveryTag, false, false);
                return;
            }

            messageId = Guid.Parse(args.BasicProperties.MessageId);
            messageType = args.BasicProperties.Type;
            var content = Encoding.UTF8.GetString(args.Body.ToArray());
            var occurredAt = DateTimeOffset.FromUnixTimeSeconds(args.BasicProperties.Timestamp.UnixTime).UtcDateTime;

            _logger.InboxMessageReceived(messageId.Value, messageType);

            await using var scope = _serviceProvider.CreateAsyncScope();
            var inboxService = scope.ServiceProvider.GetRequiredService<IInboxMessagesService>();

            // Try to insert - idempotency handled in service
            var inserted = await inboxService.TryInsertAsync(messageId.Value, messageType, content, occurredAt);

            if (!inserted)
            {
                _logger.InboxMessageDuplicate(messageId.Value, messageType);
            }
            else
            {
                // Get the inserted record and enqueue for processing
                var messages = await inboxService.GetUnprocessedListAsync();
                var message = messages.FirstOrDefault(m => m.Id == messageId.Value);

                if (message is not null)
                {
                    _inboxQueue.Enqueue(message);
                    _logger.InboxMessageEnqueued(messageId.Value, messageType);
                }
                else
                {
                    _logger.LogWarning("Failed to retrieve inbox message after insertion - MessageId: {MessageId}", messageId.Value);
                }
            }

            await _channel!.BasicAckAsync(args.DeliveryTag, false);
            _logger.LogDebug("Acknowledged RabbitMQ message - MessageId: {MessageId}", messageId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from RabbitMQ - MessageId: {MessageId}, Type: {MessageType}",
                messageId, messageType ?? "unknown");

            try
            {
                // Requeue the message for retry
                await _channel!.BasicNackAsync(args.DeliveryTag, false, true);
                _logger.LogDebug("Requeued failed message - MessageId: {MessageId}", messageId);
            }
            catch (Exception nackEx)
            {
                _logger.LogError(nackEx, "CRITICAL: Failed to NACK message - MessageId: {MessageId}, message may be lost", messageId);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.RabbitMqSubscriberStopping(_options.QueueName);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogDebug("Disposing RabbitMQ Subscriber");

        try
        {
            if (_channel is not null)
            {
                await _channel.CloseAsync();
                await _channel.DisposeAsync();
                _logger.LogDebug("RabbitMQ Subscriber channel disposed successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while disposing RabbitMQ Subscriber channel");
        }
    }
}