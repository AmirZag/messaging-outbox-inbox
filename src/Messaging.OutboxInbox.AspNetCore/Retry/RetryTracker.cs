using System.Collections.Concurrent;

namespace Messaging.OutboxInbox.AspNetCore.Retry;

internal sealed class RetryTracker : IRetryTracker
{
    private readonly ConcurrentDictionary<Guid, int> _attemptCounts = new();

    public bool ShouldRetry(Guid messageId, int maxRetryAttempts)
    {
        var attempts = _attemptCounts.GetOrAdd(messageId, 0);
        return attempts < maxRetryAttempts;
    }

    public void RecordAttempt(Guid messageId)
    {
        _attemptCounts.AddOrUpdate(messageId, 1, (_, count) => count + 1);
    }

    public void Reset(Guid messageId)
    {
        _attemptCounts.TryRemove(messageId, out _);
    }
}