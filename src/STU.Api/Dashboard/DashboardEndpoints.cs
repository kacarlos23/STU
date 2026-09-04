using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using STU.Application.Security;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.Api.Dashboard;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dashboard/summary", GetMainSummaryAsync)
            .WithTags("Dashboard")
            .RequireAuthorization(StuPolicies.PasswordChanged)
            .RequireRateLimiting("api");

        endpoints.MapGet("/api/admin/overview", GetAdminOverviewAsync)
            .WithTags("Global administration")
            .RequireAuthorization(StuPolicies.GlobalAdministration)
            .RequireRateLimiting("api");

        return endpoints;
    }

    private static async Task<IResult> GetMainSummaryAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        StuDbContext dbContext)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null || user.IsArchived)
        {
            return Results.Unauthorized();
        }

        var healthUnitName = user.HealthUnitId.HasValue
            ? await dbContext.HealthUnits
                .Where(unit => unit.Id == user.HealthUnitId.Value)
                .Select(unit => unit.Name)
                .SingleOrDefaultAsync()
            : null;

        var scopedMicroregionIds = user.HealthUnitId.HasValue
            ? await dbContext.Microregions.AsNoTracking()
                .Where(item => item.HealthUnitId == user.HealthUnitId.Value && item.ArchivedAtUtc == null)
                .Select(item => item.Id).ToArrayAsync()
            : [];
        if (await userManager.IsInRoleAsync(user, SystemRoles.HealthAgent))
        {
            scopedMicroregionIds = await dbContext.Microregions.AsNoTracking()
                .Where(item => item.HealthUnitId == user.HealthUnitId && item.ArchivedAtUtc == null && item.AssignedAgentId == user.Id)
                .Select(item => item.Id).ToArrayAsync();
        }

        var unassignedMicroregions = user.HealthUnitId.HasValue
            ? await dbContext.Microregions.CountAsync(item => item.HealthUnitId == user.HealthUnitId.Value && item.ArchivedAtUtc == null && item.AssignedAgentId == null)
            : 0;
        var activeMicroregions = scopedMicroregionIds.Length;
        var propertyRows = await dbContext.Properties.AsNoTracking()
            .Where(item => item.ArchivedAtUtc == null && item.RegistrationStatus == STU.Domain.Properties.PropertyRegistrationStatus.Active && scopedMicroregionIds.Contains(item.MicroregionId))
            .Select(item => new { item.Id, item.MicroregionId }).ToListAsync();
        var propertyIds = propertyRows.Select(item => item.Id).ToArray();
        var monthStart = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var visitsThisMonth = await dbContext.PropertyVisits.CountAsync(item => propertyIds.Contains(item.PropertyId) && item.ArchivedAtUtc == null && item.VisitedAtUtc >= monthStart);
        var lastVisits = await dbContext.PropertyVisits.AsNoTracking()
            .Where(item => propertyIds.Contains(item.PropertyId) && item.ArchivedAtUtc == null)
            .GroupBy(item => item.PropertyId)
            .Select(group => new { PropertyId = group.Key, LastVisit = group.Max(item => item.VisitedAtUtc) })
            .ToDictionaryAsync(item => item.PropertyId, item => item.LastVisit);
        var coverageRules = await dbContext.CoverageRules.AsNoTracking()
            .Where(item => scopedMicroregionIds.Contains(item.MicroregionId))
            .ToDictionaryAsync(item => item.MicroregionId, item => item.MaxDaysWithoutVisit);
        var now = DateTimeOffset.UtcNow;
        var coverageAlerts = propertyRows.Count(item => coverageRules.TryGetValue(item.MicroregionId, out var days)
            && (!lastVisits.TryGetValue(item.Id, out var lastVisit) || lastVisit.AddDays(days) < now));

        return Results.Ok(new
        {
            healthUnitName,
            activeProperties = propertyRows.Count,
            visitsThisMonth,
            coverageAlerts,
            unassignedMicroregions,
            stage = activeMicroregions == 0 ? "Cadastre os primeiros bairros e microrregiões" : $"{activeMicroregions} microrregião(ões) ativa(s) no mapa",
        });
    }

    private static async Task<IResult> GetAdminOverviewAsync(StuDbContext dbContext)
    {
        var activeHealthUnits = await dbContext.HealthUnits.CountAsync(unit => unit.ArchivedAtUtc == null);
        var activeUsers = await dbContext.Users.CountAsync(user => user.ArchivedAtUtc == null);
        var pendingPasswordChanges = await dbContext.Users.CountAsync(user =>
            user.ArchivedAtUtc == null && user.MustChangePassword);
        var activeRoles = await dbContext.Roles.CountAsync(role => role.ArchivedAtUtc == null);

        return Results.Ok(new
        {
            activeHealthUnits,
            activeUsers,
            pendingPasswordChanges,
            activeRoles,
        });
    }
}
