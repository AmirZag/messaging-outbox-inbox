using Messaging.OutboxInbox.AspNetCore.Options;
using Messaging.OutboxInbox.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text;

namespace Messaging.OutboxInbox.AspNetCore.MessageBroker;

internal sealed class RabbitMqPublisher : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly MessagePublisherOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private IChannel? _channel;
    private volatile bool _isInitialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public RabbitMqPublisher(
        IConnection connection,
        IOptions<MessagePublisherOptions> options,
        ILogger<RabbitMqPublisher> logger)
    {
        _connection = connection;
        _options = options.Value;
        _logger = logger;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized) return;

            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

            await _channel.ExchangeDeclareAsync(
                exchange: _options.ExchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);

            _isInitialized = true;

            _logger.LogInformation(
                "RabbitMQ Publisher initialized - Exchange: {ExchangeName}, RoutingKey: {RoutingKey}",
                _options.ExchangeName,
                _options.RoutingKey);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task PublishAsync(OutboxRecord message, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (_channel is null)
            throw new InvalidOperationException("RabbitMQ channel not available");

        var body = Encoding.UTF8.GetBytes(message.Content);

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = message.Id.ToString(),
            Timestamp = new AmqpTimestamp(new DateTimeOffset(message.OccurredAt).ToUnixTimeSeconds()),
            Type = message.Type
        };

        await _channel.BasicPublishAsync(
            exchange: _options.ExchangeName,
            routingKey: _options.RoutingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Published message {MessageId} (Type: {MessageType}) to RabbitMQ",
            message.Id,
            message.Type);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync();
            await _channel.DisposeAsync();
        }
        _initLock.Dispose();
    }
}