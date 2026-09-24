namespace STU.Domain.Families;

public sealed class FamilyVersion
{
    private FamilyVersion() { }

    public Guid Id { get; private init; } = Guid.NewGuid();
    public Guid FamilyId { get; private init; }
    public int VersionNumber { get; private init; }
    public Guid HealthUnitId { get; private init; }
    public string Number { get; private init; } = string.Empty;
    public string ResponsibleName { get; private init; } = string.Empty;
    public bool IsArchived { get; private init; }
    public string ChangeKind { get; private init; } = string.Empty;
    public Guid ChangedByUserId { get; private init; }
    public DateTimeOffset ChangedAtUtc { get; private init; } = DateTimeOffset.UtcNow;

    public static FamilyVersion Capture(Family family, int number, string kind, Guid actorId) => new()
    {
        FamilyId = family.Id,
        VersionNumber = number,
        HealthUnitId = family.HealthUnitId,
        Number = family.Number,
        ResponsibleName = family.ResponsibleName,
        IsArchived = family.IsArchived,
        ChangeKind = kind,
        ChangedByUserId = actorId,
    };
}
