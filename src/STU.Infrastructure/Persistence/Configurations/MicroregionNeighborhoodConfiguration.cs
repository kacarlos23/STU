using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STU.Domain.Territories;

namespace STU.Infrastructure.Persistence.Configurations;

internal sealed class MicroregionNeighborhoodConfiguration : IEntityTypeConfiguration<MicroregionNeighborhood>
{
    public void Configure(EntityTypeBuilder<MicroregionNeighborhood> builder)
    {
        builder.ToTable("microregion_neighborhoods");
        builder.HasKey(item => new { item.MicroregionId, item.NeighborhoodId });
        builder.HasIndex(item => item.NeighborhoodId);
        builder.HasOne<Microregion>().WithMany().HasForeignKey(item => item.MicroregionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Neighborhood>().WithMany().HasForeignKey(item => item.NeighborhoodId).OnDelete(DeleteBehavior.Restrict);
    }
}
