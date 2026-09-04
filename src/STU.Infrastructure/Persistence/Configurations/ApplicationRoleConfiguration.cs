using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using STU.Infrastructure.Identity;

namespace STU.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {
        builder.Property(role => role.DisplayName)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(role => role.Description)
            .HasMaxLength(320);
    }
}

