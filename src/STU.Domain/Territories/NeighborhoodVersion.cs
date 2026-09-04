using NetTopologySuite.Geometries;

namespace STU.Domain.Territories;

public sealed class NeighborhoodVersion
{
    private NeighborhoodVersion() { }
    public Guid Id { get; private init; } = Guid.NewGuid();
    public Guid NeighborhoodId { get; private init; }
    public int VersionNumber { get; private init; }
    public string Name { get; private init; } = string.Empty;
    public Geometry Geometry { get; private init; } = default!;
    public TerritorySource Source { get; private init; }
    public string? ExternalReference { get; private init; }
    public string Color { get; private init; } = TerritoryColor.DefaultNeighborhood;
    public bool IsArchived { get; private init; }
    public string ChangeKind { get; private init; } = string.Empty;
    public Guid ChangedByUserId { get; private init; }
    public DateTimeOffset ChangedAtUtc { get; private init; } = DateTimeOffset.UtcNow;

    public static NeighborhoodVersion Capture(Neighborhood item, int versionNumber, string changeKind, Guid actorId) => new()
    {
        NeighborhoodId = item.Id, VersionNumber = versionNumber, Name = item.Name, Geometry = (Geometry)item.Geometry.Copy(),
        Source = item.Source, ExternalReference = item.ExternalReference, Color = item.Color, IsArchived = item.IsArchived,
        ChangeKind = changeKind, ChangedByUserId = actorId,
    };
}
