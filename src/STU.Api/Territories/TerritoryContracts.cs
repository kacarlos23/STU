using System.Text.Json;

namespace STU.Api.Territories;

public sealed record SaveNeighborhoodRequest(
    string Name,
    JsonElement Geometry,
    string Source,
    string? ExternalReference,
    string? Color,
    Guid? ExpectedVersion);

public sealed record SaveMicroregionRequest(
    string Code,
    string Name,
    Guid HealthUnitId,
    Guid? AssignedAgentId,
    JsonElement Geometry,
    string Source,
    string? Color,
    Guid? ExpectedVersion,
    Guid? MicroregionId);
