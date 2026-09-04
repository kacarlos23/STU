using System.Reflection;
using Microsoft.EntityFrameworkCore;
using STU.Domain.Operations;
using STU.Infrastructure.Persistence;

namespace STU.Worker;

public sealed class WorkerHeartbeatReporter(StuDbContext db)
{
    public const string ComponentName = "worker";
    private static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

    public async Task ReportAsync(CancellationToken cancellationToken)
    {
        var observedAtUtc = DateTimeOffset.UtcNow;
        var heartbeat = await db.SystemHeartbeats.SingleOrDefaultAsync(
            item => item.Component == ComponentName,
            cancellationToken);
        if (heartbeat is null)
        {
            db.SystemHeartbeats.Add(SystemHeartbeat.Start(
                ComponentName,
                Environment.MachineName,
                Version,
                observedAtUtc));
        }
        else
        {
            heartbeat.Report(Environment.MachineName, Version, observedAtUtc);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
