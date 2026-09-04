namespace STU.Domain.Operations;

public sealed class BackupRun
{
    private BackupRun()
    {
    }

    private BackupRun(BackupTrigger trigger, Guid? requestedByUserId, string? scheduleKey)
    {
        Trigger = trigger;
        RequestedByUserId = requestedByUserId;
        ScheduleKey = scheduleKey;
    }

    public Guid Id { get; private init; } = Guid.NewGuid();
    public BackupTrigger Trigger { get; private init; }
    public BackupRunStatus Status { get; private set; } = BackupRunStatus.Queued;
    public Guid? RequestedByUserId { get; private init; }
    public string? ScheduleKey { get; private init; }
    public DateTimeOffset RequestedAtUtc { get; private init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? FileName { get; private set; }
    public long? SizeBytes { get; private set; }
    public string? Sha256 { get; private set; }
    public string? ErrorSummary { get; private set; }
    public DateTimeOffset? FilePrunedAtUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();

    public static BackupRun CreateManual(Guid actorUserId) => new(BackupTrigger.Manual, actorUserId, null);

    public static BackupRun CreateScheduled(string scheduleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleKey);
        return new BackupRun(BackupTrigger.Scheduled, null, scheduleKey.Trim());
    }

    public void Start()
    {
        if (Status != BackupRunStatus.Queued)
        {
            throw new InvalidOperationException("A cópia não está aguardando execução.");
        }

        Status = BackupRunStatus.Running;
        StartedAtUtc = DateTimeOffset.UtcNow;
        CompletedAtUtc = null;
        ErrorSummary = null;
        AttemptCount++;
        Touch();
    }

    public void Complete(string fileName, long sizeBytes, string sha256)
    {
        if (Status != BackupRunStatus.Running)
        {
            throw new InvalidOperationException("A cópia não está em execução.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);
        if (string.IsNullOrWhiteSpace(sha256) || sha256.Length != 64)
        {
            throw new ArgumentException("Informe um SHA-256 válido.", nameof(sha256));
        }

        FileName = fileName.Trim();
        SizeBytes = sizeBytes;
        Sha256 = sha256.ToLowerInvariant();
        Status = BackupRunStatus.Completed;
        CompletedAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Fail(string summary)
    {
        if (Status is not (BackupRunStatus.Running or BackupRunStatus.Queued))
        {
            throw new InvalidOperationException("A cópia já foi finalizada.");
        }

        var normalized = string.IsNullOrWhiteSpace(summary) ? "Falha não identificada." : summary.Trim();
        ErrorSummary = normalized[..Math.Min(normalized.Length, 1000)];
        Status = BackupRunStatus.Failed;
        CompletedAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }

    public void RecoverInterrupted()
    {
        if (Status != BackupRunStatus.Running) return;
        if (AttemptCount >= 3)
        {
            Fail("A execução foi interrompida três vezes e exige revisão manual.");
            return;
        }

        Status = BackupRunStatus.Queued;
        StartedAtUtc = null;
        Touch();
    }

    public void MarkFilePruned()
    {
        if (Status != BackupRunStatus.Completed || FileName is null || FilePrunedAtUtc.HasValue) return;
        FilePrunedAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }

    private void Touch() => ConcurrencyToken = Guid.NewGuid();
}
