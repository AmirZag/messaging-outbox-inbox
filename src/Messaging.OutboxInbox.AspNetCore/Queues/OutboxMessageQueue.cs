using Messaging.OutboxInbox.Entities;
using System.Collections.Concurrent;

namespace Messaging.OutboxInbox.AspNetCore.Queues;

internal sealed class OutboxMessageQueue : IOutboxMessageQueue
{
    private readonly SemaphoreSlim _signal = new(0);

    private readonly ConcurrentQueue<OutboxRecord> _queue = new();

    public void Enqueue(OutboxRecord message)
    {
        ArgumentNullException.ThrowIfNull(message);

        _queue.Enqueue(message);
        _signal.Release();
    }

    public async Task<OutboxRecord?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        await _signal.WaitAsync(cancellationToken);

        _queue.TryDequeue(out var message);
        return message;
    }
}