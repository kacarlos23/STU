namespace STU.Worker;

public sealed class Worker(IServiceScopeFactory scopeFactory, ILogger<Worker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogCycleFailure = LoggerMessage.Define(
        LogLevel.Error, new EventId(1, "OperationCycleFailure"), "Falha no ciclo do processador de operações do STU.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextHeartbeatAtUtc = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                if (DateTimeOffset.UtcNow >= nextHeartbeatAtUtc)
                {
                    var heartbeatReporter = scope.ServiceProvider.GetRequiredService<WorkerHeartbeatReporter>();
                    await heartbeatReporter.ReportAsync(stoppingToken);
                    nextHeartbeatAtUtc = DateTimeOffset.UtcNow.AddSeconds(15);
                }
                var backupProcessor = scope.ServiceProvider.GetRequiredService<BackupProcessor>();
                var operationProcessor = scope.ServiceProvider.GetRequiredService<OperationJobProcessor>();
                var processed = await backupProcessor.ProcessNextAsync(stoppingToken) ||
                    await operationProcessor.ProcessNextAsync(stoppingToken);
                if (!processed) await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                LogCycleFailure(logger, exception);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
