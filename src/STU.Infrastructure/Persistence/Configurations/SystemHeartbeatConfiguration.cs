using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STU.Domain.Operations;

namespace STU.Infrastructure.Persistence.Configurations;

internal sealed class SystemHeartbeatConfiguration : IEntityTypeConfiguration<SystemHeartbeat>
{
    public void Configure(EntityTypeBuilder<SystemHeartbeat> builder)
    {
        builder.ToTable("system_heartbeats");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Component).HasMaxLength(64);
        builder.Property(item => item.Instance).HasMaxLength(120);
        builder.Property(item => item.Version).HasMaxLength(64);
        builder.Property(item => item.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(item => item.Component).IsUnique();
        builder.HasIndex(item => item.LastSeenAtUtc);
    }
}
