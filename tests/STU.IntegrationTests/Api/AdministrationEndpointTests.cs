using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace STU.IntegrationTests.Api;

[Collection(StuApiFixtureDefinition.Name)]
public sealed class AdministrationEndpointTests(StuApiFactory factory)
{
    private static readonly string TestSuffix = Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task GlobalAdministratorCanManageOrganizationWithAuditAndArchiveRules()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

        using var login = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/auth/login", new
        {
            userName = StuApiFactory.AdministratorUserName,
            password = StuApiFactory.AdministratorPassword,
            portal = "admin",
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var passwordChange = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/auth/change-password", new
        {
            currentPassword = StuApiFactory.AdministratorPassword,
            newPassword = "Definitive!Admin67890",
            confirmPassword = "Definitive!Admin67890",
        });
        Assert.Equal(HttpStatusCode.OK, passwordChange.StatusCode);

        using var overviewCheck = await client.GetAsync("/api/admin/overview");
        Assert.Equal(HttpStatusCode.OK, overviewCheck.StatusCode);
        using var unitListCheck = await client.GetAsync("/api/admin/health-units");
        Assert.Equal(HttpStatusCode.OK, unitListCheck.StatusCode);

        var firstUnitId = await CreateUnitAsync(client, $"UN-{TestSuffix}", "UBS Norte");
        var secondUnitId = await CreateUnitAsync(client, $"US-{TestSuffix}", "UBS Sul");

        using var roleResponse = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/admin/roles", new
        {
            displayName = "Apoiador territorial",
            description = "Apoio operacional de teste",
            permissions = TestRolePermissions,
        });
        Assert.Equal(HttpStatusCode.Created, roleResponse.StatusCode);
        var rolePayload = await roleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var roleId = rolePayload.GetProperty("id").GetGuid();
        var roleName = rolePayload.GetProperty("name").GetString();

        using var userResponse = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/admin/users", new
        {
            userName = $"servidor.{TestSuffix}",
            displayName = "Servidor de teste",
            healthUnitId = firstUnitId,
            roleName,
        });
        Assert.Equal(HttpStatusCode.Created, userResponse.StatusCode);
        var userPayload = await userResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userId = userPayload.GetProperty("userId").GetGuid();
        Assert.False(string.IsNullOrWhiteSpace(userPayload.GetProperty("temporaryPassword").GetString()));

        using var inUseRole = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/admin/roles/{roleId}/archive", null);
        Assert.Equal(HttpStatusCode.Conflict, inUseRole.StatusCode);

        using var transfer = await SendWithCsrfAsync(client, HttpMethod.Put, $"/api/admin/users/{userId}", new
        {
            displayName = "Servidor transferido",
            healthUnitId = secondUnitId,
            roleName,
        });
        Assert.Equal(HttpStatusCode.NoContent, transfer.StatusCode);

        using var reset = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/admin/users/{userId}/reset-password", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var resetPayload = await reset.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(resetPayload.GetProperty("temporaryPassword").GetString()));

        using var archiveUser = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/admin/users/{userId}/archive", null);
        Assert.Equal(HttpStatusCode.NoContent, archiveUser.StatusCode);

        using var archiveRole = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/admin/roles/{roleId}/archive", null);
        Assert.Equal(HttpStatusCode.NoContent, archiveRole.StatusCode);

        using var blockedRestore = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/admin/users/{userId}/restore", null);
        Assert.Equal(HttpStatusCode.Conflict, blockedRestore.StatusCode);

        using var restoreRole = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/admin/roles/{roleId}/restore", null);
        Assert.Equal(HttpStatusCode.NoContent, restoreRole.StatusCode);

        using var restoreUser = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/admin/users/{userId}/restore", null);
        Assert.Equal(HttpStatusCode.NoContent, restoreUser.StatusCode);

        using var users = await client.GetAsync($"/api/admin/users?query=servidor.{TestSuffix}");
        users.EnsureSuccessStatusCode();
        var usersPayload = await users.Content.ReadFromJsonAsync<JsonElement>();
        var transferredUser = Assert.Single(usersPayload.GetProperty("items").EnumerateArray());
        Assert.Equal(userId, transferredUser.GetProperty("id").GetGuid());
        Assert.Equal(secondUnitId, transferredUser.GetProperty("healthUnitId").GetGuid());

        using var backupSettings = await client.GetAsync("/api/admin/backups/settings");
        backupSettings.EnsureSuccessStatusCode();
        var backupSettingsPayload = await backupSettings.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(backupSettingsPayload.GetProperty("enabled").GetBoolean());
        Assert.Equal("Sunday", backupSettingsPayload.GetProperty("dayOfWeek").GetString());

        using var invalidBackupSettings = await SendWithCsrfAsync(client, HttpMethod.Put, "/api/admin/backups/settings", new
        {
            enabled = true,
            dayOfWeek = "Wednesday",
            localHour = 24,
            retentionCount = 8,
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidBackupSettings.StatusCode);

        using var updateBackupSettings = await SendWithCsrfAsync(client, HttpMethod.Put, "/api/admin/backups/settings", new
        {
            enabled = true,
            dayOfWeek = "Wednesday",
            localHour = 2,
            retentionCount = 12,
        });
        Assert.Equal(HttpStatusCode.OK, updateBackupSettings.StatusCode);

        using var requestBackup = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/admin/backups/runs", null);
        Assert.Equal(HttpStatusCode.Accepted, requestBackup.StatusCode);
        var requestBackupPayload = await requestBackup.Content.ReadFromJsonAsync<JsonElement>();
        var backupRunId = requestBackupPayload.GetProperty("id").GetGuid();

        using var backupRuns = await client.GetAsync("/api/admin/backups/runs");
        backupRuns.EnsureSuccessStatusCode();
        var backupRunsPayload = await backupRuns.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(backupRunsPayload.EnumerateArray(), item =>
            item.GetProperty("id").GetGuid() == backupRunId &&
            item.GetProperty("status").GetString() == "Queued");

        using var monitoring = await client.GetAsync("/api/admin/monitoring/status");
        monitoring.EnsureSuccessStatusCode();
        var monitoringPayload = await monitoring.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, monitoringPayload.GetProperty("checks").GetArrayLength());
        Assert.Contains(
            monitoringPayload.GetProperty("checks").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "worker");
        Assert.Equal(JsonValueKind.Array, monitoringPayload.GetProperty("alerts").ValueKind);

        using var audit = await client.GetAsync("/api/admin/audit?pageSize=100");
        audit.EnsureSuccessStatusCode();
        var auditPayload = await audit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(auditPayload.GetProperty("total").GetInt32() >= 10);
        Assert.Contains(
            auditPayload.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("action").GetString() == "Transfer");
        Assert.Contains(
            auditPayload.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("entityType").GetString() == "BackupRun" &&
                item.GetProperty("action").GetString() == "Request");
    }

    private static async Task<Guid> CreateUnitAsync(HttpClient client, string code, string name)
    {
        using var response = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/admin/health-units", new { code, name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("id").GetGuid();
    }

    private static readonly string[] TestRolePermissions = ["map.view", "properties.view"];

    private static async Task<HttpResponseMessage> SendWithCsrfAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body)
    {
        using var csrfResponse = await client.GetAsync("/api/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = csrf.GetProperty("token").GetString();

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-STU-CSRF", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }
}
