using Microsoft.Extensions.Logging;

namespace Messaging.OutboxInbox.AspNetCore.Extensions;

internal static class LoggerExtensions
{
    // Outbox logging
    private static readonly Action<ILogger, Guid, string, Exception?> _outboxMessageEnqueued =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Debug,
            new EventId(1001, nameof(OutboxMessageEnqueued)),
            "Outbox message enqueued - MessageId: {MessageId}, Type: {MessageType}");

    private static readonly Action<ILogger, Guid, string, Exception?> _outboxMessageProcessing =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(1002, nameof(OutboxMessageProcessing)),
            "Processing outbox message - MessageId: {MessageId}, Type: {MessageType}");

    private static readonly Action<ILogger, Guid, string, long, Exception?> _outboxMessageProcessed =
        LoggerMessage.Define<Guid, string, long>(
            LogLevel.Information,
            new EventId(1003, nameof(OutboxMessageProcessed)),
            "Outbox message processed successfully - MessageId: {MessageId}, Type: {MessageType}, DurationMs: {DurationMs}");

    private static readonly Action<ILogger, Guid, string, Exception?> _outboxMessageAlreadyProcessed =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(1004, nameof(OutboxMessageAlreadyProcessed)),
            "Outbox message already processed (idempotency check) - MessageId: {MessageId}, Type: {MessageType}");

    private static readonly Action<ILogger, Guid, string, string, Exception?> _outboxMessageFailed =
        LoggerMessage.Define<Guid, string, string>(
            LogLevel.Error,
            new EventId(1005, nameof(OutboxMessageFailed)),
            "Outbox message processing failed - MessageId: {MessageId}, Type: {MessageType}, Error: {Error}");

    private static readonly Action<ILogger, int, Exception?> _outboxUnprocessedMessagesLoaded =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1006, nameof(OutboxUnprocessedMessagesLoaded)),
            "Loaded {Count} unprocessed outbox messages from database");

    // Inbox logging
    private static readonly Action<ILogger, Guid, string, Exception?> _inboxMessageReceived =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Debug,
            new EventId(2001, nameof(InboxMessageReceived)),
            "Inbox message received from RabbitMQ - MessageId: {MessageId}, Type: {MessageType}");

    private static readonly Action<ILogger, Guid, string, Exception?> _inboxMessageDuplicate =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(2002, nameof(InboxMessageDuplicate)),
            "Inbox message already exists (idempotency) - MessageId: {MessageId}, Type: {MessageType}");

    private static readonly Action<ILogger, Guid, string, Exception?> _inboxMessageEnqueued =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Debug,
            new EventId(2003, nameof(InboxMessageEnqueued)),
            "Inbox message enqueued for processing - MessageId: {MessageId}, Type: {MessageType}");

    private static readonly Action<ILogger, Guid, string, Exception?> _inboxMessageProcessing =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(2004, nameof(InboxMessageProcessing)),
            "Processing inbox message - MessageId: {MessageId}, Type: {MessageType}");

    private static readonly Action<ILogger, Guid, string, long, Exception?> _inboxMessageProcessed =
        LoggerMessage.Define<Guid, string, long>(
            LogLevel.Information,
            new EventId(2005, nameof(InboxMessageProcessed)),
            "Inbox message processed successfully - MessageId: {MessageId}, Type: {MessageType}, DurationMs: {DurationMs}");

    private static readonly Action<ILogger, Guid, string, Exception?> _inboxMessageAlreadyProcessed =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(2006, nameof(InboxMessageAlreadyProcessed)),
            "Inbox message already processed (idempotency check) - MessageId: {MessageId}, Type: {MessageType}");

    private static readonly Action<ILogger, Guid, string, string, Exception?> _inboxMessageFailed =
        LoggerMessage.Define<Guid, string, string>(
            LogLevel.Error,
            new EventId(2007, nameof(InboxMessageFailed)),
            "Inbox message processing failed - MessageId: {MessageId}, Type: {MessageType}, Error: {Error}");

    private static readonly Action<ILogger, int, Exception?> _inboxUnprocessedMessagesLoaded =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(2008, nameof(InboxUnprocessedMessagesLoaded)),
            "Loaded {Count} unprocessed inbox messages from database");

    // RabbitMQ logging
    private static readonly Action<ILogger, string, string, Exception?> _rabbitMqPublisherInitialized =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(3001, nameof(RabbitMqPublisherInitialized)),
            "RabbitMQ Publisher initialized - Exchange: {ExchangeName}, RoutingKey: {RoutingKey}");

    private static readonly Action<ILogger, Guid, string, string, string, Exception?> _rabbitMqMessagePublished =
        LoggerMessage.Define<Guid, string, string, string>(
            LogLevel.Information,
            new EventId(3002, nameof(RabbitMqMessagePublished)),
            "Published message to RabbitMQ - MessageId: {MessageId}, Type: {MessageType}, Exchange: {ExchangeName}, RoutingKey: {RoutingKey}");

    private static readonly Action<ILogger, string, Exception?> _rabbitMqSubscriberStarted =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(3003, nameof(RabbitMqSubscriberStarted)),
            "RabbitMQ Subscriber started - Queue: {QueueName}");

    private static readonly Action<ILogger, string, Exception?> _rabbitMqSubscriberStopping =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(3004, nameof(RabbitMqSubscriberStopping)),
            "RabbitMQ Subscriber stopping - Queue: {QueueName}");

    // Service lifecycle logging
    private static readonly Action<ILogger, string, Exception?> _hostedServiceStarted =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(4001, nameof(HostedServiceStarted)),
            "{ServiceName} started");

    private static readonly Action<ILogger, string, Exception?> _hostedServiceStopped =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(4002, nameof(HostedServiceStopped)),
            "{ServiceName} stopped");

    private static readonly Action<ILogger, string, Exception?> _hostedServiceError =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(4003, nameof(HostedServiceError)),
            "Error in {ServiceName}");

    // Public methods
    public static void OutboxMessageEnqueued(this ILogger logger, Guid messageId, string messageType)
        => _outboxMessageEnqueued(logger, messageId, messageType, null);

    public static void OutboxMessageProcessing(this ILogger logger, Guid messageId, string messageType)
        => _outboxMessageProcessing(logger, messageId, messageType, null);

    public static void OutboxMessageProcessed(this ILogger logger, Guid messageId, string messageType, long durationMs)
        => _outboxMessageProcessed(logger, messageId, messageType, durationMs, null);

    public static void OutboxMessageAlreadyProcessed(this ILogger logger, Guid messageId, string messageType)
        => _outboxMessageAlreadyProcessed(logger, messageId, messageType, null);

    public static void OutboxMessageFailed(this ILogger logger, Guid messageId, string messageType, string error, Exception? exception = null)
        => _outboxMessageFailed(logger, messageId, messageType, error, exception);

    public static void OutboxUnprocessedMessagesLoaded(this ILogger logger, int count)
        => _outboxUnprocessedMessagesLoaded(logger, count, null);

    public static void InboxMessageReceived(this ILogger logger, Guid messageId, string messageType)
        => _inboxMessageReceived(logger, messageId, messageType, null);

    public static void InboxMessageDuplicate(this ILogger logger, Guid messageId, string messageType)
        => _inboxMessageDuplicate(logger, messageId, messageType, null);

    public static void InboxMessageEnqueued(this ILogger logger, Guid messageId, string messageType)
        => _inboxMessageEnqueued(logger, messageId, messageType, null);

    public static void InboxMessageProcessing(this ILogger logger, Guid messageId, string messageType)
        => _inboxMessageProcessing(logger, messageId, messageType, null);

    public static void InboxMessageProcessed(this ILogger logger, Guid messageId, string messageType, long durationMs)
        => _inboxMessageProcessed(logger, messageId, messageType, durationMs, null);

    public static void InboxMessageAlreadyProcessed(this ILogger logger, Guid messageId, string messageType)
        => _inboxMessageAlreadyProcessed(logger, messageId, messageType, null);

    public static void InboxMessageFailed(this ILogger logger, Guid messageId, string messageType, string error, Exception? exception = null)
        => _inboxMessageFailed(logger, messageId, messageType, error, exception);

    public static void InboxUnprocessedMessagesLoaded(this ILogger logger, int count)
        => _inboxUnprocessedMessagesLoaded(logger, count, null);

    public static void RabbitMqPublisherInitialized(this ILogger logger, string exchangeName, string routingKey)
        => _rabbitMqPublisherInitialized(logger, exchangeName, routingKey, null);

    public static void RabbitMqMessagePublished(this ILogger logger, Guid messageId, string messageType, string exchangeName, string routingKey)
        => _rabbitMqMessagePublished(logger, messageId, messageType, exchangeName, routingKey, null);

    public static void RabbitMqSubscriberStarted(this ILogger logger, string queueName)
        => _rabbitMqSubscriberStarted(logger, queueName, null);

    public static void RabbitMqSubscriberStopping(this ILogger logger, string queueName)
        => _rabbitMqSubscriberStopping(logger, queueName, null);

    public static void HostedServiceStarted(this ILogger logger, string serviceName)
        => _hostedServiceStarted(logger, serviceName, null);

    public static void HostedServiceStopped(this ILogger logger, string serviceName)
        => _hostedServiceStopped(logger, serviceName, null);

    public static void HostedServiceError(this ILogger logger, string serviceName, Exception exception)
        => _hostedServiceError(logger, serviceName, exception);
}
