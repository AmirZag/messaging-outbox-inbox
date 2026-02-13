// src/Messaging.OutboxInbox.AspNetCore/Publishers/RabbitMqPublisher.cs
using System.Text;
using Messaging.OutboxInbox.AspNetCore.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Messaging.OutboxInbox.AspNetCore.Publishers;

internal sealed class RabbitMqPublisher : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly MessagePublisherOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private IChannel? _channel;
    private bool _isInitialized;
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

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
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
            _logger.LogInformation("RabbitMQ Publisher initialized - Exchange: {ExchangeName}", _options.ExchangeName);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task PublishToRabbitMqAsync(
        Guid messageId,
        string messageType,
        string content,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        var body = Encoding.UTF8.GetBytes(content);
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = messageId.ToString(),
            Timestamp = new AmqpTimestamp(new DateTimeOffset(occurredAt).ToUnixTimeSeconds()),
            Type = messageType
        };

        await _channel!.BasicPublishAsync(
            exchange: _options.ExchangeName,
            routingKey: _options.RoutingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Published message {MessageId} to RabbitMQ", messageId);
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