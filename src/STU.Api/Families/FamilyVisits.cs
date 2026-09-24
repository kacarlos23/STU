using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using STU.Api.Properties;
using STU.Application.Security;
using STU.Domain.Auditing;
using STU.Domain.Properties;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.Api.Families;

public static partial class FamilyEndpoints
{
    private static void MapVisits(IEndpointRouteBuilder endpoints)
    {
        var read = endpoints.MapGroup("/api/families/{familyId:guid}/visits").WithTags("Family visits")
            .RequireAuthorization(StuPolicies.FamiliesView, StuPolicies.VisitsView).RequireRateLimiting("api");
        read.MapGet("", GetVisitsAsync);
        var write = endpoints.MapGroup("/api/families/{familyId:guid}/visits").WithTags("Family visits")
            .RequireAuthorization(StuPolicies.FamiliesView, StuPolicies.VisitsManage).RequireRateLimiting("api");
        Secure(write.MapPost("", CreateVisitAsync));
        Secure(write.MapPut("/{visitId:guid}", UpdateVisitAsync));
        Secure(write.MapPost("/{visitId:guid}/archive", ArchiveVisitAsync));
        Secure(write.MapPost("/{visitId:guid}/restore", RestoreVisitAsync));
    }

    private static async Task<IResult> GetVisitsAsync(Guid familyId, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        if (await FindAsync(familyId, http, users, db) is null) return Results.NotFound();
        return Results.Ok(await (from v in db.PropertyVisits.AsNoTracking()
            join p in db.Properties.AsNoTracking() on v.PropertyId equals p.Id
            join a in db.Users.AsNoTracking() on v.AgentId equals a.Id
            where v.FamilyId == familyId orderby v.VisitedAtUtc descending
            select new { v.Id, v.FamilyId, v.PropertyId, p.Street, p.HouseNumber, v.VisitedAtUtc, v.Type, v.Outcome, v.ObservedSituation,
                v.AccessDifficulty, v.Note, v.ArchivedAtUtc, v.ConcurrencyToken, agentId = a.Id, agentName = a.DisplayName }).ToListAsync());
    }

    internal static async Task<IResult> CreateVisitAsync(Guid familyId, SaveVisitRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var family = await FindAsync(familyId, http, users, db); if (family is null) return Results.NotFound();
        if (family.IsArchived) return Conflict("Família arquivada", "Reative a família antes de registrar visitas.");
        var link = await db.FamilyPropertyLinks.SingleOrDefaultAsync(l => l.FamilyId == familyId && l.EndedAtUtc == null);
        if (link is null) return Conflict("Família sem imóvel", "Vincule um imóvel antes de registrar a visita.");
        // The form must still refer to the same residence when it is submitted.
        if (request.ExpectedFamilyVersion != family.ConcurrencyToken) return Stale();
        var property = await db.Properties.SingleAsync(p => p.Id == link.PropertyId);
        var actor = await ActorAsync(http, users);
        if (property.IsArchived || !await FamilyAccess.CanUsePropertyAsync(property, actor, http.User, db)) return Results.Forbid();
        var error = PropertyEndpoints.ValidateVisit(request, out var type, out var outcome, out var situation); if (error is not null) return error;
        var visit = PropertyVisit.Create(familyId, property.Id, family.HealthUnitId, actor.Id, request.VisitedAtUtc, type, outcome, situation, request.AccessDifficulty, request.Note);
        db.PropertyVisits.Add(visit); family.Touch(actor.Id);
        AuditVisit(db, http, actor, visit.Id, "Create");
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return Results.Created($"/api/families/{familyId}/visits/{visit.Id}", new { visit.Id, visit.ConcurrencyToken });
    }

    private static async Task<IResult> UpdateVisitAsync(Guid familyId, Guid visitId, SaveVisitRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        if (await FindAsync(familyId, http, users, db) is null) return Results.NotFound();
        var visit = await db.PropertyVisits.SingleOrDefaultAsync(v => v.Id == visitId && v.FamilyId == familyId);
        if (visit is null) return Results.NotFound();
        if (visit.ConcurrencyToken != request.ExpectedVersion) return Stale();
        var error = PropertyEndpoints.ValidateVisit(request, out var type, out var outcome, out var situation); if (error is not null) return error;
        visit.Update(request.VisitedAtUtc, type, outcome, situation, request.AccessDifficulty, request.Note);
        AuditVisit(db, http, await ActorAsync(http, users), visit.Id, "Update"); await db.SaveChangesAsync();
        return Results.Ok(new { visit.ConcurrencyToken });
    }

    private static Task<IResult> ArchiveVisitAsync(Guid familyId, Guid visitId, VersionRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db) => SetVisitArchiveAsync(familyId, visitId, request, true, http, users, db);
    private static Task<IResult> RestoreVisitAsync(Guid familyId, Guid visitId, VersionRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db) => SetVisitArchiveAsync(familyId, visitId, request, false, http, users, db);
    private static async Task<IResult> SetVisitArchiveAsync(Guid familyId, Guid visitId, VersionRequest request, bool archive, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        if (await FindAsync(familyId, http, users, db) is null) return Results.NotFound();
        var visit = await db.PropertyVisits.SingleOrDefaultAsync(v => v.Id == visitId && v.FamilyId == familyId);
        if (visit is null) return Results.NotFound();
        if (visit.ConcurrencyToken != request.ExpectedVersion) return Stale();
        if (archive) visit.Archive(); else visit.Restore();
        AuditVisit(db, http, await ActorAsync(http, users), visit.Id, archive ? "Archive" : "Restore"); await db.SaveChangesAsync();
        return Results.Ok(new { visit.ConcurrencyToken });
    }

    private static void AuditVisit(StuDbContext db, HttpContext http, ApplicationUser actor, Guid id, string action) =>
        db.AuditEntries.Add(AuditEntry.Create(actor.Id, actor.UserName ?? actor.DisplayName, action, "PropertyVisit", id.ToString(), "Visita familiar atualizada; imóvel da visita preservado.", null, null, http.Connection.RemoteIpAddress?.ToString()));
}
