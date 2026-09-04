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
public sealed class OperationEndpointTests(StuApiFactory factory)
{
    [Fact]
    public async Task ExportJobIsQueuedAndHiddenFromAnotherHealthUnit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "Operations!Test123";
        Guid firstUnitId;
        string firstName = $"operacoes.a.{suffix}";
        string secondName = $"operacoes.b.{suffix}";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var first = HealthUnit.Create($"OA-{suffix}", "UBS Operações A");
            var second = HealthUnit.Create($"OB-{suffix}", "UBS Operações B");
            db.HealthUnits.AddRange(first, second); await db.SaveChangesAsync(); firstUnitId = first.Id;
            await CreateManagerAsync(users, firstName, password, first.Id);
            await CreateManagerAsync(users, secondName, password, second.Id);
        }

        using var firstClient = CreateClient(); await LoginAsync(firstClient, firstName, password);
        using var queued = await SendWithCsrfAsync(firstClient, HttpMethod.Post, "/api/operations/exports", new { format = "GeoJson" });
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        using var ownJobs = await firstClient.GetAsync("/api/operations/jobs");
        Assert.Equal(HttpStatusCode.OK, ownJobs.StatusCode);
        var jobs = await ownJobs.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(jobs.EnumerateArray(), job => job.GetProperty("format").GetString() == "GeoJson");

        using var secondClient = CreateClient(); await LoginAsync(secondClient, secondName, password);
        using var forbidden = await secondClient.GetAsync($"/api/operations/jobs?healthUnitId={firstUnitId}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = true });
    private static async Task CreateManagerAsync(UserManager<ApplicationUser> users, string name, string password, Guid unitId)
    {
        var user = ApplicationUser.Create(name, "Gerente operacional", unitId, mustChangePassword: false);
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, SystemRoles.HealthUnitManager)).Succeeded);
    }
    private static async Task LoginAsync(HttpClient client, string userName, string password)
    {
        using var response = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/auth/login", new { userName, password, portal = "main" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    private static async Task<HttpResponseMessage> SendWithCsrfAsync(HttpClient client, HttpMethod method, string path, object? body)
    {
        using var csrfResponse = await client.GetAsync("/api/auth/csrf"); csrfResponse.EnsureSuccessStatusCode();
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var request = new HttpRequestMessage(method, path); request.Headers.Add("X-STU-CSRF", csrf.GetProperty("token").GetString());
        if (body is not null) request.Content = JsonContent.Create(body); return await client.SendAsync(request);
    }
}
