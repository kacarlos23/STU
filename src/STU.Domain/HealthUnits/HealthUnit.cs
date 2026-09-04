using STU.Domain.Common;

namespace STU.Domain.HealthUnits;

public sealed class HealthUnit : Entity
{
    private HealthUnit()
    {
    }

    private HealthUnit(string code, string name)
    {
        Code = code;
        Name = name;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public static HealthUnit Create(string code, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new HealthUnit(code.Trim().ToUpperInvariant(), name.Trim());
    }

    public void Update(string code, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Archive()
    {
        if (IsArchived)
        {
            return;
        }

        ArchivedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = ArchivedAtUtc;
    }

    public void Restore()
    {
        if (!IsArchived)
        {
            return;
        }

        ArchivedAtUtc = null;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
