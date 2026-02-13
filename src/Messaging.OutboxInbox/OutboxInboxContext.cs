using Messaging.OutboxInbox.Configurations;
using Messaging.OutboxInbox.Entities;
using Messaging.OutboxInbox.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Messaging.OutboxInbox;

public abstract class OutboxInboxContext : DbContext
{
    private readonly bool _includeMessageInbox;
    private readonly bool _includeMessageOutbox;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        if (_includeMessageOutbox)
            new OutboxRecordConfiguration().Configure(modelBuilder.Entity<OutboxRecord>());

        if (_includeMessageInbox)
            new InboxRecordConfiguration().Configure(modelBuilder.Entity<InboxRecord>());
    }
}