using System.Net;
using Microsoft.Extensions.DependencyInjection;
using STU.Domain.Operations;
using STU.Infrastructure.Persistence;

namespace STU.IntegrationTests.Api;

[Collection(StuApiFixtureDefinition.Name)]
public sealed class HealthEndpointTests(StuApiFactory factory)
{
    [Fact]
    public async Task LiveHealthReturnsOk()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadinessDetectsWorkerAndRecoversAfterHeartbeat()
    {
        using var client = factory.CreateClient();
        using var beforeHeartbeat = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, beforeHeartbeat.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<StuDbContext>();
            database.SystemHeartbeats.Add(SystemHeartbeat.Start(
                "worker",
                "integration-test",
                "1.0.0",
                DateTimeOffset.UtcNow));
            await database.SaveChangesAsync();
        }

        using var afterHeartbeat = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, afterHeartbeat.StatusCode);
    }
}
