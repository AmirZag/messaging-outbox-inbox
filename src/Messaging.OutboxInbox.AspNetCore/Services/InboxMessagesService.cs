using System.Text.Json;
using Messaging.OutboxInbox.Entities;
using Microsoft.EntityFrameworkCore;

namespace Messaging.OutboxInbox.Services;

public sealed class InboxMessagesService : IInboxMessagesService
{
    private readonly DbContext _context;

    public InboxMessagesService(DbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<InboxRecord>> GetUnprocessedListAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Set<InboxRecord>()
            .Where(x => x.ProcessedAt == null)
            .OrderBy(x => x.OccurredAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryInsertAsync<TMessage>(Guid messageId, TMessage message, DateTime occurredAt, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        string messageType = typeof(TMessage).AssemblyQualifiedName
            ?? throw new InvalidOperationException($"Cannot determine type name for {typeof(TMessage).Name}");

        string content = JsonSerializer.Serialize(message);

        return await TryInsertAsync(messageId, messageType, content, occurredAt, cancellationToken);
    }

    public async Task<bool> TryInsertAsync(Guid messageId, string messageType, string content, DateTime occurredAt, CancellationToken cancellationToken = default)
    {
        try
        {
            var inboxRecord = new InboxRecord
            {
                Id = messageId,
                Type = messageType,
                Content = content,
                OccurredAt = occurredAt
            };

            _context.Set<InboxRecord>().Add(inboxRecord);
            await _context.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Message already exists - this is OK (idempotent)
            return false;
        }
    }

    public async Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        await _context.Set<InboxRecord>()
            .Where(x => x.Id == messageId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ProcessedAt, DateTime.UtcNow), cancellationToken);
    }

    public async Task MarkAsFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default)
    {
        await _context.Set<InboxRecord>()
            .Where(x => x.Id == messageId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Error, error), cancellationToken);
    }

    public async Task RemoveAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        await _context.Set<InboxRecord>()
            .Where(x => x.Id == messageId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // PostgreSQL unique constraint violation code
        return ex.InnerException?.Message?.Contains("23505") == true ||
               ex.InnerException?.Message?.Contains("duplicate key") == true;
    }

    public async Task<bool> IsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<InboxRecord>()
            .Where(x => x.Id == messageId)
            .Select(x => x.ProcessedAt != null)
            .FirstOrDefaultAsync(cancellationToken);
    }
}