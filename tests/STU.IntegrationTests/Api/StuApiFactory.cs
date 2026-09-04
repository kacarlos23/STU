using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using STU.Infrastructure.Persistence;
using STU.Domain.HealthUnits;
using Testcontainers.PostgreSql;

namespace STU.IntegrationTests.Api;

public sealed class StuApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly string ManagerUserName = $"gerente.teste.{Guid.NewGuid():N}";
    public const string ManagerPassword = "Temporary!12345";
    public static readonly string AdministratorUserName = $"admin.teste.{Guid.NewGuid():N}";
    public const string AdministratorPassword = "Temporary!54321";

    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgis/postgis:18-3.6")
        .WithDatabase("stu_tests")
        .WithUsername("stu_tests")
        .WithPassword("stu_tests_password")
        .Build();

    public Task InitializeAsync() => database.StartAsync();

    Task IAsyncLifetime.DisposeAsync() => database.DisposeAsync().AsTask();

    public async Task<Guid> CreateHealthUnitAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var unit = HealthUnit.Create($"SV-{suffix}", $"UBS SeguranÃ§a {suffix}");
        db.HealthUnits.Add(unit);
        await db.SaveChangesAsync();
        return unit.Id;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:StuDatabase"] = database.GetConnectionString(),
                ["Database:ApplyMigrations"] = "true",
                ["RateLimiting:ApiPermitLimit"] = "10000",
                ["RateLimiting:LoginPermitLimit"] = "10000",
                ["Bootstrap:GlobalAdministrator:UserName"] = AdministratorUserName,
                ["Bootstrap:GlobalAdministrator:Password"] = AdministratorPassword,
                ["Bootstrap:GlobalAdministrator:DisplayName"] = "Administrador de teste",
                ["Bootstrap:PilotManager:UserName"] = ManagerUserName,
                ["Bootstrap:PilotManager:Password"] = ManagerPassword,
                ["Bootstrap:PilotManager:DisplayName"] = "Gerente de teste",
                ["Bootstrap:PilotHealthUnit:Code"] = "UBS-TESTE",
                ["Bootstrap:PilotHealthUnit:Name"] = "UBS de Teste",
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextOptionsConfiguration<StuDbContext>>();
            services.RemoveAll<DbContextOptions<StuDbContext>>();
            services.RemoveAll<StuDbContext>();
            services.AddDbContext<StuDbContext>(options =>
                options.UseNpgsql(
                    database.GetConnectionString(),
                    npgsql => npgsql.UseNetTopologySuite()));
        });
    }
}

[CollectionDefinition(Name)]
public sealed class StuApiFixtureDefinition : ICollectionFixture<StuApiFactory>
{
    public const string Name = "STU API";
}
