using Messaging.OutboxInbox;
using Microsoft.EntityFrameworkCore;
using OutboxInbox.Api.Models;

namespace OutboxInbox.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<ConversionRecord> ConversionRecords => Set<ConversionRecord>();
    public DbSet<ConversionAuditLog> ConversionAuditLogs => Set<ConversionAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure ConversionRecord
        modelBuilder.Entity<ConversionRecord>(entity =>
        {
            entity.ToTable("ConversionRecords");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.DataSource)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.FileName)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.FilePath)
                .IsRequired()
                .HasMaxLength(1000);

            entity.HasIndex(e => e.StartedAt);
            entity.HasIndex(e => e.DataSource);
        });

        // Configure ConversionAuditLog
        modelBuilder.Entity<ConversionAuditLog>(entity =>
        {
            entity.ToTable("ConversionAuditLogs");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.DataSource)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.FileName)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.Notes)
                .HasMaxLength(2000);

            entity.HasIndex(e => e.ConversionId);
            entity.HasIndex(e => e.AuditedAt);
        });
    }
}