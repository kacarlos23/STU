using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STU.Domain.HealthUnits;

namespace STU.Infrastructure.Persistence.Configurations;

internal sealed class HealthUnitConfiguration : IEntityTypeConfiguration<HealthUnit>
{
    public void Configure(EntityTypeBuilder<HealthUnit> builder)
    {
        builder.ToTable("health_units");
        builder.HasKey(unit => unit.Id);

        builder.Property(unit => unit.Code)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(unit => unit.Code)
            .IsUnique();

        builder.Property(unit => unit.Name)
            .HasMaxLength(160)
            .IsRequired();
    }
}
