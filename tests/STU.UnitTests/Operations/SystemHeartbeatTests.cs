using STU.Domain.Operations;

namespace STU.UnitTests.Operations;

public sealed class SystemHeartbeatTests
{
    [Fact]
    public void ReportAdvancesObservationAndRefreshesInstance()
    {
        var startedAt = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);
        var heartbeat = SystemHeartbeat.Start("worker", "worker-a", "1.0.0", startedAt);
        var previousToken = heartbeat.ConcurrencyToken;

        heartbeat.Report("worker-b", "1.1.0", startedAt.AddSeconds(15));

        Assert.Equal("worker-b", heartbeat.Instance);
        Assert.Equal("1.1.0", heartbeat.Version);
        Assert.Equal(startedAt.AddSeconds(15), heartbeat.LastSeenAtUtc);
        Assert.NotEqual(previousToken, heartbeat.ConcurrencyToken);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            heartbeat.Report("worker-b", "1.1.0", startedAt));
    }
}
