using Messaging.OutboxInbox.Abstractions;
using Messaging.OutboxInbox.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Messaging.OutboxInbox;

internal sealed class MessagePublisher : IMessagePublisher
{
    private readonly DbContext _context;

    public MessagePublisher(DbContext context)
    {
        _context = context;
    }

    public async Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : IMessage
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageType = typeof(TMessage).AssemblyQualifiedName
            ?? throw new InvalidOperationException($"Cannot determine type name for {typeof(TMessage).Name}");

        var content = JsonSerializer.Serialize(message);

        var outboxRecord = new OutboxRecord
        {
            Id = Guid.NewGuid(),
            Type = messageType,
            Content = content,
            OccurredAt = DateTime.UtcNow
        };

        _context.Set<OutboxRecord>().Add(outboxRecord);

    }
}