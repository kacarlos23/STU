using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using STU.Application.Security;
using STU.Domain.HealthUnits;
using STU.Infrastructure.Identity;

namespace STU.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    private static readonly RoleDefinition[] SystemRoleDefinitions =
    [
        new(
            SystemRoles.GlobalAdministrator,
            "Administrador global",
            "Controle integral e isolado de toda a plataforma.",
            [StuPermissions.All]),
        new(
            SystemRoles.HealthUnitManager,
            "Gerente",
            "Gestão da UBS, servidores, território e operação.",
            [
                StuPermissions.MapView,
                StuPermissions.PropertiesView,
                StuPermissions.PropertiesManage,
                StuPermissions.VisitsView,
                StuPermissions.VisitsManage,
                StuPermissions.TerritoryManage,
                StuPermissions.HealthUnitUsersManage,
                StuPermissions.ReportsExport,
            ]),
        new(
            SystemRoles.HealthAgent,
            "Agente de saúde",
            "Cadastro de imóveis e registro de visitas na área atribuída.",
            [
                StuPermissions.MapView,
                StuPermissions.PropertiesView,
                StuPermissions.PropertiesManage,
                StuPermissions.VisitsView,
                StuPermissions.VisitsManage,
            ]),
        new(
            SystemRoles.Receptionist,
            "Recepcionista",
            "Consulta operacional dos imóveis e visitas da UBS.",
            [StuPermissions.MapView, StuPermissions.PropertiesView, StuPermissions.VisitsView]),
        new(
            SystemRoles.Doctor,
            "Médico",
            "Consulta operacional dos imóveis e histórico de visitas da UBS.",
            [StuPermissions.MapView, StuPermissions.PropertiesView, StuPermissions.VisitsView]),
    ];

    public static async Task InitializeAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var scopedServices = scope.ServiceProvider;
        var dbContext = scopedServices.GetRequiredService<StuDbContext>();

        await dbContext.Database.MigrateAsync(cancellationToken);

        var roleManager = scopedServices.GetRequiredService<RoleManager<ApplicationRole>>();
        foreach (var definition in SystemRoleDefinitions)
        {
            await EnsureRoleAsync(roleManager, definition);
        }

        var userManager = scopedServices.GetRequiredService<UserManager<ApplicationUser>>();
        await EnsureGlobalAdministratorAsync(userManager, configuration);
        await EnsurePilotManagerAsync(dbContext, userManager, configuration, cancellationToken);
    }

    private static async Task EnsureRoleAsync(
        RoleManager<ApplicationRole> roleManager,
        RoleDefinition definition)
    {
        var role = await roleManager.FindByNameAsync(definition.Name);
        if (role is null)
        {
            role = ApplicationRole.CreateSystem(
                definition.Name,
                definition.DisplayName,
                definition.Description);
            EnsureSucceeded(await roleManager.CreateAsync(role), $"create role {definition.Name}");
        }

        var currentClaims = await roleManager.GetClaimsAsync(role);
        foreach (var permission in definition.Permissions)
        {
            if (currentClaims.Any(claim =>
                    claim.Type == StuClaimTypes.Permission && claim.Value == permission))
            {
                continue;
            }

            EnsureSucceeded(
                await roleManager.AddClaimAsync(
                    role,
                    new Claim(StuClaimTypes.Permission, permission)),
                $"add permission {permission} to {definition.Name}");
        }
    }

    private static async Task EnsureGlobalAdministratorAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration)
    {
        var userName = configuration["Bootstrap:GlobalAdministrator:UserName"];
        var password = configuration["Bootstrap:GlobalAdministrator:Password"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var displayName = configuration["Bootstrap:GlobalAdministrator:DisplayName"]
            ?? "Administrador global";
        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            user = ApplicationUser.Create(userName, displayName, healthUnitId: null);
            EnsureSucceeded(
                await userManager.CreateAsync(user, password),
                "create global administrator");
        }

        if (!await userManager.IsInRoleAsync(user, SystemRoles.GlobalAdministrator))
        {
            EnsureSucceeded(
                await userManager.AddToRoleAsync(user, SystemRoles.GlobalAdministrator),
                "assign global administrator role");
        }
    }

    private static async Task EnsurePilotManagerAsync(
        StuDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var userName = configuration["Bootstrap:PilotManager:UserName"];
        var password = configuration["Bootstrap:PilotManager:Password"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var unitCode = (configuration["Bootstrap:PilotHealthUnit:Code"] ?? "UBS-PILOTO")
            .Trim()
            .ToUpperInvariant();
        var unitName = configuration["Bootstrap:PilotHealthUnit:Name"] ?? "UBS Piloto";
        var healthUnit = await dbContext.HealthUnits
            .SingleOrDefaultAsync(unit => unit.Code == unitCode, cancellationToken);
        if (healthUnit is null)
        {
            healthUnit = HealthUnit.Create(unitCode, unitName);
            dbContext.HealthUnits.Add(healthUnit);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var displayName = configuration["Bootstrap:PilotManager:DisplayName"] ?? "Gerente da UBS";
        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            user = ApplicationUser.Create(userName, displayName, healthUnit.Id);
            EnsureSucceeded(await userManager.CreateAsync(user, password), "create pilot manager");
        }

        if (!await userManager.IsInRoleAsync(user, SystemRoles.HealthUnitManager))
        {
            EnsureSucceeded(
                await userManager.AddToRoleAsync(user, SystemRoles.HealthUnitManager),
                "assign pilot manager role");
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join(", ", result.Errors.Select(error => error.Description));
        throw new InvalidOperationException($"Could not {operation}: {errors}");
    }

    private sealed record RoleDefinition(
        string Name,
        string DisplayName,
        string Description,
        string[] Permissions);
}

