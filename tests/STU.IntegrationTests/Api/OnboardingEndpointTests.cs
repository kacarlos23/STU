using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using STU.Application.Security;
using STU.Domain.HealthUnits;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.IntegrationTests.Api;

[Collection(StuApiFixtureDefinition.Name)]
public sealed class OnboardingEndpointTests(StuApiFactory factory)
{
    [Fact]
    public async Task ManagerSeesOnlyOwnReadinessAndCannotApproveWithBlockers()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "Onboarding!Test123";
        var userName = $"onboarding.{suffix}";
        Guid ownUnitId;
        Guid otherUnitId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var own = HealthUnit.Create($"ON-{suffix}", "UBS Ensaio");
            var other = HealthUnit.Create($"OO-{suffix}", "UBS Fora do Escopo");
            db.HealthUnits.AddRange(own, other);
            await db.SaveChangesAsync();
            ownUnitId = own.Id;
            otherUnitId = other.Id;
            var manager = ApplicationUser.Create(userName, "Gerente do ensaio", own.Id, mustChangePassword: false);
            Assert.True((await users.CreateAsync(manager, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(manager, SystemRoles.HealthUnitManager)).Succeeded);
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });
        using var login = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/auth/login", new { userName, password, portal = "main" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var reportResponse = await client.GetAsync($"/api/onboarding/readiness?healthUnitId={ownUnitId}");
        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        var report = await reportResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ownUnitId, report.GetProperty("healthUnit").GetProperty("id").GetGuid());
        Assert.False(report.GetProperty("readyForApproval").GetBoolean());
        Assert.Contains(report.GetProperty("checks").EnumerateArray(), item =>
            item.GetProperty("id").GetString() == "neighborhood-count" && item.GetProperty("status").GetString() == "blocked");

        using var forbidden = await client.GetAsync($"/api/onboarding/readiness?healthUnitId={otherUnitId}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var approval = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/onboarding/approve", new
        {
            healthUnitId = ownUnitId,
            backupSha256 = new string('a', 64),
            offsiteDestination = "Cofre de teste",
        });
        Assert.Equal(HttpStatusCode.Conflict, approval.StatusCode);
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
