using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using STU.Application.Security;
using STU.Domain.Auditing;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.Api.HealthUnitUsers;

public static class HealthUnitUserEndpoints
{
    public static IEndpointRouteBuilder MapHealthUnitUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/health-unit-users")
            .WithTags("Health unit users")
            .RequireAuthorization(StuPolicies.HealthUnitUsersManage)
            .RequireRateLimiting("api");

        group.MapGet("", GetUsersAsync);
        group.MapGet("/reference-data", GetReferenceDataAsync);
        Secure(group.MapPost("", CreateUserAsync));
        Secure(group.MapPut("/{id:guid}", UpdateUserAsync));
        Secure(group.MapPost("/{id:guid}/reset-password", ResetPasswordAsync));
        Secure(group.MapPost("/{id:guid}/archive", ArchiveUserAsync));
        Secure(group.MapPost("/{id:guid}/restore", RestoreUserAsync));
        return endpoints;
    }

    private static RouteHandlerBuilder Secure(RouteHandlerBuilder route) =>
        route.WithMetadata(new RequireAntiforgeryTokenAttribute(true));

    private static async Task<IResult> GetUsersAsync(
        string? query,
        bool? includeArchived,
        int? page,
        int? pageSize,
        HttpContext http,
        UserManager<ApplicationUser> userManager,
        StuDbContext db,
        CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(http, userManager);
        if (!actor.HealthUnitId.HasValue) return Results.Forbid();
        var currentPage = Math.Max(page ?? 1, 1);
        var currentPageSize = Math.Clamp(pageSize ?? 50, 1, 100);
        var usersQuery = db.Users.AsNoTracking().Where(item =>
            item.HealthUnitId == actor.HealthUnitId && (includeArchived == true || item.ArchivedAtUtc == null));
        if (!string.IsNullOrWhiteSpace(query))
        {
            var search = $"%{query.Trim()}%";
            usersQuery = usersQuery.Where(item =>
                EF.Functions.ILike(item.DisplayName, search) ||
                (item.UserName != null && EF.Functions.ILike(item.UserName, search)));
        }

        var total = await usersQuery.CountAsync(cancellationToken);
        var users = await usersQuery.OrderBy(item => item.DisplayName).ThenBy(item => item.Id)
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize).ToListAsync(cancellationToken);
        return Results.Ok(new
        {
            items = await ResponsesAsync(users, actor.Id, db, cancellationToken),
            total,
            page = currentPage,
            pageSize = currentPageSize,
        });
    }

    private static async Task<IResult> GetReferenceDataAsync(
        HttpContext http,
        UserManager<ApplicationUser> userManager,
        StuDbContext db,
        CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(http, userManager);
        if (!actor.HealthUnitId.HasValue) return Results.Forbid();
        var unit = await db.HealthUnits.AsNoTracking().SingleAsync(item => item.Id == actor.HealthUnitId, cancellationToken);
        var roles = await db.Roles.AsNoTracking()
            .Where(item => item.ArchivedAtUtc == null && item.Name != SystemRoles.GlobalAdministrator && item.Name != SystemRoles.HealthUnitManager)
            .OrderByDescending(item => item.IsSystem)
            .ThenBy(item => item.DisplayName)
            .Select(item => new { item.Id, item.Name, item.DisplayName, item.Description, item.IsSystem })
            .ToListAsync(cancellationToken);
        return Results.Ok(new { healthUnit = new { unit.Id, unit.Code, unit.Name }, roles });
    }

    private static async Task<IResult> CreateUserAsync(
        SaveUserRequest request,
        HttpContext http,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        StuDbContext db,
        CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(http, userManager);
        if (!actor.HealthUnitId.HasValue) return Results.Forbid();
        var validation = ValidateUser(request.UserName, request.DisplayName);
        if (validation is not null) return validation;
        var roleResult = await AssignableRoleAsync(request.RoleName, roleManager);
        if (roleResult.Error is not null) return roleResult.Error;

        var temporaryPassword = GenerateTemporaryPassword();
        var user = ApplicationUser.Create(request.UserName!.Trim(), request.DisplayName!, actor.HealthUnitId.Value);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var created = await userManager.CreateAsync(user, temporaryPassword);
        if (!created.Succeeded) return IdentityFailure(created);
        var assigned = await userManager.AddToRoleAsync(user, roleResult.Role!.Name!);
        if (!assigned.Succeeded) return IdentityFailure(assigned);
        Audit(db, http, actor, "Create", user, roleResult.Role.Name!, $"Servidor {user.UserName} cadastrado na UBS.");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Created($"/api/health-unit-users/{user.Id}", new { userId = user.Id, userName = user.UserName, temporaryPassword });
    }

    private static async Task<IResult> UpdateUserAsync(
        Guid id,
        SaveUserRequest request,
        HttpContext http,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        StuDbContext db,
        CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(http, userManager);
        var target = await ManageableTargetAsync(id, actor, userManager);
        if (target.Error is not null) return target.Error;
        if (target.User!.IsArchived) return Conflict("Conta arquivada", "Reative a conta antes de alterá-la.");
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 160)
            return Validation("displayName", "Informe um nome de até 160 caracteres.");
        var roleResult = await AssignableRoleAsync(request.RoleName, roleManager);
        if (roleResult.Error is not null) return roleResult.Error;

        var currentRoles = await userManager.GetRolesAsync(target.User);
        var before = Snapshot(target.User, currentRoles.SingleOrDefault());
        target.User.UpdateProfile(request.DisplayName, actor.HealthUnitId);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var updated = await userManager.UpdateAsync(target.User);
        if (!updated.Succeeded) return IdentityFailure(updated);
        if (currentRoles.Count > 0)
        {
            var removed = await userManager.RemoveFromRolesAsync(target.User, currentRoles);
            if (!removed.Succeeded) return IdentityFailure(removed);
        }
        var assigned = await userManager.AddToRoleAsync(target.User, roleResult.Role!.Name!);
        if (!assigned.Succeeded) return IdentityFailure(assigned);
        await userManager.UpdateSecurityStampAsync(target.User);
        db.AuditEntries.Add(AuditEntry.Create(actor.Id, actor.UserName ?? actor.DisplayName, "Update", "User", target.User.Id.ToString(), $"Servidor {target.User.UserName} atualizado pela gerência da UBS.", before, Snapshot(target.User, roleResult.Role.Name), http.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ResetPasswordAsync(
        Guid id,
        HttpContext http,
        UserManager<ApplicationUser> userManager,
        StuDbContext db,
        CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(http, userManager);
        var target = await ManageableTargetAsync(id, actor, userManager);
        if (target.Error is not null) return target.Error;
        if (target.User!.IsArchived) return Conflict("Conta arquivada", "Reative a conta antes de redefinir a senha.");
        var temporaryPassword = GenerateTemporaryPassword();
        var token = await userManager.GeneratePasswordResetTokenAsync(target.User);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var reset = await userManager.ResetPasswordAsync(target.User, token, temporaryPassword);
        if (!reset.Succeeded) return IdentityFailure(reset);
        target.User.RequireTemporaryPasswordChange();
        var updated = await userManager.UpdateAsync(target.User);
        if (!updated.Succeeded) return IdentityFailure(updated);
        db.AuditEntries.Add(AuditEntry.Create(actor.Id, actor.UserName ?? actor.DisplayName, "PasswordReset", "User", target.User.Id.ToString(), $"Senha temporária de {target.User.UserName} redefinida pela gerência da UBS.", null, null, http.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { userId = target.User.Id, temporaryPassword });
    }

    private static async Task<IResult> ArchiveUserAsync(
        Guid id,
        HttpContext http,
        UserManager<ApplicationUser> userManager,
        StuDbContext db,
        CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(http, userManager);
        var target = await ManageableTargetAsync(id, actor, userManager);
        if (target.Error is not null) return target.Error;
        var roleName = (await userManager.GetRolesAsync(target.User!)).SingleOrDefault();
        var before = Snapshot(target.User!, roleName);
        target.User!.Archive();
        var updated = await userManager.UpdateAsync(target.User);
        if (!updated.Succeeded) return IdentityFailure(updated);
        await userManager.UpdateSecurityStampAsync(target.User);
        db.AuditEntries.Add(AuditEntry.Create(actor.Id, actor.UserName ?? actor.DisplayName, "Archive", "User", target.User.Id.ToString(), $"Servidor {target.User.UserName} arquivado pela gerência da UBS.", before, Snapshot(target.User, roleName), http.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RestoreUserAsync(
        Guid id,
        HttpContext http,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        StuDbContext db,
        CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(http, userManager);
        var target = await ManageableTargetAsync(id, actor, userManager);
        if (target.Error is not null) return target.Error;
        var roleName = (await userManager.GetRolesAsync(target.User!)).SingleOrDefault();
        var role = roleName is null ? null : await roleManager.FindByNameAsync(roleName);
        if (role is null || role.IsArchived || IsProtected(role.Name)) return Conflict("Função indisponível", "A função da conta precisa estar ativa e ser operacional.");
        var before = Snapshot(target.User!, roleName);
        target.User!.Restore();
        var updated = await userManager.UpdateAsync(target.User);
        if (!updated.Succeeded) return IdentityFailure(updated);
        db.AuditEntries.Add(AuditEntry.Create(actor.Id, actor.UserName ?? actor.DisplayName, "Restore", "User", target.User.Id.ToString(), $"Servidor {target.User.UserName} reativado pela gerência da UBS.", before, Snapshot(target.User, roleName), http.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<object[]> ResponsesAsync(IReadOnlyCollection<ApplicationUser> users, Guid actorId, StuDbContext db, CancellationToken cancellationToken)
    {
        var ids = users.Select(item => item.Id).ToArray();
        var roles = await (from link in db.UserRoles.AsNoTracking()
                           join role in db.Roles.AsNoTracking() on link.RoleId equals role.Id
                           where ids.Contains(link.UserId)
                           select new { link.UserId, Role = role }).ToListAsync(cancellationToken);
        var byUser = roles.GroupBy(item => item.UserId).ToDictionary(group => group.Key, group => group.First().Role);
        return users.Select(user =>
        {
            byUser.TryGetValue(user.Id, out var role);
            return (object)new
            {
                user.Id,
                userName = user.UserName,
                user.DisplayName,
                role = role is null ? null : new { role.Id, role.Name, role.DisplayName, role.IsSystem },
                user.MustChangePassword,
                user.LockoutEnd,
                user.ArchivedAtUtc,
                manageable = user.Id != actorId && !IsProtected(role?.Name),
            };
        }).ToArray();
    }

    private static async Task<TargetResult> ManageableTargetAsync(Guid id, ApplicationUser actor, UserManager<ApplicationUser> userManager)
    {
        if (!actor.HealthUnitId.HasValue || actor.Id == id) return new(null, Conflict("Alteração bloqueada", "Sua própria conta não pode ser alterada por esta área."));
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return new(null, Results.NotFound());
        if (user.HealthUnitId != actor.HealthUnitId) return new(null, Results.Forbid());
        var roles = await userManager.GetRolesAsync(user);
        if (roles.Any(IsProtected)) return new(null, Results.Forbid());
        return new(user, null);
    }

    private static async Task<RoleResult> AssignableRoleAsync(string? roleName, RoleManager<ApplicationRole> roleManager)
    {
        if (string.IsNullOrWhiteSpace(roleName)) return new(null, Validation("roleName", "Selecione uma função."));
        var role = await roleManager.FindByNameAsync(roleName.Trim());
        if (role is null || role.IsArchived || IsProtected(role.Name)) return new(null, Validation("roleName", "Selecione uma função operacional ativa."));
        return new(role, null);
    }

    private static bool IsProtected(string? roleName) => roleName is SystemRoles.GlobalAdministrator or SystemRoles.HealthUnitManager;
    private static async Task<ApplicationUser> ActorAsync(HttpContext http, UserManager<ApplicationUser> userManager) => await userManager.GetUserAsync(http.User) ?? throw new InvalidOperationException("Usuário autenticado não encontrado.");
    private static IResult? ValidateUser(string? userName, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(userName) || userName.Trim().Length is < 3 or > 120 || userName.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '_' or '-'))) return Validation("userName", "Use de 3 a 120 letras, números, pontos, hífens ou sublinhados.");
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 160) return Validation("displayName", "Informe um nome de até 160 caracteres.");
        return null;
    }
    private static void Audit(StuDbContext db, HttpContext http, ApplicationUser actor, string action, ApplicationUser user, string roleName, string summary) => db.AuditEntries.Add(AuditEntry.Create(actor.Id, actor.UserName ?? actor.DisplayName, action, "User", user.Id.ToString(), summary, null, Snapshot(user, roleName), http.Connection.RemoteIpAddress?.ToString()));
    private static string Snapshot(ApplicationUser user, string? roleName) => JsonSerializer.Serialize(new { user.Id, user.UserName, user.DisplayName, user.HealthUnitId, roleName, user.MustChangePassword, user.ArchivedAtUtc });
    private static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", lower = "abcdefghijkmnopqrstuvwxyz", digits = "23456789", symbols = "!@#$%*-_";
        var all = upper + lower + digits + symbols;
        var password = new char[18];
        password[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)]; password[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)]; password[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)]; password[3] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];
        for (var index = 4; index < password.Length; index++) password[index] = all[RandomNumberGenerator.GetInt32(all.Length)];
        for (var index = password.Length - 1; index > 0; index--) { var swap = RandomNumberGenerator.GetInt32(index + 1); (password[index], password[swap]) = (password[swap], password[index]); }
        return new string(password);
    }
    private static IResult Validation(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
    private static IResult Conflict(string title, string detail) => Results.Problem(title: title, detail: detail, statusCode: StatusCodes.Status409Conflict);
    private static IResult IdentityFailure(IdentityResult result) => Results.ValidationProblem(new Dictionary<string, string[]> { ["identity"] = result.Errors.Select(item => item.Description).ToArray() });

    private sealed record SaveUserRequest(string? UserName, string? DisplayName, string? RoleName);
    private sealed record RoleResult(ApplicationRole? Role, IResult? Error);
    private sealed record TargetResult(ApplicationUser? User, IResult? Error);
}
