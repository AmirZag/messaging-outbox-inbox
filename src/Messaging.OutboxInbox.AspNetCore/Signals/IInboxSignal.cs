namespace Messaging.OutboxInbox.AspNetCore.Signals;

internal interface IInboxSignal
{
    void Notify();
    Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}