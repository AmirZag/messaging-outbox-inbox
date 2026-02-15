namespace Messaging.OutboxInbox;

/// <summary>
/// Publisher for messages using the Outbox pattern.
/// Call PublishAsync within your transaction, then call SaveChangesAsync.
/// The message will be persisted atomically with your business data and published in the background.
/// </summary>
public interface IMessagePublisher
{
    /// <summary>
    /// Publishes a message using the Outbox pattern.
    /// The messageId will be set to match your business entity's database-generated ID.
    /// </summary>
    Task PublishAsync<TMessage>(TMessage message, Guid messageId, CancellationToken cancellationToken = default)
        where TMessage : IMessage;
}