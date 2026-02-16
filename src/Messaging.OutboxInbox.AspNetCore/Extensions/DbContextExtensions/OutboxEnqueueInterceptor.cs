using Messaging.OutboxInbox.AspNetCore.Queues;
using Messaging.OutboxInbox.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Messaging.OutboxInbox.AspNetCore.Extensions.DbContextExtensions;

internal sealed class OutboxEnqueueInterceptor : SaveChangesInterceptor
{
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

    private void CaptureOutboxRecords(DbContext? context)
    {
        if (context is null) return;

        var trackedRecords = context.ChangeTracker.Entries<OutboxRecord>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        if (trackedRecords.Any())
        {
            PendingOutboxRecords.Set(context, trackedRecords);
        }
    }

    private void EnqueueCapturedRecords(DbContext? context)
    {
        if (context is null) return;

        var records = PendingOutboxRecords.GetAndRemove(context);

        if (records is null) return;

        // Get the queue from the DbContext's service provider (application services)
        var queue = context.GetService<IOutboxMessageQueue>();

        if (queue is null) return;

        foreach (var record in records)
        {
            queue.Enqueue(record);
        }
    }

    private static class PendingOutboxRecords
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<DbContext, List<OutboxRecord>>
            _storage = new();

        public static void Set(DbContext context, List<OutboxRecord> records)
        {
            _storage[context] = records;
        }

        public static List<OutboxRecord>? GetAndRemove(DbContext context)
        {
            _storage.TryRemove(context, out var records);
            return records;
        }
    }
}