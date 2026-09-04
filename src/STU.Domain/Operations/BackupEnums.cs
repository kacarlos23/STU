namespace STU.Domain.Operations;

public enum BackupTrigger
{
    Manual,
    Scheduled,
}

public enum BackupRunStatus
{
    Queued,
    Running,
    Completed,
    Failed,
}
