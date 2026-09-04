using NetTopologySuite.Geometries;

namespace STU.Domain.Territories;

public sealed class MicroregionVersion
{
    private MicroregionVersion() { }
    public Guid Id { get; private init; } = Guid.NewGuid();
    public Guid MicroregionId { get; private init; }
    public int VersionNumber { get; private init; }
    public string Code { get; private init; } = string.Empty;
    public string Name { get; private init; } = string.Empty;
    public Guid[] NeighborhoodIds { get; private init; } = [];
    public Guid HealthUnitId { get; private init; }
    public Guid? AssignedAgentId { get; private init; }
    public MultiPolygon Boundary { get; private init; } = default!;
    public TerritorySource Source { get; private init; }
    public string Color { get; private init; } = TerritoryColor.DefaultMicroregion;
    public bool IsArchived { get; private init; }
    public string ChangeKind { get; private init; } = string.Empty;
    public Guid ChangedByUserId { get; private init; }
    public DateTimeOffset ChangedAtUtc { get; private init; } = DateTimeOffset.UtcNow;

    public static MicroregionVersion Capture(Microregion item, IReadOnlyCollection<Guid> neighborhoodIds, int versionNumber, string changeKind, Guid actorId) => new()
    {
        MicroregionId = item.Id, VersionNumber = versionNumber, Code = item.Code, Name = item.Name,
        NeighborhoodIds = neighborhoodIds.Distinct().Order().ToArray(), HealthUnitId = item.HealthUnitId, AssignedAgentId = item.AssignedAgentId,
        Boundary = (MultiPolygon)item.Boundary.Copy(), Source = item.Source, Color = item.Color, IsArchived = item.IsArchived,
        ChangeKind = changeKind, ChangedByUserId = actorId,
    };
}
