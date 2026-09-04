using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace STU.IntegrationTests.Api;

[Collection(StuApiFixtureDefinition.Name)]
public sealed class AuthenticationEndpointTests(StuApiFactory factory)
{
    [Fact]
    public async Task TemporaryPasswordMustBeChangedBeforeOperationalAccess()
    {
        using var deniedClient = CreateClient();
        using var deniedResponse = await PostWithCsrfAsync(deniedClient, "/api/auth/login", new
        {
            userName = StuApiFactory.ManagerUserName,
            password = StuApiFactory.ManagerPassword,
            portal = "admin",
        });
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var client = CreateClient();

        using var loginResponse = await PostWithCsrfAsync(client, "/api/auth/login", new
        {
            userName = StuApiFactory.ManagerUserName,
            password = StuApiFactory.ManagerPassword,
            portal = "main",
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var initialSession = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(initialSession.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal("UBS de Teste", initialSession.GetProperty("healthUnit").GetProperty("name").GetString());

        using var blockedDashboard = await client.GetAsync("/api/dashboard/summary");
        Assert.Equal(HttpStatusCode.Forbidden, blockedDashboard.StatusCode);

        using var passwordResponse = await PostWithCsrfAsync(client, "/api/auth/change-password", new
        {
            currentPassword = StuApiFactory.ManagerPassword,
            newPassword = "Definitive!67890",
            confirmPassword = "Definitive!67890",
        });
        Assert.Equal(HttpStatusCode.OK, passwordResponse.StatusCode);
        var updatedSession = await passwordResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(updatedSession.GetProperty("mustChangePassword").GetBoolean());

        using var dashboard = await client.GetAsync("/api/dashboard/summary");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);

        using var forbiddenAdmin = await client.GetAsync("/api/admin/overview");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenAdmin.StatusCode);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
    });

    private static async Task<HttpResponseMessage> PostWithCsrfAsync(
        HttpClient client,
        string path,
        object body)
    {
        using var csrfResponse = await client.GetAsync("/api/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = csrf.GetProperty("token").GetString();

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-STU-CSRF", token);
        return await client.SendAsync(request);
    }
}
