using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using STU.Api.Monitoring;
using STU.Api.Onboarding;
using STU.Application.Security;
using STU.Domain.Auditing;
using STU.Domain.Operations;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.Api.PilotRelease;

public static class PilotReleaseEndpoints
{
    private const string EvidenceVersion = "phase-7.5-2026-08-31";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapPilotReleaseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/pilot-release")
            .WithTags("Pilot release")
            .RequireAuthorization(StuPolicies.PasswordChanged)
            .RequireRateLimiting("api");

        group.MapGet("/readiness", GetReadinessAsync);
        group.MapPost("/decision", RecordDecisionAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        return endpoints;
    }

    private static async Task<IResult> GetReadinessAsync(
        Guid? healthUnitId,
        HttpContext http,
        UserManager<ApplicationUser> userManager,
        StuDbContext db,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(healthUnitId, http, userManager, db, cancellationToken);
        if (scope.Error is not null) return scope.Error;
        return Results.Ok(await BuildReportAsync(scope.UnitId, db, configuration, cancellationToken));
    }

    private static async Task<IResult> RecordDecisionAsync(
        ReleaseDecisionRequest request,
        HttpContext http,
        UserManager<ApplicationUser> userManager,
        StuDbContext db,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(request.HealthUnitId, http, userManager, db, cancellationToken);
        if (scope.Error is not null) return scope.Error;

        var decision = request.Decision?.Trim().ToLowerInvariant();
        if (decision is not ("go" or "no-go"))
        {
            return Validation("decision", "Escolha liberar ou não liberar o piloto.");
        }

        var report = await BuildReportAsync(scope.UnitId, db, configuration, cancellationToken);
        if (decision == "go")
        {
            if (!report.ReadyForDecision)
            {
                return Results.Conflict(new
                {
                    title = "O piloto ainda possui pendências obrigatórias.",
                    blockers = report.Checks.Where(item => item.Status == "blocked").ToArray(),
                });
            }

            var missingConfirmations = new List<string>();
            if (!request.HumanAccessibilityAccepted) missingConfirmations.Add("aceitação assistida de acessibilidade");
            if (!request.TrainingCompleted) missingConfirmations.Add("treinamento por função");
            if (!request.UserAcceptanceCompleted) missingConfirmations.Add("aceitação dos usuários");
            if (!request.NoCriticalHighDefects) missingConfirmations.Add("ausência de defeitos críticos ou altos");
            if (missingConfirmations.Count > 0)
            {
                return Validation("confirmations", $"Confirme: {string.Join(", ", missingConfirmations)}.");
            }

            var ownerError = ValidateOwners(request);
            if (ownerError is not null) return ownerError;
        }
        else if (string.IsNullOrWhiteSpace(request.Note) || request.Note.Trim().Length < 10)
        {
            return Validation("note", "Descreva em pelo menos 10 caracteres por que o piloto não deve ser liberado.");
        }

        var actor = await userManager.GetUserAsync(http.User)
            ?? throw new InvalidOperationException("Usuário autenticado não encontrado.");
        var evidence = new
        {
            report.ReleaseSnapshotHash,
            report.OnboardingSnapshotHash,
            HealthUnitId = scope.UnitId,
            Decision = decision,
            request.HumanAccessibilityAccepted,
            request.TrainingCompleted,
            request.UserAcceptanceCompleted,
            request.NoCriticalHighDefects,
            SupportOwner = Clean(request.SupportOwner, 120),
            IncidentOwner = Clean(request.IncidentOwner, 120),
            RollbackOwner = Clean(request.RollbackOwner, 120),
            Note = Clean(request.Note, 500),
            EvidenceVersion,
        };
        var action = decision == "go" ? "Approve" : "Hold";
        var summary = decision == "go"
            ? $"Piloto da UBS {report.HealthUnit.Code} liberado após confirmação do portão final."
            : $"Piloto da UBS {report.HealthUnit.Code} mantido bloqueado por decisão operacional.";
        db.AuditEntries.Add(AuditEntry.Create(
            actor.Id,
            actor.UserName ?? actor.DisplayName,
            action,
            "PilotReleaseDecision",
            scope.UnitId.ToString(),
            summary,
            null,
            JsonSerializer.Serialize(evidence, JsonOptions),
            http.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new
        {
            decision,
            report.ReleaseSnapshotHash,
            recordedAtUtc = DateTimeOffset.UtcNow,
            recordedBy = actor.DisplayName,
        });
    }

    private static async Task<ReleaseReport> BuildReportAsync(
        Guid unitId,
        StuDbContext db,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var onboarding = await OnboardingEndpoints.BuildReportAsync(unitId, db, cancellationToken);
        var onboardingApprovalCurrent = onboarding.ReadyForApproval &&
            onboarding.LatestApproval?.SnapshotHash == onboarding.SnapshotHash;
        var offsiteEvidence = onboardingApprovalCurrent &&
            !string.IsNullOrWhiteSpace(onboarding.LatestApproval?.OffsiteDestination);

        var now = DateTimeOffset.UtcNow;
        var workerLastSeen = await db.SystemHeartbeats.AsNoTracking()
            .Where(item => item.Component == "worker")
            .Select(item => (DateTimeOffset?)item.LastSeenAtUtc)
            .SingleOrDefaultAsync(cancellationToken);
        var workerHealthy = workerLastSeen.HasValue && now - workerLastSeen.Value <= TimeSpan.FromSeconds(45);
        var storage = OperationsStorageProbe.Check(configuration);
        var stalledBackups = await db.BackupRuns.AsNoTracking().CountAsync(item =>
            item.Status == BackupRunStatus.Running && item.StartedAtUtc < now.AddMinutes(-30), cancellationToken);
        var blockingOperations = await db.OperationJobs.AsNoTracking().CountAsync(item =>
            (item.Status == OperationJobStatus.Pending && item.CreatedAtUtc < now.AddMinutes(-30)) ||
            (item.Status == OperationJobStatus.Processing && item.StartedAtUtc < now.AddMinutes(-15)) ||
            (item.Status == OperationJobStatus.Failed && item.CompletedAtUtc >= now.AddHours(-24)), cancellationToken);

        var checks = new List<ReleaseCheck>
        {
            Passed("capacity", "Capacidade e desempenho", "Ensaio de 300 sessões aprovado: 60.292 verificações, sem falhas e dentro das metas."),
            Passed("accessibility-automation", "Acessibilidade técnica", "Revisão WCAG 2.2 AA e regressões automatizadas concluídas sem defeito crítico ou alto conhecido."),
            Check("onboarding", "Pré-implantação aprovada e atual", onboardingApprovalCurrent, onboardingApprovalCurrent ? "A aprovação corresponde ao estado territorial atual." : "Conclua e aprove a pré-implantação da UBS para o estado atual."),
            Check("offsite-backup", "Cópia externa criptografada comprovada", offsiteEvidence, offsiteEvidence ? $"Destino registrado: {onboarding.LatestApproval!.OffsiteDestination}." : "Registre na pré-implantação a cópia criptografada armazenada fora do computador servidor."),
            Check("recent-backup", "Backup íntegro e recente", onboarding.Backup.Recent && stalledBackups == 0, onboarding.Backup.Recent && stalledBackups == 0 ? "Existe uma cópia íntegra dos últimos sete dias e nenhuma execução está travada." : "Gere ou conclua um backup íntegro antes da decisão."),
            Check("worker", "Processamento em segundo plano ativo", workerHealthy, workerHealthy ? "O serviço responsável por importações e backups está respondendo." : "O processador em segundo plano está indisponível ou atrasado."),
            Check("storage", "Armazenamento operacional disponível", storage.Available, storage.Available ? "O armazenamento está disponível para gravação." : "O armazenamento de arquivos não está disponível."),
            Check("operations", "Sem operação crítica pendente", blockingOperations == 0, blockingOperations == 0 ? "Nenhuma importação, exportação ou backup exige intervenção." : $"{blockingOperations} operação(ões) atrasada(s), falha(s) ou pendente(s) exigem revisão."),
        };

        var hashPayload = JsonSerializer.Serialize(new
        {
            onboarding.HealthUnit.Id,
            OnboardingSnapshotHash = onboarding.SnapshotHash,
            OnboardingApprovalCurrent = onboardingApprovalCurrent,
            OffsiteEvidence = onboarding.LatestApproval?.OffsiteDestination,
            BackupId = onboarding.Backup.RunId,
            BackupSha256 = onboarding.Backup.Sha256,
            WorkerHealthy = workerHealthy,
            StorageAvailable = storage.Available,
            BlockingOperations = blockingOperations,
            StalledBackups = stalledBackups,
            EvidenceVersion,
        }, JsonOptions);
        var releaseHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashPayload))).ToLowerInvariant();
        var latestDecision = await LatestDecisionAsync(unitId, db, cancellationToken);
        var decisionCurrent = latestDecision?.ReleaseSnapshotHash == releaseHash;

        return new ReleaseReport(
            now,
            releaseHash,
            onboarding.SnapshotHash,
            onboarding.HealthUnit,
            checks.All(item => item.Status == "passed"),
            latestDecision?.Decision == "go" && decisionCurrent,
            checks,
            new[]
            {
                new EvidenceLink("Capacidade", "PHASE2_CAPACITY_REPORT_2026-08-28.md"),
                new EvidenceLink("Acessibilidade", "PHASE3_ACCESSIBILITY_REPORT_2026-08-31.md"),
                new EvidenceLink("Pré-implantação", "PHASE4_ONBOARDING_BASELINE_2026-08-31.md"),
            },
            latestDecision is null ? null : latestDecision with { Current = decisionCurrent });
    }

    private static async Task<ReleaseDecisionSummary?> LatestDecisionAsync(Guid unitId, StuDbContext db, CancellationToken cancellationToken)
    {
        var entry = await db.AuditEntries.AsNoTracking()
            .Where(item => item.EntityType == "PilotReleaseDecision" && item.EntityId == unitId.ToString())
            .OrderByDescending(item => item.OccurredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (entry is null || string.IsNullOrWhiteSpace(entry.AfterJson)) return null;

        using var document = JsonDocument.Parse(entry.AfterJson);
        var root = document.RootElement;
        return new ReleaseDecisionSummary(
            entry.Action == "Approve" ? "go" : "no-go",
            entry.OccurredAtUtc,
            entry.ActorUserName,
            ReadString(root, "releaseSnapshotHash"),
            ReadString(root, "supportOwner"),
            ReadString(root, "incidentOwner"),
            ReadString(root, "rollbackOwner"),
            ReadString(root, "note"),
            false);
    }

    private static async Task<ScopeResult> ResolveScopeAsync(
        Guid? requestedUnitId,
        HttpContext http,
        UserManager<ApplicationUser> userManager,
        StuDbContext db,
        CancellationToken cancellationToken)
    {
        var actor = await userManager.GetUserAsync(http.User);
        if (actor is null) return new(Results.Unauthorized(), Guid.Empty);
        var global = http.User.IsInRole(SystemRoles.GlobalAdministrator);
        var manager = http.User.IsInRole(SystemRoles.HealthUnitManager);
        if (!global && !manager) return new(Results.Forbid(), Guid.Empty);
        var unitId = global ? requestedUnitId : actor.HealthUnitId;
        if (!unitId.HasValue || (!global && requestedUnitId.HasValue && requestedUnitId != actor.HealthUnitId)) return new(Results.Forbid(), Guid.Empty);
        if (!await db.HealthUnits.AsNoTracking().AnyAsync(item => item.Id == unitId && item.ArchivedAtUtc == null, cancellationToken)) return new(Results.NotFound(), Guid.Empty);
        return new(null, unitId.Value);
    }

    private static IResult? ValidateOwners(ReleaseDecisionRequest request)
    {
        if (!ValidOwner(request.SupportOwner)) return Validation("supportOwner", "Informe o responsável pelo suporte.");
        if (!ValidOwner(request.IncidentOwner)) return Validation("incidentOwner", "Informe o responsável por incidentes.");
        if (!ValidOwner(request.RollbackOwner)) return Validation("rollbackOwner", "Informe o responsável pela restauração ou reversão.");
        return null;
    }

    private static bool ValidOwner(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length is >= 3 and <= 120;
    private static ReleaseCheck Passed(string id, string label, string detail) => new(id, label, "passed", detail);
    private static ReleaseCheck Check(string id, string label, bool passed, string detail) => new(id, label, passed ? "passed" : "blocked", detail);
    private static string? Clean(string? value, int limit) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, limit)];
    private static string? ReadString(JsonElement element, string property) => element.TryGetProperty(property, out var value) ? value.GetString() : null;
    private static IResult Validation(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private sealed record ScopeResult(IResult? Error, Guid UnitId);
    private sealed record ReleaseDecisionRequest(
        Guid? HealthUnitId,
        string? Decision,
        bool HumanAccessibilityAccepted,
        bool TrainingCompleted,
        bool UserAcceptanceCompleted,
        bool NoCriticalHighDefects,
        string? SupportOwner,
        string? IncidentOwner,
        string? RollbackOwner,
        string? Note);
    private sealed record ReleaseCheck(string Id, string Label, string Status, string Detail);
    private sealed record EvidenceLink(string Label, string Document);
    private sealed record ReleaseDecisionSummary(string Decision, DateTimeOffset RecordedAtUtc, string RecordedBy, string? ReleaseSnapshotHash, string? SupportOwner, string? IncidentOwner, string? RollbackOwner, string? Note, bool Current);
    private sealed record ReleaseReport(DateTimeOffset GeneratedAtUtc, string ReleaseSnapshotHash, string OnboardingSnapshotHash, OnboardingEndpoints.HealthUnitSummary HealthUnit, bool ReadyForDecision, bool Released, IReadOnlyList<ReleaseCheck> Checks, IReadOnlyList<EvidenceLink> Evidence, ReleaseDecisionSummary? LatestDecision);
}
