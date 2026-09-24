using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using STU.Application.Security;
using STU.Domain.HealthUnits;
using STU.Domain.Operations;
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

    [Fact]
    public async Task RetryingImportRequiresPropertyManagementPermission()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "Operations!Test123";
        var userName = $"operacoes.restritas.{suffix}";
        Guid jobId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var unit = HealthUnit.Create($"OR-{suffix}", "UBS Operações Restritas");
            db.HealthUnits.Add(unit); await db.SaveChangesAsync();

            var roleName = $"operacoes-restritas-{suffix}";
            var role = ApplicationRole.CreateCustom(roleName, "Operações restritas", null);
            Assert.True((await roles.CreateAsync(role)).Succeeded);
            foreach (var permission in new[] { StuPermissions.TerritoryManage, StuPermissions.FamiliesManage })
                Assert.True((await roles.AddClaimAsync(role, new Claim(StuClaimTypes.Permission, permission))).Succeeded);

            var user = ApplicationUser.Create(userName, "Operador restrito", unit.Id, mustChangePassword: false);
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, roleName)).Succeeded);

            var job = OperationJob.CreateImport(unit.Id, user.Id, OperationFileFormat.Csv, "synthetic.csv", "synthetic.csv");
            job.Start(); job.Fail("Falha sintética."); db.OperationJobs.Add(job); await db.SaveChangesAsync(); jobId = job.Id;
        }

        using var client = CreateClient(); await LoginAsync(client, userName, password);
        using var retry = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/operations/jobs/{jobId}/retry", null);
        Assert.Equal(HttpStatusCode.Forbidden, retry.StatusCode);

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<StuDbContext>();
        Assert.Equal(OperationJobStatus.Failed, (await verificationDb.OperationJobs.SingleAsync(job => job.Id == jobId)).Status);
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
