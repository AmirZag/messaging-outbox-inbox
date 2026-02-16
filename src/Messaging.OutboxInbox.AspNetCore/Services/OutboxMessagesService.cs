using Messaging.OutboxInbox.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Messaging.OutboxInbox.Services;

public sealed class OutboxMessagesService : IOutboxMessagesService
{
    private readonly DbContext _context;
    private readonly ILogger<OutboxMessagesService> _logger;

    public OutboxMessagesService(DbContext context, ILogger<OutboxMessagesService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IEnumerable<OutboxRecord>> GetUnprocessedListAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Entering {Method}", nameof(GetUnprocessedListAsync));

        try
        {
            var messages = await _context.Set<OutboxRecord>()
                .Where(x => x.ProcessedAt == null)
                .OrderBy(x => x.OccurredAt)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            _logger.LogDebug("Retrieved {Count} unprocessed outbox messages", messages.Count);
            return messages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving unprocessed outbox messages");
            throw;
        }
        finally
        {
            _logger.LogDebug("Exiting {Method}", nameof(GetUnprocessedListAsync));
        }
    }

    public async Task<bool> IsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Entering {Method} - MessageId: {MessageId}", nameof(IsProcessedAsync), messageId);

        try
        {
            var isProcessed = await _context.Set<OutboxRecord>()
                .Where(x => x.Id == messageId)
                .Select(x => x.ProcessedAt != null)
                .FirstOrDefaultAsync(cancellationToken);

            _logger.LogDebug("MessageId: {MessageId}, IsProcessed: {IsProcessed}", messageId, isProcessed);
            return isProcessed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if outbox message is processed - MessageId: {MessageId}", messageId);
            throw;
        }
        finally
        {
            _logger.LogDebug("Exiting {Method} - MessageId: {MessageId}", nameof(IsProcessedAsync), messageId);
        }
    }

    public async Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Entering {Method} - MessageId: {MessageId}", nameof(MarkAsProcessedAsync), messageId);

        try
        {
            var rowsAffected = await _context.Set<OutboxRecord>()
                .Where(x => x.Id == messageId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.ProcessedAt, DateTime.UtcNow), cancellationToken);

            if (rowsAffected == 0)
            {
                _logger.LogWarning("No rows updated when marking message as processed - MessageId: {MessageId}", messageId);
            }
            else
            {
                _logger.LogDebug("Marked message as processed - MessageId: {MessageId}", messageId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking outbox message as processed - MessageId: {MessageId}", messageId);
            throw;
        }
        finally
        {
            _logger.LogDebug("Exiting {Method} - MessageId: {MessageId}", nameof(MarkAsProcessedAsync), messageId);
        }
    }

    public async Task MarkAsFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Entering {Method} - MessageId: {MessageId}", nameof(MarkAsFailedAsync), messageId);

        try
        {
            var rowsAffected = await _context.Set<OutboxRecord>()
                .Where(x => x.Id == messageId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Error, error), cancellationToken);

            if (rowsAffected == 0)
            {
                _logger.LogWarning("No rows updated when marking message as failed - MessageId: {MessageId}", messageId);
            }
            else
            {
                _logger.LogDebug("Marked message as failed - MessageId: {MessageId}, Error: {Error}", messageId, error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking outbox message as failed - MessageId: {MessageId}", messageId);
            throw;
        }
        finally
        {
            _logger.LogDebug("Exiting {Method} - MessageId: {MessageId}", nameof(MarkAsFailedAsync), messageId);
        }
    }
}