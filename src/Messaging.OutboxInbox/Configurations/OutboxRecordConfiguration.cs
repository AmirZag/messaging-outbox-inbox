using Messaging.OutboxInbox.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messaging.OutboxInbox.Configurations;

internal sealed class OutboxRecordConfiguration : IEntityTypeConfiguration<OutboxRecord>
{
    public void Configure(EntityTypeBuilder<OutboxRecord> builder)
    {
        builder.ToTable("OutboxRecords");
        builder.HasKey(x => x.Id);

        builder.Property(o => o.Type)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(o => o.Content)
            .IsRequired()
            .HasMaxLength(2000)
            .HasColumnType("jsonb");

        builder.Property(o => o.OccurredAt)
            .IsRequired();

        builder.Property(o => o.ProcessedAt);

        builder.Property(o => o.Error)
            .HasMaxLength(2000);

        builder.HasIndex(x => new { x.ProcessedAt, x.OccurredAt })
            .HasFilter("\"ProcessedAt\" IS NULL");
    }
}