using STU.Domain.Common;

namespace STU.Domain.Families;

public sealed class FamilyPropertyLink : Entity
{
    private FamilyPropertyLink() { }

    private FamilyPropertyLink(Guid healthUnitId, Guid familyId, Guid propertyId, Guid createdByUserId)
    {
        HealthUnitId = healthUnitId;
        FamilyId = familyId;
        PropertyId = propertyId;
        StartedAtUtc = DateTimeOffset.UtcNow;
        CreatedByUserId = createdByUserId;
    }

    public Guid HealthUnitId { get; private init; }
    public Guid FamilyId { get; private init; }
    public Guid PropertyId { get; private init; }
    public DateTimeOffset StartedAtUtc { get; private init; }
    public DateTimeOffset? EndedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private init; }
    public Guid? EndedByUserId { get; private set; }
    public string? EndReason { get; private set; }

    public bool IsCurrent => !EndedAtUtc.HasValue;

    public static FamilyPropertyLink Create(Guid healthUnitId, Guid familyId, Guid propertyId, Guid createdByUserId) =>
        new(healthUnitId, familyId, propertyId, createdByUserId);

    public void End(Guid endedByUserId, string reason = "Unlink")
    {
        if (!IsCurrent) throw new InvalidOperationException("O vínculo residencial já foi encerrado.");
        var now = DateTimeOffset.UtcNow;
        EndedAtUtc = now > StartedAtUtc.AddTicks(10) ? now : StartedAtUtc.AddTicks(10);
        EndedByUserId = endedByUserId;
        EndReason = reason;
        UpdatedAtUtc = EndedAtUtc;
    }
}
