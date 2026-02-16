using Messaging.OutboxInbox.Entities;

namespace Messaging.OutboxInbox.Services;

public interface IInboxMessagesService
{
    Task<IEnumerable<InboxRecord>> GetUnprocessedListAsync(CancellationToken cancellationToken = default);

    Task<bool> IsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task<bool> TryInsertAsync<TMessage>(Guid messageId, TMessage message, DateTime occurredAt, CancellationToken cancellationToken = default)
        where TMessage : class;

    Task<bool> TryInsertAsync(Guid messageId, string messageType, string content, DateTime occurredAt, CancellationToken cancellationToken = default);

    Task MarkAsProcessedAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task MarkAsFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid messageId, CancellationToken cancellationToken = default);
}