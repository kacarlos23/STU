using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using STU.Infrastructure.Persistence;

namespace STU.Api.Monitoring;

public sealed class DatabaseReadinessCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<StuDbContext>();
            return await database.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Banco de dados disponível.")
                : HealthCheckResult.Unhealthy("Banco de dados indisponível.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Banco de dados indisponível.", exception);
        }
    }
}

public sealed class StorageReadinessCheck(IConfiguration configuration) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = OperationsStorageProbe.Check(configuration);
        return Task.FromResult(result.Available
            ? HealthCheckResult.Healthy("Armazenamento operacional gravável.")
            : HealthCheckResult.Unhealthy("Armazenamento operacional indisponível."));
    }
}

public sealed class WorkerReadinessCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<StuDbContext>();
        var lastSeenAtUtc = await database.SystemHeartbeats
            .Where(item => item.Component == "worker")
            .Select(item => (DateTimeOffset?)item.LastSeenAtUtc)
            .SingleOrDefaultAsync(cancellationToken);
        if (!lastSeenAtUtc.HasValue)
        {
            return HealthCheckResult.Unhealthy("Worker ainda não enviou heartbeat.");
        }

        var age = DateTimeOffset.UtcNow - lastSeenAtUtc.Value;
        return age <= TimeSpan.FromSeconds(45)
            ? HealthCheckResult.Healthy("Worker ativo.")
            : HealthCheckResult.Unhealthy("Heartbeat do worker está atrasado.");
    }
}

public static class OperationsStorageProbe
{
    public static StorageProbeResult Check(IConfiguration configuration)
    {
        try
        {
            var operationsRoot = configuration["Operations:StoragePath"];
            if (string.IsNullOrWhiteSpace(operationsRoot))
            {
                operationsRoot = Path.Combine(Path.GetTempPath(), "stu-operations");
            }

            var root = Path.GetFullPath(operationsRoot);
            Directory.CreateDirectory(root);
            var probePath = Path.Combine(root, $".health-{Guid.NewGuid():N}.tmp");
            using (new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }

            var drive = new DriveInfo(Path.GetPathRoot(root) ?? root);
            return new StorageProbeResult(true, drive.AvailableFreeSpace);
        }
        catch
        {
            return new StorageProbeResult(false, null);
        }
    }
}

public sealed record StorageProbeResult(bool Available, long? AvailableBytes);
