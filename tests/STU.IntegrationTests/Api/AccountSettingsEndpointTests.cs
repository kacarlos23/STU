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
public sealed class AccountSettingsEndpointTests(StuApiFactory factory)
{
    private const string Password = "Profile!Test12345";
    private static readonly string[] EscalatedRoles = [SystemRoles.GlobalAdministrator];
    private static readonly string[] AgentRoles = [SystemRoles.HealthAgent];
    private const string Photo = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAACXBIWXMAAAPoAAAD6AG1e1JrAAAADUlEQVQImWNocPj/HwAFwgK/kgLZcAAAAABJRU5ErkJggg==";

    [Fact]
    public async Task OwnProfileValidatesPhotoAndConcurrencyWithoutChangingScopeOrRoles()
    {
        var actors = await CreateActorsAsync();
        using var client = await LoginAsync(actors.AgentName);
        using var anonymous = NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/auth/profile")).StatusCode);
        var profile = await client.GetFromJsonAsync<JsonElement>("/api/auth/profile");
        var token = profile.GetProperty("concurrencyToken").GetString();
        using var noCsrf = await client.PutAsJsonAsync("/api/auth/profile", new { displayName = "Negado", appearance = "light", concurrencyToken = token });
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        foreach (var invalid in new[] {
            new { displayName = "  ", appearance = "light", concurrencyToken = token, photo = (string?)null },
            new { displayName = "Agente", appearance = "system", concurrencyToken = token, photo = (string?)null },
            new { displayName = "Agente", appearance = "light", concurrencyToken = token, photo = (string?)Convert.ToBase64String("<svg onload='alert(1)'/>"u8) },
            // Valid chunk checksums with a corrupt zlib stream must be rejected without a server error.
            new { displayName = "Agente", appearance = "light", concurrencyToken = token, photo = (string?)"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAACXBIWXMAAAPoAAAD6AG1e1JrAAAADUlEQVQAmWNocPj/HwAFwgK/yAEKHQAAAABJRU5ErkJggg==" },
            new { displayName = "Agente", appearance = "light", concurrencyToken = token, photo = (string?)Convert.ToBase64String(new byte[524289]) },
        })
        {
            using var rejected = await SendAsync(client, HttpMethod.Put, "/api/auth/profile", invalid);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        using var save = await SendAsync(client, HttpMethod.Put, "/api/auth/profile", new
        {
            displayName = "Nome atualizado", appearance = "dark", concurrencyToken = token, photo = Photo,
            healthUnitId = actors.OtherUnitId, roles = EscalatedRoles, id = actors.ManagerId,
        });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var updated = await save.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Nome atualizado", updated.GetProperty("displayName").GetString());
        Assert.Equal("dark", updated.GetProperty("appearance").GetString());
        var avatar = updated.GetProperty("avatarUrl").GetString()!;
        using var photo = await client.GetAsync(avatar);
        Assert.Equal("image/png", photo.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", photo.Headers.CacheControl!.ToString());
        Assert.Equal(Convert.FromBase64String(Photo), await photo.Content.ReadAsByteArrayAsync());
        using var other = await LoginAsync(actors.OutsideManagerName);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(avatar)).StatusCode);
        using var stale = await SendAsync(client, HttpMethod.Put, "/api/auth/profile", new { displayName = "Sobrescrito", appearance = "light", concurrencyToken = token });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var refresh = await LoginAsync(actors.AgentName);
        var reloaded = await refresh.GetFromJsonAsync<JsonElement>("/api/auth/profile");
        Assert.Equal("dark", reloaded.GetProperty("appearance").GetString());
        using var remove = await SendAsync(refresh, HttpMethod.Put, "/api/auth/profile", new { displayName = "Nome atualizado", appearance = "dark", concurrencyToken = reloaded.GetProperty("concurrencyToken").GetString(), removePhoto = true });
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await refresh.GetAsync(avatar)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == actors.AgentId);
        Assert.Equal(actors.UnitId, user.HealthUnitId);
        Assert.Null(user.ProfilePhoto);
        Assert.Equal("dark", user.AppearancePreference);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Equal(AgentRoles, await users.GetRolesAsync(user));
        Assert.Equal("Gerente do perfil", (await db.Users.SingleAsync(u => u.Id == actors.ManagerId)).DisplayName);
    }

    [Fact]
    public async Task PasswordRequestNotifiesOwnManagerOnceAndIsCompletedThroughExistingReset()
    {
        var actors = await CreateActorsAsync();
        using var agent = await LoginAsync(actors.AgentName);
        for (var i = 0; i < 2; i++)
        {
            using var request = await SendAsync(agent, HttpMethod.Post, "/api/auth/profile/password-reset-request", null);
            Assert.Equal(HttpStatusCode.OK, request.StatusCode);
            Assert.Equal(JsonValueKind.String, (await request.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("passwordResetRequestedAtUtc").ValueKind);
        }
        using var manager = await LoginAsync(actors.ManagerName);
        var notifications = await manager.GetFromJsonAsync<JsonElement>("/api/notifications?unreadOnly=false");
        var notice = Assert.Single(notifications.GetProperty("items").EnumerateArray());
        Assert.Contains(actors.AgentName, notice.GetProperty("message").GetString());
        Assert.Equal("/users", notice.GetProperty("link").GetString());
        using var outside = await LoginAsync(actors.OutsideManagerName);
        var others = await outside.GetFromJsonAsync<JsonElement>("/api/notifications?unreadOnly=false");
        Assert.Empty(others.GetProperty("items").EnumerateArray());
        using var denied = await SendAsync(outside, HttpMethod.Post, $"/api/health-unit-users/{actors.AgentId}/reset-password", null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var reset = await SendAsync(manager, HttpMethod.Post, $"/api/health-unit-users/{actors.AgentId}/reset-password", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
        var saved = await db.Users.SingleAsync(u => u.Id == actors.AgentId);
        Assert.Null(saved.PasswordResetRequestedAtUtc);
        Assert.True(saved.MustChangePassword);
        Assert.True(await db.UserNotifications.AnyAsync(n => n.UserId == saved.Id && n.Title == "Pedido de senha atendido"));
        var temporary = (await reset.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("temporaryPassword").GetString();
        using var forced = NewClient();
        using var login = await SendAsync(forced, HttpMethod.Post, "/api/auth/login", new { userName = actors.AgentName, password = temporary, portal = "main" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await forced.GetAsync("/api/auth/profile")).StatusCode);
    }

    [Fact]
    public async Task ManagerPasswordRequestIsEscalatedToGlobalAdministration()
    {
        var actors = await CreateActorsAsync();
        using var manager = await LoginAsync(actors.ManagerName);
        using var request = await SendAsync(manager, HttpMethod.Post, "/api/auth/profile/password-reset-request", null);
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var global = (await users.GetUsersInRoleAsync(SystemRoles.GlobalAdministrator)).Where(u => !u.IsArchived).Select(u => u.Id).ToArray();
        var notices = await db.UserNotifications.Where(n => n.Message.Contains(actors.ManagerName)).ToListAsync();
        Assert.NotEmpty(notices);
        Assert.All(notices, n => { Assert.Contains(n.UserId, global); Assert.Equal("/admin/users", n.Link); });
    }

    private HttpClient NewClient() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = true });
    private async Task<HttpClient> LoginAsync(string userName)
    {
        var client = NewClient();
        using var response = await SendAsync(client, HttpMethod.Post, "/api/auth/login", new { userName, password = Password, portal = "main" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return client;
    }
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("X-STU-CSRF", csrf.GetProperty("token").GetString()); return await client.SendAsync(request);
    }
    private async Task<Actors> CreateActorsAsync()
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var suffix = Guid.NewGuid().ToString("N")[..8];
        var unit = HealthUnit.Create($"PS-{suffix}", "UBS de perfil"); var other = HealthUnit.Create($"PX-{suffix}", "Outra UBS");
        db.HealthUnits.AddRange(unit, other); await db.SaveChangesAsync();
        var manager = ApplicationUser.Create($"profile.manager.{suffix}", "Gerente do perfil", unit.Id, false);
        var outsider = ApplicationUser.Create($"profile.outside.{suffix}", "Outro gerente", other.Id, false);
        var agent = ApplicationUser.Create($"profile.agent.{suffix}", "Agente do perfil", unit.Id, false);
        foreach (var user in new[] { manager, outsider, agent }) { Assert.True((await users.CreateAsync(user, Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, user == agent ? SystemRoles.HealthAgent : SystemRoles.HealthUnitManager)).Succeeded); }
        return new(unit.Id, other.Id, manager.Id, agent.Id, manager.UserName!, outsider.UserName!, agent.UserName!);
    }
    private sealed record Actors(Guid UnitId, Guid OtherUnitId, Guid ManagerId, Guid AgentId, string ManagerName, string OutsideManagerName, string AgentName);
}
