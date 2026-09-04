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
public sealed class HealthUnitUserEndpointTests(StuApiFactory factory)
{
    [Fact]
    public async Task ManagerManagesOperationalAccountsOnlyInsideOwnUnit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "UnitUsers!Test123";
        var managerName = $"manager.users.{suffix}";
        Guid ownUnitId;
        Guid otherUserId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var own = HealthUnit.Create($"HU-{suffix}", "UBS da Equipe");
            var other = HealthUnit.Create($"HX-{suffix}", "UBS Externa");
            db.HealthUnits.AddRange(own, other);
            await db.SaveChangesAsync();
            ownUnitId = own.Id;
            var manager = ApplicationUser.Create(managerName, "Gerente da equipe", own.Id, mustChangePassword: false);
            Assert.True((await users.CreateAsync(manager, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(manager, SystemRoles.HealthUnitManager)).Succeeded);
            var outsider = ApplicationUser.Create($"outside.{suffix}", "Servidor externo", other.Id, mustChangePassword: false);
            Assert.True((await users.CreateAsync(outsider, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(outsider, SystemRoles.HealthAgent)).Succeeded);
            otherUserId = outsider.Id;
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        using var login = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/auth/login", new { userName = managerName, password, portal = "main" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var referenceResponse = await client.GetAsync("/api/health-unit-users/reference-data");
        Assert.Equal(HttpStatusCode.OK, referenceResponse.StatusCode);
        var reference = await referenceResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ownUnitId, reference.GetProperty("healthUnit").GetProperty("id").GetGuid());
        Assert.DoesNotContain(reference.GetProperty("roles").EnumerateArray(), item => item.GetProperty("name").GetString() is SystemRoles.GlobalAdministrator or SystemRoles.HealthUnitManager);

        var employeeName = $"agent.{suffix}";
        using var create = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/health-unit-users", new { userName = employeeName, displayName = "Agente cadastrado", roleName = SystemRoles.HealthAgent });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var createdId = created.GetProperty("userId").GetGuid();
        Assert.True(created.GetProperty("temporaryPassword").GetString()!.Length >= 14);

        using var protectedRole = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/health-unit-users", new { userName = $"manager.denied.{suffix}", displayName = "Gerente negado", roleName = SystemRoles.HealthUnitManager });
        Assert.Equal(HttpStatusCode.BadRequest, protectedRole.StatusCode);

        using var crossUnit = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/health-unit-users/{otherUserId}/reset-password", null);
        Assert.Equal(HttpStatusCode.Forbidden, crossUnit.StatusCode);

        using var update = await SendWithCsrfAsync(client, HttpMethod.Put, $"/api/health-unit-users/{createdId}", new { displayName = "Servidor atualizado", roleName = SystemRoles.Doctor });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        using var reset = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/health-unit-users/{createdId}/reset-password", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        using var archive = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/health-unit-users/{createdId}/archive", null);
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);

        using var list = await client.GetAsync($"/api/health-unit-users?includeArchived=true&pageSize=100&query={suffix}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listed = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(listed.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == createdId && item.GetProperty("archivedAtUtc").ValueKind == JsonValueKind.String);
        Assert.DoesNotContain(listed.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == otherUserId);

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<StuDbContext>();
        var saved = await verificationDb.Users.AsNoTracking().SingleAsync(item => item.Id == createdId);
        Assert.Equal(ownUnitId, saved.HealthUnitId);
        Assert.True(saved.MustChangePassword);
        Assert.NotNull(saved.ArchivedAtUtc);
        Assert.True(await verificationDb.AuditEntries.AnyAsync(item => item.EntityType == "User" && item.EntityId == createdId.ToString() && item.Action == "Create"));
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
