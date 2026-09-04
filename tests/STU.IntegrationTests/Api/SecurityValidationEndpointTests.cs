using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using STU.Application.Security;
using STU.Infrastructure.Identity;

namespace STU.IntegrationTests.Api;

[Collection(StuApiFixtureDefinition.Name)]
public sealed class SecurityValidationEndpointTests(StuApiFactory factory)
{
    private const string Password = "Security!Test123";

    [Theory]
    [InlineData("/api/admin/overview")]
    [InlineData("/api/admin/audit")]
    [InlineData("/api/admin/backups/settings")]
    [InlineData("/api/admin/monitoring/status")]
    [InlineData("/api/territories/map")]
    [InlineData("/api/properties")]
    [InlineData("/api/operations/jobs")]
    public async Task PrivateEndpointsRejectAnonymousRequests(string path)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GlobalAdministratorIsRestrictedToTheIsolatedPortal()
    {
        var userName = $"security.global.{Guid.NewGuid():N}";
        await CreateUserAsync(userName, SystemRoles.GlobalAdministrator, null);

        using var mainClient = CreateClient();
        using var rejected = await SendWithCsrfAsync(mainClient, HttpMethod.Post, "/api/auth/login", new
        {
            userName,
            password = Password,
            portal = "main",
        });
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);

        using var adminClient = CreateClient();
        using var accepted = await SendWithCsrfAsync(adminClient, HttpMethod.Post, "/api/auth/login", new
        {
            userName,
            password = Password,
            portal = "admin",
        });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task MutationsRequireCsrfAndLogoutInvalidatesTheSession()
    {
        var userName = $"security.manager.{Guid.NewGuid():N}";
        var unitId = await factory.CreateHealthUnitAsync();
        await CreateUserAsync(userName, SystemRoles.HealthUnitManager, unitId);

        using var anonymousClient = CreateClient();
        using var loginWithoutCsrf = await anonymousClient.PostAsJsonAsync("/api/auth/login", new
        {
            userName,
            password = Password,
            portal = "main",
        });
        Assert.Equal(HttpStatusCode.BadRequest, loginWithoutCsrf.StatusCode);

        using var client = CreateClient();
        using var login = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/auth/login", new
        {
            userName,
            password = Password,
            portal = "main",
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var sessionCookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), value => value.StartsWith("stu.session=", StringComparison.Ordinal));
        Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", sessionCookie, StringComparison.OrdinalIgnoreCase);

        using var mutationWithoutCsrf = await client.PostAsJsonAsync("/api/territories/neighborhoods", new
        {
            name = "NÃ£o deve ser criado",
            geometry = new { type = "Polygon", coordinates = Array.Empty<double[]>() },
            source = "Manual",
        });
        Assert.Equal(HttpStatusCode.BadRequest, mutationWithoutCsrf.StatusCode);

        using var logout = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var afterLogout = await client.GetAsync("/api/dashboard/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task ArchivingAnAccountRevokesItsExistingSessionImmediately()
    {
        var userName = $"security.archive.{Guid.NewGuid():N}";
        var unitId = await factory.CreateHealthUnitAsync();
        var userId = await CreateUserAsync(userName, SystemRoles.HealthUnitManager, unitId);
        using var client = CreateClient();
        using var login = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/auth/login", new
        {
            userName,
            password = Password,
            portal = "main",
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/dashboard/summary")).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(userId.ToString());
            Assert.NotNull(user);
            user.Archive();
            Assert.True((await users.UpdateAsync(user)).Succeeded);
            Assert.True((await users.UpdateSecurityStampAsync(user)).Succeeded);
        }

        using var afterArchive = await client.GetAsync("/api/dashboard/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, afterArchive.StatusCode);
    }

    [Fact]
    public async Task DisallowedOriginsAndMalformedJsonDoNotExposeDetails()
    {
        using var client = CreateClient();
        using var originRequest = new HttpRequestMessage(HttpMethod.Get, "/api/system/info");
        originRequest.Headers.Add("Origin", "https://attacker.invalid");
        using var originResponse = await client.SendAsync(originRequest);
        Assert.Equal(HttpStatusCode.OK, originResponse.StatusCode);
        Assert.False(originResponse.Headers.Contains("Access-Control-Allow-Origin"));

        using var csrfResponse = await client.GetAsync("/api/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var malformedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new StringContent("{not-json", Encoding.UTF8, "application/json"),
        };
        malformedRequest.Headers.Add("X-STU-CSRF", csrf.GetProperty("token").GetString());
        using var malformedResponse = await client.SendAsync(malformedRequest);
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        var body = await malformedResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", body, StringComparison.Ordinal);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
    });

    private async Task<Guid> CreateUserAsync(string userName, string role, Guid? healthUnitId)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = ApplicationUser.Create(userName, "UsuÃ¡rio da validaÃ§Ã£o", healthUnitId, mustChangePassword: false);
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user.Id;
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
