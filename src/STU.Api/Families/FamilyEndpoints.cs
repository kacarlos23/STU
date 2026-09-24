using System.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using STU.Application.Security;
using STU.Domain.Auditing;
using STU.Domain.Families;
using STU.Domain.Properties;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.Api.Families;

public static partial class FamilyEndpoints
{
    public sealed record SaveFamilyRequest(string Number, string ResponsibleName, Guid? HealthUnitId, Guid? ExpectedVersion);
    public sealed record VersionRequest(Guid ExpectedVersion);
    public sealed record LinkRequest(Guid PropertyId, Guid ExpectedVersion, Guid ExpectedPropertyVersion);

    public static IEndpointRouteBuilder MapFamilyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var read = endpoints.MapGroup("/api/families").WithTags("Families").RequireAuthorization(StuPolicies.FamiliesView).RequireRateLimiting("api");
        read.MapGet("", ListAsync);
        read.MapGet("/{id:guid}", GetAsync);
        read.MapGet("/{id:guid}/history", HistoryAsync);
        read.MapGet("/available-properties", AvailableAsync).RequireAuthorization(StuPolicies.PropertiesView);
        var write = endpoints.MapGroup("/api/families").WithTags("Families").RequireAuthorization(StuPolicies.FamiliesManage).RequireRateLimiting("api");
        Secure(write.MapPost("", CreateAsync));
        Secure(write.MapPut("/{id:guid}", UpdateAsync));
        Secure(write.MapPost("/{id:guid}/archive", ArchiveAsync));
        Secure(write.MapPost("/{id:guid}/restore", RestoreAsync));
        Secure(write.MapPost("/{id:guid}/property", LinkAsync)).RequireAuthorization(StuPolicies.PropertiesView);
        Secure(write.MapPost("/{id:guid}/unlink", UnlinkAsync));
        MapVisits(endpoints);
        return endpoints;
    }

    private static RouteHandlerBuilder Secure(RouteHandlerBuilder route) => route.WithMetadata(new RequireAntiforgeryTokenAttribute(true));
    private static async Task<IResult> ListAsync(Guid? healthUnitId, string? query, string? state, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db, int page = 1, int pageSize = 50)
    {
        var actor = await ActorAsync(http, users);
        var unit = Unit(actor, http, healthUnitId);
        if (unit is null) return Results.Forbid();
        page = Math.Clamp(page, 1, 1_000_000); pageSize = Math.Clamp(pageSize, 1, 100);
        var families = FamilyAccess.Authorized(db, actor, http.User, unit.Value).AsNoTracking();
        if (state == "archived") families = families.Where(f => f.ArchivedAtUtc != null);
        else if (state != "all") families = families.Where(f => f.ArchivedAtUtc == null);
        if (state is "linked" or "unlinked") families = families.Where(f => db.FamilyPropertyLinks.Any(l => l.FamilyId == f.Id && l.EndedAtUtc == null) == (state == "linked"));
        else if (state is not (null or "" or "active" or "archived" or "all")) return Invalid("state", "Situação de família inválida.");
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = "%" + query.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            families = families.Where(f => EF.Functions.ILike(f.Number, term) || EF.Functions.ILike(f.ResponsibleName, term));
        }
        var total = await families.CountAsync();
        var items = await families.OrderBy(f => f.Number).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Results.Ok(new { items = await ProjectAsync(items, db), total, page, pageSize });
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var family = await FindAsync(id, http, users, db);
        return family is null ? Results.NotFound() : Results.Ok((await ProjectAsync([family], db)).Single());
    }

    private static async Task<IResult> HistoryAsync(Guid id, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        if (await FindAsync(id, http, users, db) is null) return Results.NotFound();
        var links = await (from l in db.FamilyPropertyLinks.AsNoTracking() join p in db.Properties.AsNoTracking() on l.PropertyId equals p.Id
            where l.FamilyId == id orderby l.StartedAtUtc descending
            select new { l.Id, l.PropertyId, p.Street, p.HouseNumber, l.StartedAtUtc, l.EndedAtUtc, l.CreatedByUserId, l.EndedByUserId, l.EndReason }).ToListAsync();
        var versions = await db.FamilyVersions.AsNoTracking().Where(v => v.FamilyId == id).OrderByDescending(v => v.VersionNumber).ToListAsync();
        return Results.Ok(new { links, versions });
    }

    private static async Task<IResult> CreateAsync(SaveFamilyRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var actor = await ActorAsync(http, users); var unit = Unit(actor, http, request.HealthUnitId);
        if (unit is null) return Results.Forbid();
        if (!await db.HealthUnits.AnyAsync(u => u.Id == unit && u.ArchivedAtUtc == null)) return Invalid("healthUnitId", "Selecione uma UBS ativa.");
        var error = await ValidateAsync(request, unit.Value, null, db); if (error is not null) return error;
        var family = Family.Create(unit.Value, request.Number, request.ResponsibleName, actor.Id);
        db.Families.Add(family); await CaptureAsync(family, "Create", actor, http, db);
        await db.SaveChangesAsync();
        return Results.Created($"/api/families/{family.Id}", new { family.Id, family.ConcurrencyToken });
    }

    private static async Task<IResult> UpdateAsync(Guid id, SaveFamilyRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var family = await FindAsync(id, http, users, db); if (family is null) return Results.NotFound();
        if (request.ExpectedVersion != family.ConcurrencyToken) return Stale();
        if (request.HealthUnitId.HasValue && request.HealthUnitId != family.HealthUnitId) return Invalid("healthUnitId", "A família não pode ser transferida para outra UBS por edição.");
        if (family.IsArchived) return Conflict("Família arquivada", "Reative a família antes de editar.");
        var error = await ValidateAsync(request, family.HealthUnitId, id, db); if (error is not null) return error;
        var actor = await ActorAsync(http, users); family.Update(request.Number, request.ResponsibleName, actor.Id);
        await CaptureAsync(family, "Update", actor, http, db); await db.SaveChangesAsync();
        return Results.Ok(new { family.ConcurrencyToken });
    }

    private static Task<IResult> ArchiveAsync(Guid id, VersionRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db) => SetArchiveAsync(id, request, true, http, users, db);
    private static Task<IResult> RestoreAsync(Guid id, VersionRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db) => SetArchiveAsync(id, request, false, http, users, db);
    private static async Task<IResult> SetArchiveAsync(Guid id, VersionRequest request, bool archive, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var family = await FindAsync(id, http, users, db); if (family is null) return Results.NotFound();
        if (request.ExpectedVersion != family.ConcurrencyToken) return Stale();
        if (archive && await db.FamilyPropertyLinks.AnyAsync(l => l.FamilyId == id && l.EndedAtUtc == null)) return Conflict("Família com imóvel", "Encerre o vínculo com o imóvel antes de arquivar a família.");
        if (!archive && !await db.HealthUnits.AnyAsync(u => u.Id == family.HealthUnitId && u.ArchivedAtUtc == null)) return Conflict("UBS arquivada", "Reative a UBS antes de reativar a família.");
        var actor = await ActorAsync(http, users); if (archive) family.Archive(actor.Id); else family.Restore(actor.Id);
        await CaptureAsync(family, archive ? "Archive" : "Restore", actor, http, db); await db.SaveChangesAsync();
        return Results.Ok(new { family.ConcurrencyToken });
    }

    private static async Task<IResult> AvailableAsync(Guid? healthUnitId, string? query, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db, int page = 1, int pageSize = 50)
    {
        var actor = await ActorAsync(http, users); var unit = Unit(actor, http, healthUnitId); if (unit is null) return Results.Forbid();
        var items = db.Properties.AsNoTracking().Where(p => p.HealthUnitId == unit && p.ArchivedAtUtc == null && p.RegistrationStatus == PropertyRegistrationStatus.Active &&
            !db.FamilyPropertyLinks.Any(l => l.PropertyId == p.Id && l.EndedAtUtc == null) && db.Microregions.Any(m => m.Id == p.MicroregionId && m.ArchivedAtUtc == null && m.Boundary.Covers(p.Geometry)));
        if (http.User.IsInRole(SystemRoles.HealthAgent)) items = items.Where(p => db.Microregions.Any(m => m.Id == p.MicroregionId && m.AssignedAgentId == actor.Id));
        if (!string.IsNullOrWhiteSpace(query)) { var term = "%" + query.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%"; items = items.Where(p => EF.Functions.ILike(p.Street, term) || EF.Functions.ILike(p.HouseNumber, term)); }
        page = Math.Clamp(page, 1, 1_000_000); pageSize = Math.Clamp(pageSize, 1, 100);
        return Results.Ok(new { total = await items.CountAsync(), page, pageSize, items = await items.OrderBy(p => p.Street).ThenBy(p => p.HouseNumber).Skip((page - 1) * pageSize).Take(pageSize).Select(p => new { p.Id, p.Street, p.HouseNumber, p.MicroregionId, p.ConcurrencyToken }).ToListAsync() });
    }

    private static async Task<IResult> LinkAsync(Guid id, LinkRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var family = await FindAsync(id, http, users, db); if (family is null) return Results.NotFound();
        if (family.ConcurrencyToken != request.ExpectedVersion) return Stale();
        if (family.IsArchived) return Conflict("Família arquivada", "Reative a família antes de vincular um imóvel.");
        var actor = await ActorAsync(http, users);
        var property = await db.Properties.SingleOrDefaultAsync(p => p.Id == request.PropertyId);
        if (property is null || !await FamilyAccess.CanUsePropertyAsync(property, actor, http.User, db)) return Results.NotFound();
        if (property.HealthUnitId != family.HealthUnitId) return Conflict("UBS diferentes", "Família e imóvel precisam pertencer à mesma UBS.");
        if (property.ConcurrencyToken != request.ExpectedPropertyVersion) return Stale();
        if (property.IsArchived || property.RegistrationStatus != PropertyRegistrationStatus.Active) return Conflict("Imóvel indisponível", "Selecione um imóvel ativo.");
        if (!await db.HealthUnits.AnyAsync(u => u.Id == family.HealthUnitId && u.ArchivedAtUtc == null) ||
            !await db.Microregions.AnyAsync(m => m.Id == property.MicroregionId && m.HealthUnitId == family.HealthUnitId && m.ArchivedAtUtc == null && m.Boundary.Covers(property.Geometry)))
            return Conflict("Vínculo territorial inválido", "O imóvel precisa estar dentro de uma microrregião ativa da UBS.");
        if (await db.FamilyPropertyLinks.AnyAsync(l => l.PropertyId == property.Id && l.EndedAtUtc == null)) return Conflict("Imóvel com família", "Este imóvel já possui uma família. Escolha um imóvel disponível.");
        var previous = await db.FamilyPropertyLinks.SingleOrDefaultAsync(l => l.FamilyId == id && l.EndedAtUtc == null);
        if (previous is not null)
        {
            previous.End(actor.Id, "Move");
            (await db.Properties.SingleAsync(p => p.Id == previous.PropertyId)).Touch();
        }
        family.Touch(actor.Id); property.Touch();
        // Release the partial unique index before inserting, inside the same transaction.
        await db.SaveChangesAsync();
        var link = FamilyPropertyLink.Create(family.HealthUnitId, id, property.Id, actor.Id);
        db.FamilyPropertyLinks.Add(link); await CaptureAsync(family, previous is null ? "Link" : "Move", actor, http, db);
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return Results.Ok(new { family.ConcurrencyToken, linkId = link.Id });
    }

    private static async Task<IResult> UnlinkAsync(Guid id, VersionRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var family = await FindAsync(id, http, users, db); if (family is null) return Results.NotFound();
        if (family.ConcurrencyToken != request.ExpectedVersion) return Stale();
        var link = await db.FamilyPropertyLinks.SingleOrDefaultAsync(l => l.FamilyId == id && l.EndedAtUtc == null);
        if (link is null) return Conflict("Família sem imóvel", "A família já está sem vínculo atual.");
        var actor = await ActorAsync(http, users); link.End(actor.Id); family.Touch(actor.Id);
        (await db.Properties.SingleAsync(p => p.Id == link.PropertyId)).Touch();
        await CaptureAsync(family, "Unlink", actor, http, db); await db.SaveChangesAsync(); await transaction.CommitAsync();
        return Results.Ok(new { family.ConcurrencyToken });
    }

    private static async Task<List<object>> ProjectAsync(List<Family> families, StuDbContext db)
    {
        var ids = families.Select(f => f.Id).ToArray();
        var links = await (from l in db.FamilyPropertyLinks.AsNoTracking() join p in db.Properties.AsNoTracking() on l.PropertyId equals p.Id where ids.Contains(l.FamilyId) && l.EndedAtUtc == null
            select new { l.FamilyId, l.Id, l.StartedAtUtc, Property = p }).ToDictionaryAsync(l => l.FamilyId);
        var visits = await db.PropertyVisits.AsNoTracking().Where(v => ids.Contains(v.FamilyId) && v.ArchivedAtUtc == null).GroupBy(v => v.FamilyId).Select(g => new { Id = g.Key, Last = g.Max(v => v.VisitedAtUtc) }).ToDictionaryAsync(v => v.Id, v => v.Last);
        var microIds = links.Values.Select(l => l.Property.MicroregionId).ToArray();
        var rules = await db.CoverageRules.Where(r => microIds.Contains(r.MicroregionId)).ToDictionaryAsync(r => r.MicroregionId, r => r.MaxDaysWithoutVisit);
        return families.Select(f => {
            var link = links.GetValueOrDefault(f.Id); DateTimeOffset? last = visits.TryGetValue(f.Id, out var date) ? date : null;
            return (object)new { f.Id, f.HealthUnitId, f.Number, f.ResponsibleName, f.ConcurrencyToken, f.ArchivedAtUtc, f.CreatedAtUtc, f.UpdatedAtUtc, f.CreatedByUserId, f.UpdatedByUserId,
                state = f.IsArchived ? "archived" : link is null ? "unlinked" : "linked", lastVisitAtUtc = last,
                coverageStatus = f.IsArchived ? "archived" : link is null ? "noProperty" : FamilyAccess.Coverage(last, rules.GetValueOrDefault(link.Property.MicroregionId)),
                currentProperty = link is null ? null : new { link.Property.Id, link.Property.Street, link.Property.HouseNumber, situation = link.Property.Situation.ToString(), link.Property.MicroregionId, link.Property.ConcurrencyToken, linkId = link.Id, link.StartedAtUtc } };
        }).ToList();
    }

    internal static async Task<Family?> FindAsync(Guid id, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var actor = await ActorAsync(http, users);
        var unitId = await db.Families.Where(f => f.Id == id).Select(f => (Guid?)f.HealthUnitId).SingleOrDefaultAsync();
        return unitId is null ? null : await FamilyAccess.Authorized(db, actor, http.User, unitId.Value).SingleOrDefaultAsync(f => f.Id == id);
    }
    private static Guid? Unit(ApplicationUser actor, HttpContext http, Guid? requested) => http.User.IsInRole(SystemRoles.GlobalAdministrator) ? requested ?? actor.HealthUnitId : requested.HasValue && requested != actor.HealthUnitId ? null : actor.HealthUnitId;
    private static async Task<IResult?> ValidateAsync(SaveFamilyRequest request, Guid unit, Guid? id, StuDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.Number) || request.Number.Trim().Length > 32 || request.Number.Any(char.IsControl)) return Invalid("number", "Informe o número da família em uma linha, com até 32 caracteres.");
        if (string.IsNullOrWhiteSpace(request.ResponsibleName) || request.ResponsibleName.Trim().Length > 120 || request.ResponsibleName.Any(char.IsControl)) return Invalid("responsibleName", "Informe o nome do responsável em uma linha, com até 120 caracteres.");
        var number = request.Number.Trim().ToUpperInvariant();
        return await db.Families.AnyAsync(f => f.HealthUnitId == unit && f.Id != id && f.Number == number) ? Conflict("Número familiar já utilizado", "Este número já identifica uma família da UBS, inclusive se estiver arquivada.") : null;
    }
    private static async Task CaptureAsync(Family family, string action, ApplicationUser actor, HttpContext http, StuDbContext db)
    {
        var version = (await db.FamilyVersions.Where(v => v.FamilyId == family.Id).MaxAsync(v => (int?)v.VersionNumber) ?? 0) + 1;
        db.FamilyVersions.Add(FamilyVersion.Capture(family, version, action, actor.Id));
        db.AuditEntries.Add(AuditEntry.Create(actor.Id, actor.UserName ?? actor.DisplayName, action, "Family", family.Id.ToString(), "Cadastro familiar atualizado; detalhes no histórico autorizado.", null, null, http.Connection.RemoteIpAddress?.ToString()));
    }
    private static async Task<ApplicationUser> ActorAsync(HttpContext http, UserManager<ApplicationUser> users) => await users.GetUserAsync(http.User) ?? throw new InvalidOperationException("Usuário não encontrado.");
    private static IResult Stale() => Conflict("Cadastro alterado", "Recarregue a ficha: outro usuário alterou a família ou o imóvel.");
    private static IResult Conflict(string title, string detail) => Results.Problem(statusCode: 409, title: title, detail: detail);
    private static IResult Invalid(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
