namespace STU.Domain.Auditing;

public sealed class AuditEntry
{
    private AuditEntry()
    {
    }

    private AuditEntry(
        Guid actorUserId,
        string actorUserName,
        string action,
        string entityType,
        string entityId,
        string summary,
        string? beforeJson,
        string? afterJson,
        string? ipAddress)
    {
        ActorUserId = actorUserId;
        ActorUserName = actorUserName;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Summary = summary;
        BeforeJson = beforeJson;
        AfterJson = afterJson;
        IpAddress = ipAddress;
    }

    public Guid Id { get; private init; } = Guid.NewGuid();

    public DateTimeOffset OccurredAtUtc { get; private init; } = DateTimeOffset.UtcNow;

    public Guid ActorUserId { get; private init; }

    public string ActorUserName { get; private init; } = string.Empty;

    public string Action { get; private init; } = string.Empty;

    public string EntityType { get; private init; } = string.Empty;

    public string EntityId { get; private init; } = string.Empty;

    public string Summary { get; private init; } = string.Empty;

    public string? BeforeJson { get; private init; }

    public string? AfterJson { get; private init; }

    public string? IpAddress { get; private init; }

    public static AuditEntry Create(
        Guid actorUserId,
        string actorUserName,
        string action,
        string entityType,
        string entityId,
        string summary,
        string? beforeJson,
        string? afterJson,
        string? ipAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserName);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        return new AuditEntry(
            actorUserId,
            actorUserName.Trim(),
            action.Trim(),
            entityType.Trim(),
            entityId.Trim(),
            summary.Trim(),
            beforeJson,
            afterJson,
            ipAddress);
    }
}
