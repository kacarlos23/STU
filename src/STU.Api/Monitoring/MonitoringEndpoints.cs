using Microsoft.EntityFrameworkCore;
using STU.Application.Security;
using STU.Domain.Operations;
using STU.Infrastructure.Persistence;

namespace STU.Api.Monitoring;

public static class MonitoringEndpoints
{
    public static IEndpointRouteBuilder MapMonitoringEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/monitoring/status", GetStatusAsync)
            .WithTags("Monitoring")
            .RequireAuthorization(StuPolicies.GlobalAdministration)
            .RequireRateLimiting("api");
        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(
        StuDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var worker = await dbContext.SystemHeartbeats.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Component == "worker", cancellationToken);
        var workerHealthy = worker is not null && now - worker.LastSeenAtUtc <= TimeSpan.FromSeconds(45);
        var storage = OperationsStorageProbe.Check(configuration);
        var settings = await dbContext.BackupSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var lastBackup = await dbContext.BackupRuns.AsNoTracking()
            .Where(item => item.Status == BackupRunStatus.Completed)
            .OrderByDescending(item => item.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var lastBackupFailure = await dbContext.BackupRuns.AsNoTracking()
            .Where(item => item.Status == BackupRunStatus.Failed)
            .OrderByDescending(item => item.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var stalledBackups = await dbContext.BackupRuns.CountAsync(item =>
            item.Status == BackupRunStatus.Running && item.StartedAtUtc < now.AddMinutes(-30), cancellationToken);
        var recentOperationFailures = await dbContext.OperationJobs.CountAsync(item =>
            item.Status == OperationJobStatus.Failed && item.CompletedAtUtc >= now.AddHours(-24), cancellationToken);
        var stalledOperations = await dbContext.OperationJobs.CountAsync(item =>
            (item.Status == OperationJobStatus.Pending && item.CreatedAtUtc < now.AddMinutes(-30)) ||
            (item.Status == OperationJobStatus.Processing && item.StartedAtUtc < now.AddMinutes(-15)), cancellationToken);

        var alerts = new List<MonitoringAlert>();
        var workerStatus = workerHealthy ? "Healthy" : "Critical";
        if (!workerHealthy)
        {
            alerts.Add(new("worker-offline", "Critical", "Processador em segundo plano indisponível", "Importações, exportações e backups automáticos podem não ser processados."));
        }

        var storageStatus = storage.Available ? "Healthy" : "Critical";
        if (!storage.Available)
        {
            alerts.Add(new("storage-unavailable", "Critical", "Armazenamento operacional indisponível", "Arquivos de importação, exportação e backup não podem ser gravados."));
        }
        else if (storage.AvailableBytes is < 536_870_912)
        {
            storageStatus = "Warning";
            alerts.Add(new("storage-low", "Warning", "Pouco espaço disponível", "O armazenamento operacional possui menos de 512 MB livres."));
        }

        var backupStatus = "Healthy";
        var backupsEnabled = settings?.Enabled ?? true;
        if (stalledBackups > 0)
        {
            backupStatus = "Critical";
            alerts.Add(new("backup-stalled", "Critical", "Backup sem progresso", "Existe uma cópia em execução há mais de 30 minutos."));
        }
        else if (lastBackupFailure is not null &&
            (!lastBackup?.CompletedAtUtc.HasValue ?? true || lastBackupFailure.CompletedAtUtc > lastBackup.CompletedAtUtc))
        {
            backupStatus = "Critical";
            alerts.Add(new("backup-failed", "Critical", "Último backup falhou", "Solicite uma nova cópia e verifique o armazenamento e o banco de dados."));
        }
        else if (backupsEnabled && (lastBackup?.CompletedAtUtc is null || lastBackup.CompletedAtUtc < now.AddDays(-8)))
        {
            backupStatus = "Warning";
            alerts.Add(new("backup-overdue", "Warning", "Backup semanal atrasado", "Não há uma cópia concluída nos últimos oito dias."));
        }

        var operationsStatus = recentOperationFailures > 0 || stalledOperations > 0 ? "Warning" : "Healthy";
        if (stalledOperations > 0)
        {
            alerts.Add(new("operations-stalled", "Warning", "Trabalhos operacionais atrasados", $"{stalledOperations} trabalho(s) estão aguardando ou processando além do limite esperado."));
        }
        if (recentOperationFailures > 0)
        {
            alerts.Add(new("operations-failed", "Warning", "Falhas operacionais recentes", $"{recentOperationFailures} trabalho(s) falharam nas últimas 24 horas."));
        }

        var checks = new[]
        {
            new MonitoringCheck("database", "Banco de dados", "Healthy", "Conexão utilizada pelo painel está disponível.", now),
            new MonitoringCheck("worker", "Processador em segundo plano", workerStatus, workerHealthy ? "Heartbeat recebido dentro do limite." : "Heartbeat ausente ou atrasado.", worker?.LastSeenAtUtc),
            new MonitoringCheck("storage", "Armazenamento de arquivos", storageStatus, storage.Available ? "Diretório compartilhado disponível para gravação." : "Não foi possível gravar no diretório compartilhado.", now),
            new MonitoringCheck("backups", "Rotina de backups", backupStatus, lastBackup?.CompletedAtUtc is null ? "Nenhuma cópia concluída." : "Última cópia concluída com sucesso.", lastBackup?.CompletedAtUtc),
            new MonitoringCheck("operations", "Importações e exportações", operationsStatus, operationsStatus == "Healthy" ? "Nenhum atraso ou falha recente." : "Existem trabalhos que exigem atenção.", now),
        };
        var overallStatus = checks.Any(item => item.Status == "Critical")
            ? "Critical"
            : checks.Any(item => item.Status == "Warning") ? "Warning" : "Healthy";

        return Results.Ok(new
        {
            checkedAtUtc = now,
            overallStatus,
            checks,
            alerts = alerts.OrderBy(item => item.Severity == "Critical" ? 0 : 1).ToArray(),
        });
    }
}

public sealed record MonitoringCheck(
    string Id,
    string Label,
    string Status,
    string Detail,
    DateTimeOffset? LastObservedAtUtc);

public sealed record MonitoringAlert(string Code, string Severity, string Title, string Detail);
