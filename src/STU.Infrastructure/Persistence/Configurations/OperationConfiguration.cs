using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STU.Domain.Operations;

namespace STU.Infrastructure.Persistence.Configurations;

internal sealed class OperationJobConfiguration : IEntityTypeConfiguration<OperationJob>
{
    public void Configure(EntityTypeBuilder<OperationJob> builder)
    {
        builder.ToTable("operation_jobs", table =>
        {
            table.HasCheckConstraint("CK_operation_jobs_Progress", "\"ProgressPercentage\" BETWEEN 0 AND 100");
            table.HasCheckConstraint("CK_operation_jobs_Attempts", "\"AttemptCount\" BETWEEN 0 AND 3");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(item => item.Format).HasConversion<string>().HasMaxLength(24);
        builder.Property(item => item.ParametersJson).HasColumnType("jsonb");
        builder.Property(item => item.SourceFileName).HasMaxLength(160);
        builder.Property(item => item.OriginalFileName).HasMaxLength(255);
        builder.Property(item => item.StagedFileName).HasMaxLength(160);
        builder.Property(item => item.ResultFileName).HasMaxLength(160);
        builder.Property(item => item.ErrorSummary).HasMaxLength(2000);
        builder.Property(item => item.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(item => new { item.Status, item.CreatedAtUtc });
        builder.HasIndex(item => new { item.HealthUnitId, item.CreatedAtUtc });
        builder.HasOne<STU.Domain.HealthUnits.HealthUnit>().WithMany().HasForeignKey(item => item.HealthUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<STU.Infrastructure.Identity.ApplicationUser>().WithMany().HasForeignKey(item => item.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> builder)
    {
        builder.ToTable("user_notifications");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(item => item.Title).HasMaxLength(120);
        builder.Property(item => item.Message).HasMaxLength(500);
        builder.Property(item => item.Link).HasMaxLength(240);
        builder.HasIndex(item => new { item.UserId, item.ReadAtUtc, item.CreatedAtUtc });
        builder.HasOne<STU.Infrastructure.Identity.ApplicationUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<STU.Domain.HealthUnits.HealthUnit>().WithMany().HasForeignKey(item => item.HealthUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
