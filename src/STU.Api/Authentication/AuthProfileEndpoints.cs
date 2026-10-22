using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using STU.Application.Security;
using STU.Domain.Auditing;
using STU.Domain.Operations;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.Api.Authentication;

public static partial class AuthEndpoints
{
    private static void MapProfileEndpoints(RouteGroupBuilder auth)
    {
        var profile = auth.MapGroup("/profile").RequireAuthorization(StuPolicies.PasswordChanged);
        profile.MapGet("", GetProfileAsync);
        profile.MapGet("/photo", GetProfilePhotoAsync);
        profile.MapPut("", SaveProfileAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true), new RequestSizeLimitAttribute(750_000));
        profile.MapPost("/password-reset-request", RequestPasswordResetAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
    }

    private static async Task<IResult> GetProfileAsync(HttpContext http, UserManager<ApplicationUser> users)
    {
        var user = await users.GetUserAsync(http.User);
        return user is null || user.IsArchived ? Results.Unauthorized() : Results.Ok(ProfileResponse(user));
    }

    private static async Task<IResult> GetProfilePhotoAsync(HttpContext http, UserManager<ApplicationUser> users)
    {
        var user = await users.GetUserAsync(http.User);
        if (user is null || user.IsArchived) return Results.Unauthorized();
        return user.ProfilePhoto is null ? Results.NotFound() : Results.Bytes(user.ProfilePhoto, "image/png");
    }

    private static async Task<IResult> SaveProfileAsync(SaveProfileRequest request, HttpContext http,
        UserManager<ApplicationUser> users, StuDbContext db, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(http.User);
        if (user is null || user.IsArchived) return Results.Unauthorized();
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 160)
            errors["displayName"] = ["Informe um nome de até 160 caracteres."];
        if (request.Appearance is not ("light" or "dark")) errors["appearance"] = ["Escolha Claro ou Escuro."];
        if (request.Photo is not null && (request.RemovePhoto || !ProfilePhotoValidator.IsValid(request.Photo)))
            errors["photo"] = ["Envie uma foto PNG válida de até 512 KB e 512 × 512 pixels."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (string.IsNullOrEmpty(request.ConcurrencyToken) || request.ConcurrencyToken != user.ConcurrencyStamp)
            return ProfileConflict();

        var before = ProfileSnapshot(user);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        user.UpdateAccountSettings(request.DisplayName!, request.Appearance!);
        if (request.RemovePhoto || request.Photo is not null) user.SetProfilePhoto(request.RemovePhoto ? null : request.Photo);
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) return ProfileConflict();
        db.AuditEntries.Add(AuditEntry.Create(user.Id, user.UserName ?? user.DisplayName, "Update", "User", user.Id.ToString(),
            "Preferências e perfil da própria conta atualizados.", before, ProfileSnapshot(user), http.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(ProfileResponse(user));
    }

    private static async Task<IResult> RequestPasswordResetAsync(HttpContext http, UserManager<ApplicationUser> users,
        StuDbContext db, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(http.User);
        if (user is null || user.IsArchived) return Results.Unauthorized();
        if (user.PasswordResetRequestedAtUtc.HasValue) return Results.Ok(ProfileResponse(user));

        var protectedAccount = await users.IsInRoleAsync(user, SystemRoles.HealthUnitManager) ||
                               await users.IsInRoleAsync(user, SystemRoles.GlobalAdministrator);
        var recipients = protectedAccount || user.HealthUnitId is null
            ? new List<ApplicationUser>()
            : (await users.GetUsersInRoleAsync(SystemRoles.HealthUnitManager))
                .Where(candidate => candidate.HealthUnitId == user.HealthUnitId && candidate.Id != user.Id && !candidate.IsArchived).ToList();
        var global = recipients.Count == 0;
        if (global) recipients = (await users.GetUsersInRoleAsync(SystemRoles.GlobalAdministrator))
            .Where(candidate => candidate.Id != user.Id && !candidate.IsArchived).ToList();
        if (recipients.Count == 0) return Results.Problem(statusCode: 409, title: "Responsável indisponível",
            detail: "Não há um responsável ativo para receber o pedido. Procure a administração do sistema.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        user.RequestPasswordReset();
        if (!(await users.UpdateAsync(user)).Succeeded) return ProfileConflict();
        foreach (var recipient in recipients)
            db.UserNotifications.Add(UserNotification.Create(recipient.Id, user.HealthUnitId, NotificationKind.Information,
                "Pedido de redefinição de senha", $"{user.DisplayName} (@{user.UserName}) pediu a redefinição de senha. Abra a conta na gestão de usuários para atender.",
                global ? "/admin/users" : "/users"));
        db.AuditEntries.Add(AuditEntry.Create(user.Id, user.UserName ?? user.DisplayName, "PasswordResetRequest", "User", user.Id.ToString(),
            "Solicitada redefinição de senha ao responsável.", null, null, http.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(ProfileResponse(user));
    }

    private static object ProfileResponse(ApplicationUser user) => new
    {
        user.DisplayName, user.UserName, appearance = user.AppearancePreference,
        avatarUrl = user.ProfilePhotoVersion is null ? null : $"/api/auth/profile/photo?v={user.ProfilePhotoVersion}",
        user.PasswordResetRequestedAtUtc, concurrencyToken = user.ConcurrencyStamp,
    };
    private static string ProfileSnapshot(ApplicationUser user) => JsonSerializer.Serialize(new
        { user.DisplayName, user.AppearancePreference, user.ProfilePhotoVersion });
    private static IResult ProfileConflict() => Results.Problem(statusCode: 409, title: "Perfil alterado",
        detail: "A conta foi alterada em outra sessão. Recarregue as configurações antes de salvar.");
    private sealed record SaveProfileRequest(string? DisplayName, string? Appearance, string? ConcurrencyToken, byte[]? Photo, bool RemovePhoto);
}
