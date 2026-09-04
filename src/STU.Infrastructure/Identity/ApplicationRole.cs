using Microsoft.AspNetCore.Identity;

namespace STU.Infrastructure.Identity;

public sealed class ApplicationRole : IdentityRole<Guid>
{
    public string DisplayName { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsSystem { get; private set; }

    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public bool IsArchived => ArchivedAtUtc.HasValue;

    public static ApplicationRole CreateSystem(string name, string displayName, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new ApplicationRole
        {
            Name = name,
            DisplayName = displayName.Trim(),
            Description = description.Trim(),
            IsSystem = true,
        };
    }

    public static ApplicationRole CreateCustom(string name, string displayName, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new ApplicationRole
        {
            Name = name.Trim(),
            DisplayName = displayName.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            IsSystem = false,
        };
    }

    public void UpdateDetails(string displayName, string? description)
    {
        if (IsSystem)
        {
            throw new InvalidOperationException("System roles cannot be edited.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public void Archive()
    {
        if (IsSystem)
        {
            throw new InvalidOperationException("System roles cannot be archived.");
        }

        ArchivedAtUtc ??= DateTimeOffset.UtcNow;
    }

    public void Restore()
    {
        if (IsSystem)
        {
            return;
        }

        ArchivedAtUtc = null;
    }
}
