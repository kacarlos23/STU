using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using STU.Application.Security;
using STU.Domain.HealthUnits;
using STU.Domain.Territories;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.IntegrationTests.Api;

[Collection(StuApiFixtureDefinition.Name)]
public sealed class PropertyEndpointTests(StuApiFactory factory)
{
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);

    [Fact]
    public async Task ManagerCanReassignIdentifiersAndRecordStructuredVisitWithoutCrossUnitExposure()
    {
        var setup = await CreateSetupAsync(includeAgent: false);
        using var client = CreateClient();
        await LoginAsync(client, setup.ManagerName, setup.Password);
        var propertyBody = PropertyBody(setup.MicroregionId, setup.UnitId, "10", "F-100");

        using var createdResponse = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/properties", propertyBody);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<JsonElement>();
        var propertyId = created.GetProperty("id").GetGuid();
        var token = created.GetProperty("concurrencyToken").GetGuid();

        using var duplicate = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/properties", PropertyBody(setup.MicroregionId, setup.UnitId, "11", "F-100"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var updatedBody = PropertyBody(setup.MicroregionId, setup.UnitId, "12", "F-101", token);
        using var updated = await SendWithCsrfAsync(client, HttpMethod.Put, $"/api/properties/{propertyId}", updatedBody);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var versionsResponse = await client.GetAsync($"/api/properties/{propertyId}/versions");
        var versions = await versionsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, versions.GetArrayLength());
        Assert.Equal("ReassignIdentifiers", versions[0].GetProperty("changeKind").GetString());
        Assert.Equal("F-100", versions[1].GetProperty("familyNumber").GetString());

        var invalidVisit = VisitBody("Retorno pelo CPF 123.456.789-00");
        using var piiResponse = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/properties/{propertyId}/visits", invalidVisit);
        Assert.Equal(HttpStatusCode.BadRequest, piiResponse.StatusCode);
        using var visitResponse = await SendWithCsrfAsync(client, HttpMethod.Post, $"/api/properties/{propertyId}/visits", VisitBody("Portão lateral fechado."));
        Assert.Equal(HttpStatusCode.Created, visitResponse.StatusCode);

        using var otherClient = CreateClient();
        await LoginAsync(otherClient, setup.OtherManagerName, setup.Password);
        using var forbidden = await otherClient.GetAsync($"/api/properties/{propertyId}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task AgentCanOnlyCreatePropertyInsideAssignedMicroregion()
    {
        var setup = await CreateSetupAsync(includeAgent: true);
        using var client = CreateClient();
        await LoginAsync(client, setup.AgentName!, setup.Password);

        using var allowed = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/properties", PropertyBody(setup.MicroregionId, setup.UnitId, "20", "F-200"));
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        using var forbidden = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/properties", PropertyBody(setup.OtherMicroregionId, setup.UnitId, "21", "F-201"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var referenceResponse = await client.GetAsync($"/api/properties/reference-data?healthUnitId={setup.UnitId}");
        Assert.Equal(HttpStatusCode.OK, referenceResponse.StatusCode);
        var reference = await referenceResponse.Content.ReadFromJsonAsync<JsonElement>();
        var microregions = reference.GetProperty("microregions");
        Assert.Single(microregions.EnumerateArray());
        Assert.Equal(setup.MicroregionId, microregions[0].GetProperty("id").GetGuid());
    }

    private async Task<TestSetup> CreateSetupAsync(bool includeAgent)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "Property!Test123";
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var first = HealthUnit.Create($"PA-{suffix}", "UBS Imóveis A");
        var second = HealthUnit.Create($"PB-{suffix}", "UBS Imóveis B");
        db.HealthUnits.AddRange(first, second);
        var neighborhood = Neighborhood.Create("Bairro Operacional", Boundary(), TerritorySource.Manual);
        db.Neighborhoods.Add(neighborhood);
        await db.SaveChangesAsync();

        var managerName = $"imoveis.a.{suffix}";
        var otherManagerName = $"imoveis.b.{suffix}";
        await CreateUserAsync(users, managerName, password, first.Id, SystemRoles.HealthUnitManager);
        await CreateUserAsync(users, otherManagerName, password, second.Id, SystemRoles.HealthUnitManager);
        ApplicationUser? agent = null;
        var agentName = includeAgent ? $"agente.{suffix}" : null;
        if (agentName is not null) agent = await CreateUserAsync(users, agentName, password, first.Id, SystemRoles.HealthAgent);

        var assigned = Microregion.Create($"MR-A-{suffix}", "Área atribuída", first.Id, agent?.Id, Boundary(), TerritorySource.Manual);
        var other = Microregion.Create($"MR-B-{suffix}", "Outra área", first.Id, null, Boundary(-45), TerritorySource.Manual);
        db.Microregions.AddRange(assigned, other);
        db.MicroregionNeighborhoods.AddRange(
            MicroregionNeighborhood.Create(assigned.Id, neighborhood.Id),
            MicroregionNeighborhood.Create(other.Id, neighborhood.Id));
        await db.SaveChangesAsync();
        return new(first.Id, assigned.Id, other.Id, managerName, otherManagerName, agentName, password);
    }

    private static MultiPolygon Boundary(double longitude = -47)
    {
        var polygon = GeometryFactory.CreatePolygon([
            new(longitude, -24), new(longitude + 1, -24), new(longitude + 1, -23), new(longitude, -23), new(longitude, -24),
        ]);
        return GeometryFactory.CreateMultiPolygon([polygon]);
    }

    private static object PropertyBody(Guid microregionId, Guid healthUnitId, string house, string family, Guid? expectedVersion = null) => new
    {
        microregionId,
        healthUnitId,
        street = "Rua das Flores",
        houseNumber = house,
        familyNumber = family,
        postalCode = "12345-000",
        complement = (string?)null,
        geometry = new { type = "Point", coordinates = new[] { -46.5, -23.5 } },
        registrationStatus = "Active",
        situation = "Occupied",
        tagIds = Array.Empty<Guid>(),
        expectedVersion,
    };

    private static object VisitBody(string? note) => new
    {
        visitedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
        type = "Routine",
        outcome = "Completed",
        observedSituation = "Occupied",
        accessDifficulty = false,
        note,
        expectedVersion = (Guid?)null,
    };

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private static async Task LoginAsync(HttpClient client, string userName, string password)
    {
        using var response = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/auth/login", new { userName, password, portal = "main" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<ApplicationUser> CreateUserAsync(UserManager<ApplicationUser> users, string userName, string password, Guid unitId, string role)
    {
        var user = ApplicationUser.Create(userName, "Usuário operacional", unitId, mustChangePassword: false);
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user;
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

    private sealed record TestSetup(Guid UnitId, Guid MicroregionId, Guid OtherMicroregionId, string ManagerName, string OtherManagerName, string? AgentName, string Password);
}
