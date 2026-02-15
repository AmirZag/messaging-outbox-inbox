using Messaging.OutboxInbox.Entities;

namespace Messaging.OutboxInbox.AspNetCore.Queues;

public interface IOutboxMessageQueue
{
    void Enqueue(OutboxRecord message);

    Task<OutboxRecord?> DequeueAsync(CancellationToken cancellationToken = default);
}