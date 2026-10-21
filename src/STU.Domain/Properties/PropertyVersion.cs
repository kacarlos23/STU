using NetTopologySuite.Geometries;

namespace STU.Domain.Properties;

public sealed class PropertyVersion
{
    private PropertyVersion() { }
    public Guid Id { get; private init; } = Guid.NewGuid();
    public Guid PropertyId { get; private init; }
    public int VersionNumber { get; private init; }
    public Guid HealthUnitId { get; private init; }
    public Guid MicroregionId { get; private init; }
    public string Street { get; private init; } = string.Empty;
    public string HouseNumber { get; private init; } = string.Empty;
    public string? PostalCode { get; private init; }
    public string? Complement { get; private init; }
    public Geometry? Geometry { get; private init; }
    public Guid? MicroregionAgentId { get; private init; }
    public Guid? NeighborhoodId { get; private init; }
    public string? Reference { get; private init; }
    public PropertyRegistrationStatus RegistrationStatus { get; private init; }
    public PropertySituation Situation { get; private init; }
    public bool IsArchived { get; private init; }
    public string ChangeKind { get; private init; } = string.Empty;
    public Guid ChangedByUserId { get; private init; }
    public DateTimeOffset ChangedAtUtc { get; private init; } = DateTimeOffset.UtcNow;

    public static PropertyVersion Capture(HealthProperty item, int number, string kind, Guid actorId) => new()
    {
        PropertyId=item.Id,VersionNumber=number,HealthUnitId=item.HealthUnitId,MicroregionId=item.MicroregionId,Street=item.Street,
        HouseNumber=item.HouseNumber,PostalCode=item.PostalCode,Complement=item.Complement,
        Geometry=item.Geometry?.Copy(),MicroregionAgentId=item.MicroregionAgentId,NeighborhoodId=item.NeighborhoodId,Reference=item.Reference,
        RegistrationStatus=item.RegistrationStatus,Situation=item.Situation,IsArchived=item.IsArchived,
        ChangeKind=kind,ChangedByUserId=actorId,
    };
}
