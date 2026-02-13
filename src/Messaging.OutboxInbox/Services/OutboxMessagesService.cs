using Messaging.OutboxInbox.Entities;
using Microsoft.EntityFrameworkCore;

namespace Messaging.OutboxInbox.Services;

internal sealed class OutboxMessagesService : IOutboxMessagesService
{
    private readonly DbContext _context;

    public OutboxMessagesService(DbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<OutboxRecord>> GetUnprocessedListAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Set<OutboxRecord>()
            .Where(x => x.ProcessedAt == null)
            .OrderBy(x => x.OccurredAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        await _context.Set<OutboxRecord>()
            .Where(x => x.Id == messageId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ProcessedAt, DateTime.UtcNow), cancellationToken);
    }

    public async Task MarkAsFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default)
    {
        await _context.Set<OutboxRecord>()
            .Where(x => x.Id == messageId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Error, error), cancellationToken);
    }
}