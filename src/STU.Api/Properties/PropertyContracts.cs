using System.Text.Json;

namespace STU.Api.Properties;

public sealed record SavePropertyRequest(Guid MicroregionId,string Street,string HouseNumber,string? PostalCode,string? Complement,JsonElement Geometry,string RegistrationStatus,string Situation,Guid[] TagIds,Guid? ExpectedVersion);
public sealed record SaveVisitRequest(DateTimeOffset VisitedAtUtc,string Type,string Outcome,string ObservedSituation,bool AccessDifficulty,string? Note,Guid? ExpectedVersion,Guid? ExpectedFamilyVersion = null);
public sealed record SaveTagRequest(Guid HealthUnitId,string Name,string Color);
public sealed record SaveCoverageRuleRequest(Guid HealthUnitId,int MaxDaysWithoutVisit);
