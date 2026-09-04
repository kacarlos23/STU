namespace STU.Domain.Operations;

public sealed class SystemHeartbeat
{
    private SystemHeartbeat()
    {
    }

    private SystemHeartbeat(string component, string instance, string version, DateTimeOffset observedAtUtc)
    {
        Component = Normalize(component, 64, nameof(component));
        Instance = Normalize(instance, 120, nameof(instance));
        Version = Normalize(version, 64, nameof(version));
        StartedAtUtc = observedAtUtc;
        LastSeenAtUtc = observedAtUtc;
    }

    public Guid Id { get; private init; } = Guid.NewGuid();
    public string Component { get; private init; } = string.Empty;
    public string Instance { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; private init; }
    public DateTimeOffset LastSeenAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();

    public static SystemHeartbeat Start(string component, string instance, string version, DateTimeOffset observedAtUtc) =>
        new(component, instance, version, observedAtUtc);

    public void Report(string instance, string version, DateTimeOffset observedAtUtc)
    {
        if (observedAtUtc < LastSeenAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(observedAtUtc), "O heartbeat não pode retroceder no tempo.");
        }

        Instance = Normalize(instance, 120, nameof(instance));
        Version = Normalize(version, 64, nameof(version));
        LastSeenAtUtc = observedAtUtc;
        ConcurrencyToken = Guid.NewGuid();
    }

    private static string Normalize(string value, int maximum, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        var normalized = value.Trim();
        if (normalized.Length > maximum) throw new ArgumentOutOfRangeException(parameter);
        return normalized;
    }
}
