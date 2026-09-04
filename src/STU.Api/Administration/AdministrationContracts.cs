namespace STU.Api.Administration;

public sealed record CreateHealthUnitRequest(string Code, string Name);

public sealed record UpdateHealthUnitRequest(string Code, string Name);

public sealed record CreateUserRequest(
    string UserName,
    string DisplayName,
    Guid? HealthUnitId,
    string RoleName);

public sealed record UpdateUserRequest(
    string DisplayName,
    Guid? HealthUnitId,
    string RoleName);

public sealed record CreateRoleRequest(
    string DisplayName,
    string? Description,
    string[] Permissions);

public sealed record UpdateRoleRequest(
    string DisplayName,
    string? Description,
    string[] Permissions);
