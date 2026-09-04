using NetTopologySuite.Geometries;
using STU.Domain.Common;

namespace STU.Domain.Territories;

public sealed class Neighborhood : Entity
{
    private Neighborhood() { }
    private Neighborhood(string name, Geometry geometry, TerritorySource source, string? externalReference, string? color)
    {
        Name = name.Trim(); Geometry = geometry; Source = source; ExternalReference = Normalize(externalReference);
        Color = TerritoryColor.Normalize(color, TerritoryColor.DefaultNeighborhood);
    }

    public string Name { get; private set; } = string.Empty;
    public Geometry Geometry { get; private set; } = default!;
    public TerritorySource Source { get; private set; }
    public string? ExternalReference { get; private set; }
    public string Color { get; private set; } = TerritoryColor.DefaultNeighborhood;
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();

    public static Neighborhood Create(string name, Geometry geometry, TerritorySource source, string? externalReference = null, string? color = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentNullException.ThrowIfNull(geometry);
        return new Neighborhood(name, geometry, source, externalReference, color);
    }

    public void Update(string name, Geometry geometry, TerritorySource source, string? externalReference, string? color = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentNullException.ThrowIfNull(geometry);
        Name = name.Trim(); Geometry = geometry; Source = source; ExternalReference = Normalize(externalReference);
        Color = TerritoryColor.Normalize(color, TerritoryColor.DefaultNeighborhood); Touch();
    }

    public void Archive() { if (!IsArchived) { ArchivedAtUtc = DateTimeOffset.UtcNow; Touch(); } }
    public void Restore() { if (IsArchived) { ArchivedAtUtc = null; Touch(); } }
    private void Touch() { UpdatedAtUtc = DateTimeOffset.UtcNow; ConcurrencyToken = Guid.NewGuid(); }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
