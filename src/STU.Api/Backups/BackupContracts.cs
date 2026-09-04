namespace STU.Api.Backups;

public sealed record UpdateBackupSettingsRequest(
    bool Enabled,
    DayOfWeek DayOfWeek,
    int LocalHour,
    int RetentionCount);
