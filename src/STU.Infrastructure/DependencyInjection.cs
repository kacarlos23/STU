using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("StuDatabase")
            ?? throw new InvalidOperationException("Connection string 'StuDatabase' was not configured.");

        var connectionBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        var maximumPoolSize = configuration.GetValue<int?>("Database:MaximumPoolSize");
        if (maximumPoolSize is > 0)
        {
            connectionBuilder.MaxPoolSize = maximumPoolSize.Value;
        }

        services.AddDbContext<StuDbContext>(options =>
            options.UseNpgsql(connectionBuilder.ConnectionString, npgsql => npgsql.UseNetTopologySuite()));

        services.AddDataProtection()
            .SetApplicationName("STU")
            .PersistKeysToDbContext<StuDbContext>();

        return services;
    }

    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services)
    {
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.User.RequireUniqueEmail = false;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<StuDbContext>()
            .AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = TimeSpan.Zero);

        return services;
    }
}
