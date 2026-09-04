namespace STU.Api.Operations;

public sealed record CreateExportRequest(Guid? HealthUnitId, string Format, Guid? MicroregionId, string? Situation, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc);
