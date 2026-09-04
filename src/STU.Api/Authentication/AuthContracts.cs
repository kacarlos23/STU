namespace STU.Api.Authentication;

public sealed record LoginRequest(string UserName, string Password, string Portal);

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword);

public sealed record RoleResponse(string Name, string DisplayName);

public sealed record HealthUnitResponse(Guid Id, string Code, string Name);

public sealed record SessionResponse(
    Guid Id,
    string UserName,
    string DisplayName,
    bool MustChangePassword,
    HealthUnitResponse? HealthUnit,
    IReadOnlyList<RoleResponse> Roles,
    IReadOnlyList<string> Permissions);

