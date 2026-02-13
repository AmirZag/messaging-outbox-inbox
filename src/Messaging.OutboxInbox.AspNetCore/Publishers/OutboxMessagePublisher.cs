using Messaging.OutboxInbox.Abstractions;
using Messaging.OutboxInbox.AspNetCore.Signals;
using Messaging.OutboxInbox.Entities;
using System.Text.Json;

namespace Messaging.OutboxInbox.AspNetCore.Publishers;

internal sealed class OutboxMessagePublisher : IMessagePublisher
{
    private readonly OutboxInboxContext _context;
    private readonly IOutboxSignal _signal;

    public OutboxMessagePublisher(OutboxInboxContext context, IOutboxSignal signal)
    {
        _context = context;
        _signal = signal;
    }

    public Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default) 
        where TMessage : IMessage
    {
        var record = new OutboxRecord
        {
            Id = Guid.NewGuid(),
            Type = typeof(TMessage).AssemblyQualifiedName!,
            Content = JsonSerializer.Serialize(message),
            OccurredAt = DateTime.UtcNow
        };

        // Add to DbContext - will be saved with user's SaveChangesAsync
        _context.Set<OutboxRecord>().Add(record);

        // Signal processor that new work may exist after SaveChanges
        _signal.Notify();

        return Task.CompletedTask;
    }
}