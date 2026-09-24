using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STU.Domain.Families;
using STU.Domain.Properties;

namespace STU.Infrastructure.Persistence.Configurations;

internal sealed class FamilyConfiguration : IEntityTypeConfiguration<Family>
{
    public void Configure(EntityTypeBuilder<Family> builder)
    {
        builder.ToTable("families");
        builder.HasKey(item => item.Id);
        builder.HasAlternateKey(item => new { item.Id, item.HealthUnitId });
        builder.Property(item => item.Number).HasMaxLength(32).IsRequired();
        builder.Property(item => item.ResponsibleName).HasMaxLength(120).IsRequired();
        builder.HasOne<STU.Infrastructure.Identity.ApplicationUser>().WithMany().HasForeignKey(item => item.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<STU.Infrastructure.Identity.ApplicationUser>().WithMany().HasForeignKey(item => item.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(item => item.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(item => new { item.HealthUnitId, item.Number }).IsUnique();
        builder.HasIndex(item => new { item.HealthUnitId, item.ArchivedAtUtc });
        builder.HasOne<STU.Domain.HealthUnits.HealthUnit>().WithMany().HasForeignKey(item => item.HealthUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FamilyVersionConfiguration : IEntityTypeConfiguration<FamilyVersion>
{
    public void Configure(EntityTypeBuilder<FamilyVersion> builder)
    {
        builder.ToTable("family_versions");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Number).HasMaxLength(32).IsRequired();
        builder.Property(item => item.ResponsibleName).HasMaxLength(120).IsRequired();
        builder.Property(item => item.ChangeKind).HasMaxLength(32).IsRequired();
        builder.HasIndex(item => new { item.FamilyId, item.VersionNumber }).IsUnique();
        builder.HasIndex(item => new { item.HealthUnitId, item.ChangedAtUtc });
        builder.HasOne<Family>().WithMany().HasForeignKey(item => item.FamilyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FamilyPropertyLinkConfiguration : IEntityTypeConfiguration<FamilyPropertyLink>
{
    public void Configure(EntityTypeBuilder<FamilyPropertyLink> builder)
    {
        builder.ToTable("family_property_links", table =>
            table.HasCheckConstraint("CK_family_property_links_Period", "\"EndedAtUtc\" IS NULL OR \"EndedAtUtc\" > \"StartedAtUtc\""));
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => item.FamilyId).IsUnique().HasFilter("\"EndedAtUtc\" IS NULL");
        builder.HasIndex(item => item.PropertyId).IsUnique().HasFilter("\"EndedAtUtc\" IS NULL");
        builder.HasIndex(item => new { item.HealthUnitId, item.StartedAtUtc });
        builder.Property(item => item.EndReason).HasMaxLength(32);
        builder.HasOne<Family>().WithMany().HasForeignKey(item => new { item.FamilyId, item.HealthUnitId }).HasPrincipalKey(item => new { item.Id, item.HealthUnitId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HealthProperty>().WithMany().HasForeignKey(item => new { item.PropertyId, item.HealthUnitId }).HasPrincipalKey(item => new { item.Id, item.HealthUnitId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<STU.Infrastructure.Identity.ApplicationUser>().WithMany().HasForeignKey(item => item.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<STU.Infrastructure.Identity.ApplicationUser>().WithMany().HasForeignKey(item => item.EndedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
