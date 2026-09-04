using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STU.Domain.Auditing;

namespace STU.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.ActorUserName).HasMaxLength(120).IsRequired();
        builder.Property(entry => entry.Action).HasMaxLength(64).IsRequired();
        builder.Property(entry => entry.EntityType).HasMaxLength(80).IsRequired();
        builder.Property(entry => entry.EntityId).HasMaxLength(80).IsRequired();
        builder.Property(entry => entry.Summary).HasMaxLength(500).IsRequired();
        builder.Property(entry => entry.BeforeJson).HasColumnType("jsonb");
        builder.Property(entry => entry.AfterJson).HasColumnType("jsonb");
        builder.Property(entry => entry.IpAddress).HasMaxLength(64);

        builder.HasIndex(entry => entry.OccurredAtUtc).IsDescending();
        builder.HasIndex(entry => new { entry.EntityType, entry.EntityId });
        builder.HasIndex(entry => entry.ActorUserId);
    }
}
