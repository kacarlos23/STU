using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STU.Domain.Territories;

namespace STU.Infrastructure.Persistence.Configurations;

internal sealed class NeighborhoodConfiguration : IEntityTypeConfiguration<Neighborhood>
{
    public void Configure(EntityTypeBuilder<Neighborhood> builder)
    {
        builder.ToTable("neighborhoods", table =>
            table.HasCheckConstraint("CK_neighborhoods_Color", "\"Color\" ~ '^#[0-9a-f]{6}$'"));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Name).HasMaxLength(160).IsRequired();
        builder.Property(item => item.Geometry).HasColumnType("geometry(Geometry,4326)").IsRequired();
        builder.Property(item => item.Source).HasConversion<string>().HasMaxLength(32);
        builder.Property(item => item.ExternalReference).HasMaxLength(160);
        builder.Property(item => item.Color).HasMaxLength(7).IsRequired();
        builder.Property(item => item.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(item => item.Name);
        builder.HasIndex(item => item.Geometry).HasMethod("gist");
    }
}
