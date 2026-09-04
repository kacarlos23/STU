using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using STU.Domain.HealthUnits;
using STU.Domain.Auditing;
using STU.Domain.Territories;
using STU.Domain.Properties;
using STU.Domain.Operations;
using STU.Infrastructure.Identity;

namespace STU.Infrastructure.Persistence;

public sealed class StuDbContext(DbContextOptions<StuDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<HealthUnit> HealthUnits => Set<HealthUnit>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<Neighborhood> Neighborhoods => Set<Neighborhood>();

    public DbSet<Microregion> Microregions => Set<Microregion>();

    public DbSet<MicroregionNeighborhood> MicroregionNeighborhoods => Set<MicroregionNeighborhood>();

    public DbSet<NeighborhoodVersion> NeighborhoodVersions => Set<NeighborhoodVersion>();

    public DbSet<MicroregionVersion> MicroregionVersions => Set<MicroregionVersion>();

    public DbSet<HealthProperty> Properties => Set<HealthProperty>();
    public DbSet<PropertyVersion> PropertyVersions => Set<PropertyVersion>();
    public DbSet<PropertyVisit> PropertyVisits => Set<PropertyVisit>();
    public DbSet<OperationalTag> OperationalTags => Set<OperationalTag>();
    public DbSet<PropertyTag> PropertyTags => Set<PropertyTag>();
    public DbSet<CoverageRule> CoverageRules => Set<CoverageRule>();
    public DbSet<OperationJob> OperationJobs => Set<OperationJob>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();

    public DbSet<BackupSettings> BackupSettings => Set<BackupSettings>();

    public DbSet<BackupRun> BackupRuns => Set<BackupRun>();

    public DbSet<SystemHeartbeat> SystemHeartbeats => Set<SystemHeartbeat>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("postgis");
        builder.ApplyConfigurationsFromAssembly(typeof(StuDbContext).Assembly);
    }
}
