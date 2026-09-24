using NetTopologySuite.Geometries;
using STU.Domain.Properties;

namespace STU.UnitTests.Properties;

public sealed class PropertyTests
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    [Fact]
    public void PropertyNormalizesIdentifiersAndPreservesPreviousVersion()
    {
        var actorId = Guid.NewGuid();
        var item = HealthProperty.Create(
            Guid.NewGuid(), Guid.NewGuid(), " Rua das Flores ", " 12-a ",
            "12345-000", null, Factory.CreatePoint(new Coordinate(-46.63, -23.55)),
            PropertyRegistrationStatus.Active, PropertySituation.Occupied);

        var firstVersion = PropertyVersion.Capture(item, 1, "Create", actorId);
        item.Update(item.MicroregionId, item.Street, "14", item.PostalCode, null,
            item.Geometry, item.RegistrationStatus, item.Situation);

        Assert.Equal("12-A", firstVersion.HouseNumber);
        Assert.Equal("14", item.HouseNumber);
        Assert.NotSame(firstVersion.Geometry, item.Geometry);
    }

    [Fact]
    public void ArchiveIsRecoverableAndChangesConcurrencyToken()
    {
        var item = HealthProperty.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Rua A", "1", null, null,
            Factory.CreatePoint(new Coordinate(-46.63, -23.55)),
            PropertyRegistrationStatus.Draft, PropertySituation.Vacant);
        var token = item.ConcurrencyToken;

        item.Archive();

        Assert.True(item.IsArchived);
        Assert.NotEqual(token, item.ConcurrencyToken);
        item.Restore();
        Assert.False(item.IsArchived);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(731)]
    public void CoverageRuleRejectsInvalidPeriod(int days)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CoverageRule.Create(Guid.NewGuid(), Guid.NewGuid(), days));
    }

    [Fact]
    public void OperationalTagNormalizesValidColor()
    {
        var tag = OperationalTag.Create(Guid.NewGuid(), " Difícil acesso ", "#4F9A7D");

        Assert.Equal("Difícil acesso", tag.Name);
        Assert.Equal("#4f9a7d", tag.Color);
        Assert.Throws<ArgumentException>(() => tag.Update("Teste", "verde"));
    }
}
