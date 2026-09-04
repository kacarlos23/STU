using STU.Application.Security;
using STU.PilotData;

namespace STU.UnitTests.Pilot;

public sealed class PilotDatasetTests
{
    [Fact]
    public void BlueprintHasTheApprovedScaleAndValidGeometries()
    {
        var blueprint = new PilotDatasetBlueprint();

        Assert.Equal(3, blueprint.Neighborhoods.Count);
        Assert.Equal(20, blueprint.Microregions.Count);
        Assert.Equal(100, blueprint.Accounts.Count);
        Assert.Equal(3_000, blueprint.Microregions.Sum(item => PilotDatasetBlueprint.GetProperties(item).Count()));
        Assert.All(blueprint.Neighborhoods, item => Assert.True(item.Geometry.IsValid));
        Assert.All(blueprint.Microregions, item => Assert.True(item.Boundary.IsValid));
        Assert.Contains(blueprint.Microregions, microregion =>
            blueprint.Neighborhoods.Count(neighborhood => neighborhood.Geometry.Intersection(microregion.Boundary).Area > 0) > 1);
    }

    [Fact]
    public void BlueprintHasTheApprovedRoleDistributionAndUniqueIdentifiers()
    {
        var blueprint = new PilotDatasetBlueprint();
        var properties = blueprint.Microregions.SelectMany(PilotDatasetBlueprint.GetProperties).ToArray();

        Assert.Equal(50, blueprint.Accounts.Count(item => item.Role == SystemRoles.HealthAgent));
        Assert.Equal(20, blueprint.Accounts.Count(item => item.Role == SystemRoles.Receptionist));
        Assert.Equal(20, blueprint.Accounts.Count(item => item.Role == SystemRoles.Doctor));
        Assert.Equal(10, blueprint.Accounts.Count(item => item.Role == SystemRoles.HealthUnitManager));
        Assert.Single(blueprint.Accounts, item => item.IsIsolationAccount);
        Assert.Equal(3_000, properties.Select(item => item.FamilyNumber).Distinct().Count());
        Assert.Equal(3_000, properties.Select(item => item.HouseNumber).Distinct().Count());
        Assert.All(properties, item => Assert.Equal(4326, item.Geometry.SRID));
    }

    [Theory]
    [InlineData("Host=127.0.0.1;Port=55432;Database=stu;Username=stu;Password=x")]
    [InlineData("Host=127.0.0.1;Port=55433;Database=stu;Username=stu;Password=x")]
    [InlineData("Host=remote.example;Port=55433;Database=stu_load_pilot;Username=stu;Password=x")]
    public void DatabaseGuardRejectsNonIsolatedTargets(string connectionString)
    {
        Assert.Throws<InvalidOperationException>(() => PilotDatabaseGuard.Validate(connectionString));
    }

    [Fact]
    public void DatabaseGuardAcceptsTheDedicatedLocalTarget()
    {
        var target = PilotDatabaseGuard.Validate(
            "Host=127.0.0.1;Port=55433;Database=stu_load_pilot;Username=stu;Password=x");

        Assert.Equal("stu_load_pilot", target.Database);
        Assert.Equal(55433, target.Port);
    }
}
