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
        StuDbContext dbContext,
        Guid? healthUnitId)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null || user.IsArchived)
        {
            return Results.Unauthorized();
        }

        var isGlobalAdministrator = await userManager.IsInRoleAsync(user, SystemRoles.GlobalAdministrator);
        if (!isGlobalAdministrator && healthUnitId.HasValue && healthUnitId != user.HealthUnitId)
        {
            return Results.Forbid();
        }

        var selectedHealthUnitId = isGlobalAdministrator
            ? healthUnitId ?? user.HealthUnitId
            : user.HealthUnitId;
        var healthUnitName = selectedHealthUnitId.HasValue
            ? await dbContext.HealthUnits
                .Where(unit => unit.Id == selectedHealthUnitId.Value && unit.ArchivedAtUtc == null)
                .Select(unit => unit.Name)
                .SingleOrDefaultAsync()
            : null;

        var scopedMicroregions = selectedHealthUnitId.HasValue
            ? await dbContext.Microregions.AsNoTracking()
                .Where(item => item.HealthUnitId == selectedHealthUnitId.Value && item.ArchivedAtUtc == null)
                .Select(item => new { item.Id, item.Code, item.Name, item.Color, item.AssignedAgentId })
                .ToListAsync()
            : [];
        if (await userManager.IsInRoleAsync(user, SystemRoles.HealthAgent))
        {
            scopedMicroregions = scopedMicroregions
                .Where(item => item.AssignedAgentId == user.Id)
                .ToList();
        }

        var scopedMicroregionIds = scopedMicroregions.Select(item => item.Id).ToArray();
        var unassignedMicroregions = selectedHealthUnitId.HasValue
            ? await dbContext.Microregions.CountAsync(item => item.HealthUnitId == selectedHealthUnitId.Value && item.ArchivedAtUtc == null && item.AssignedAgentId == null)
            : 0;
        var activeMicroregions = scopedMicroregions.Count;
        var propertyRows = await dbContext.Properties.AsNoTracking()
            .Where(item => item.ArchivedAtUtc == null && item.RegistrationStatus == STU.Domain.Properties.PropertyRegistrationStatus.Active && scopedMicroregionIds.Contains(item.MicroregionId))
            .Select(item => new { item.Id, item.MicroregionId, item.FamilyNumber }).ToListAsync();
        var propertyIds = propertyRows.Select(item => item.Id).ToArray();
        var monthStart = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var previousMonthStart = monthStart.AddMonths(-1);
        var visitsThisMonth = await dbContext.PropertyVisits.CountAsync(item => propertyIds.Contains(item.PropertyId) && item.ArchivedAtUtc == null && item.VisitedAtUtc >= monthStart);
        var visitsPreviousMonth = await dbContext.PropertyVisits.CountAsync(item => propertyIds.Contains(item.PropertyId) && item.ArchivedAtUtc == null && item.VisitedAtUtc >= previousMonthStart && item.VisitedAtUtc < monthStart);
        int? visitsChangePercent = visitsPreviousMonth == 0
            ? null
            : (int)Math.Round((visitsThisMonth - visitsPreviousMonth) * 100d / visitsPreviousMonth, MidpointRounding.AwayFromZero);
        var lastVisits = await dbContext.PropertyVisits.AsNoTracking()
            .Where(item => propertyIds.Contains(item.PropertyId) && item.ArchivedAtUtc == null)
            .GroupBy(item => item.PropertyId)
            .Select(group => new { PropertyId = group.Key, LastVisit = group.Max(item => item.VisitedAtUtc) })
            .ToDictionaryAsync(item => item.PropertyId, item => item.LastVisit);
        var coverageRules = await dbContext.CoverageRules.AsNoTracking()
            .Where(item => scopedMicroregionIds.Contains(item.MicroregionId))
            .ToDictionaryAsync(item => item.MicroregionId, item => item.MaxDaysWithoutVisit);
        var now = DateTimeOffset.UtcNow;
        var coverageRows = propertyRows.Select(item => new
        {
            Property = item,
            Status = CoverageStatus(item.Id, item.MicroregionId, lastVisits, coverageRules, now),
        }).ToList();
        var coverage = new
        {
            covered = coverageRows.Count(item => item.Status == "covered"),
            overdue = coverageRows.Count(item => item.Status == "overdue"),
            neverVisited = coverageRows.Count(item => item.Status == "neverVisited"),
            notConfigured = coverageRows.Count(item => item.Status == "notConfigured"),
        };
        var coverageAlerts = coverage.overdue + coverage.neverVisited;
        var microregions = scopedMicroregions
            .OrderBy(item => item.Code)
            .Select(item =>
            {
                var rows = coverageRows.Where(row => row.Property.MicroregionId == item.Id).ToList();
                return new
                {
                    item.Id,
                    item.Code,
                    item.Name,
                    item.Color,
                    assigned = item.AssignedAgentId.HasValue,
                    activeProperties = rows.Count,
                    covered = rows.Count(row => row.Status == "covered"),
                    alerts = rows.Count(row => row.Status is "overdue" or "neverVisited"),
                };
            }).ToList();

        return Results.Ok(new
        {
            healthUnitName,
            activeProperties = propertyRows.Count,
            activeFamilyIdentifiers = propertyRows.Select(item => item.FamilyNumber).Distinct().Count(),
            visitsThisMonth,
            visitsPreviousMonth,
            visitsChangePercent,
            coverageAlerts,
            unassignedMicroregions,
            coverage,
            microregions,
            stage = activeMicroregions == 0 ? "Cadastre os primeiros bairros e microrregiões" : $"{activeMicroregions} microrregião(ões) ativa(s) no mapa",
        });
    }

    private static string CoverageStatus(
        Guid propertyId,
        Guid microregionId,
        Dictionary<Guid, DateTimeOffset> lastVisits,
        Dictionary<Guid, int> coverageRules,
        DateTimeOffset now)
    {
        if (!coverageRules.TryGetValue(microregionId, out var days)) return "notConfigured";
        if (!lastVisits.TryGetValue(propertyId, out var lastVisit)) return "neverVisited";
        return lastVisit.AddDays(days) < now ? "overdue" : "covered";
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
