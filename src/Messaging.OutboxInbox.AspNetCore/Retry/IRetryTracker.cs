namespace Messaging.OutboxInbox.AspNetCore.Retry;

internal interface IRetryTracker
{
    bool ShouldRetry(Guid messageId, int maxRetryAttempts);
    void RecordAttempt(Guid messageId);
    void Reset(Guid messageId);
}