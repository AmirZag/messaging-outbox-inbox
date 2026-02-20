using Messaging.OutboxInbox.AspNetCore.Extensions;
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
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            _logger.RabbitMqPublisherInitialized(_options.ExchangeName, _options.RoutingKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize RabbitMQ Publisher - Exchange: {ExchangeName}", _options.ExchangeName);
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task PublishAsync(OutboxRecord message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await EnsureInitializedAsync(cancellationToken);

        var body = Encoding.UTF8.GetBytes(message.Content);
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = message.Id.ToString(),
            Timestamp = new AmqpTimestamp(new DateTimeOffset(message.OccurredAt).ToUnixTimeSeconds()),
            Type = message.Type
        };

        try
        {
            await _channel!.BasicPublishAsync(
                exchange: _options.ExchangeName,
                routingKey: _options.RoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);

            _logger.RabbitMqMessagePublished(message.Id, message.Type, _options.ExchangeName, _options.RoutingKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to publish - MessageId: {MessageId}, Type: {MessageType}, Exchange: {ExchangeName}, RoutingKey: {RoutingKey}",
                message.Id, message.Type, _options.ExchangeName, _options.RoutingKey);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_channel is not null)
            {
                await _channel.CloseAsync();
                await _channel.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while disposing RabbitMQ channel");
        }
        finally
        {
            _initLock.Dispose();
        }
    }
}