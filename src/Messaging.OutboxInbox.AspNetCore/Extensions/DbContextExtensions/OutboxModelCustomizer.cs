using Messaging.OutboxInbox.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Messaging.OutboxInbox.AspNetCore.Extensions.DbContextExtensions;

internal class OutboxModelCustomizer : ModelCustomizer
{
    public OutboxModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        if (context.Database.ProviderName?.Contains("Npgsql") == true)
        {
            modelBuilder.ApplyConfiguration(new OutboxRecordConfiguration());
        }
    }
}