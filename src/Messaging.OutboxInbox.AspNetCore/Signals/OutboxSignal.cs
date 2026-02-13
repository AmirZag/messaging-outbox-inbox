namespace Messaging.OutboxInbox.AspNetCore.Signals;

internal sealed class OutboxSignal : IOutboxSignal
{
    private readonly SemaphoreSlim _signal = new(0);

    public void Notify()
    {
        // Release semaphore to wake up processor
        if (_signal.CurrentCount == 0)
        {
            _signal.Release();
        }
    }

    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            await _signal.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout - this is expected
        }
    }
}