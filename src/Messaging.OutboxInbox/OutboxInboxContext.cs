using Messaging.OutboxInbox.Configurations;
using Messaging.OutboxInbox.Entities;
using Messaging.OutboxInbox.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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

    /// <summary>
    /// Sets the outbox enqueue callback - called by DI container
    /// </summary>
    public void SetOutboxEnqueue(Action<OutboxRecord> enqueueAction)
    {
        _outboxEnqueueAction = enqueueAction;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Collect new outbox messages BEFORE save
        List<OutboxRecord>? newOutboxMessages = null;

        if (_includeMessageOutbox && _outboxEnqueueAction is not null)
        {
            newOutboxMessages = ChangeTracker
                .Entries<OutboxRecord>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .ToList();
        }

        // Save to database
        var result = await base.SaveChangesAsync(cancellationToken);

        // Enqueue AFTER successful save
        if (newOutboxMessages?.Count > 0)
        {
            foreach (var message in newOutboxMessages)
            {
                _outboxEnqueueAction!(message);
            }
        }

        return result;
    }

    public override int SaveChanges()
    {
        // Collect new outbox messages BEFORE save
        List<OutboxRecord>? newOutboxMessages = null;

        if (_includeMessageOutbox && _outboxEnqueueAction is not null)
        {
            newOutboxMessages = ChangeTracker
                .Entries<OutboxRecord>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .ToList();
        }

        // Save to database
        var result = base.SaveChanges();

        // Enqueue AFTER successful save
        if (newOutboxMessages?.Count > 0)
        {
            foreach (var message in newOutboxMessages)
            {
                _outboxEnqueueAction!(message);
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