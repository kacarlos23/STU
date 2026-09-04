using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using STU.Domain.Auditing;
using STU.Domain.Operations;
using STU.Infrastructure.Persistence;

namespace STU.Worker;

public sealed class BackupProcessor(StuDbContext db, IConfiguration configuration, ILogger<BackupProcessor> logger)
{
    private static readonly Action<ILogger, Guid, Exception?> LogBackupFailure = LoggerMessage.Define<Guid>(
        LogLevel.Error, new EventId(20, "BackupFailure"), "Falha ao executar o backup {BackupRunId}.");
    private static readonly Action<ILogger, Guid, string, Exception?> LogBackupCompleted = LoggerMessage.Define<Guid, string>(
        LogLevel.Information, new EventId(21, "BackupCompleted"), "Backup {BackupRunId} concluído no arquivo {FileName}.");
    private readonly string backupRoot = EnsureStorage(configuration);

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        await QueueScheduledRunAsync(settings, cancellationToken);
        await RecoverInterruptedRunsAsync(cancellationToken);

        var run = await db.BackupRuns
            .Where(item => item.Status == BackupRunStatus.Queued)
            .OrderBy(item => item.RequestedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null) return false;

        var temporaryPath = string.Empty;
        try
        {
            run.Start();
            await db.SaveChangesAsync(cancellationToken);

            var fileName = $"stu-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{run.Id:N}.dump";
            var finalPath = SafePath(fileName);
            temporaryPath = finalPath + ".partial";
            await ExecutePgDumpAsync(temporaryPath, cancellationToken);
            File.Move(temporaryPath, finalPath, false);

            var file = new FileInfo(finalPath);
            var sha256 = await ComputeSha256Async(finalPath, cancellationToken);
            run.Complete(fileName, file.Length, sha256);
            AddSystemAudit("Complete", run, $"Backup {run.Id} concluído.");
            await db.SaveChangesAsync(cancellationToken);
            await ApplyRetentionAsync(settings.RetentionCount, cancellationToken);
            LogBackupCompleted(logger, run.Id, fileName, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogBackupFailure(logger, run.Id, exception);
            DeleteIfPresent(temporaryPath);
            db.ChangeTracker.Clear();
            run = await db.BackupRuns.SingleAsync(item => item.Id == run.Id, cancellationToken);
            run.Fail("Não foi possível gerar o arquivo. Verifique o armazenamento e a conexão com o banco de dados.");
            AddSystemAudit("Fail", run, $"Backup {run.Id} falhou.");
            await db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private async Task QueueScheduledRunAsync(BackupSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.Enabled) return;
        var slot = settings.MostRecentSlot(DateTimeOffset.UtcNow);
        if (await db.BackupRuns.AnyAsync(item => item.ScheduleKey == slot.Key, cancellationToken)) return;

        var run = BackupRun.CreateScheduled(slot.Key);
        db.BackupRuns.Add(run);
        AddSystemAudit("Queue", run, $"Backup semanal de {slot.Key} incluído na fila.");
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (!await db.BackupRuns.AnyAsync(item => item.ScheduleKey == slot.Key, cancellationToken)) throw;
        }
    }

    private async Task RecoverInterruptedRunsAsync(CancellationToken cancellationToken)
    {
        var interrupted = await db.BackupRuns
            .Where(item => item.Status == BackupRunStatus.Running && item.StartedAtUtc < DateTimeOffset.UtcNow.AddMinutes(-30))
            .ToListAsync(cancellationToken);
        foreach (var run in interrupted)
        {
            run.RecoverInterrupted();
            AddSystemAudit(run.Status == BackupRunStatus.Failed ? "Fail" : "Recover", run, $"Execução interrompida do backup {run.Id} tratada.");
        }

        if (interrupted.Count > 0) await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ExecutePgDumpAsync(string outputPath, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("StuDatabase")
            ?? throw new InvalidOperationException("A conexão do banco de dados não foi configurada.");
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        var startInfo = new ProcessStartInfo
        {
            FileName = configuration["Backups:PgDumpPath"] ?? "pg_dump",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--format=custom");
        startInfo.ArgumentList.Add("--no-owner");
        startInfo.ArgumentList.Add("--no-privileges");
        startInfo.ArgumentList.Add($"--file={outputPath}");
        startInfo.ArgumentList.Add($"--host={connection.Host}");
        startInfo.ArgumentList.Add($"--port={connection.Port}");
        startInfo.ArgumentList.Add($"--username={connection.Username}");
        startInfo.ArgumentList.Add($"--dbname={connection.Database}");
        if (!string.IsNullOrEmpty(connection.Password)) startInfo.Environment["PGPASSWORD"] = connection.Password;

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("Não foi possível iniciar o utilitário de backup.");
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }

        var standardError = await standardErrorTask;
        _ = await standardOutputTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"pg_dump terminou com código {process.ExitCode}: {Limit(standardError, 800)}");
        }
    }

    private async Task ApplyRetentionAsync(int retentionCount, CancellationToken cancellationToken)
    {
        var expired = await db.BackupRuns
            .Where(item => item.Status == BackupRunStatus.Completed && item.FileName != null && item.FilePrunedAtUtc == null)
            .OrderByDescending(item => item.CompletedAtUtc)
            .Skip(retentionCount)
            .ToListAsync(cancellationToken);
        foreach (var item in expired)
        {
            DeleteIfPresent(SafePath(item.FileName!));
            item.MarkFilePruned();
            AddSystemAudit("Prune", item, $"Arquivo do backup {item.Id} removido pela retenção configurada.");
        }

        if (expired.Count > 0) await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<BackupSettings> GetOrCreateSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await db.BackupSettings.SingleOrDefaultAsync(cancellationToken);
        if (settings is not null) return settings;
        settings = BackupSettings.CreateDefault();
        db.BackupSettings.Add(settings);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return settings;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return await db.BackupSettings.SingleAsync(cancellationToken);
        }
    }

    private void AddSystemAudit(string action, BackupRun run, string summary) =>
        db.AuditEntries.Add(AuditEntry.Create(
            Guid.Empty,
            "stu-worker",
            action,
            "BackupRun",
            run.Id.ToString(),
            summary,
            null,
            null,
            null));

    private string SafePath(string fileName)
    {
        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Nome de arquivo de backup inválido.");
        }

        var path = Path.GetFullPath(Path.Combine(backupRoot, fileName));
        if (!path.StartsWith(backupRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Caminho de backup inválido.");
        }

        return path;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static string EnsureStorage(IConfiguration configuration)
    {
        var operationsRoot = configuration["Operations:StoragePath"];
        if (string.IsNullOrWhiteSpace(operationsRoot)) operationsRoot = Path.Combine(Path.GetTempPath(), "stu-operations");
        var root = Path.GetFullPath(Path.Combine(operationsRoot, "backups"));
        Directory.CreateDirectory(root);
        return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void DeleteIfPresent(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
    }

    private static string Limit(string value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? "sem detalhes" : value.Trim()[..Math.Min(value.Trim().Length, maximum)];
}
