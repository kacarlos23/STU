using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STU.Domain.Territories;

namespace STU.Infrastructure.Persistence.Configurations;

internal sealed class MicroregionConfiguration : IEntityTypeConfiguration<Microregion>
{
    public void Configure(EntityTypeBuilder<Microregion> builder)
    {
        builder.ToTable("microregions", table =>
            table.HasCheckConstraint("CK_microregions_Color", "\"Color\" ~ '^#[0-9a-f]{6}$'"));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Code).HasMaxLength(32).IsRequired();
        builder.Property(item => item.Name).HasMaxLength(160).IsRequired();
        builder.Property(item => item.NormalizedName).HasMaxLength(160).IsRequired();
        builder.Property(item => item.Boundary).HasColumnType("geometry(MultiPolygon,4326)").IsRequired();
        builder.Property(item => item.Source).HasConversion<string>().HasMaxLength(32);
        builder.Property(item => item.Color).HasMaxLength(7).IsRequired();
        builder.Property(item => item.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(item => new { item.HealthUnitId, item.Code })
            .IsUnique()
            .HasFilter("\"ArchivedAtUtc\" IS NULL");
        builder.HasIndex(item => new { item.HealthUnitId, item.NormalizedName })
            .IsUnique()
            .HasFilter("\"ArchivedAtUtc\" IS NULL");
        builder.HasIndex(item => item.HealthUnitId);
        builder.HasIndex(item => item.AssignedAgentId);
        builder.HasIndex(item => item.Boundary).HasMethod("gist");
        builder.HasOne<STU.Domain.HealthUnits.HealthUnit>().WithMany().HasForeignKey(item => item.HealthUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<STU.Infrastructure.Identity.ApplicationUser>().WithMany().HasForeignKey(item => item.AssignedAgentId).OnDelete(DeleteBehavior.SetNull);
    }
}
