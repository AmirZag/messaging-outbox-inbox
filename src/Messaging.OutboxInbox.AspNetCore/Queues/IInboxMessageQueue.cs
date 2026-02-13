using Messaging.OutboxInbox.Entities;

namespace Messaging.OutboxInbox.AspNetCore.Queues;

public interface IInboxMessageQueue
{
    void Enqueue(InboxRecord message);
    Task<InboxRecord?> DequeueAsync(CancellationToken cancellationToken = default);
}