namespace Messaging.OutboxInbox.Abstractions;

/// <summary>
/// Publisher for messages using the Outbox pattern.
/// Call PublishAsync within your transaction, then call SaveChangesAsync.
/// The message will be persisted atomically with your business data and published in the background.
/// </summary>
public interface IMessagePublisher
{
    /// <summary>
    /// Publishes a message using the Outbox pattern.
    /// This adds the message to the outbox table (persisted when you call SaveChangesAsync on your DbContext)
    /// and signals the background processor to check for new messages.
    /// </summary>
    Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : IMessage;
}