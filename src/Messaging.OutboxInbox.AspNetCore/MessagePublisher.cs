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

    public async Task PublishAsync<TMessage>(TMessage message, Guid messageId, CancellationToken cancellationToken = default)
        where TMessage : IMessage
    {
        ArgumentNullException.ThrowIfNull(message);

        bool exists = await _context.Set<OutboxRecord>()
            .AnyAsync(x => x.Id == messageId, cancellationToken);

        if (exists)
        {
            return;
        }

        var messageType = typeof(TMessage).AssemblyQualifiedName
            ?? throw new InvalidOperationException($"Cannot determine type name for {typeof(TMessage).Name}");

        var content = JsonSerializer.Serialize(message);

        var outboxRecord = new OutboxRecord
        {
            Id = messageId,
            Type = messageType,
            Content = content
        };

        _context.Set<OutboxRecord>().Add(outboxRecord);
    }
}