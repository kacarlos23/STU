using NetTopologySuite.Geometries;
using STU.Domain.Territories;

namespace STU.UnitTests.Territories;

public sealed class TerritoryTests
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    [Fact]
    public void MicroregionNormalizesCodeAndPreservesAssignment()
    {
        var polygon = Factory.CreatePolygon([
            new(-46.7, -23.6), new(-46.6, -23.6), new(-46.6, -23.5), new(-46.7, -23.6),
        ]);
        var boundary = Factory.CreateMultiPolygon([polygon]);
        var agentId = Guid.NewGuid();

        var item = Microregion.Create(" mr-01 ", " Norte ", Guid.NewGuid(), agentId, boundary, TerritorySource.Manual, "#A1B2C3");

        Assert.Equal("MR-01", item.Code);
        Assert.Equal("Norte", item.Name);
        Assert.Equal("NORTE", item.NormalizedName);
        Assert.Equal(agentId, item.AssignedAgentId);
        Assert.Equal("#a1b2c3", item.Color);
        Assert.False(item.IsArchived);
    }

    [Fact]
    public void TerritorialUpdateChangesConcurrencyTokenAndArchiveIsRecoverable()
    {
        var point = Factory.CreatePoint(new Coordinate(-46.65, -23.55));
        var item = Neighborhood.Create("Centro", point, TerritorySource.OpenStreetMap, "relation/1");
        var initialToken = item.ConcurrencyToken;

        item.Update("Centro ampliado", point, TerritorySource.GeoJsonImport, null);
        item.Archive();

        Assert.NotEqual(initialToken, item.ConcurrencyToken);
        Assert.True(item.IsArchived);
        item.Restore();
        Assert.False(item.IsArchived);
    }

    [Fact]
    public void NewBoundaryIsCutAgainstExistingAreaWithoutChangingTheFirstArea()
    {
        var existing = Square(0, 0, 2, 2);
        var candidate = Square(1, 0, 3, 2);
        var existingCopy = (Polygon)existing.Copy();

        var result = TerritoryBoundaryFitter.Fit(candidate, [existing]);

        Assert.False(result.IsEmpty);
        Assert.True(result.Adjusted);
        Assert.Equal(2, result.Geometry!.Area, 6);
        Assert.Equal(0, existing.Intersection(result.Geometry).Area, 6);
        Assert.Equal(2, existing.Boundary.Intersection(result.Geometry.Boundary).Length, 6);
        Assert.True(existing.EqualsExact(existingCopy));
    }

    [Fact]
    public void FullyCoveredBoundaryIsRejectedByTheFitter()
    {
        var result = TerritoryBoundaryFitter.Fit(Square(0.5, 0.5, 1.5, 1.5), [Square(0, 0, 2, 2)]);

        Assert.True(result.IsEmpty);
        Assert.True(result.Adjusted);
    }

    private static Polygon Square(double minX, double minY, double maxX, double maxY) => Factory.CreatePolygon([
        new(minX, minY), new(maxX, minY), new(maxX, maxY), new(minX, maxY), new(minX, minY),
    ]);
}
