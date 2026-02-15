using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Messaging.OutboxInbox.AspNetCore.Extensions.DbContextExtensions;

internal sealed class OutboxMessageOnlySupportOption : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services) { }
    public void Validate(IDbContextOptions options) { }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(IDbContextOptionsExtension extension) : base(extension) { }

        public override bool IsDatabaseProvider => false;
        
        public override string LogFragment => "OutboxOnly";
        
        public override int GetServiceProviderHashCode() => 0;
        
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;
        
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) => debugInfo["Messaging:OutboxOnly"] = "1";
    }
}