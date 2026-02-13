using System.Text.Json;
using Messaging.OutboxInbox.Abstractions;
using Messaging.OutboxInbox.Entities;
using Microsoft.EntityFrameworkCore;

namespace Messaging.OutboxInbox.Services;

internal sealed class MessagePublisher : IMessagePublisher
{
    private readonly DbContext _context;

    public MessagePublisher(DbContext context)
    {
        _context = context;
    }

    public Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : IMessage
    {
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

        return Task.CompletedTask;
    }
}