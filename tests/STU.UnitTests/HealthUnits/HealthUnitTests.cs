using STU.Domain.HealthUnits;

namespace STU.UnitTests.HealthUnits;

public sealed class HealthUnitTests
{
    [Fact]
    public void CreateNormalizesCodeAndName()
    {
        var unit = HealthUnit.Create(" ubs-01 ", " UBS Central ");

        Assert.Equal("UBS-01", unit.Code);
        Assert.Equal("UBS Central", unit.Name);
        Assert.False(unit.IsArchived);
    }

    [Fact]
    public void CreateRejectsBlankCode()
    {
        Assert.Throws<ArgumentException>(() => HealthUnit.Create(" ", "UBS Central"));
    }
}
