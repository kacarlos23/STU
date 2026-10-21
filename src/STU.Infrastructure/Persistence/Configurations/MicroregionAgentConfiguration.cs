using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STU.Domain.Territories;
using STU.Infrastructure.Identity;

namespace STU.Infrastructure.Persistence.Configurations;

internal sealed class MicroregionAgentConfiguration : IEntityTypeConfiguration<MicroregionAgent>
{
    public void Configure(EntityTypeBuilder<MicroregionAgent> b)
    {
        b.ToTable("microregion_agents", table => table.HasCheckConstraint("CK_microregion_agents_Dates", "\"EndedAtUtc\" IS NULL OR \"EndedAtUtc\" >= \"StartedAtUtc\""));
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.Id, x.HealthUnitId, x.MicroregionId });
        b.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        b.HasIndex(x => new { x.MicroregionId, x.AgentId }).IsUnique().HasFilter("\"EndedAtUtc\" IS NULL");
        b.HasIndex(x => new { x.HealthUnitId, x.AgentId });
        b.HasOne<Microregion>().WithMany().HasForeignKey(x => new { x.MicroregionId, x.HealthUnitId }).HasPrincipalKey(x => new { x.Id, x.HealthUnitId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
    }
}
