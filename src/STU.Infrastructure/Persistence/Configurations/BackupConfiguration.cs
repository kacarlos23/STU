using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STU.Domain.Operations;

namespace STU.Infrastructure.Persistence.Configurations;

internal sealed class BackupSettingsConfiguration : IEntityTypeConfiguration<BackupSettings>
{
    public void Configure(EntityTypeBuilder<BackupSettings> builder)
    {
        builder.ToTable("backup_settings", table =>
        {
            table.HasCheckConstraint("CK_backup_settings_LocalHour", "\"LocalHour\" BETWEEN 0 AND 23");
            table.HasCheckConstraint("CK_backup_settings_RetentionCount", "\"RetentionCount\" BETWEEN 1 AND 52");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.DayOfWeek).HasConversion<string>().HasMaxLength(16);
        builder.Property(item => item.TimeZoneId).HasMaxLength(64);
        builder.Property(item => item.ConcurrencyToken).IsConcurrencyToken();
    }
}

internal sealed class BackupRunConfiguration : IEntityTypeConfiguration<BackupRun>
{
    public void Configure(EntityTypeBuilder<BackupRun> builder)
    {
        builder.ToTable("backup_runs", table =>
            table.HasCheckConstraint("CK_backup_runs_Attempts", "\"AttemptCount\" BETWEEN 0 AND 3"));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Trigger).HasConversion<string>().HasMaxLength(24);
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);
        builder.Property(item => item.ScheduleKey).HasMaxLength(16);
        builder.Property(item => item.FileName).HasMaxLength(160);
        builder.Property(item => item.Sha256).HasMaxLength(64);
        builder.Property(item => item.ErrorSummary).HasMaxLength(1000);
        builder.Property(item => item.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(item => item.ScheduleKey).IsUnique();
        builder.HasIndex(item => new { item.Status, item.RequestedAtUtc });
        builder.HasOne<STU.Infrastructure.Identity.ApplicationUser>()
            .WithMany()
            .HasForeignKey(item => item.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
