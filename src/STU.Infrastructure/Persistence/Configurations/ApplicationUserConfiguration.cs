using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STU.Domain.HealthUnits;
using STU.Infrastructure.Identity;

namespace STU.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(user => user.DisplayName)
            .HasMaxLength(160)
            .IsRequired();

        builder.HasOne<HealthUnit>()
            .WithMany()
            .HasForeignKey(user => user.HealthUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(user => new { user.HealthUnitId, user.ArchivedAtUtc });
    }
}

