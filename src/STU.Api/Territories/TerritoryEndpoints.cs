using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;
using NetTopologySuite.Operation.Union;
using STU.Application.Security;
using STU.Domain.Auditing;
using STU.Domain.Territories;
using STU.Domain.Operations;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.Api.Territories;

public static class TerritoryEndpoints
{
    private static readonly JsonSerializerOptions GeometryJsonOptions = CreateGeometryOptions();

    public static IEndpointRouteBuilder MapTerritoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var read = endpoints.MapGroup("/api/territories").WithTags("Territories").RequireAuthorization(StuPolicies.MapView).RequireRateLimiting("api");
        read.MapGet("/map", GetMapAsync);
        read.MapGet("/reference-data", GetReferenceDataAsync);
        read.MapGet("/microregions/{id:guid}/versions", GetVersionsAsync);

        var archiveRead = endpoints.MapGroup("/api/territories").WithTags("Territories").RequireAuthorization(StuPolicies.TerritoryManage).RequireRateLimiting("api");
        archiveRead.MapGet("/microregions/archived", GetArchivedMicroregionsAsync);

        var write = endpoints.MapGroup("/api/territories").WithTags("Territories").RequireAuthorization(StuPolicies.TerritoryManage).RequireRateLimiting("api");
        Secure(write.MapPost("/neighborhoods", CreateNeighborhoodAsync));
        Secure(write.MapPut("/neighborhoods/{id:guid}", UpdateNeighborhoodAsync));
        Secure(write.MapPost("/neighborhoods/{id:guid}/archive", ArchiveNeighborhoodAsync));
        Secure(write.MapPost("/neighborhoods/{id:guid}/restore", RestoreNeighborhoodAsync));
        Secure(write.MapPost("/microregions/preview", PreviewMicroregionAsync));
        Secure(write.MapPost("/microregions", CreateMicroregionAsync));
        Secure(write.MapPut("/microregions/{id:guid}", UpdateMicroregionAsync));
        Secure(write.MapPost("/microregions/{id:guid}/archive", ArchiveMicroregionAsync));
        Secure(write.MapPost("/microregions/{id:guid}/restore", RestoreMicroregionAsync));
        return endpoints;
    }

    private static RouteHandlerBuilder Secure(RouteHandlerBuilder route) => route.WithMetadata(new RequireAntiforgeryTokenAttribute(true));

    private static async Task<IResult> GetMapAsync(Guid? healthUnitId, DateTimeOffset? atUtc, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var actor = await GetActorAsync(http, users);
        var scope = ResolveScope(actor, http.User, healthUnitId);
        if (scope.Error is not null) return scope.Error;

        if (atUtc.HasValue)
        {
            var versions = await db.MicroregionVersions.AsNoTracking()
                .Where(v => v.HealthUnitId == scope.HealthUnitId && v.ChangedAtUtc <= atUtc.Value)
                .OrderBy(v => v.MicroregionId).ThenByDescending(v => v.VersionNumber).ToListAsync();
            var latest = versions.GroupBy(v => v.MicroregionId).Select(g => g.First()).Where(v => !v.IsArchived).ToList();
            var neighborhoodIds = latest.SelectMany(v => v.NeighborhoodIds).Distinct().ToArray();
            var neighborhoodVersions = await db.NeighborhoodVersions.AsNoTracking()
                .Where(v => neighborhoodIds.Contains(v.NeighborhoodId) && v.ChangedAtUtc <= atUtc.Value)
                .OrderBy(v => v.NeighborhoodId).ThenByDescending(v => v.VersionNumber).ToListAsync();
            var latestNeighborhoods = neighborhoodVersions.GroupBy(v => v.NeighborhoodId).Select(g => g.First()).Where(v => !v.IsArchived);
            return Results.Ok(FeatureCollection(
                latestNeighborhoods.Select(v => NeighborhoodFeature(v.NeighborhoodId, v.Name, v.Geometry, v.Source.ToString(), v.Color, v.VersionNumber, null))
                    .Concat(latest.Select(v => MicroregionFeature(v.MicroregionId, v.Code, v.Name, v.NeighborhoodIds, v.HealthUnitId, v.AssignedAgentId, v.Boundary, v.Source.ToString(), v.Color, v.VersionNumber, null)))));
        }

        var microregions = await db.Microregions.AsNoTracking().Where(item => item.HealthUnitId == scope.HealthUnitId && item.ArchivedAtUtc == null).OrderBy(item => item.Code).ToListAsync();
        var microregionIds = microregions.Select(item => item.Id).ToArray();
        var neighborhoodLinks = await db.MicroregionNeighborhoods.AsNoTracking()
            .Where(item => microregionIds.Contains(item.MicroregionId)).ToListAsync();
        var neighborhoodIdsByMicroregion = neighborhoodLinks.GroupBy(item => item.MicroregionId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.NeighborhoodId).Order().ToArray());
        var neighborhoods = await db.Neighborhoods.AsNoTracking().Where(item => item.ArchivedAtUtc == null).OrderBy(item => item.Name).ToListAsync();
        return Results.Ok(FeatureCollection(
            neighborhoods.Select(item => NeighborhoodFeature(item.Id, item.Name, item.Geometry, item.Source.ToString(), item.Color, null, item.ConcurrencyToken))
                .Concat(microregions.Select(item => MicroregionFeature(item.Id, item.Code, item.Name, neighborhoodIdsByMicroregion.GetValueOrDefault(item.Id) ?? [], item.HealthUnitId, item.AssignedAgentId, item.Boundary, item.Source.ToString(), item.Color, null, item.ConcurrencyToken)))));
    }

    private static async Task<IResult> GetReferenceDataAsync(Guid? healthUnitId, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var actor = await GetActorAsync(http, users); var scope = ResolveScope(actor, http.User, healthUnitId); if (scope.Error is not null) return scope.Error;
        var neighborhoods = await db.Neighborhoods.AsNoTracking().Where(item => item.ArchivedAtUtc == null).OrderBy(item => item.Name)
            .Select(item => new { item.Id, item.Name, source = item.Source.ToString(), item.ExternalReference, item.Color, geometry = item.Geometry, item.ConcurrencyToken }).ToListAsync();
        var agents = await (from user in db.Users.AsNoTracking()
                            join link in db.UserRoles on user.Id equals link.UserId
                            join role in db.Roles on link.RoleId equals role.Id
                            where user.HealthUnitId == scope.HealthUnitId && user.ArchivedAtUtc == null && role.Name == SystemRoles.HealthAgent
                            orderby user.DisplayName select new { user.Id, user.DisplayName }).ToListAsync();
        var units = http.User.IsInRole(SystemRoles.GlobalAdministrator)
            ? await db.HealthUnits.AsNoTracking().Where(item => item.ArchivedAtUtc == null).OrderBy(item => item.Name).Select(item => new { item.Id, item.Code, item.Name }).ToListAsync()
            : await db.HealthUnits.AsNoTracking().Where(item => item.Id == scope.HealthUnitId).Select(item => new { item.Id, item.Code, item.Name }).ToListAsync();
        return Results.Ok(new { selectedHealthUnitId = scope.HealthUnitId, neighborhoods, agents, healthUnits = units });
    }

    private static async Task<IResult> GetArchivedMicroregionsAsync(Guid? healthUnitId, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var actor = await GetActorAsync(http, users);
        var scope = ResolveScope(actor, http.User, healthUnitId);
        if (scope.Error is not null) return scope.Error;

        var microregions = await db.Microregions.AsNoTracking()
            .Where(item => item.HealthUnitId == scope.HealthUnitId && item.ArchivedAtUtc != null)
            .OrderBy(item => item.Code)
            .ToListAsync();
        var microregionIds = microregions.Select(item => item.Id).ToArray();
        var neighborhoodLinks = await db.MicroregionNeighborhoods.AsNoTracking()
            .Where(item => microregionIds.Contains(item.MicroregionId))
            .ToListAsync();
        var neighborhoodIdsByMicroregion = neighborhoodLinks.GroupBy(item => item.MicroregionId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.NeighborhoodId).Order().ToArray());

        return Results.Ok(FeatureCollection(microregions.Select(item =>
            MicroregionFeature(
                item.Id,
                item.Code,
                item.Name,
                neighborhoodIdsByMicroregion.GetValueOrDefault(item.Id) ?? [],
                item.HealthUnitId,
                item.AssignedAgentId,
                item.Boundary,
                item.Source.ToString(),
                item.Color,
                null,
                item.ConcurrencyToken,
                item.ArchivedAtUtc))));
    }

    private static async Task<IResult> GetVersionsAsync(Guid id, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var actor = await GetActorAsync(http, users);
        var current = await db.Microregions.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id);
        if (current is null) return Results.NotFound();
        var scope = ResolveScope(actor, http.User, current.HealthUnitId); if (scope.Error is not null || scope.HealthUnitId != current.HealthUnitId) return Results.Forbid();
        var versions = await db.MicroregionVersions.AsNoTracking().Where(item => item.MicroregionId == id).OrderByDescending(item => item.VersionNumber)
            .Select(item => new { item.VersionNumber, item.ChangeKind, item.ChangedAtUtc, item.ChangedByUserId, item.Code, item.Name, item.NeighborhoodIds, item.HealthUnitId, item.AssignedAgentId, item.Color, item.IsArchived, geometry = item.Boundary }).ToListAsync();
        return Results.Ok(versions);
    }

    private static async Task<IResult> CreateNeighborhoodAsync(SaveNeighborhoodRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var error = ParseNeighborhood(request, out var geometry, out var source, out var color); if (error is not null) return error;
        if (await db.Neighborhoods.AnyAsync(item => EF.Functions.ILike(item.Name, request.Name.Trim()) && item.ArchivedAtUtc == null)) return Conflict("Bairro já cadastrado", "Já existe um bairro ativo com este nome.");
        var fit = await FitNeighborhoodAsync(geometry!, null, db); if (fit.IsEmpty) return Validation("geometry", "A área informada está totalmente coberta por bairros existentes.");
        var actor = await GetActorAsync(http, users); var item = Neighborhood.Create(request.Name, fit.Geometry!, source, request.ExternalReference, color);
        db.Neighborhoods.Add(item); db.NeighborhoodVersions.Add(NeighborhoodVersion.Capture(item, 1, "Create", actor.Id)); AddAudit(db, http, actor, "Create", "Neighborhood", item.Id, $"Bairro {item.Name} criado.");
        await db.SaveChangesAsync(); return Results.Created($"/api/territories/neighborhoods/{item.Id}", new { item.Id, item.ConcurrencyToken, geometry = item.Geometry, adjustedToExistingBoundaries = fit.Adjusted });
    }

    private static async Task<IResult> UpdateNeighborhoodAsync(Guid id, SaveNeighborhoodRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var error = ParseNeighborhood(request, out var geometry, out var source, out var color); if (error is not null) return error;
        var item = await db.Neighborhoods.SingleOrDefaultAsync(value => value.Id == id); if (item is null) return Results.NotFound();
        if (request.ExpectedVersion != item.ConcurrencyToken) return Conflict("Cadastro alterado", "Recarregue os dados antes de editar novamente.");
        var actor = await GetActorAsync(http, users); if (!await CanManageNeighborhoodAsync(id, actor, http.User, db)) return Results.Forbid();
        if (await db.Neighborhoods.AnyAsync(other => other.Id != id && EF.Functions.ILike(other.Name, request.Name.Trim()) && other.ArchivedAtUtc == null)) return Conflict("Bairro já cadastrado", "Já existe um bairro ativo com este nome.");
        var fit = await FitNeighborhoodAsync(geometry!, id, db); if (fit.IsEmpty) return Validation("geometry", "A área informada está totalmente coberta por bairros existentes.");
        if (!item.Geometry.EqualsExact(fit.Geometry))
        {
            var impactError = await UpdateNeighborhoodLinksAsync(item, fit.Geometry!, actor, http, db);
            if (impactError is not null) return impactError;
        }
        item.Update(request.Name, fit.Geometry!, source, request.ExternalReference, color);
        var number = await NextNeighborhoodVersionAsync(id, db); db.NeighborhoodVersions.Add(NeighborhoodVersion.Capture(item, number, "Update", actor.Id)); AddAudit(db, http, actor, "Update", "Neighborhood", item.Id, $"Bairro {item.Name} atualizado.");
        await db.SaveChangesAsync(); return Results.Ok(new { item.ConcurrencyToken, geometry = item.Geometry, adjustedToExistingBoundaries = fit.Adjusted });
    }

    private static async Task<IResult?> UpdateNeighborhoodLinksAsync(Neighborhood neighborhood, Geometry candidate, ApplicationUser actor, HttpContext http, StuDbContext db)
    {
        var others = await db.Neighborhoods.AsNoTracking().Where(n => n.Id != neighborhood.Id && n.ArchivedAtUtc == null).ToListAsync();
        var neighborhoods = others.Select(n => (n.Id, n.Geometry)).Append((Id: neighborhood.Id, Geometry: candidate)).ToArray();
        var existingLinks = await db.MicroregionNeighborhoods.ToListAsync();
        var linkedIds = existingLinks.Where(link => link.NeighborhoodId == neighborhood.Id).Select(link => link.MicroregionId).ToArray();
        var microregions = await db.Microregions.Where(m => m.ArchivedAtUtc == null &&
            (linkedIds.Contains(m.Id) || m.Boundary.Intersects(candidate))).ToListAsync();
        var changes = new List<(Microregion Microregion, Guid[] NeighborhoodIds)>();
        foreach (var micro in microregions)
        {
            var matches = neighborhoods.Where(n => MatchesNeighborhood(n.Geometry, micro.Boundary)).ToArray();
            if (matches.Length == 0 || (matches.All(n => n.Geometry is Polygon or MultiPolygon) &&
                !UnaryUnionOp.Union(matches.Select(n => n.Geometry).ToArray()).Covers(micro.Boundary)))
                return Conflict("Microrregiões fora dos bairros", "O novo contorno deixaria uma microrregião ativa fora dos bairros cadastrados. Revise os limites das microrregiões antes de alterar este bairro.");
            var ids = matches.Select(n => n.Id).Order().ToArray();
            var previous = existingLinks.Where(link => link.MicroregionId == micro.Id).Select(link => link.NeighborhoodId).Order();
            if (ids.SequenceEqual(previous)) continue;
            if (!http.User.IsInRole(SystemRoles.GlobalAdministrator) && micro.HealthUnitId != actor.HealthUnitId) return Results.Forbid();
            changes.Add((micro, ids));
        }
        // Validate all affected areas before changing any entity; SaveChanges commits the batch atomically.
        foreach (var (micro, ids) in changes)
        {
            var links = existingLinks.Where(link => link.MicroregionId == micro.Id).ToArray();
            db.MicroregionNeighborhoods.RemoveRange(links.Where(link => !ids.Contains(link.NeighborhoodId)));
            db.MicroregionNeighborhoods.AddRange(ids.Where(id => !links.Any(link => link.NeighborhoodId == id)).Select(id => MicroregionNeighborhood.Create(micro.Id, id)));
            micro.Update(micro.Code, micro.Name, micro.HealthUnitId, micro.AssignedAgentId, micro.Boundary, micro.Source, micro.Color);
            db.MicroregionVersions.Add(MicroregionVersion.Capture(micro, ids, await NextMicroregionVersionAsync(micro.Id, db), "Update", actor.Id));
            AddAudit(db, http, actor, "Update", "Microregion", micro.Id, "Bairros relacionados recalculados após edição do limite de bairro.");
        }
        return null;
    }

    private static async Task<IResult> ArchiveNeighborhoodAsync(Guid id, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db) => await SetNeighborhoodArchivedAsync(id, true, http, users, db);
    private static async Task<IResult> RestoreNeighborhoodAsync(Guid id, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db) => await SetNeighborhoodArchivedAsync(id, false, http, users, db);
    private static async Task<IResult> SetNeighborhoodArchivedAsync(Guid id, bool archive, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var item = await db.Neighborhoods.SingleOrDefaultAsync(value => value.Id == id); if (item is null) return Results.NotFound();
        var actor = await GetActorAsync(http, users); if (!await CanManageNeighborhoodAsync(id, actor, http.User, db)) return Results.Forbid();
        if (archive && await (from link in db.MicroregionNeighborhoods join microregion in db.Microregions on link.MicroregionId equals microregion.Id where link.NeighborhoodId == id && microregion.ArchivedAtUtc == null select link).AnyAsync()) return Conflict("Bairro em uso", "Arquive ou mova as microrregiões ativas antes de arquivar o bairro.");
        if (archive) item.Archive(); else item.Restore(); var action = archive ? "Archive" : "Restore";
        db.NeighborhoodVersions.Add(NeighborhoodVersion.Capture(item, await NextNeighborhoodVersionAsync(id, db), action, actor.Id)); AddAudit(db, http, actor, action, "Neighborhood", item.Id, $"Bairro {item.Name} {(archive ? "arquivado" : "reativado")}.");
        await db.SaveChangesAsync(); return Results.NoContent();
    }

    private static async Task<IResult> PreviewMicroregionAsync(SaveMicroregionRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        Microregion? current = null;
        Guid[] currentNeighborhoodIds = [];
        if (request.MicroregionId.HasValue)
        {
            current = await db.Microregions.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.MicroregionId.Value);
            if (current is null) return Results.NotFound();
            var actor = await GetActorAsync(http, users); var currentScope = ResolveScope(actor, http.User, current.HealthUnitId);
            if (currentScope.Error is not null || currentScope.HealthUnitId != current.HealthUnitId) return Results.Forbid();
            if (request.ExpectedVersion != current.ConcurrencyToken) return Conflict("Cadastro alterado", "Recarregue os dados antes de validar novamente.");
            currentNeighborhoodIds = await db.MicroregionNeighborhoods.AsNoTracking().Where(item => item.MicroregionId == current.Id).Select(item => item.NeighborhoodId).Order().ToArrayAsync();
        }
        var validation = await ValidateMicroregionAsync(request, request.MicroregionId, http, users, db); if (validation.Error is not null) return validation.Error;
        var impact = await GetPropertyImpactAsync(current, validation.HealthUnitId, request.AssignedAgentId, validation.Boundary!, db);
        var canViewProperties = http.User.HasClaim(StuClaimTypes.Permission, StuPermissions.All) || http.User.HasClaim(StuClaimTypes.Permission, StuPermissions.PropertiesView);
        return Results.Ok(new
        {
            valid = impact.BlockedCount == 0,
            adjustedToExistingBoundaries = validation.Adjusted,
            conflicts = impact.BlockedCount == 0 ? Array.Empty<string>() : new[] { PropertyImpactMessage(impact.BlockedCount) },
            affectedProperties = canViewProperties ? impact.Properties : [],
            propertyDetailsVisible = canViewProperties,
            affectedPropertyCount = impact.AffectedCount,
            blockedPropertyCount = impact.BlockedCount,
            before = current is null ? (object?)null : new { current.Code, current.Name, neighborhoodIds = currentNeighborhoodIds, current.Color, geometry = current.Boundary },
            after = new { request.Code, request.Name, neighborhoodIds = validation.NeighborhoodIds, color = validation.Color, geometry = validation.Boundary },
        });
    }

    private static async Task<IResult> CreateMicroregionAsync(SaveMicroregionRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var validation = await ValidateMicroregionAsync(request, null, http, users, db); if (validation.Error is not null) return validation.Error;
        var actor = await GetActorAsync(http, users); var item = Microregion.Create(request.Code, request.Name, validation.HealthUnitId, request.AssignedAgentId, validation.Boundary!, validation.Source, validation.Color);
        db.Microregions.Add(item);
        db.MicroregionNeighborhoods.AddRange(validation.NeighborhoodIds.Select(neighborhoodId => MicroregionNeighborhood.Create(item.Id, neighborhoodId)));
        db.MicroregionVersions.Add(MicroregionVersion.Capture(item, validation.NeighborhoodIds, 1, "Create", actor.Id)); AddAudit(db, http, actor, "Create", "Microregion", item.Id, $"Microrregião {item.Code} criada em {validation.NeighborhoodIds.Length} bairro(s).");
        if (item.AssignedAgentId.HasValue) db.UserNotifications.Add(UserNotification.Create(item.AssignedAgentId.Value, item.HealthUnitId, NotificationKind.Assignment, "Nova microrregião atribuída", $"Você agora é responsável por {item.Code} — {item.Name}.", "/map"));
        await db.SaveChangesAsync(); return Results.Created($"/api/territories/microregions/{item.Id}", new { item.Id, item.ConcurrencyToken, geometry = item.Boundary, adjustedToExistingBoundaries = validation.Adjusted });
    }

    private static async Task<IResult> UpdateMicroregionAsync(Guid id, SaveMicroregionRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var item = await db.Microregions.SingleOrDefaultAsync(value => value.Id == id); if (item is null) return Results.NotFound();
        var actor = await GetActorAsync(http, users); var currentScope = ResolveScope(actor, http.User, item.HealthUnitId); if (currentScope.Error is not null || currentScope.HealthUnitId != item.HealthUnitId) return Results.Forbid();
        if (request.ExpectedVersion != item.ConcurrencyToken) return Conflict("Cadastro alterado", "Recarregue os dados antes de editar novamente.");
        var validation = await ValidateMicroregionAsync(request, id, http, users, db); if (validation.Error is not null) return validation.Error;
        var impact = await GetPropertyImpactAsync(item, validation.HealthUnitId, request.AssignedAgentId, validation.Boundary!, db);
        if (impact.BlockedCount > 0) return Conflict("Imóveis afetados", PropertyImpactMessage(impact.BlockedCount));
        var previousAgentId = item.AssignedAgentId;
        item.Update(request.Code, request.Name, validation.HealthUnitId, request.AssignedAgentId, validation.Boundary!, validation.Source, validation.Color);
        var currentLinks = await db.MicroregionNeighborhoods.Where(link => link.MicroregionId == id).ToListAsync();
        db.MicroregionNeighborhoods.RemoveRange(currentLinks);
        db.MicroregionNeighborhoods.AddRange(validation.NeighborhoodIds.Select(neighborhoodId => MicroregionNeighborhood.Create(item.Id, neighborhoodId)));
        db.MicroregionVersions.Add(MicroregionVersion.Capture(item, validation.NeighborhoodIds, await NextMicroregionVersionAsync(id, db), "Update", actor.Id)); AddAudit(db, http, actor, "Update", "Microregion", item.Id, $"Microrregião {item.Code} atualizada em {validation.NeighborhoodIds.Length} bairro(s).");
        if (previousAgentId != item.AssignedAgentId)
        {
            if (previousAgentId.HasValue) db.UserNotifications.Add(UserNotification.Create(previousAgentId.Value, item.HealthUnitId, NotificationKind.Assignment, "Microrregião reatribuída", $"Você não é mais responsável por {item.Code} — {item.Name}.", "/map"));
            if (item.AssignedAgentId.HasValue) db.UserNotifications.Add(UserNotification.Create(item.AssignedAgentId.Value, item.HealthUnitId, NotificationKind.Assignment, "Nova microrregião atribuída", $"Você agora é responsável por {item.Code} — {item.Name}.", "/map"));
        }
        await db.SaveChangesAsync(); return Results.Ok(new { item.ConcurrencyToken, geometry = item.Boundary, adjustedToExistingBoundaries = validation.Adjusted });
    }

    private static async Task<IResult> ArchiveMicroregionAsync(Guid id, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db) => await SetMicroregionArchivedAsync(id, true, http, users, db);
    private static async Task<IResult> RestoreMicroregionAsync(Guid id, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db) => await SetMicroregionArchivedAsync(id, false, http, users, db);
    private static async Task<IResult> SetMicroregionArchivedAsync(Guid id, bool archive, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var item = await db.Microregions.SingleOrDefaultAsync(value => value.Id == id); if (item is null) return Results.NotFound();
        var actor = await GetActorAsync(http, users); var scope = ResolveScope(actor, http.User, item.HealthUnitId); if (scope.Error is not null || scope.HealthUnitId != item.HealthUnitId) return Results.Forbid();
        var neighborhoodIds = await db.MicroregionNeighborhoods.AsNoTracking().Where(link => link.MicroregionId == id).Select(link => link.NeighborhoodId).Order().ToArrayAsync();
        if (!archive)
        {
            var restorationError = await ValidateMicroregionRestorationAsync(item, neighborhoodIds, db);
            if (restorationError is not null) return restorationError;
        }
        if (archive) item.Archive(); else item.Restore(); var action = archive ? "Archive" : "Restore";
        db.MicroregionVersions.Add(MicroregionVersion.Capture(item, neighborhoodIds, await NextMicroregionVersionAsync(id, db), action, actor.Id)); AddAudit(db, http, actor, action, "Microregion", item.Id, $"Microrregião {item.Code} {(archive ? "arquivada" : "reativada")}.");
        await db.SaveChangesAsync(); return Results.NoContent();
    }

    private static async Task<IResult?> ValidateMicroregionRestorationAsync(Microregion item, Guid[] neighborhoodIds, StuDbContext db)
    {
        if (!item.IsArchived) return Conflict("Microrregião já ativa", "Este registro já está ativo no mapa operacional.");
        if (await db.Microregions.AsNoTracking().AnyAsync(other =>
                other.Id != item.Id && other.HealthUnitId == item.HealthUnitId &&
                other.ArchivedAtUtc == null && other.Code == item.Code))
            return Conflict("Código em uso", "Edite o código da microrregião arquivada ou arquive a área ativa que está usando este código.");
        if (await db.Microregions.AsNoTracking().AnyAsync(other =>
                other.Id != item.Id && other.HealthUnitId == item.HealthUnitId &&
                other.ArchivedAtUtc == null && other.NormalizedName == item.NormalizedName))
            return Conflict("Nome em uso", "Edite o nome da microrregião arquivada ou arquive a área ativa que está usando este nome.");
        if (neighborhoodIds.Length == 0)
            return Conflict("Bairro indisponível", "A microrregião não possui bairro relacionado. Edite o limite antes de desarquivar.");
        var activeNeighborhoodIds = await db.Neighborhoods.AsNoTracking()
            .Where(neighborhood => neighborhoodIds.Contains(neighborhood.Id) && neighborhood.ArchivedAtUtc == null)
            .Select(neighborhood => neighborhood.Id)
            .ToArrayAsync();
        if (activeNeighborhoodIds.Length != neighborhoodIds.Length)
            return Conflict("Bairro arquivado", "Reative os bairros relacionados ou edite o limite da microrregião antes de desarquivar.");
        var neighborhoods = await db.Neighborhoods.AsNoTracking().Where(n => activeNeighborhoodIds.Contains(n.Id)).ToListAsync();
        if (neighborhoods.Any(n => !MatchesNeighborhood(n.Geometry, item.Boundary)) ||
            (neighborhoods.All(n => n.Geometry is Polygon or MultiPolygon) &&
             !UnaryUnionOp.Union(neighborhoods.Select(n => n.Geometry).ToArray()).Covers(item.Boundary)))
            return Conflict("Limites dos bairros alterados", "Edite o limite arquivado para atualizar os bairros relacionados antes de desarquivar.");
        if (!await db.HealthUnits.AnyAsync(unit => unit.Id == item.HealthUnitId && unit.ArchivedAtUtc == null))
            return Conflict("UBS indisponível", "A UBS precisa estar ativa antes de desarquivar a microrregião.");
        if (item.AssignedAgentId.HasValue && !await (from user in db.Users
                join link in db.UserRoles on user.Id equals link.UserId
                join role in db.Roles on link.RoleId equals role.Id
                where user.Id == item.AssignedAgentId && user.HealthUnitId == item.HealthUnitId && user.ArchivedAtUtc == null && role.Name == SystemRoles.HealthAgent
                select user.Id).AnyAsync())
            return Conflict("Agente indisponível", "Edite a microrregião e selecione um agente ativo da mesma UBS ou remova a atribuição antes de desarquivar.");
        if (await db.Properties.AnyAsync(property => property.MicroregionId == item.Id && property.ArchivedAtUtc == null &&
                (property.HealthUnitId != item.HealthUnitId || !item.Boundary.Covers(property.Geometry))))
            return Conflict("Imóveis fora do limite", "O limite arquivado precisa conter todos os imóveis ativos vinculados antes de desarquivar.");
        var activeBoundaries = await db.Microregions.AsNoTracking()
            .Where(other => other.Id != item.Id && other.ArchivedAtUtc == null)
            .Select(other => other.Boundary)
            .ToListAsync();
        if (activeBoundaries.Any(boundary => boundary.Intersection(item.Boundary).Area > 0))
            return Conflict("Limite territorial em uso", "A área arquivada sobrepõe uma microrregião ativa. Edite o limite ou arquive a área conflitante antes de desarquivar.");
        return null;
    }

    private static async Task<MicroregionValidation> ValidateMicroregionAsync(SaveMicroregionRequest request, Guid? currentId, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 32) return MicroregionValidation.Fail(Validation("code", "Informe um código de até 32 caracteres."));
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160) return MicroregionValidation.Fail(Validation("name", "Informe um nome de até 160 caracteres."));
        if (!TrySource(request.Source, out var source)) return MicroregionValidation.Fail(Validation("source", "Origem territorial inválida."));
        string color;
        try { color = TerritoryColor.Normalize(request.Color, TerritoryColor.DefaultMicroregion); }
        catch (ArgumentException exception) { return MicroregionValidation.Fail(Validation("color", exception.Message)); }
        var parsed = ParseGeometry(request.Geometry); if (parsed.Error is not null) return MicroregionValidation.Fail(parsed.Error);
        var requestedBoundary = TerritoryBoundaryFitter.AsMultiPolygon(parsed.Geometry!); if (requestedBoundary is null) return MicroregionValidation.Fail(Validation("geometry", "A microrregião deve ser um polígono ou multipolígono."));
        var actor = await GetActorAsync(http, users); var scope = ResolveScope(actor, http.User, request.HealthUnitId); if (scope.Error is not null) return MicroregionValidation.Fail(scope.Error);
        if (!await db.HealthUnits.AnyAsync(unit => unit.Id == scope.HealthUnitId && unit.ArchivedAtUtc == null)) return MicroregionValidation.Fail(Validation("healthUnitId", "Selecione uma UBS ativa."));
        var editingArchived = currentId.HasValue && await db.Microregions.AsNoTracking().AnyAsync(item => item.Id == currentId.Value && item.ArchivedAtUtc != null);
        var occupiedBoundaries = editingArchived
            ? []
            : await db.Microregions.AsNoTracking().Where(item => item.Id != currentId && item.ArchivedAtUtc == null).Select(item => item.Boundary).ToListAsync();
        var fit = editingArchived
            ? new TerritoryBoundaryFit((Geometry)requestedBoundary.Copy(), false)
            : TerritoryBoundaryFitter.Fit(requestedBoundary, occupiedBoundaries);
        var boundary = TerritoryBoundaryFitter.AsMultiPolygon(fit.Geometry);
        if (boundary is null) return MicroregionValidation.Fail(Validation("geometry", "A área informada está totalmente coberta por microrregiões existentes."));
        var neighborhoodQuery = db.Neighborhoods.AsNoTracking();
        if (!editingArchived) neighborhoodQuery = neighborhoodQuery.Where(item => item.ArchivedAtUtc == null);
        var neighborhoods = (await neighborhoodQuery.ToListAsync())
            .Where(item => MatchesNeighborhood(item.Geometry, boundary)).ToList();
        if (neighborhoods.Count == 0) return MicroregionValidation.Fail(Validation("geometry", editingArchived ? "A área precisa atravessar ao menos um bairro cadastrado." : "A área precisa atravessar ao menos um bairro ativo."));
        if (neighborhoods.All(item => item.Geometry is Polygon or MultiPolygon))
        {
            var neighborhoodUnion = UnaryUnionOp.Union(neighborhoods.Select(item => item.Geometry).ToArray());
            if (!neighborhoodUnion.Covers(boundary)) return MicroregionValidation.Fail(Validation("geometry", "O limite precisa estar contido na união dos bairros identificados automaticamente."));
        }
        var neighborhoodIds = neighborhoods.Select(item => item.Id).Distinct().Order().ToArray();
        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var normalizedName = request.Name.Trim().ToUpperInvariant();
        if (!editingArchived && await db.Microregions.AnyAsync(item => item.Id != currentId && item.HealthUnitId == scope.HealthUnitId && item.ArchivedAtUtc == null && item.Code == normalizedCode)) return MicroregionValidation.Fail(Conflict("Código em uso", "Já existe uma microrregião ativa com este código na UBS."));
        if (!editingArchived && await db.Microregions.AnyAsync(item => item.Id != currentId && item.HealthUnitId == scope.HealthUnitId && item.ArchivedAtUtc == null && item.NormalizedName == normalizedName)) return MicroregionValidation.Fail(Conflict("Nome em uso", "Já existe uma microrregião ativa com este nome na UBS."));
        if (request.AssignedAgentId.HasValue)
        {
            var agent = await users.FindByIdAsync(request.AssignedAgentId.Value.ToString());
            if (agent is null || agent.IsArchived || agent.HealthUnitId != scope.HealthUnitId || !await users.IsInRoleAsync(agent, SystemRoles.HealthAgent)) return MicroregionValidation.Fail(Validation("assignedAgentId", "Selecione um agente de saúde ativo da mesma UBS."));
        }
        return new(null, boundary, source, scope.HealthUnitId, neighborhoodIds, color, fit.Adjusted);
    }

    private static async Task<PropertyImpact> GetPropertyImpactAsync(Microregion? current, Guid unitId, Guid? agentId, MultiPolygon boundary, StuDbContext db)
    {
        if (current is null) return new(0, 0, []);
        var movingUnit = current.HealthUnitId != unitId;
        var changingAgent = current.AssignedAgentId != agentId;
        // Historical properties retain their UBS too; moving only the territory would break that link.
        var affected = db.Properties.AsNoTracking().Where(property => property.MicroregionId == current.Id &&
            (movingUnit || (property.ArchivedAtUtc == null && (changingAgent || !boundary.Covers(property.Geometry)))));
        var count = await affected.CountAsync();
        var blocked = await affected.CountAsync(property => movingUnit || (!current.IsArchived && !boundary.Covers(property.Geometry)));
        var rows = await affected.OrderBy(property => property.Street).ThenBy(property => property.HouseNumber).ThenBy(property => property.Id)
            .Take(100).Select(property => new
            {
                property.Id, property.Street, property.HouseNumber,
                OutsideBoundary = !boundary.Covers(property.Geometry),
            }).ToListAsync();
        return new(count, blocked, rows.Select(property => new AffectedProperty(
            property.Id, property.Street, property.HouseNumber,
            movingUnit ? "Mudança de UBS exige transferência dos vínculos do imóvel." : property.OutsideBoundary
                ? "Imóvel ficará fora do novo limite." : "Imóvel passará a ser atendido por outro responsável.")).ToArray());
    }

    private static string PropertyImpactMessage(int count) => $"{count} imóvel(is) ficariam fora do limite ou com a UBS incompatível. Revise o contorno ou regularize os vínculos dos imóveis antes de salvar. Nenhum imóvel foi transferido automaticamente.";
    private sealed record PropertyImpact(int AffectedCount, int BlockedCount, AffectedProperty[] Properties);
    private sealed record AffectedProperty(Guid Id, string Street, string HouseNumber, string Reason);

    private static bool MatchesNeighborhood(Geometry neighborhood, MultiPolygon boundary)
    {
        if (neighborhood is Polygon or MultiPolygon)
        {
            var intersection = neighborhood.Intersection(boundary);
            return !intersection.IsEmpty && intersection.Area > 0;
        }

        return boundary.Covers(neighborhood) || boundary.Intersects(neighborhood);
    }

    private static IResult? ParseNeighborhood(SaveNeighborhoodRequest request, out Geometry? geometry, out TerritorySource source, out string color)
    {
        geometry = null; source = default; color = TerritoryColor.DefaultNeighborhood;
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160) return Validation("name", "Informe um nome de até 160 caracteres.");
        if (!TrySource(request.Source, out source)) return Validation("source", "Origem territorial inválida.");
        try { color = TerritoryColor.Normalize(request.Color, TerritoryColor.DefaultNeighborhood); }
        catch (ArgumentException exception) { return Validation("color", exception.Message); }
        var parsed = ParseGeometry(request.Geometry); if (parsed.Error is not null) return parsed.Error; geometry = parsed.Geometry;
        if (geometry is not (Point or Polygon or MultiPolygon)) return Validation("geometry", "O bairro deve usar ponto, polígono ou multipolígono.");
        return null;
    }

    private static GeometryParse ParseGeometry(JsonElement element)
    {
        try
        {
            var geometry = JsonSerializer.Deserialize<Geometry>(element.GetRawText(), GeometryJsonOptions);
            if (geometry is null || geometry.IsEmpty) return GeometryParse.Fail(Validation("geometry", "Informe uma geometria GeoJSON."));
            geometry.SRID = 4326;
            var envelope = geometry.EnvelopeInternal;
            if (envelope.MinX < -180 || envelope.MaxX > 180 || envelope.MinY < -90 || envelope.MaxY > 90) return GeometryParse.Fail(Validation("geometry", "As coordenadas devem usar longitude/latitude (SRID 4326)."));
            if (!geometry.IsValid) return GeometryParse.Fail(Validation("geometry", "A geometria possui cruzamentos ou anéis inválidos."));
            return new(null, geometry);
        }
        catch (JsonException) { return GeometryParse.Fail(Validation("geometry", "GeoJSON inválido.")); }
    }

    private static bool TrySource(string value, out TerritorySource source) => Enum.TryParse(value, true, out source) && Enum.IsDefined(source);
    private static object FeatureCollection(IEnumerable<object> features) => new { type = "FeatureCollection", features = features.ToArray() };
    private static object NeighborhoodFeature(Guid id, string name, Geometry geometry, string source, string color, int? version, Guid? token) => new { type = "Feature", id, geometry, properties = new { entityType = "neighborhood", id, name, source, color, version, concurrencyToken = token } };
    private static object MicroregionFeature(Guid id, string code, string name, IReadOnlyCollection<Guid> neighborhoodIds, Guid healthUnitId, Guid? agentId, Geometry geometry, string source, string color, int? version, Guid? token, DateTimeOffset? archivedAtUtc = null) => new { type = "Feature", id, geometry, properties = new { entityType = "microregion", id, code, name, neighborhoodIds, healthUnitId, assignedAgentId = agentId, source, color, version, concurrencyToken = token, archivedAtUtc } };

    private static async Task<TerritoryBoundaryFit> FitNeighborhoodAsync(Geometry candidate, Guid? currentId, StuDbContext db)
    {
        var occupied = await db.Neighborhoods.AsNoTracking()
            .Where(item => item.Id != currentId && item.ArchivedAtUtc == null)
            .Select(item => item.Geometry).ToListAsync();
        return TerritoryBoundaryFitter.Fit(candidate, occupied);
    }

    private static async Task<int> NextNeighborhoodVersionAsync(Guid id, StuDbContext db) => (await db.NeighborhoodVersions.Where(item => item.NeighborhoodId == id).MaxAsync(item => (int?)item.VersionNumber) ?? 0) + 1;
    private static async Task<int> NextMicroregionVersionAsync(Guid id, StuDbContext db) => (await db.MicroregionVersions.Where(item => item.MicroregionId == id).MaxAsync(item => (int?)item.VersionNumber) ?? 0) + 1;
    private static async Task<ApplicationUser> GetActorAsync(HttpContext context, UserManager<ApplicationUser> users) => await users.GetUserAsync(context.User) ?? throw new InvalidOperationException("Usuário autenticado não encontrado.");
    private static ScopeResult ResolveScope(ApplicationUser actor, System.Security.Claims.ClaimsPrincipal principal, Guid? requested)
    {
        if (principal.IsInRole(SystemRoles.GlobalAdministrator)) return requested.HasValue ? new(null, requested.Value) : new(Validation("healthUnitId", "Selecione uma UBS."), Guid.Empty);
        if (!actor.HealthUnitId.HasValue) return new(Results.Forbid(), Guid.Empty);
        if (requested.HasValue && requested.Value != actor.HealthUnitId.Value) return new(Results.Forbid(), Guid.Empty);
        return new(null, actor.HealthUnitId.Value);
    }

    private static async Task<bool> CanManageNeighborhoodAsync(Guid neighborhoodId, ApplicationUser actor, System.Security.Claims.ClaimsPrincipal principal, StuDbContext db)
    {
        if (principal.IsInRole(SystemRoles.GlobalAdministrator)) return true;
        if (!actor.HealthUnitId.HasValue) return false;
        return !await (from link in db.MicroregionNeighborhoods
                       join microregion in db.Microregions on link.MicroregionId equals microregion.Id
                       where link.NeighborhoodId == neighborhoodId && microregion.ArchivedAtUtc == null && microregion.HealthUnitId != actor.HealthUnitId.Value
                       select link).AnyAsync();
    }

    private static void AddAudit(StuDbContext db, HttpContext http, ApplicationUser actor, string action, string entityType, Guid entityId, string summary) =>
        db.AuditEntries.Add(AuditEntry.Create(actor.Id, actor.UserName ?? actor.DisplayName, action, entityType, entityId.ToString(), summary, null, null, http.Connection.RemoteIpAddress?.ToString()));
    private static IResult Validation(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
    private static IResult Conflict(string title, string detail) => Results.Problem(title: title, detail: detail, statusCode: StatusCodes.Status409Conflict);
    private static JsonSerializerOptions CreateGeometryOptions() { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); options.Converters.Add(new GeoJsonConverterFactory()); return options; }

    private sealed record ScopeResult(IResult? Error, Guid HealthUnitId);
    private sealed record GeometryParse(IResult? Error, Geometry? Geometry) { public static GeometryParse Fail(IResult error) => new(error, null); }
    private sealed record MicroregionValidation(IResult? Error, MultiPolygon? Boundary, TerritorySource Source, Guid HealthUnitId, Guid[] NeighborhoodIds, string Color, bool Adjusted)
    {
        public static MicroregionValidation Fail(IResult error) => new(error, null, default, Guid.Empty, [], TerritoryColor.DefaultMicroregion, false);
    }
}
