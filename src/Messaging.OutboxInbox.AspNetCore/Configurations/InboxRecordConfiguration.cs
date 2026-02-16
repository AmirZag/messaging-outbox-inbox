using Messaging.OutboxInbox.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messaging.OutboxInbox.Configurations;

public class InboxRecordConfiguration : IEntityTypeConfiguration<InboxRecord>
{
    public void Configure(EntityTypeBuilder<InboxRecord> builder)
    {
        builder.ToTable("InboxRecords");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.Id)
        .IsUnique();

        builder.Property(o => o.Type)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(o => o.Content)
            .IsRequired()
            .HasMaxLength(8000)
            .HasColumnType("jsonb");

        builder.Property(o => o.OccurredAt)
            .IsRequired();

        builder.Property(o => o.ProcessedAt);

        builder.Property(o => o.Error)
            .HasMaxLength(2000);

        builder.HasIndex(x => x.ProcessedAt)
            .HasFilter("\"ProcessedAt\" IS NULL");
    }
}