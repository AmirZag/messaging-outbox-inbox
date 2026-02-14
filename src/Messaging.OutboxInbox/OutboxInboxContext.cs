using Messaging.OutboxInbox.Configurations;
using Messaging.OutboxInbox.Entities;
using Messaging.OutboxInbox.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Messaging.OutboxInbox;

public abstract class OutboxInboxContext : DbContext
{
    private readonly bool _includeMessageInbox;
    private readonly bool _includeMessageOutbox;
    private Action<OutboxRecord>? _outboxEnqueueAction;

    protected OutboxInboxContext(DbContextOptions options) : base(options)
    {
        _includeMessageInbox = options.FindExtension<InboxMessageOnlySupportOption>() is not null;
        _includeMessageOutbox = options.FindExtension<OutboxMessageOnlySupportOption>() is not null;
    }

    protected OutboxInboxContext()
    {
        _includeMessageInbox = true;
        _includeMessageOutbox = true;
    }

    internal void SetOutboxEnqueueAction(Action<OutboxRecord> action)
    {
        _outboxEnqueueAction = action;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Capture outbox records that are being added BEFORE saving
        List<OutboxRecord>? pendingOutboxRecords = null;

        if (_includeMessageOutbox && _outboxEnqueueAction is not null)
        {
            pendingOutboxRecords = ChangeTracker.Entries<OutboxRecord>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .ToList();
        }

        // Save changes - this commits the transaction
        var result = await base.SaveChangesAsync(cancellationToken);

        // After successful save, enqueue the messages for background processing
        if (pendingOutboxRecords is not null && pendingOutboxRecords.Count > 0)
        {
            foreach (var record in pendingOutboxRecords)
            {
                _outboxEnqueueAction!(record);
            }
        }

        return result;
    }

    public override int SaveChanges()
    {
        // Capture outbox records that are being added BEFORE saving
        List<OutboxRecord>? pendingOutboxRecords = null;

        if (_includeMessageOutbox && _outboxEnqueueAction is not null)
        {
            pendingOutboxRecords = ChangeTracker.Entries<OutboxRecord>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .ToList();
        }

        // Save changes - this commits the transaction
        var result = base.SaveChanges();

        // After successful save, enqueue the messages for background processing
        if (pendingOutboxRecords is not null && pendingOutboxRecords.Count > 0)
        {
            foreach (var record in pendingOutboxRecords)
            {
                _outboxEnqueueAction!(record);
            }
        }

        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        if (_includeMessageOutbox)
            new OutboxRecordConfiguration().Configure(modelBuilder.Entity<OutboxRecord>());

        if (_includeMessageInbox)
            new InboxRecordConfiguration().Configure(modelBuilder.Entity<InboxRecord>());
    }
}