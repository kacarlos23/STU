using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using STU.Application.Security;
using STU.Domain.Auditing;
using STU.Domain.HealthUnits;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.Api.Administration;

public static class AdministrationEndpoints
{
    public static IEndpointRouteBuilder MapAdministrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin")
            .WithTags("Global administration")
            .RequireAuthorization(StuPolicies.GlobalAdministration)
            .RequireRateLimiting("api");

        group.MapGet("/health-units", GetHealthUnitsAsync);
        RequireAntiforgery(group.MapPost("/health-units", CreateHealthUnitAsync));
        RequireAntiforgery(group.MapPut("/health-units/{id:guid}", UpdateHealthUnitAsync));
        RequireAntiforgery(group.MapPost("/health-units/{id:guid}/archive", ArchiveHealthUnitAsync));
        RequireAntiforgery(group.MapPost("/health-units/{id:guid}/restore", RestoreHealthUnitAsync));

        group.MapGet("/users", GetUsersAsync);
        RequireAntiforgery(group.MapPost("/users", CreateUserAsync));
        RequireAntiforgery(group.MapPut("/users/{id:guid}", UpdateUserAsync));
        RequireAntiforgery(group.MapPost("/users/{id:guid}/reset-password", ResetUserPasswordAsync));
        RequireAntiforgery(group.MapPost("/users/{id:guid}/archive", ArchiveUserAsync));
        RequireAntiforgery(group.MapPost("/users/{id:guid}/restore", RestoreUserAsync));

        group.MapGet("/roles", GetRolesAsync);
        group.MapGet("/permissions", GetPermissions);
        RequireAntiforgery(group.MapPost("/roles", CreateRoleAsync));
        RequireAntiforgery(group.MapPut("/roles/{id:guid}", UpdateRoleAsync));
        RequireAntiforgery(group.MapPost("/roles/{id:guid}/archive", ArchiveRoleAsync));
        RequireAntiforgery(group.MapPost("/roles/{id:guid}/restore", RestoreRoleAsync));

        group.MapGet("/audit", GetAuditAsync);
        return endpoints;
    }

    private static RouteHandlerBuilder RequireAntiforgery(RouteHandlerBuilder builder) =>
        builder.WithMetadata(new RequireAntiforgeryTokenAttribute(true));

    private static async Task<IResult> GetHealthUnitsAsync(
        StuDbContext dbContext,
        bool includeArchived = false)
    {
        var units = await dbContext.HealthUnits
            .Where(unit => includeArchived || unit.ArchivedAtUtc == null)
            .OrderBy(unit => unit.Name)
            .Select(unit => new
            {
                unit.Id,
                unit.Code,
                unit.Name,
                unit.CreatedAtUtc,
                unit.UpdatedAtUtc,
                unit.ArchivedAtUtc,
                activeUsers = dbContext.Users.Count(user =>
                    user.HealthUnitId == unit.Id && user.ArchivedAtUtc == null),
            })
            .ToListAsync();

        return Results.Ok(units);
    }

    private static async Task<IResult> CreateHealthUnitAsync(
        CreateHealthUnitRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        StuDbContext dbContext)
    {
        var validation = ValidateHealthUnit(request.Code, request.Name);
        if (validation is not null)
        {
            return validation;
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (await dbContext.HealthUnits.AnyAsync(unit => unit.Code == code))
        {
            return Conflict("Código já utilizado", "Já existe uma UBS com este código.");
        }

        var actor = await GetActorAsync(context, userManager);
        var unit = HealthUnit.Create(code, request.Name);
        dbContext.HealthUnits.Add(unit);
        AddAudit(dbContext, context, actor, "Create", "HealthUnit", unit.Id, $"UBS {unit.Code} criada.", null, Snapshot(unit));
        await dbContext.SaveChangesAsync();

        return Results.Created($"/api/admin/health-units/{unit.Id}", new
        {
            unit.Id,
            unit.Code,
            unit.Name,
            unit.CreatedAtUtc,
            unit.UpdatedAtUtc,
            unit.ArchivedAtUtc,
            activeUsers = 0,
        });
    }

    private static async Task<IResult> UpdateHealthUnitAsync(
        Guid id,
        UpdateHealthUnitRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        StuDbContext dbContext)
    {
        var validation = ValidateHealthUnit(request.Code, request.Name);
        if (validation is not null)
        {
            return validation;
        }

        var unit = await dbContext.HealthUnits.FindAsync(id);
        if (unit is null)
        {
            return Results.NotFound();
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (await dbContext.HealthUnits.AnyAsync(other => other.Id != id && other.Code == code))
        {
            return Conflict("Código já utilizado", "Já existe uma UBS com este código.");
        }

        var actor = await GetActorAsync(context, userManager);
        var before = Snapshot(unit);
        unit.Update(code, request.Name);
        AddAudit(dbContext, context, actor, "Update", "HealthUnit", unit.Id, $"UBS {unit.Code} atualizada.", before, Snapshot(unit));
        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ArchiveHealthUnitAsync(
        Guid id,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        StuDbContext dbContext)
    {
        var unit = await dbContext.HealthUnits.FindAsync(id);
        if (unit is null)
        {
            return Results.NotFound();
        }

        if (await dbContext.Users.AnyAsync(user => user.HealthUnitId == id && user.ArchivedAtUtc == null))
        {
            return Conflict("UBS em uso", "Transfira ou arquive os servidores ativos antes de arquivar esta UBS.");
        }

        var actor = await GetActorAsync(context, userManager);
        var before = Snapshot(unit);
        unit.Archive();
        AddAudit(dbContext, context, actor, "Archive", "HealthUnit", unit.Id, $"UBS {unit.Code} arquivada.", before, Snapshot(unit));
        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> RestoreHealthUnitAsync(
        Guid id,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        StuDbContext dbContext)
    {
        var unit = await dbContext.HealthUnits.FindAsync(id);
        if (unit is null)
        {
            return Results.NotFound();
        }

        var actor = await GetActorAsync(context, userManager);
        var before = Snapshot(unit);
        unit.Restore();
        AddAudit(dbContext, context, actor, "Restore", "HealthUnit", unit.Id, $"UBS {unit.Code} reativada.", before, Snapshot(unit));
        await dbContext.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> GetUsersAsync(
        StuDbContext dbContext,
        string? query = null,
        Guid? healthUnitId = null,
        bool includeArchived = false,
        int page = 1,
        int pageSize = 50)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var usersQuery = dbContext.Users.AsNoTracking()
            .Where(user => includeArchived || user.ArchivedAtUtc == null);

        if (healthUnitId.HasValue)
        {
            usersQuery = usersQuery.Where(user => user.HealthUnitId == healthUnitId);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var search = $"%{query.Trim()}%";
            usersQuery = usersQuery.Where(user =>
                (user.UserName != null && EF.Functions.ILike(user.UserName, search)) ||
                EF.Functions.ILike(user.DisplayName, search));
        }

        var total = await usersQuery.CountAsync();
        var users = await usersQuery
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        var items = await BuildUserResponsesAsync(users, dbContext);

        return Results.Ok(new { items, total, page, pageSize });
    }

    private static async Task<IResult> CreateUserAsync(
        CreateUserRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        StuDbContext dbContext)
    {
        var validation = ValidateUser(request.UserName, request.DisplayName);
        if (validation is not null)
        {
            return validation;
        }

        var role = await roleManager.FindByNameAsync(request.RoleName.Trim());
        if (role is null || role.IsArchived)
        {
            return Validation("roleName", "Selecione uma função ativa.");
        }

        var healthUnitResult = await ValidateHealthUnitForRoleAsync(request.HealthUnitId, role, dbContext);
        if (healthUnitResult.Error is not null)
        {
            return healthUnitResult.Error;
        }

        var actor = await GetActorAsync(context, userManager);
        var temporaryPassword = GenerateTemporaryPassword();
        var user = ApplicationUser.Create(
            request.UserName.Trim(),
            request.DisplayName,
            healthUnitResult.HealthUnitId);

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var createResult = await userManager.CreateAsync(user, temporaryPassword);
        if (!createResult.Succeeded)
        {
            return IdentityFailure(createResult);
        }

        var roleResult = await userManager.AddToRoleAsync(user, role.Name!);
        if (!roleResult.Succeeded)
        {
            return IdentityFailure(roleResult);
        }

        AddAudit(dbContext, context, actor, "Create", "User", user.Id, $"Usuário {user.UserName} criado.", null, UserSnapshot(user, role.Name!));
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return Results.Created($"/api/admin/users/{user.Id}", new
        {
            userId = user.Id,
            userName = user.UserName,
            temporaryPassword,
        });
    }

    private static async Task<IResult> UpdateUserAsync(
        Guid id,
        UpdateUserRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        StuDbContext dbContext)
    {
        var actor = await GetActorAsync(context, userManager);
        if (actor.Id == id)
        {
            return Conflict("Alteração bloqueada", "Sua própria conta não pode ser alterada por esta área.");
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 160)
        {
            return Validation("displayName", "Informe um nome de até 160 caracteres.");
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Results.NotFound();
        }

        if (user.IsArchived)
        {
            return Conflict("Conta arquivada", "Reative a conta antes de alterá-la.");
        }

        var role = await roleManager.FindByNameAsync(request.RoleName.Trim());
        if (role is null || role.IsArchived)
        {
            return Validation("roleName", "Selecione uma função ativa.");
        }

        var healthUnitResult = await ValidateHealthUnitForRoleAsync(request.HealthUnitId, role, dbContext);
        if (healthUnitResult.Error is not null)
        {
            return healthUnitResult.Error;
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        var before = UserSnapshot(user, currentRoles.SingleOrDefault());
        var previousHealthUnitId = user.HealthUnitId;
        user.UpdateProfile(request.DisplayName, healthUnitResult.HealthUnitId);

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return IdentityFailure(updateResult);
        }

        if (currentRoles.Count > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
            {
                return IdentityFailure(removeResult);
            }
        }

        var addResult = await userManager.AddToRoleAsync(user, role.Name!);
        if (!addResult.Succeeded)
        {
            return IdentityFailure(addResult);
        }

        await userManager.UpdateSecurityStampAsync(user);
        var action = previousHealthUnitId != user.HealthUnitId ? "Transfer" : "Update";
        var summary = action == "Transfer"
            ? $"Usuário {user.UserName} transferido de UBS."
            : $"Usuário {user.UserName} atualizado.";
        AddAudit(dbContext, context, actor, action, "User", user.Id, summary, before, UserSnapshot(user, role.Name!));
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ResetUserPasswordAsync(
        Guid id,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        StuDbContext dbContext)
    {
        var actor = await GetActorAsync(context, userManager);
        if (actor.Id == id)
        {
            return Conflict("Redefinição bloqueada", "Use a troca de senha da própria conta.");
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Results.NotFound();
        }

        if (user.IsArchived)
        {
            return Conflict("Conta arquivada", "Reative a conta antes de redefinir a senha.");
        }

        var temporaryPassword = GenerateTemporaryPassword();
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var result = await userManager.ResetPasswordAsync(user, token, temporaryPassword);
        if (!result.Succeeded)
        {
            return IdentityFailure(result);
        }

        user.RequireTemporaryPasswordChange();
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return IdentityFailure(updateResult);
        }

        AddAudit(dbContext, context, actor, "PasswordReset", "User", user.Id, $"Senha temporária de {user.UserName} redefinida.", null, null);
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Ok(new { userId = user.Id, temporaryPassword });
    }

    private static async Task<IResult> ArchiveUserAsync(
        Guid id,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        StuDbContext dbContext)
    {
        var actor = await GetActorAsync(context, userManager);
        if (actor.Id == id)
        {
            return Conflict("Arquivamento bloqueado", "Você não pode arquivar sua própria conta.");
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Results.NotFound();
        }

        var roles = await userManager.GetRolesAsync(user);
        var before = UserSnapshot(user, roles.SingleOrDefault());
        user.Archive();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return IdentityFailure(result);
        }

        await userManager.UpdateSecurityStampAsync(user);
        AddAudit(dbContext, context, actor, "Archive", "User", user.Id, $"Usuário {user.UserName} arquivado.", before, UserSnapshot(user, roles.SingleOrDefault()));
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> RestoreUserAsync(
        Guid id,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        StuDbContext dbContext)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Results.NotFound();
        }

        var roleNames = await userManager.GetRolesAsync(user);
        var roleName = roleNames.SingleOrDefault();
        var role = roleName is null ? null : await roleManager.FindByNameAsync(roleName);
        if (role is null || role.IsArchived)
        {
            return Conflict("Função indisponível", "A função da conta precisa estar ativa antes da reativação.");
        }

        var healthUnitResult = await ValidateHealthUnitForRoleAsync(user.HealthUnitId, role, dbContext);
        if (healthUnitResult.Error is not null)
        {
            return healthUnitResult.Error;
        }

        var actor = await GetActorAsync(context, userManager);
        var before = UserSnapshot(user, roleName);
        user.Restore();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return IdentityFailure(result);
        }

        AddAudit(dbContext, context, actor, "Restore", "User", user.Id, $"Usuário {user.UserName} reativado.", before, UserSnapshot(user, roleName));
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> GetRolesAsync(
        RoleManager<ApplicationRole> roleManager,
        bool includeArchived = false)
    {
        var roles = await roleManager.Roles
            .Where(role => includeArchived || role.ArchivedAtUtc == null)
            .OrderByDescending(role => role.IsSystem)
            .ThenBy(role => role.DisplayName)
            .ToListAsync();
        var response = new List<object>();
        foreach (var role in roles)
        {
            var claims = await roleManager.GetClaimsAsync(role);
            response.Add(new
            {
                role.Id,
                name = role.Name,
                role.DisplayName,
                role.Description,
                role.IsSystem,
                role.ArchivedAtUtc,
                permissions = claims
                    .Where(claim => claim.Type == StuClaimTypes.Permission)
                    .Select(claim => claim.Value)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
            });
        }

        return Results.Ok(response);
    }

    private static IResult GetPermissions() => Results.Ok(StuPermissions.Catalog);

    private static async Task<IResult> CreateRoleAsync(
        CreateRoleRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        StuDbContext dbContext)
    {
        var validation = ValidateRole(request.DisplayName, request.Description, request.Permissions);
        if (validation is not null)
        {
            return validation;
        }

        var actor = await GetActorAsync(context, userManager);
        var role = ApplicationRole.CreateCustom(
            $"Custom_{Guid.NewGuid():N}",
            request.DisplayName,
            request.Description);

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var createResult = await roleManager.CreateAsync(role);
        if (!createResult.Succeeded)
        {
            return IdentityFailure(createResult);
        }

        foreach (var permission in request.Permissions.Distinct(StringComparer.Ordinal))
        {
            var claimResult = await roleManager.AddClaimAsync(role, new Claim(StuClaimTypes.Permission, permission));
            if (!claimResult.Succeeded)
            {
                return IdentityFailure(claimResult);
            }
        }

        AddAudit(dbContext, context, actor, "Create", "Role", role.Id, $"Função {role.DisplayName} criada.", null, RoleSnapshot(role, request.Permissions));
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.Created($"/api/admin/roles/{role.Id}", new { role.Id, role.Name });
    }

    private static async Task<IResult> UpdateRoleAsync(
        Guid id,
        UpdateRoleRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        StuDbContext dbContext)
    {
        var validation = ValidateRole(request.DisplayName, request.Description, request.Permissions);
        if (validation is not null)
        {
            return validation;
        }

        var role = await roleManager.FindByIdAsync(id.ToString());
        if (role is null)
        {
            return Results.NotFound();
        }

        if (role.IsSystem)
        {
            return Conflict("Função protegida", "Funções padrão do STU não podem ser alteradas.");
        }

        if (role.IsArchived)
        {
            return Conflict("Função arquivada", "Reative a função antes de alterá-la.");
        }

        var actor = await GetActorAsync(context, userManager);
        var currentClaims = await roleManager.GetClaimsAsync(role);
        var currentPermissions = currentClaims
            .Where(claim => claim.Type == StuClaimTypes.Permission)
            .Select(claim => claim.Value)
            .ToArray();
        var before = RoleSnapshot(role, currentPermissions);
        role.UpdateDetails(request.DisplayName, request.Description);

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var updateResult = await roleManager.UpdateAsync(role);
        if (!updateResult.Succeeded)
        {
            return IdentityFailure(updateResult);
        }

        foreach (var claim in currentClaims.Where(claim => claim.Type == StuClaimTypes.Permission))
        {
            var removeResult = await roleManager.RemoveClaimAsync(role, claim);
            if (!removeResult.Succeeded)
            {
                return IdentityFailure(removeResult);
            }
        }

        foreach (var permission in request.Permissions.Distinct(StringComparer.Ordinal))
        {
            var addResult = await roleManager.AddClaimAsync(role, new Claim(StuClaimTypes.Permission, permission));
            if (!addResult.Succeeded)
            {
                return IdentityFailure(addResult);
            }
        }

        await InvalidateRoleUsersAsync(role.Id, userManager, dbContext);
        AddAudit(dbContext, context, actor, "Update", "Role", role.Id, $"Função {role.DisplayName} atualizada.", before, RoleSnapshot(role, request.Permissions));
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ArchiveRoleAsync(
        Guid id,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        StuDbContext dbContext)
    {
        var role = await roleManager.FindByIdAsync(id.ToString());
        if (role is null)
        {
            return Results.NotFound();
        }

        if (role.IsSystem)
        {
            return Conflict("Função protegida", "Funções padrão do STU não podem ser arquivadas.");
        }

        var inUse = await dbContext.UserRoles
            .Where(link => link.RoleId == role.Id)
            .Join(dbContext.Users.Where(user => user.ArchivedAtUtc == null), link => link.UserId, user => user.Id, (_, _) => true)
            .AnyAsync();
        if (inUse)
        {
            return Conflict("Função em uso", "Altere ou arquive as contas vinculadas antes de arquivar esta função.");
        }

        var claims = await roleManager.GetClaimsAsync(role);
        var actor = await GetActorAsync(context, userManager);
        var permissionValues = claims.Where(claim => claim.Type == StuClaimTypes.Permission).Select(claim => claim.Value).ToArray();
        var before = RoleSnapshot(role, permissionValues);
        role.Archive();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var result = await roleManager.UpdateAsync(role);
        if (!result.Succeeded)
        {
            return IdentityFailure(result);
        }

        AddAudit(dbContext, context, actor, "Archive", "Role", role.Id, $"Função {role.DisplayName} arquivada.", before, RoleSnapshot(role, permissionValues));
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> RestoreRoleAsync(
        Guid id,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        StuDbContext dbContext)
    {
        var role = await roleManager.FindByIdAsync(id.ToString());
        if (role is null)
        {
            return Results.NotFound();
        }

        var claims = await roleManager.GetClaimsAsync(role);
        var actor = await GetActorAsync(context, userManager);
        var before = RoleSnapshot(role, claims.Where(claim => claim.Type == StuClaimTypes.Permission).Select(claim => claim.Value));
        role.Restore();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var result = await roleManager.UpdateAsync(role);
        if (!result.Succeeded)
        {
            return IdentityFailure(result);
        }

        AddAudit(dbContext, context, actor, "Restore", "Role", role.Id, $"Função {role.DisplayName} reativada.", before, RoleSnapshot(role, claims.Select(claim => claim.Value)));
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> GetAuditAsync(
        StuDbContext dbContext,
        string? entityType = null,
        int page = 1,
        int pageSize = 50)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = dbContext.AuditEntries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(entry => entry.EntityType == entityType.Trim());
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(entry => new
            {
                entry.Id,
                entry.OccurredAtUtc,
                entry.ActorUserId,
                entry.ActorUserName,
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                entry.Summary,
                entry.BeforeJson,
                entry.AfterJson,
                entry.IpAddress,
            })
            .ToListAsync();
        return Results.Ok(new { items, total, page, pageSize });
    }

    private static async Task<object[]> BuildUserResponsesAsync(
        IReadOnlyCollection<ApplicationUser> users,
        StuDbContext dbContext)
    {
        var userIds = users.Select(user => user.Id).ToArray();
        var unitIds = users.Where(user => user.HealthUnitId.HasValue).Select(user => user.HealthUnitId!.Value).Distinct().ToArray();
        var units = await dbContext.HealthUnits
            .Where(unit => unitIds.Contains(unit.Id))
            .ToDictionaryAsync(unit => unit.Id, unit => new { unit.Id, unit.Code, unit.Name, unit.ArchivedAtUtc });
        var roles = await dbContext.UserRoles
            .Where(link => userIds.Contains(link.UserId))
            .Join(dbContext.Roles, link => link.RoleId, role => role.Id, (link, role) => new { link.UserId, Role = role })
            .ToDictionaryAsync(item => item.UserId, item => item.Role);

        return users.Select(user => (object)new
        {
            user.Id,
            userName = user.UserName,
            user.DisplayName,
            user.HealthUnitId,
            healthUnit = user.HealthUnitId.HasValue && units.TryGetValue(user.HealthUnitId.Value, out var unit) ? unit : null,
            role = roles.TryGetValue(user.Id, out var role) ? new { role.Id, name = role.Name, role.DisplayName, role.IsSystem, role.ArchivedAtUtc } : null,
            user.MustChangePassword,
            user.LockoutEnd,
            user.ArchivedAtUtc,
        }).ToArray();
    }

    private static async Task<(Guid? HealthUnitId, IResult? Error)> ValidateHealthUnitForRoleAsync(
        Guid? requestedHealthUnitId,
        ApplicationRole role,
        StuDbContext dbContext)
    {
        if (role.Name == SystemRoles.GlobalAdministrator)
        {
            return (null, null);
        }

        if (!requestedHealthUnitId.HasValue)
        {
            return (null, Validation("healthUnitId", "Selecione a UBS desta conta."));
        }

        var unit = await dbContext.HealthUnits.FindAsync(requestedHealthUnitId.Value);
        if (unit is null || unit.IsArchived)
        {
            return (null, Validation("healthUnitId", "Selecione uma UBS ativa."));
        }

        return (unit.Id, null);
    }

    private static async Task InvalidateRoleUsersAsync(
        Guid roleId,
        UserManager<ApplicationUser> userManager,
        StuDbContext dbContext)
    {
        var userIds = await dbContext.UserRoles
            .Where(link => link.RoleId == roleId)
            .Select(link => link.UserId)
            .ToListAsync();
        foreach (var userId in userIds)
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            if (user is not null)
            {
                await userManager.UpdateSecurityStampAsync(user);
            }
        }
    }

    private static async Task<ApplicationUser> GetActorAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager) =>
        await userManager.GetUserAsync(context.User)
        ?? throw new InvalidOperationException("Authenticated administrator was not found.");

    private static void AddAudit(
        StuDbContext dbContext,
        HttpContext context,
        ApplicationUser actor,
        string action,
        string entityType,
        Guid entityId,
        string summary,
        string? beforeJson,
        string? afterJson)
    {
        dbContext.AuditEntries.Add(AuditEntry.Create(
            actor.Id,
            actor.UserName ?? actor.DisplayName,
            action,
            entityType,
            entityId.ToString(),
            summary,
            beforeJson,
            afterJson,
            context.Connection.RemoteIpAddress?.ToString()));
    }

    private static string Snapshot(HealthUnit unit) => JsonSerializer.Serialize(new
    {
        unit.Id,
        unit.Code,
        unit.Name,
        unit.ArchivedAtUtc,
    });

    private static string UserSnapshot(ApplicationUser user, string? roleName) => JsonSerializer.Serialize(new
    {
        user.Id,
        user.UserName,
        user.DisplayName,
        user.HealthUnitId,
        roleName,
        user.MustChangePassword,
        user.ArchivedAtUtc,
    });

    private static string RoleSnapshot(ApplicationRole role, IEnumerable<string> permissions) => JsonSerializer.Serialize(new
    {
        role.Id,
        role.Name,
        role.DisplayName,
        role.Description,
        role.IsSystem,
        role.ArchivedAtUtc,
        permissions = permissions.Order(StringComparer.Ordinal).ToArray(),
    });

    private static IResult? ValidateHealthUnit(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 32)
        {
            return Validation("code", "Informe um código de até 32 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 160)
        {
            return Validation("name", "Informe um nome de até 160 caracteres.");
        }

        return null;
    }

    private static IResult? ValidateUser(string userName, string displayName)
    {
        if (string.IsNullOrWhiteSpace(userName) || userName.Trim().Length is < 3 or > 120 ||
            userName.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '_' or '-')))
        {
            return Validation("userName", "Use de 3 a 120 letras, números, pontos, hífens ou sublinhados.");
        }

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 160)
        {
            return Validation("displayName", "Informe um nome de até 160 caracteres.");
        }

        return null;
    }

    private static IResult? ValidateRole(string displayName, string? description, string[] permissions)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 120)
        {
            return Validation("displayName", "Informe um nome de até 120 caracteres.");
        }

        if (description?.Trim().Length > 320)
        {
            return Validation("description", "A descrição pode ter até 320 caracteres.");
        }

        if (permissions.Length == 0 || permissions.Any(permission => !StuPermissions.IsConfigurable(permission)))
        {
            return Validation("permissions", "Selecione ao menos uma permissão válida.");
        }

        return null;
    }

    private static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%*-_";
        const string all = upper + lower + digits + symbols;
        var password = new char[18];
        password[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        password[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        password[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        password[3] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];
        for (var index = 4; index < password.Length; index++)
        {
            password[index] = all[RandomNumberGenerator.GetInt32(all.Length)];
        }

        for (var index = password.Length - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (password[index], password[swapIndex]) = (password[swapIndex], password[index]);
        }

        return new string(password);
    }

    private static IResult Validation(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static IResult Conflict(string title, string detail) => Results.Problem(
        title: title,
        detail: detail,
        statusCode: StatusCodes.Status409Conflict);

    private static IResult IdentityFailure(IdentityResult result) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["identity"] = result.Errors.Select(error => error.Description).ToArray(),
        });
}
