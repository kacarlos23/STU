using STU.Domain.Families;

namespace STU.UnitTests.Families;

public sealed class FamilyTests
{
    [Fact]
    public void NumberIsNormalizedWhileResponsibleSpellingAndHistoryArePreserved()
    {
        var actor = Guid.NewGuid();
        var family = Family.Create(Guid.NewGuid(), " fam-a1 ", "  Ana-María D'Ávila  ", actor);
        var initial = FamilyVersion.Capture(family, 1, "Create", actor);
        var version = family.ConcurrencyToken;
        Assert.Equal("FAM-A1", family.Number);
        Assert.Equal("Ana-María D'Ávila", family.ResponsibleName);
        family.Update("FAM-A2", "João da Silva", actor);
        Assert.Equal("FAM-A1", initial.Number);
        Assert.Equal("Ana-María D'Ávila", initial.ResponsibleName);
        Assert.NotEqual(version, family.ConcurrencyToken);
        family.Archive(actor); Assert.True(family.IsArchived);
        family.Restore(actor); Assert.False(family.IsArchived);
    }

    [Theory]
    [InlineData("", "Responsável")]
    [InlineData("1", " ")]
    [InlineData("1", "Nome\nOutro")]
    [InlineData("F\t1", "Responsável")]
    [InlineData("123456789012345678901234567890123", "Responsável")]
    public void RequiredIdentifiersAreValidated(string number, string name) =>
        Assert.Throws<ArgumentException>(() => Family.Create(Guid.NewGuid(), number, name, Guid.NewGuid()));

    [Fact]
    public void ResponsibleNameCannotExceedLimit() => Assert.Throws<ArgumentException>(() => Family.Create(Guid.NewGuid(), "1", new string('a', 121), Guid.NewGuid()));

    [Fact]
    public void LinkCanBeClosedOnlyOnceAndRetainsItsIdentity()
    {
        var link = FamilyPropertyLink.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var family = link.FamilyId; var property = link.PropertyId; var start = link.StartedAtUtc;
        link.End(Guid.NewGuid(), "Move");
        Assert.False(link.IsCurrent); Assert.Equal(family, link.FamilyId); Assert.Equal(property, link.PropertyId);
        Assert.True(link.EndedAtUtc > start); Assert.Equal("Move", link.EndReason);
        Assert.Throws<InvalidOperationException>(() => link.End(Guid.NewGuid()));
    }
}
