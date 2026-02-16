using Messaging.OutboxInbox.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Messaging.OutboxInbox.Services;

public sealed class InboxMessagesService : IInboxMessagesService
{
    private readonly DbContext _context;
    private readonly ILogger<InboxMessagesService> _logger;

    public InboxMessagesService(DbContext context, ILogger<InboxMessagesService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IEnumerable<InboxRecord>> GetUnprocessedListAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Entering {Method}", nameof(GetUnprocessedListAsync));

        try
        {
            var messages = await _context.Set<InboxRecord>()
                .Where(x => x.ProcessedAt == null)
                .OrderBy(x => x.OccurredAt)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            _logger.LogDebug("Retrieved {Count} unprocessed inbox messages", messages.Count);
            return messages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving unprocessed inbox messages");
            throw;
        }
        finally
        {
            _logger.LogDebug("Exiting {Method}", nameof(GetUnprocessedListAsync));
        }
    }

    public async Task<bool> TryInsertAsync<TMessage>(Guid messageId, TMessage message, DateTime occurredAt, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        _logger.LogDebug("Entering {Method} - MessageId: {MessageId}, Type: {MessageType}",
            nameof(TryInsertAsync), messageId, typeof(TMessage).Name);

        string messageType = typeof(TMessage).AssemblyQualifiedName
            ?? throw new InvalidOperationException($"Cannot determine type name for {typeof(TMessage).Name}");

        string content = JsonSerializer.Serialize(message);

        return await TryInsertAsync(messageId, messageType, content, occurredAt, cancellationToken);
    }

    public async Task<bool> TryInsertAsync(Guid messageId, string messageType, string content, DateTime occurredAt, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Entering {Method} - MessageId: {MessageId}, Type: {MessageType}",
            nameof(TryInsertAsync), messageId, messageType);

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

            _logger.LogDebug("Inbox message inserted successfully - MessageId: {MessageId}", messageId);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogDebug("Inbox message already exists (idempotency) - MessageId: {MessageId}", messageId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inserting inbox message - MessageId: {MessageId}, Type: {MessageType}",
                messageId, messageType);
            throw;
        }
        finally
        {
            _logger.LogDebug("Exiting {Method} - MessageId: {MessageId}", nameof(TryInsertAsync), messageId);
        }
    }

    public async Task<bool> IsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Entering {Method} - MessageId: {MessageId}", nameof(IsProcessedAsync), messageId);

        try
        {
            var isProcessed = await _context.Set<InboxRecord>()
                .Where(x => x.Id == messageId)
                .Select(x => x.ProcessedAt != null)
                .FirstOrDefaultAsync(cancellationToken);

            _logger.LogDebug("MessageId: {MessageId}, IsProcessed: {IsProcessed}", messageId, isProcessed);
            return isProcessed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if inbox message is processed - MessageId: {MessageId}", messageId);
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
            var rowsAffected = await _context.Set<InboxRecord>()
                .Where(x => x.Id == messageId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.ProcessedAt, DateTime.UtcNow), cancellationToken);

            if (rowsAffected == 0)
            {
                _logger.LogWarning("No rows updated when marking inbox message as processed - MessageId: {MessageId}", messageId);
            }
            else
            {
                _logger.LogDebug("Marked inbox message as processed - MessageId: {MessageId}", messageId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking inbox message as processed - MessageId: {MessageId}", messageId);
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
            var rowsAffected = await _context.Set<InboxRecord>()
                .Where(x => x.Id == messageId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Error, error), cancellationToken);

            if (rowsAffected == 0)
            {
                _logger.LogWarning("No rows updated when marking inbox message as failed - MessageId: {MessageId}", messageId);
            }
            else
            {
                _logger.LogDebug("Marked inbox message as failed - MessageId: {MessageId}, Error: {Error}", messageId, error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking inbox message as failed - MessageId: {MessageId}", messageId);
            throw;
        }
        finally
        {
            _logger.LogDebug("Exiting {Method} - MessageId: {MessageId}", nameof(MarkAsFailedAsync), messageId);
        }
    }

    public async Task RemoveAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Entering {Method} - MessageId: {MessageId}", nameof(RemoveAsync), messageId);

        try
        {
            var rowsAffected = await _context.Set<InboxRecord>()
                .Where(x => x.Id == messageId)
                .ExecuteDeleteAsync(cancellationToken);

            if (rowsAffected == 0)
            {
                _logger.LogWarning("No rows deleted when removing inbox message - MessageId: {MessageId}", messageId);
            }
            else
            {
                _logger.LogDebug("Removed inbox message - MessageId: {MessageId}", messageId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing inbox message - MessageId: {MessageId}", messageId);
            throw;
        }
        finally
        {
            _logger.LogDebug("Exiting {Method} - MessageId: {MessageId}", nameof(RemoveAsync), messageId);
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // PostgreSQL unique constraint violation code
        return ex.InnerException?.Message?.Contains("23505") == true ||
               ex.InnerException?.Message?.Contains("duplicate key") == true;
    }
}