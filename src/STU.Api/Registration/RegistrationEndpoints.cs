using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using STU.Api.Families;
using STU.Api.HealthUnitUsers;
using STU.Application.Security;
using STU.Domain.Auditing;
using STU.Domain.Families;
using STU.Domain.Properties;
using STU.Domain.Territories;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.Api.Registration;

public static class RegistrationEndpoints
{
    public static IEndpointRouteBuilder MapRegistrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/registration").WithTags("Registration")
            .RequireAuthorization(StuPolicies.PropertiesView, StuPolicies.FamiliesView).RequireRateLimiting("api");
        group.MapGet("/units", UnitsAsync);
        group.MapGet("", ContextAsync);
        Secure(group.MapPost("/regions", CreateRegionAsync)).RequireAuthorization(StuPolicies.TerritoryManage);
        Secure(group.MapPut("/regions/{id:guid}", UpdateRegionAsync)).RequireAuthorization(StuPolicies.TerritoryManage);
        Secure(group.MapPost("/regions/{id:guid}/agents", LinkAgentAsync)).RequireAuthorization(StuPolicies.TerritoryManage);
        Secure(group.MapPost("/assignments/{id:guid}/unlink", UnlinkAgentAsync)).RequireAuthorization(StuPolicies.TerritoryManage);
        Secure(group.MapPost("/addresses", SaveAddressAsync)).RequireAuthorization(StuPolicies.PropertiesManage, StuPolicies.FamiliesManage);
        Secure(group.MapPut("/addresses/{id:guid}", UpdateAddressAsync)).RequireAuthorization(StuPolicies.PropertiesManage);
        return endpoints;
    }
    private static RouteHandlerBuilder Secure(RouteHandlerBuilder route) => route.WithMetadata(new RequireAntiforgeryTokenAttribute(true));

    private static async Task<IResult> UnitsAsync(HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var actor = await Actor(http, users);
        return Results.Ok(await db.HealthUnits.AsNoTracking().Where(u => u.ArchivedAtUtc == null && (http.User.IsInRole(SystemRoles.GlobalAdministrator) || u.Id == actor.HealthUnitId))
            .OrderBy(u => u.Name).Select(u => new { u.Id, u.Name, u.Code }).ToListAsync());
    }

    private static async Task<IResult> ContextAsync(Guid? healthUnitId, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var actor = await Actor(http, users); var unit = Unit(actor, http.User, healthUnitId);
        if (unit is null) return Results.Forbid();
        var healthUnit = await db.HealthUnits.AsNoTracking().Where(u => u.Id == unit && u.ArchivedAtUtc == null).Select(u => new { u.Id, u.Name, u.Code }).SingleOrDefaultAsync();
        if (healthUnit is null) return Results.NotFound();
        var isAgent = http.User.IsInRole(SystemRoles.HealthAgent);
        var microregions = await db.Microregions.AsNoTracking().Where(m => m.HealthUnitId == unit && m.ArchivedAtUtc == null &&
            (!isAgent || m.AssignedAgentId == actor.Id || db.MicroregionAgents.Any(a => a.MicroregionId == m.Id && a.AgentId == actor.Id && a.EndedAtUtc == null))).OrderBy(m => m.Code).ToListAsync();
        var regionIds = microregions.Select(m => m.Id).ToArray();
        var neighborhoods = await db.Neighborhoods.AsNoTracking().Where(n => n.ArchivedAtUtc == null).OrderBy(n => n.Name).Select(n => new { n.Id, n.Name }).ToListAsync();
        var neighborhoodNames = neighborhoods.ToDictionary(n => n.Id, n => n.Name);
        var neighborhoodLinks = await db.MicroregionNeighborhoods.AsNoTracking().Where(l => regionIds.Contains(l.MicroregionId)).ToListAsync();
        var assignments = await db.MicroregionAgents.AsNoTracking().Where(a => regionIds.Contains(a.MicroregionId) && a.EndedAtUtc == null && (!isAgent || a.AgentId == actor.Id)).ToListAsync();
        var assignmentIds = assignments.Select(a => a.Id).ToArray();
        var assignedUserIds = assignments.Select(a => a.AgentId).ToArray();
        var agents = await db.Users.AsNoTracking().Where(user => user.HealthUnitId == unit && (!isAgent || user.Id == actor.Id) &&
            (assignedUserIds.Contains(user.Id) || (user.ArchivedAtUtc == null && db.UserRoles.Any(ur => ur.UserId == user.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == SystemRoles.HealthAgent)))))
            .OrderBy(user => user.DisplayName).Select(user => new { user.Id, name = user.DisplayName, userName = user.UserName,
                isActive = user.ArchivedAtUtc == null && db.UserRoles.Any(ur => ur.UserId == user.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == SystemRoles.HealthAgent)) }).ToListAsync();
        var assignmentCounts = await db.Properties.AsNoTracking().Where(p => p.HealthUnitId == unit && p.MicroregionAgentId != null && assignmentIds.Contains(p.MicroregionAgentId.Value))
            .GroupBy(p => p.MicroregionAgentId!.Value).ToDictionaryAsync(g => g.Key, g => g.Count());
        var properties = await db.Properties.AsNoTracking().Where(p => p.HealthUnitId == unit && p.ArchivedAtUtc == null && p.RegistrationStatus == PropertyRegistrationStatus.Active && regionIds.Contains(p.MicroregionId) &&
            (!isAgent || (p.MicroregionAgentId.HasValue && assignmentIds.Contains(p.MicroregionAgentId.Value)) || (!p.MicroregionAgentId.HasValue && db.Microregions.Any(m => m.Id == p.MicroregionId && m.AssignedAgentId == actor.Id)))).OrderBy(p => p.Street).ThenBy(p => p.HouseNumber).ToListAsync();
        var current = await FamilyAccess.CurrentByPropertyAsync(properties.Select(p => p.Id), db);
        var families = await FamilyAccess.Authorized(db, actor, http.User, unit.Value).AsNoTracking().Where(f => f.ArchivedAtUtc == null).OrderBy(f => f.Number)
            .Select(f => new { f.Id, f.Number, f.ResponsibleName, f.ConcurrencyToken }).ToListAsync();
        return Results.Ok(new
        {
            healthUnit, neighborhoods, agents,
            regions = microregions.Select(m => { var neighborhoodId = neighborhoodLinks.Where(l => l.MicroregionId == m.Id).Select(l => (Guid?)l.NeighborhoodId).FirstOrDefault(); return new { m.Id, m.Code, m.Name, neighborhoodId, neighborhood = neighborhoodId.HasValue ? neighborhoodNames.GetValueOrDefault(neighborhoodId.Value, "Bairro arquivado") : "Bairro não definido", m.ConcurrencyToken }; }),
            assignments = assignments.Select(a => new { a.Id, regionId = a.MicroregionId, a.AgentId, a.EndedAtUtc, a.ConcurrencyToken, addressCount = assignmentCounts.GetValueOrDefault(a.Id) }),
            addresses = properties.Select(p => {
                var neighborhoodId = p.NeighborhoodId ?? neighborhoodLinks.Where(l => l.MicroregionId == p.MicroregionId).Select(l => (Guid?)l.NeighborhoodId).FirstOrDefault();
                return new { p.Id, regionId = p.MicroregionId, assignmentId = p.MicroregionAgentId ?? assignments.FirstOrDefault(a => a.MicroregionId == p.MicroregionId && microregions.Any(m => m.Id == p.MicroregionId && m.AssignedAgentId == a.AgentId))?.Id,
                    p.Street, p.HouseNumber, noNumber = p.HouseNumber == "S/N", neighborhoodId, neighborhood = neighborhoodId.HasValue ? neighborhoodNames.GetValueOrDefault(neighborhoodId.Value, "Bairro arquivado") : "Bairro não definido",
                    complement = p.Complement ?? "", postalCode = p.PostalCode ?? "", reference = p.Reference ?? "", familyId = current.GetValueOrDefault(p.Id)?.Id, p.ConcurrencyToken };
            }), families,
        });
    }

    private static async Task<IResult> CreateRegionAsync(RegionRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var actor = await Actor(http, users); var unit = Unit(actor, http.User, request.HealthUnitId);
        if (unit is null) return Results.Forbid();
        var error = await ValidateRegion(request, unit.Value, null, db); if (error is not null) return error;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var micro = Microregion.CreateRegistration(request.Code, request.Name, unit.Value);
        db.Microregions.Add(micro); db.MicroregionNeighborhoods.Add(MicroregionNeighborhood.Create(micro.Id, request.NeighborhoodId));
        db.MicroregionVersions.Add(MicroregionVersion.Capture(micro, [request.NeighborhoodId], 1, "CreateRegistration", actor.Id));
        Audit(db, http, actor, "CreateRegistration", "Microregion", micro.Id, new { request.Code, request.Name, request.NeighborhoodId });
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Created($"/api/registration/regions/{micro.Id}", new { micro.Id, micro.ConcurrencyToken });
    }

    private static async Task<IResult> UpdateRegionAsync(Guid id, RegionRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var actor = await Actor(http, users); var micro = await db.Microregions.SingleOrDefaultAsync(m => m.Id == id && m.ArchivedAtUtc == null);
        if (micro is null || !InUnit(actor, http.User, micro.HealthUnitId)) return Results.NotFound();
        if (request.HealthUnitId.HasValue && request.HealthUnitId != micro.HealthUnitId) return Invalid("healthUnitId", "A microrregião não pode mudar de UBS por edição.");
        if (request.ExpectedVersion != micro.ConcurrencyToken) return Stale();
        var error = await ValidateRegion(request, micro.HealthUnitId, id, db); if (error is not null) return error;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        micro.UpdateRegistration(request.Code, request.Name);
        db.MicroregionNeighborhoods.RemoveRange(await db.MicroregionNeighborhoods.Where(l => l.MicroregionId == id && l.NeighborhoodId != request.NeighborhoodId).ToListAsync());
        if (!await db.MicroregionNeighborhoods.AnyAsync(l => l.MicroregionId == id && l.NeighborhoodId == request.NeighborhoodId)) db.MicroregionNeighborhoods.Add(MicroregionNeighborhood.Create(id, request.NeighborhoodId));
        db.MicroregionVersions.Add(MicroregionVersion.Capture(micro, [request.NeighborhoodId], (await db.MicroregionVersions.Where(v => v.MicroregionId == id).MaxAsync(v => (int?)v.VersionNumber) ?? 0) + 1, "UpdateRegistration", actor.Id));
        Audit(db, http, actor, "UpdateRegistration", "Microregion", id, new { request.Code, request.Name, request.NeighborhoodId });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { micro.Id, micro.ConcurrencyToken });
    }

    private static async Task<IResult> LinkAgentAsync(Guid id, AgentRequest request, HttpContext http, UserManager<ApplicationUser> users, RoleManager<ApplicationRole> roles, StuDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await Actor(http, users); var micro = await db.Microregions.SingleOrDefaultAsync(m => m.Id == id && m.ArchivedAtUtc == null);
        if (micro is null || !InUnit(actor, http.User, micro.HealthUnitId)) return Results.NotFound();
        if (request.ExpectedVersion != micro.ConcurrencyToken) return Stale();
        if (!await ActiveUnit(micro.HealthUnitId, db)) return Invalid("healthUnitId", "Selecione uma UBS ativa.");
        ApplicationUser? person; string? temporaryPassword = null;
        if (request.AgentId.HasValue) person = await users.FindByIdAsync(request.AgentId.Value.ToString());
        else
        {
            if (!FamilyAccess.Has(http.User, StuPermissions.HealthUnitUsersManage)) return Results.Forbid();
            var created = await HealthUnitUserEndpoints.ProvisionAsync(request.UserName, request.Name, SystemRoles.HealthAgent, micro.HealthUnitId, actor, http, users, roles, db);
            if (created.Error is not null) return created.Error;
            person = created.User; temporaryPassword = created.TemporaryPassword;
        }
        if (person is null || person.HealthUnitId != micro.HealthUnitId || person.IsArchived || !await users.IsInRoleAsync(person, SystemRoles.HealthAgent)) return Invalid("agentId", "Selecione um agente ativo desta UBS.");
        if (await db.MicroregionAgents.AnyAsync(a => a.MicroregionId == id && a.AgentId == person.Id && a.EndedAtUtc == null)) return Conflict("Este agente já está vinculado à microrregião.");
        var link = MicroregionAgent.Create(micro.HealthUnitId, id, person.Id, actor.Id); db.MicroregionAgents.Add(link);
        micro.SetPrimaryAgent(micro.AssignedAgentId ?? person.Id);
        Audit(db, http, actor, "LinkAgent", "MicroregionAgent", link.Id, new { link.MicroregionId, link.AgentId });
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Created($"/api/registration/assignments/{link.Id}", new { link.Id, link.ConcurrencyToken, userName = person.UserName, temporaryPassword });
    }

    private static async Task<IResult> UnlinkAgentAsync(Guid id, UnlinkRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await Actor(http, users); var link = await db.MicroregionAgents.SingleOrDefaultAsync(a => a.Id == id && a.EndedAtUtc == null);
        if (link is null || !InUnit(actor, http.User, link.HealthUnitId)) return Results.NotFound();
        var micro = await db.Microregions.SingleAsync(m => m.Id == link.MicroregionId);
        if (micro.IsArchived || !await ActiveUnit(link.HealthUnitId, db)) return Conflict("A microrregião ou a UBS está arquivada.");
        if (request.ExpectedVersion != link.ConcurrencyToken || request.ExpectedRegionVersion != micro.ConcurrencyToken) return Stale();
        var properties = await db.Properties.Where(p => p.MicroregionId == link.MicroregionId && (p.MicroregionAgentId == id || (p.MicroregionAgentId == null && micro.AssignedAgentId == link.AgentId))).ToListAsync();
        MicroregionAgent? recipient = null;
        if (properties.Count > 0)
        {
            recipient = await db.MicroregionAgents.SingleOrDefaultAsync(a => a.Id == request.RecipientId && a.MicroregionId == link.MicroregionId && a.HealthUnitId == link.HealthUnitId && a.Id != id && a.EndedAtUtc == null);
            var recipientUser = recipient is null ? null : await users.FindByIdAsync(recipient.AgentId.ToString());
            if (recipientUser is null || recipientUser.IsArchived || recipientUser.HealthUnitId != link.HealthUnitId || !await users.IsInRoleAsync(recipientUser, SystemRoles.HealthAgent)) return Invalid("recipientId", "Selecione outro agente ativo da mesma microrregião para receber os endereços.");
            if (request.ExpectedRecipientVersion != recipient!.ConcurrencyToken) return Stale();
            recipient.Touch();
            foreach (var property in properties)
            {
                property.AssignAgent(recipient.Id);
                db.PropertyVersions.Add(PropertyVersion.Capture(property, await NextPropertyVersion(property.Id, db), "TransferAgent", actor.Id));
            }
        }
        link.End(actor.Id);
        var nextAgentId = recipient?.AgentId ?? await db.MicroregionAgents.Where(a => a.MicroregionId == link.MicroregionId && a.Id != id && a.EndedAtUtc == null).OrderBy(a => a.StartedAtUtc).Select(a => (Guid?)a.AgentId).FirstOrDefaultAsync();
        micro.SetPrimaryAgent(micro.AssignedAgentId == link.AgentId ? nextAgentId : micro.AssignedAgentId);
        Audit(db, http, actor, "UnlinkAgent", "MicroregionAgent", link.Id, new { link.MicroregionId, link.AgentId, recipientId = recipient?.Id, addressIds = properties.Select(p => p.Id) });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { link.Id, link.EndedAtUtc });
    }

    private static async Task<IResult> SaveAddressAsync(AddressRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var actor = await Actor(http, users); var assignment = await db.MicroregionAgents.SingleOrDefaultAsync(a => a.Id == request.AssignmentId && a.EndedAtUtc == null);
        if (assignment is null || !InUnit(actor, http.User, assignment.HealthUnitId) || (http.User.IsInRole(SystemRoles.HealthAgent) && assignment.AgentId != actor.Id)) return Results.NotFound();
        var micro = await db.Microregions.SingleAsync(m => m.Id == assignment.MicroregionId);
        if (micro.IsArchived || !await ActiveUnit(assignment.HealthUnitId, db)) return Conflict("A microrregião ou a UBS está arquivada.");
        var agent = await users.FindByIdAsync(assignment.AgentId.ToString());
        if (agent is null || agent.IsArchived || !await users.IsInRoleAsync(agent, SystemRoles.HealthAgent)) return Conflict("O agente não está ativo.");
        if (request.ExpectedAssignmentVersion != assignment.ConcurrencyToken) return Stale();
        var error = await ValidateAddress(request, micro.Id, db); if (error is not null) return error;
        Family family;
        if (request.FamilyId.HasValue)
        {
            family = (await FamilyAccess.Authorized(db, actor, http.User, assignment.HealthUnitId).SingleOrDefaultAsync(f => f.Id == request.FamilyId && f.ArchivedAtUtc == null))!;
            if (family is null) return Results.NotFound();
            if (family.ConcurrencyToken != request.ExpectedFamilyVersion) return Stale();
            if (await db.FamilyPropertyLinks.AnyAsync(l => l.FamilyId == family.Id && l.EndedAtUtc == null)) return Conflict("Esta família já possui endereço. Recarregue a consulta.");
        }
        else
        {
            try { family = Family.Create(assignment.HealthUnitId, request.FamilyNumber ?? "", request.FamilyResponsibleName ?? "", actor.Id); }
            catch (ArgumentException exception) { return Invalid("family", exception.Message); }
            if (await db.Families.AnyAsync(f => f.HealthUnitId == assignment.HealthUnitId && f.Number == family.Number)) return Conflict("Este número de família já está reservado na UBS.");
            db.Families.Add(family); db.FamilyVersions.Add(FamilyVersion.Capture(family, 1, "Create", actor.Id));
        }
        HealthProperty property;
        if (request.ExistingAddressId.HasValue)
        {
            property = (await db.Properties.SingleOrDefaultAsync(p => p.Id == request.ExistingAddressId && p.HealthUnitId == assignment.HealthUnitId && p.MicroregionId == micro.Id && p.MicroregionAgentId == assignment.Id && p.ArchivedAtUtc == null))!;
            if (property is null) return Results.NotFound();
            if (property.ConcurrencyToken != request.ExpectedVersion) return Stale();
            if (await db.FamilyPropertyLinks.AnyAsync(l => l.PropertyId == property.Id && l.EndedAtUtc == null)) return Conflict("Este endereço já possui família.");
            property.Touch();
        }
        else
        {
            property = HealthProperty.CreateAddress(assignment.HealthUnitId, micro.Id, assignment.Id, request.NeighborhoodId, request.Street, request.NoNumber ? "S/N" : request.HouseNumber, request.PostalCode, request.Complement, request.Reference);
            db.Properties.Add(property); db.PropertyVersions.Add(PropertyVersion.Capture(property, 1, "CreateAddress", actor.Id));
        }
        family.Touch(actor.Id); assignment.Touch();
        db.FamilyPropertyLinks.Add(FamilyPropertyLink.Create(assignment.HealthUnitId, family.Id, property.Id, actor.Id));
        db.FamilyVersions.Add(FamilyVersion.Capture(family, request.FamilyId.HasValue ? (await db.FamilyVersions.Where(v => v.FamilyId == family.Id).MaxAsync(v => (int?)v.VersionNumber) ?? 0) + 1 : 2, "Link", actor.Id));
        Audit(db, http, actor, "CreateAddressFamily", "Property", property.Id, new { assignmentId = assignment.Id, familyId = family.Id });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Created($"/api/properties/{property.Id}", new { property.Id, familyId = family.Id, property.ConcurrencyToken });
    }

    private static async Task<IResult> UpdateAddressAsync(Guid id, AddressRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var actor = await Actor(http, users); var property = await db.Properties.SingleOrDefaultAsync(p => p.Id == id && p.ArchivedAtUtc == null);
        if (property is null || !await FamilyAccess.CanUsePropertyAsync(property, actor, http.User, db)) return Results.NotFound();
        if (request.AssignmentId != property.MicroregionAgentId) return Invalid("assignmentId", "A edição do endereço não altera o agente responsável.");
        if (request.ExpectedVersion != property.ConcurrencyToken) return Stale();
        if (!await ActiveUnit(property.HealthUnitId, db) || !await db.Microregions.AnyAsync(m => m.Id == property.MicroregionId && m.ArchivedAtUtc == null)) return Conflict("A microrregião ou a UBS está arquivada.");
        var error = await ValidateAddress(request, property.MicroregionId, db); if (error is not null) return error;
        property.UpdateAddress(request.NeighborhoodId, request.Street, request.NoNumber ? "S/N" : request.HouseNumber, request.PostalCode, request.Complement, request.Reference);
        db.PropertyVersions.Add(PropertyVersion.Capture(property, await NextPropertyVersion(id, db), "UpdateAddress", actor.Id));
        Audit(db, http, actor, "UpdateAddress", "Property", id, null); await db.SaveChangesAsync(); return Results.Ok(new { property.ConcurrencyToken });
    }

    private static async Task<IResult?> ValidateRegion(RegionRequest r, Guid unit, Guid? id, StuDbContext db)
    {
        if (!await ActiveUnit(unit, db)) return Invalid("healthUnitId", "Selecione uma UBS ativa.");
        if (!Text(r.Code, 32) || !Text(r.Name, 160)) return Invalid("region", "Informe código de até 32 e nome de até 160 caracteres.");
        if (!await db.Neighborhoods.AnyAsync(n => n.Id == r.NeighborhoodId && n.ArchivedAtUtc == null)) return Invalid("neighborhoodId", "Selecione um bairro cadastrado e ativo.");
        var code = r.Code.Trim().ToUpperInvariant(); var name = r.Name.Trim().ToUpperInvariant();
        return await db.Microregions.AnyAsync(m => m.HealthUnitId == unit && m.Id != id && m.ArchivedAtUtc == null && (m.Code == code || m.NormalizedName == name)) ? Conflict("Código ou nome de microrregião já utilizado nesta UBS.") : null;
    }
    private static async Task<IResult?> ValidateAddress(AddressRequest r, Guid regionId, StuDbContext db)
    {
        if (!Text(r.Street, 180) || (!r.NoNumber && !Text(r.HouseNumber, 32)) || !OptionalText(r.Complement, 120) || !OptionalText(r.Reference, 180) || !OptionalText(r.PostalCode, 16)) return Invalid("address", "Revise logradouro, número, complemento e referência.");
        if (!string.IsNullOrWhiteSpace(r.PostalCode) && !System.Text.RegularExpressions.Regex.IsMatch(r.PostalCode.Trim(), @"^\d{5}-?\d{3}$")) return Invalid("postalCode", "Informe um CEP com 8 dígitos.");
        return await db.Neighborhoods.AnyAsync(n => n.Id == r.NeighborhoodId && n.ArchivedAtUtc == null) && await db.MicroregionNeighborhoods.AnyAsync(l => l.MicroregionId == regionId && l.NeighborhoodId == r.NeighborhoodId) ? null : Invalid("neighborhoodId", "Selecione um bairro da microrregião.");
    }
    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max && !value.Any(char.IsControl);
    private static bool OptionalText(string? value, int max) => string.IsNullOrEmpty(value) || (value.Length <= max && !value.Any(char.IsControl));
    private static Task<bool> ActiveUnit(Guid unit, StuDbContext db) => db.HealthUnits.AnyAsync(u => u.Id == unit && u.ArchivedAtUtc == null);
    private static bool InUnit(ApplicationUser actor, ClaimsPrincipal principal, Guid unit) => principal.IsInRole(SystemRoles.GlobalAdministrator) || actor.HealthUnitId == unit;
    private static Guid? Unit(ApplicationUser actor, ClaimsPrincipal principal, Guid? requested) => principal.IsInRole(SystemRoles.GlobalAdministrator) ? requested ?? actor.HealthUnitId : requested.HasValue && requested != actor.HealthUnitId ? null : actor.HealthUnitId;
    private static async Task<int> NextPropertyVersion(Guid id, StuDbContext db) => (await db.PropertyVersions.Where(v => v.PropertyId == id).MaxAsync(v => (int?)v.VersionNumber) ?? 0) + 1;
    private static async Task<ApplicationUser> Actor(HttpContext http, UserManager<ApplicationUser> users) => await users.GetUserAsync(http.User) ?? throw new InvalidOperationException("Usuário autenticado não encontrado.");
    private static void Audit(StuDbContext db, HttpContext http, ApplicationUser actor, string action, string entity, Guid id, object? detail) => db.AuditEntries.Add(AuditEntry.Create(actor.Id, actor.UserName ?? actor.DisplayName, action, entity, id.ToString(), "Cadastro territorial atualizado; detalhes no histórico autorizado.", null, detail is null ? null : JsonSerializer.Serialize(detail), http.Connection.RemoteIpAddress?.ToString()));
    private static IResult Invalid(string field, string detail) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [detail] });
    private static IResult Conflict(string detail) => Results.Problem(statusCode: 409, title: "Cadastro alterado", detail: detail);
    private static IResult Stale() => Conflict("Outro usuário alterou o cadastro. Atualize a consulta antes de tentar novamente.");

    public sealed record RegionRequest(string Code, string Name, Guid NeighborhoodId, Guid? HealthUnitId, Guid? ExpectedVersion);
    public sealed record AgentRequest(Guid? AgentId, string? Name, string? UserName, Guid? ExpectedVersion);
    public sealed record UnlinkRequest(Guid? RecipientId, Guid? ExpectedVersion, Guid? ExpectedRegionVersion, Guid? ExpectedRecipientVersion);
    public sealed record AddressRequest(Guid AssignmentId, string Street, string HouseNumber, bool NoNumber, Guid NeighborhoodId, string? Complement, string? PostalCode, string? Reference,
        Guid? FamilyId, string? FamilyNumber, string? FamilyResponsibleName, Guid? ExpectedAssignmentVersion, Guid? ExpectedFamilyVersion, Guid? ExistingAddressId, Guid? ExpectedVersion);
}
