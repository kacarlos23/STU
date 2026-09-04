using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STU.Domain.Properties;

namespace STU.Infrastructure.Persistence.Configurations;

internal sealed class PropertyConfiguration : IEntityTypeConfiguration<HealthProperty>
{
    public void Configure(EntityTypeBuilder<HealthProperty> b)
    {
        b.ToTable("properties",table=>
        {
            table.HasCheckConstraint("CK_properties_GeometryValid", "ST_IsValid(\"Geometry\")");
            table.HasCheckConstraint("CK_properties_GeometryType", "ST_GeometryType(\"Geometry\") IN ('ST_Point','ST_Polygon','ST_MultiPolygon')");
        });b.HasKey(x=>x.Id);
        b.Property(x=>x.Street).HasMaxLength(180).IsRequired();b.Property(x=>x.HouseNumber).HasMaxLength(32).IsRequired();b.Property(x=>x.FamilyNumber).HasMaxLength(32).IsRequired();
        b.Property(x=>x.PostalCode).HasMaxLength(16);b.Property(x=>x.Complement).HasMaxLength(120);
        b.Property(x=>x.Geometry).HasColumnType("geometry(Geometry,4326)").IsRequired();b.Property(x=>x.RegistrationStatus).HasConversion<string>().HasMaxLength(24);b.Property(x=>x.Situation).HasConversion<string>().HasMaxLength(24);b.Property(x=>x.ConcurrencyToken).IsConcurrencyToken();
        b.HasIndex(x=>new{x.HealthUnitId,x.FamilyNumber}).IsUnique().HasFilter("\"ArchivedAtUtc\" IS NULL");b.HasIndex(x=>x.MicroregionId);b.HasIndex(x=>x.Geometry).HasMethod("gist");
        b.HasOne<STU.Domain.HealthUnits.HealthUnit>().WithMany().HasForeignKey(x=>x.HealthUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<STU.Domain.Territories.Microregion>().WithMany().HasForeignKey(x=>x.MicroregionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PropertyVersionConfiguration:IEntityTypeConfiguration<PropertyVersion>
{
    public void Configure(EntityTypeBuilder<PropertyVersion>b){b.ToTable("property_versions");b.HasKey(x=>x.Id);b.Property(x=>x.Street).HasMaxLength(180);b.Property(x=>x.HouseNumber).HasMaxLength(32);b.Property(x=>x.FamilyNumber).HasMaxLength(32);b.Property(x=>x.PostalCode).HasMaxLength(16);b.Property(x=>x.Complement).HasMaxLength(120);b.Property(x=>x.Geometry).HasColumnType("geometry(Geometry,4326)");b.Property(x=>x.RegistrationStatus).HasConversion<string>().HasMaxLength(24);b.Property(x=>x.Situation).HasConversion<string>().HasMaxLength(24);b.Property(x=>x.ChangeKind).HasMaxLength(32);b.HasIndex(x=>new{x.PropertyId,x.VersionNumber}).IsUnique();b.HasIndex(x=>new{x.HealthUnitId,x.ChangedAtUtc});}
}

internal sealed class PropertyVisitConfiguration:IEntityTypeConfiguration<PropertyVisit>
{
    public void Configure(EntityTypeBuilder<PropertyVisit>b){b.ToTable("property_visits");b.HasKey(x=>x.Id);b.Property(x=>x.Type).HasConversion<string>().HasMaxLength(24);b.Property(x=>x.Outcome).HasConversion<string>().HasMaxLength(24);b.Property(x=>x.ObservedSituation).HasConversion<string>().HasMaxLength(24);b.Property(x=>x.Note).HasMaxLength(240);b.Property(x=>x.ConcurrencyToken).IsConcurrencyToken();b.HasIndex(x=>new{x.PropertyId,x.VisitedAtUtc});b.HasIndex(x=>new{x.HealthUnitId,x.VisitedAtUtc});b.HasOne<HealthProperty>().WithMany().HasForeignKey(x=>x.PropertyId).OnDelete(DeleteBehavior.Restrict);b.HasOne<STU.Infrastructure.Identity.ApplicationUser>().WithMany().HasForeignKey(x=>x.AgentId).OnDelete(DeleteBehavior.Restrict);}
}

internal sealed class OperationalTagConfiguration:IEntityTypeConfiguration<OperationalTag>
{
    public void Configure(EntityTypeBuilder<OperationalTag>b){b.ToTable("operational_tags",table=>table.HasCheckConstraint("CK_operational_tags_Color","\"Color\" ~ '^#[0-9a-f]{6}$'"));b.HasKey(x=>x.Id);b.Property(x=>x.Name).HasMaxLength(80);b.Property(x=>x.Color).HasMaxLength(7);b.HasIndex(x=>new{x.HealthUnitId,x.Name}).IsUnique();b.HasOne<STU.Domain.HealthUnits.HealthUnit>().WithMany().HasForeignKey(x=>x.HealthUnitId).OnDelete(DeleteBehavior.Restrict);}
}

internal sealed class PropertyTagConfiguration:IEntityTypeConfiguration<PropertyTag>
{
    public void Configure(EntityTypeBuilder<PropertyTag>b){b.ToTable("property_tags");b.HasKey(x=>new{x.PropertyId,x.TagId});b.HasOne<HealthProperty>().WithMany().HasForeignKey(x=>x.PropertyId).OnDelete(DeleteBehavior.Cascade);b.HasOne<OperationalTag>().WithMany().HasForeignKey(x=>x.TagId).OnDelete(DeleteBehavior.Cascade);}
}

internal sealed class CoverageRuleConfiguration:IEntityTypeConfiguration<CoverageRule>
{
    public void Configure(EntityTypeBuilder<CoverageRule>b){b.ToTable("coverage_rules",table=>table.HasCheckConstraint("CK_coverage_rules_MaxDays","\"MaxDaysWithoutVisit\" BETWEEN 1 AND 730"));b.HasKey(x=>x.Id);b.HasIndex(x=>x.MicroregionId).IsUnique();b.HasOne<STU.Domain.HealthUnits.HealthUnit>().WithMany().HasForeignKey(x=>x.HealthUnitId).OnDelete(DeleteBehavior.Restrict);b.HasOne<STU.Domain.Territories.Microregion>().WithMany().HasForeignKey(x=>x.MicroregionId).OnDelete(DeleteBehavior.Restrict);}
}
