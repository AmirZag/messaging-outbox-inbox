using Messaging.OutboxInbox.AspNetCore.Extensions.DbContextExtensions;
using Messaging.OutboxInbox.Configurations;
using Messaging.OutboxInbox.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // If outbox is enabled, register the interceptor
        if (_includeMessageOutbox)
        {
            // Try to get interceptor from DI if available
            var serviceProvider = optionsBuilder.Options
                .FindExtension<Microsoft.EntityFrameworkCore.Infrastructure.CoreOptionsExtension>()
                ?.ApplicationServiceProvider;

            if (serviceProvider != null)
            {
                var interceptor = serviceProvider.GetService<OutboxEnqueueInterceptor>();
                if (interceptor != null)
                {
                    optionsBuilder.AddInterceptors(interceptor);
                }
            }
        }
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