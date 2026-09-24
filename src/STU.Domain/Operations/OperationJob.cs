using STU.Domain.Common;

namespace STU.Domain.Operations;

public sealed class OperationJob : Entity
{
    private OperationJob() { }

    private OperationJob(Guid healthUnitId, Guid createdByUserId, OperationJobKind kind, OperationFileFormat format, string parametersJson, string? sourceFileName, string? originalFileName)
    {
        HealthUnitId = healthUnitId;
        CreatedByUserId = createdByUserId;
        Kind = kind;
        Format = format;
        ParametersJson = parametersJson;
        SourceFileName = sourceFileName;
        OriginalFileName = originalFileName;
    }

    public Guid HealthUnitId { get; private init; }
    public Guid CreatedByUserId { get; private init; }
    public OperationJobKind Kind { get; private init; }
    public OperationJobStatus Status { get; private set; } = OperationJobStatus.Pending;
    public OperationFileFormat Format { get; private init; }
    public string ParametersJson { get; private init; } = "{}";
    public string? SourceFileName { get; private init; }
    public string? OriginalFileName { get; private init; }
    public string? StagedFileName { get; private set; }
    public string? ResultFileName { get; private set; }
    public string? ErrorSummary { get; private set; }
    public int RecordCount { get; private set; }
    public int FamilyCount { get; private set; }
    public int LinkCount { get; private set; }
    public int ValidationErrorCount { get; private set; }
    public int AttemptCount { get; private set; }
    public int ProgressPercentage { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();

    public static OperationJob CreateExport(Guid healthUnitId, Guid actorId, OperationFileFormat format, string parametersJson) =>
        new(healthUnitId, actorId, OperationJobKind.PropertyExport, format, parametersJson, null, null);

    public static OperationJob CreateImport(Guid healthUnitId, Guid actorId, OperationFileFormat format, string sourceFileName, string originalFileName) =>
        new(healthUnitId, actorId, OperationJobKind.PropertyImport, format, "{}", sourceFileName, originalFileName);

    public void Start()
    {
        if (Status != OperationJobStatus.Pending) throw new InvalidOperationException("O trabalho não está pendente.");
        Status = OperationJobStatus.Processing; StartedAtUtc = DateTimeOffset.UtcNow; AttemptCount++; ProgressPercentage = 5; Touch();
    }

    public void AwaitApproval(string stagedFileName, int recordCount, int familyCount = 0, int linkCount = 0)
    {
        if (Status != OperationJobStatus.Processing) throw new InvalidOperationException("O trabalho não está em processamento.");
        StagedFileName = stagedFileName; RecordCount = recordCount; FamilyCount = familyCount; LinkCount = linkCount; ValidationErrorCount = 0; ErrorSummary = null;
        Status = OperationJobStatus.AwaitingApproval; ProgressPercentage = 50; Touch();
    }

    public void Approve(Guid actorId)
    {
        if (Status != OperationJobStatus.AwaitingApproval) throw new InvalidOperationException("A importação ainda não pode ser aprovada.");
        ApprovedByUserId = actorId; ApprovedAtUtc = DateTimeOffset.UtcNow; Status = OperationJobStatus.Pending; ProgressPercentage = 55; Touch();
    }

    public void Complete(string? resultFileName, int recordCount)
    {
        if (Status != OperationJobStatus.Processing) throw new InvalidOperationException("O trabalho não está em processamento.");
        ResultFileName = resultFileName; RecordCount = recordCount; Status = OperationJobStatus.Completed;
        ProgressPercentage = 100; CompletedAtUtc = DateTimeOffset.UtcNow; ErrorSummary = null; Touch();
    }

    public void Fail(string summary, int validationErrorCount = 0)
    {
        ErrorSummary = string.IsNullOrWhiteSpace(summary) ? "Falha não identificada." : summary.Trim()[..Math.Min(summary.Trim().Length, 2000)];
        ValidationErrorCount = validationErrorCount; Status = OperationJobStatus.Failed; CompletedAtUtc = DateTimeOffset.UtcNow; Touch();
    }

    public void Retry()
    {
        if (Status != OperationJobStatus.Failed || AttemptCount >= 3) throw new InvalidOperationException("Este trabalho não pode ser repetido.");
        Status = OperationJobStatus.Pending; ErrorSummary = null; ValidationErrorCount = 0; CompletedAtUtc = null; ProgressPercentage = 0; Touch();
    }

    public void RecoverInterrupted()
    {
        if (Status != OperationJobStatus.Processing) return;
        if (AttemptCount >= 3)
        {
            Fail("O processamento foi interrompido três vezes e exige revisão manual.");
            return;
        }
        Status = OperationJobStatus.Pending; ProgressPercentage = 0; StartedAtUtc = null; Touch();
    }

    private void Touch() { UpdatedAtUtc = DateTimeOffset.UtcNow; ConcurrencyToken = Guid.NewGuid(); }
}
