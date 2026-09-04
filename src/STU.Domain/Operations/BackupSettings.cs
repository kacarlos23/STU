using System.Globalization;

namespace STU.Domain.Operations;

public sealed class BackupSettings
{
    public static readonly Guid SingletonId = Guid.Parse("9a362f72-b33a-4ad2-9275-a2dfdc0fe28a");
    public const string DefaultTimeZone = "America/Bahia";

    private BackupSettings()
    {
    }

    private BackupSettings(bool enabled, DayOfWeek dayOfWeek, int localHour, int retentionCount)
    {
        Id = SingletonId;
        Enabled = enabled;
        DayOfWeek = dayOfWeek;
        LocalHour = localHour;
        RetentionCount = retentionCount;
    }

    public Guid Id { get; private init; }
    public bool Enabled { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public int LocalHour { get; private set; }
    public int RetentionCount { get; private set; }
    public string TimeZoneId { get; private init; } = DefaultTimeZone;
    public DateTimeOffset CreatedAtUtc { get; private init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();

    public static BackupSettings CreateDefault() => new(true, DayOfWeek.Sunday, 0, 8);

    public void Update(bool enabled, DayOfWeek dayOfWeek, int localHour, int retentionCount)
    {
        if (localHour is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(localHour), "A hora deve estar entre 0 e 23.");
        }

        if (retentionCount is < 1 or > 52)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionCount), "A retenção deve estar entre 1 e 52 cópias.");
        }

        Enabled = enabled;
        DayOfWeek = dayOfWeek;
        LocalHour = localHour;
        RetentionCount = retentionCount;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    public BackupScheduleSlot MostRecentSlot(DateTimeOffset nowUtc)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        var daysSinceScheduledDay = ((int)localNow.DayOfWeek - (int)DayOfWeek + 7) % 7;
        var localDate = DateOnly.FromDateTime(localNow.Date).AddDays(-daysSinceScheduledDay);
        var localSlot = localDate.ToDateTime(new TimeOnly(LocalHour, 0), DateTimeKind.Unspecified);

        if (localSlot > localNow.DateTime)
        {
            localDate = localDate.AddDays(-7);
            localSlot = localDate.ToDateTime(new TimeOnly(LocalHour, 0), DateTimeKind.Unspecified);
        }

        var utcSlot = TimeZoneInfo.ConvertTimeToUtc(localSlot, timeZone);
        return new BackupScheduleSlot(
            localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            new DateTimeOffset(utcSlot, TimeSpan.Zero));
    }
}

public sealed record BackupScheduleSlot(string Key, DateTimeOffset ScheduledAtUtc);
