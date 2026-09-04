using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using STU.Application.Security;
using STU.Domain.HealthUnits;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.IntegrationTests.Api;

[Collection(StuApiFixtureDefinition.Name)]
public sealed class PilotReleaseEndpointTests(StuApiFactory factory)
{
    [Fact]
    public async Task ManagerCanRecordNoGoButCannotReleaseWithBlockersOrReadAnotherUnit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "Release!Test123";
        var userName = $"release.{suffix}";
        Guid ownUnitId;
        Guid otherUnitId;
        Guid managerId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var own = HealthUnit.Create($"RL-{suffix}", "UBS Liberação");
            var other = HealthUnit.Create($"RX-{suffix}", "UBS Fora da Liberação");
            db.HealthUnits.AddRange(own, other);
            await db.SaveChangesAsync();
            ownUnitId = own.Id;
            otherUnitId = other.Id;
            var manager = ApplicationUser.Create(userName, "Gerente da liberação", own.Id, mustChangePassword: false);
            Assert.True((await users.CreateAsync(manager, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(manager, SystemRoles.HealthUnitManager)).Succeeded);
            managerId = manager.Id;
        }

        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var unauthorized = await anonymous.GetAsync($"/api/pilot-release/readiness?healthUnitId={ownUnitId}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });
        using var login = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/auth/login", new { userName, password, portal = "main" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var reportResponse = await client.GetAsync($"/api/pilot-release/readiness?healthUnitId={ownUnitId}");
        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        var report = await reportResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ownUnitId, report.GetProperty("healthUnit").GetProperty("id").GetGuid());
        Assert.False(report.GetProperty("readyForDecision").GetBoolean());
        Assert.Contains(report.GetProperty("checks").EnumerateArray(), item =>
            item.GetProperty("id").GetString() == "onboarding" && item.GetProperty("status").GetString() == "blocked");

        using var forbidden = await client.GetAsync($"/api/pilot-release/readiness?healthUnitId={otherUnitId}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var blockedGo = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/pilot-release/decision", new
        {
            healthUnitId = ownUnitId,
            decision = "go",
            humanAccessibilityAccepted = true,
            trainingCompleted = true,
            userAcceptanceCompleted = true,
            noCriticalHighDefects = true,
            supportOwner = "Suporte",
            incidentOwner = "Incidentes",
            rollbackOwner = "Restauração",
        });
        Assert.Equal(HttpStatusCode.Conflict, blockedGo.StatusCode);

        using var noGo = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/pilot-release/decision", new
        {
            healthUnitId = ownUnitId,
            decision = "no-go",
            note = "Território real e cópia externa ainda estão pendentes.",
        });
        Assert.Equal(HttpStatusCode.OK, noGo.StatusCode);

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<StuDbContext>();
        var audit = await verificationDb.AuditEntries.AsNoTracking().SingleAsync(item =>
            item.ActorUserId == managerId && item.EntityType == "PilotReleaseDecision" && item.EntityId == ownUnitId.ToString());
        Assert.Equal("Hold", audit.Action);
        using var evidence = JsonDocument.Parse(audit.AfterJson!);
        Assert.Equal("no-go", evidence.RootElement.GetProperty("decision").GetString());
    }

    private static async Task<HttpResponseMessage> SendWithCsrfAsync(HttpClient client, HttpMethod method, string path, object? body)
    {
        using var csrfResponse = await client.GetAsync("/api/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-STU-CSRF", csrf.GetProperty("token").GetString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
