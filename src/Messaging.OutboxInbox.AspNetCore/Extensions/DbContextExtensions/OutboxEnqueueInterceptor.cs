using Messaging.OutboxInbox.AspNetCore.Queues;
using Messaging.OutboxInbox.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace Messaging.OutboxInbox.AspNetCore.Extensions.DbContextExtensions;

internal sealed class OutboxEnqueueInterceptor : SaveChangesInterceptor
{
    private static readonly ConcurrentDictionary<DbContext, List<OutboxRecord>> _storage = new();

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CaptureOutboxRecords(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        CaptureOutboxRecords(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        EnqueueCapturedRecords(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result)
    {
        EnqueueCapturedRecords(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    // Bug 2 fix: clean up on failure to prevent memory leak
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is not null)
            _storage.TryRemove(eventData.Context, out _);

        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            _storage.TryRemove(eventData.Context, out _);

        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private static void CaptureOutboxRecords(DbContext? context)
    {
        if (context is null) return;

        var trackedRecords = context.ChangeTracker.Entries<OutboxRecord>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        if (trackedRecords.Any())
            _storage[context] = trackedRecords;
    }

    private static void EnqueueCapturedRecords(DbContext? context)
    {
        if (context is null) return;

        if (!_storage.TryRemove(context, out var records)) return;

        var queue = context.GetService<IOutboxMessageQueue>();
        if (queue is null) return;

        foreach (var record in records)
            queue.Enqueue(record);
    }
}