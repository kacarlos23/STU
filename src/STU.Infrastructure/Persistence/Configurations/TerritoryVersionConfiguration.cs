using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STU.Domain.Territories;

namespace STU.Infrastructure.Persistence.Configurations;

internal sealed class NeighborhoodVersionConfiguration : IEntityTypeConfiguration<NeighborhoodVersion>
{
    public void Configure(EntityTypeBuilder<NeighborhoodVersion> builder)
    {
        builder.ToTable("neighborhood_versions", table =>
            table.HasCheckConstraint("CK_neighborhood_versions_Color", "\"Color\" ~ '^#[0-9a-f]{6}$'"));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Name).HasMaxLength(160).IsRequired();
        builder.Property(item => item.Geometry).HasColumnType("geometry(Geometry,4326)").IsRequired();
        builder.Property(item => item.Source).HasConversion<string>().HasMaxLength(32);
        builder.Property(item => item.ExternalReference).HasMaxLength(160);
        builder.Property(item => item.Color).HasMaxLength(7).IsRequired();
        builder.Property(item => item.ChangeKind).HasMaxLength(32).IsRequired();
        builder.HasIndex(item => new { item.NeighborhoodId, item.VersionNumber }).IsUnique();
        builder.HasIndex(item => item.ChangedAtUtc);
    }
}

internal sealed class MicroregionVersionConfiguration : IEntityTypeConfiguration<MicroregionVersion>
{
    public void Configure(EntityTypeBuilder<MicroregionVersion> builder)
    {
        builder.ToTable("microregion_versions", table =>
            table.HasCheckConstraint("CK_microregion_versions_Color", "\"Color\" ~ '^#[0-9a-f]{6}$'"));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Code).HasMaxLength(32).IsRequired();
        builder.Property(item => item.Name).HasMaxLength(160).IsRequired();
        builder.Property(item => item.Boundary).HasColumnType("geometry(MultiPolygon,4326)").IsRequired();
        builder.Property(item => item.Source).HasConversion<string>().HasMaxLength(32);
        builder.Property(item => item.NeighborhoodIds).HasColumnType("uuid[]").IsRequired();
        builder.Property(item => item.Color).HasMaxLength(7).IsRequired();
        builder.Property(item => item.ChangeKind).HasMaxLength(32).IsRequired();
        builder.HasIndex(item => new { item.MicroregionId, item.VersionNumber }).IsUnique();
        builder.HasIndex(item => new { item.HealthUnitId, item.ChangedAtUtc });
    }
}
