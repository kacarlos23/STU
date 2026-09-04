namespace STU.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; protected init; } = Guid.NewGuid();

    public DateTimeOffset CreatedAtUtc { get; protected init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAtUtc { get; protected set; }

    public DateTimeOffset? ArchivedAtUtc { get; protected set; }

    public bool IsArchived => ArchivedAtUtc.HasValue;
}
