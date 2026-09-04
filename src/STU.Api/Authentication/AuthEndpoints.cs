using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using STU.Application.Security;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.Api.Authentication;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth")
            .WithTags("Authentication")
            .RequireRateLimiting("api");

        group.MapGet("/csrf", GetAntiforgeryToken)
            .AllowAnonymous();

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true))
            .RequireRateLimiting("login");

        group.MapGet("/me", GetCurrentSessionAsync)
            .RequireAuthorization();

        group.MapPost("/change-password", ChangePasswordAsync)
            .RequireAuthorization()
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));

        return endpoints;
    }

    private static IResult GetAntiforgeryToken(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        return Results.Ok(new { token = tokens.RequestToken });
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<ApplicationRole> roleManager,
        StuDbContext dbContext)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            request.UserName.Length > 120 ||
            request.Password.Length > 200)
        {
            return InvalidCredentials();
        }

        var portal = request.Portal.Trim().ToLowerInvariant();
        if (portal is not ("main" or "admin"))
        {
            return Results.Problem(
                title: "Portal inválido",
                detail: "Atualize a página e tente novamente.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await userManager.FindByNameAsync(request.UserName.Trim());
        if (user is null || user.IsArchived)
        {
            await Task.Delay(Random.Shared.Next(80, 151));
            return InvalidCredentials();
        }

        var result = await signInManager.PasswordSignInAsync(
            user,
            request.Password,
            isPersistent: false,
            lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return InvalidCredentials();
        }

        var isGlobalAdministrator = await userManager.IsInRoleAsync(
            user,
            SystemRoles.GlobalAdministrator);
        if (portal == "admin" && !isGlobalAdministrator)
        {
            await signInManager.SignOutAsync();
            return Results.Problem(
                title: "Acesso não autorizado",
                detail: "Esta conta não possui acesso à administração global.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (portal == "main" && isGlobalAdministrator)
        {
            await signInManager.SignOutAsync();
            return Results.Problem(
                title: "Use a administraÃ§Ã£o global",
                detail: "Esta conta possui acesso somente ao portal administrativo isolado.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!isGlobalAdministrator && user.HealthUnitId is null)
        {
            await signInManager.SignOutAsync();
            return Results.Problem(
                title: "Conta sem UBS",
                detail: "Solicite ao gerente ou administrador a vinculação da sua conta.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Ok(await BuildSessionAsync(user, userManager, roleManager, dbContext));
    }

    private static async Task<IResult> GetCurrentSessionAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<ApplicationRole> roleManager,
        StuDbContext dbContext)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null || user.IsArchived)
        {
            await signInManager.SignOutAsync();
            return Results.Unauthorized();
        }

        return Results.Ok(await BuildSessionAsync(user, userManager, roleManager, dbContext));
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<ApplicationRole> roleManager,
        StuDbContext dbContext)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null || user.IsArchived)
        {
            return Results.Unauthorized();
        }

        var errors = ValidatePasswordRequest(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await userManager.ChangePasswordAsync(
            user,
            request.CurrentPassword,
            request.NewPassword);
        if (!result.Succeeded)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = result.Errors.Select(error => error.Description).ToArray(),
            });
        }

        user.CompleteTemporaryPasswordChange();
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return Results.Problem(
                title: "Não foi possível concluir a alteração",
                detail: "Tente novamente. Se o problema continuar, procure o administrador.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        await signInManager.RefreshSignInAsync(user);
        return Results.Ok(await BuildSessionAsync(user, userManager, roleManager, dbContext));
    }

    private static async Task<IResult> LogoutAsync(SignInManager<ApplicationUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<SessionResponse> BuildSessionAsync(
        ApplicationUser user,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        StuDbContext dbContext)
    {
        var roleNames = await userManager.GetRolesAsync(user);
        var roles = await roleManager.Roles
            .Where(role => role.Name != null && roleNames.Contains(role.Name))
            .OrderBy(role => role.DisplayName)
            .Select(role => new RoleResponse(role.Name!, role.DisplayName))
            .ToListAsync();

        var permissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            var storedRole = await roleManager.FindByNameAsync(role.Name);
            if (storedRole is null)
            {
                continue;
            }

            foreach (var claim in await roleManager.GetClaimsAsync(storedRole))
            {
                if (claim.Type == StuClaimTypes.Permission)
                {
                    permissions.Add(claim.Value);
                }
            }
        }

        HealthUnitResponse? healthUnit = null;
        if (user.HealthUnitId.HasValue)
        {
            healthUnit = await dbContext.HealthUnits
                .Where(unit => unit.Id == user.HealthUnitId.Value)
                .Select(unit => new HealthUnitResponse(unit.Id, unit.Code, unit.Name))
                .SingleOrDefaultAsync();
        }

        return new SessionResponse(
            user.Id,
            user.UserName ?? string.Empty,
            user.DisplayName,
            user.MustChangePassword,
            healthUnit,
            roles,
            permissions.Order(StringComparer.Ordinal).ToArray());
    }

    private static Dictionary<string, string[]> ValidatePasswordRequest(ChangePasswordRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            errors["currentPassword"] = ["Informe a senha temporária ou atual."];
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length > 200)
        {
            errors["newPassword"] = ["Informe uma nova senha válida."];
        }
        else if (request.NewPassword == request.CurrentPassword)
        {
            errors["newPassword"] = ["A nova senha precisa ser diferente da senha atual."];
        }

        if (request.NewPassword != request.ConfirmPassword)
        {
            errors["confirmPassword"] = ["A confirmação não corresponde à nova senha."];
        }

        return errors;
    }

    private static IResult InvalidCredentials() => Results.Problem(
        title: "Não foi possível entrar",
        detail: "Usuário ou senha inválidos.",
        statusCode: StatusCodes.Status401Unauthorized);
}
