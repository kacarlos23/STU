using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using STU.Application.Security;
using STU.Domain.Families;
using STU.Domain.Properties;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.Api.Families;

internal static class FamilyAccess
{
    internal static bool Has(ClaimsPrincipal principal, string permission) =>
        principal.HasClaim(StuClaimTypes.Permission, StuPermissions.All) || principal.HasClaim(StuClaimTypes.Permission, permission);

    internal static IQueryable<Family> Authorized(StuDbContext db, ApplicationUser actor, ClaimsPrincipal principal, Guid unitId)
    {
        var query = db.Families.Where(f => f.HealthUnitId == unitId);
        if (!principal.IsInRole(SystemRoles.GlobalAdministrator) && actor.HealthUnitId != unitId)
            return query.Where(_ => false);
        if (principal.IsInRole(SystemRoles.HealthAgent))
            query = query.Where(f => !db.FamilyPropertyLinks.Any(l => l.FamilyId == f.Id && l.EndedAtUtc == null) ||
                db.FamilyPropertyLinks.Any(l => l.FamilyId == f.Id && l.EndedAtUtc == null &&
                    db.Properties.Any(p => p.Id == l.PropertyId && db.Microregions.Any(m => m.Id == p.MicroregionId && m.AssignedAgentId == actor.Id && m.ArchivedAtUtc == null))));
        return query;
    }

    internal static async Task<Dictionary<Guid, CurrentFamily>> CurrentByPropertyAsync(IEnumerable<Guid> propertyIds, StuDbContext db)
    {
        var ids = propertyIds.ToArray();
        var rows = await (from link in db.FamilyPropertyLinks.AsNoTracking()
            join family in db.Families.AsNoTracking() on link.FamilyId equals family.Id
            where ids.Contains(link.PropertyId) && link.EndedAtUtc == null && family.ArchivedAtUtc == null
            select new { link.PropertyId, link.Id, link.StartedAtUtc, Family = family }).ToListAsync();
        var familyIds = rows.Select(r => r.Family.Id).ToArray();
        var visits = await db.PropertyVisits.AsNoTracking().Where(v => familyIds.Contains(v.FamilyId) && v.ArchivedAtUtc == null)
            .GroupBy(v => v.FamilyId).Select(g => new { Id = g.Key, Last = g.Max(v => v.VisitedAtUtc) }).ToDictionaryAsync(r => r.Id, r => r.Last);
        return rows.ToDictionary(r => r.PropertyId, r => new CurrentFamily(r.Family.Id, r.Family.Number, r.Family.ResponsibleName,
            r.Id, r.StartedAtUtc, r.Family.ConcurrencyToken, visits.TryGetValue(r.Family.Id, out var date) ? date : null));
    }

    internal static string Coverage(DateTimeOffset? last, int days) => days <= 0 ? "notConfigured" :
        !last.HasValue ? "neverVisited" : last.Value.AddDays(days) < DateTimeOffset.UtcNow ? "overdue" : "covered";

    internal static async Task<bool> CanUsePropertyAsync(HealthProperty property, ApplicationUser actor, ClaimsPrincipal principal, StuDbContext db) =>
        (principal.IsInRole(SystemRoles.GlobalAdministrator) || actor.HealthUnitId == property.HealthUnitId) &&
        (!principal.IsInRole(SystemRoles.HealthAgent) || await db.Microregions.AnyAsync(m => m.Id == property.MicroregionId && m.AssignedAgentId == actor.Id && m.ArchivedAtUtc == null));

    internal sealed record CurrentFamily(Guid Id, string Number, string ResponsibleName, Guid LinkId, DateTimeOffset StartedAtUtc, Guid ConcurrencyToken, DateTimeOffset? LastVisitAtUtc);
}
