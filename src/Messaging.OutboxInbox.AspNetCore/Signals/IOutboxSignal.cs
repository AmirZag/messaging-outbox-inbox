// src/Messaging.OutboxInbox.AspNetCore/Signals/IOutboxSignal.cs
namespace Messaging.OutboxInbox.AspNetCore.Signals;

internal interface IOutboxSignal
{
    void Notify();
    Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}