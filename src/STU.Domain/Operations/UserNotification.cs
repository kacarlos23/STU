using STU.Domain.Common;

namespace STU.Domain.Operations;

public sealed class UserNotification : Entity
{
    private UserNotification() { }
    private UserNotification(Guid userId, Guid? healthUnitId, NotificationKind kind, string title, string message, string? link)
    {
        UserId = userId; HealthUnitId = healthUnitId; Kind = kind; Title = title.Trim(); Message = message.Trim(); Link = string.IsNullOrWhiteSpace(link) ? null : link.Trim();
    }

    public Guid UserId { get; private init; }
    public Guid? HealthUnitId { get; private init; }
    public NotificationKind Kind { get; private init; }
    public string Title { get; private init; } = string.Empty;
    public string Message { get; private init; } = string.Empty;
    public string? Link { get; private init; }
    public DateTimeOffset? ReadAtUtc { get; private set; }
    public bool IsRead => ReadAtUtc.HasValue;

    public static UserNotification Create(Guid userId, Guid? healthUnitId, NotificationKind kind, string title, string message, string? link = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title); ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(userId, healthUnitId, kind, title, message, link);
    }

    public void MarkRead() { ReadAtUtc ??= DateTimeOffset.UtcNow; UpdatedAtUtc = DateTimeOffset.UtcNow; }
}
