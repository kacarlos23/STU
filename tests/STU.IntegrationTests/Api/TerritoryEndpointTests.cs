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
public sealed class TerritoryEndpointTests(StuApiFactory factory)
{
    [Fact]
    public async Task ManagerCanVersionTerritoryWithoutExposingItToAnotherHealthUnit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var password = "Territory!Test123";
        Guid firstUnitId;
        Guid secondUnitId;
        var firstUserName = $"territorio.a.{suffix}";
        var secondUserName = $"territorio.b.{suffix}";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var first = HealthUnit.Create($"TA-{suffix}", "UBS Territorial A");
            var second = HealthUnit.Create($"TB-{suffix}", "UBS Territorial B");
            db.HealthUnits.AddRange(first, second); await db.SaveChangesAsync();
            firstUnitId = first.Id; secondUnitId = second.Id;
            await CreateManagerAsync(users, firstUserName, password, firstUnitId);
            await CreateManagerAsync(users, secondUserName, password, secondUnitId);
        }

        using var firstClient = CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await SendWithCsrfAsync(firstClient, HttpMethod.Post, "/api/auth/login", new { userName = firstUserName, password, portal = "main" })).StatusCode);

        var neighborhoodGeometry = new { type = "Polygon", coordinates = new[] { new[] { new[] { -47.0, -24.0 }, new[] { -46.0, -24.0 }, new[] { -46.0, -23.0 }, new[] { -47.0, -23.0 }, new[] { -47.0, -24.0 } } } };
        using var neighborhoodResponse = await SendWithCsrfAsync(firstClient, HttpMethod.Post, "/api/territories/neighborhoods", new { name = $"Bairro {suffix}", geometry = neighborhoodGeometry, source = "GeoJsonImport", externalReference = "OSM/test" });
        Assert.Equal(HttpStatusCode.Created, neighborhoodResponse.StatusCode);
        var neighborhood = await neighborhoodResponse.Content.ReadFromJsonAsync<JsonElement>();
        var neighborhoodId = neighborhood.GetProperty("id").GetGuid();

        var boundary = new { type = "Polygon", coordinates = new[] { new[] { new[] { -46.9, -23.9 }, new[] { -46.5, -23.9 }, new[] { -46.5, -23.5 }, new[] { -46.9, -23.5 }, new[] { -46.9, -23.9 } } } };
        var body = new { code = $"MR-{suffix}", name = "Microrregião Norte", healthUnitId = firstUnitId, assignedAgentId = (Guid?)null, geometry = boundary, source = "Manual", color = "#4F9A7D" };
        using var preview = await SendWithCsrfAsync(firstClient, HttpMethod.Post, "/api/territories/microregions/preview", body);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var previewJson = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(neighborhoodId, previewJson.GetProperty("after").GetProperty("neighborhoodIds").EnumerateArray().Select(item => item.GetGuid()));
        using var create = await SendWithCsrfAsync(firstClient, HttpMethod.Post, "/api/territories/microregions", body);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var microregionId = created.GetProperty("id").GetGuid();
        var token = created.GetProperty("concurrencyToken").GetGuid();

        using var ownMap = await firstClient.GetAsync($"/api/territories/map?healthUnitId={firstUnitId}");
        ownMap.EnsureSuccessStatusCode();
        var ownMapJson = await ownMap.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(ownMapJson.GetProperty("features").EnumerateArray(), feature => feature.GetProperty("id").GetGuid() == microregionId);

        using var secondClient = CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await SendWithCsrfAsync(secondClient, HttpMethod.Post, "/api/auth/login", new { userName = secondUserName, password, portal = "main" })).StatusCode);
        using var forbiddenScope = await secondClient.GetAsync($"/api/territories/map?healthUnitId={firstUnitId}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenScope.StatusCode);
        using var secondMap = await secondClient.GetAsync($"/api/territories/map?healthUnitId={secondUnitId}");
        var secondMapJson = await secondMap.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(secondMapJson.GetProperty("features").EnumerateArray(), feature =>
            feature.GetProperty("properties").GetProperty("entityType").GetString() == "microregion");

        var updated = new { code = $"MR-{suffix}", name = "Microrregião Norte revisada", healthUnitId = firstUnitId, assignedAgentId = (Guid?)null, geometry = boundary, source = "Manual", color = "#4f9a7d", expectedVersion = token };
        using var update = await SendWithCsrfAsync(firstClient, HttpMethod.Put, $"/api/territories/microregions/{microregionId}", updated);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var versions = await firstClient.GetAsync($"/api/territories/microregions/{microregionId}/versions");
        versions.EnsureSuccessStatusCode();
        var versionsJson = await versions.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, versionsJson.GetArrayLength());

        using var archive = await SendWithCsrfAsync(firstClient, HttpMethod.Post, $"/api/territories/microregions/{microregionId}/archive", null);
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);

        using var activeMapAfterArchive = await firstClient.GetAsync($"/api/territories/map?healthUnitId={firstUnitId}");
        var activeMapAfterArchiveJson = await activeMapAfterArchive.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(activeMapAfterArchiveJson.GetProperty("features").EnumerateArray(), feature => feature.GetProperty("id").GetGuid() == microregionId);

        using var archivedResponse = await firstClient.GetAsync($"/api/territories/microregions/archived?healthUnitId={firstUnitId}");
        archivedResponse.EnsureSuccessStatusCode();
        var archivedJson = await archivedResponse.Content.ReadFromJsonAsync<JsonElement>();
        var archivedFeature = Assert.Single(archivedJson.GetProperty("features").EnumerateArray());
        Assert.Equal(microregionId, archivedFeature.GetProperty("id").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, archivedFeature.GetProperty("properties").GetProperty("archivedAtUtc").ValueKind);

        using var forbiddenArchivedScope = await secondClient.GetAsync($"/api/territories/microregions/archived?healthUnitId={firstUnitId}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenArchivedScope.StatusCode);

        var archivedToken = archivedFeature.GetProperty("properties").GetProperty("concurrencyToken").GetGuid();
        var archivedUpdateBody = new { code = $"MR-{suffix}", name = "Microrregião arquivada revisada", healthUnitId = firstUnitId, assignedAgentId = (Guid?)null, geometry = boundary, source = "Manual", color = "#64748b", expectedVersion = archivedToken };
        using var archivedUpdate = await SendWithCsrfAsync(firstClient, HttpMethod.Put, $"/api/territories/microregions/{microregionId}", archivedUpdateBody);
        Assert.Equal(HttpStatusCode.OK, archivedUpdate.StatusCode);

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<StuDbContext>();
        var archivedEntity = await verificationDb.Microregions.AsNoTracking().SingleAsync(item => item.Id == microregionId);
        Assert.NotNull(archivedEntity.ArchivedAtUtc);
        Assert.Equal("Microrregião arquivada revisada", archivedEntity.Name);
        Assert.Equal("#64748b", archivedEntity.Color);
        var archivedVersions = await verificationDb.MicroregionVersions.AsNoTracking().Where(item => item.MicroregionId == microregionId).OrderByDescending(item => item.VersionNumber).ToListAsync();
        Assert.Equal(4, archivedVersions.Count);
        Assert.True(archivedVersions[0].IsArchived);

        var replacementBody = new { code = $"MR-{suffix}", name = "Microrregião arquivada revisada", healthUnitId = firstUnitId, assignedAgentId = (Guid?)null, geometry = boundary, source = "Manual", color = "#2563eb" };
        using var replacementResponse = await SendWithCsrfAsync(firstClient, HttpMethod.Post, "/api/territories/microregions", replacementBody);
        Assert.Equal(HttpStatusCode.Created, replacementResponse.StatusCode);
        var replacementId = (await replacementResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var duplicateName = await SendWithCsrfAsync(firstClient, HttpMethod.Post, "/api/territories/microregions", new
        {
            code = $"OTHER-{suffix}", name = "MICRORREGIÃO ARQUIVADA REVISADA", healthUnitId = firstUnitId,
            assignedAgentId = (Guid?)null, geometry = PolygonGeometry(-46.4, -23.9, -46.1, -23.5), source = "Manual", color = "#15803d",
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicateName.StatusCode);

        using var blockedRestore = await SendWithCsrfAsync(firstClient, HttpMethod.Post, $"/api/territories/microregions/{microregionId}/restore", null);
        Assert.Equal(HttpStatusCode.Conflict, blockedRestore.StatusCode);

        using var archiveReplacement = await SendWithCsrfAsync(firstClient, HttpMethod.Post, $"/api/territories/microregions/{replacementId}/archive", null);
        Assert.Equal(HttpStatusCode.NoContent, archiveReplacement.StatusCode);

        using var spatialBlockerResponse = await SendWithCsrfAsync(firstClient, HttpMethod.Post, "/api/territories/microregions", new
        {
            code = $"BLOCK-{suffix}", name = "Bloqueio espacial", healthUnitId = firstUnitId,
            assignedAgentId = (Guid?)null, geometry = boundary, source = "Manual", color = "#dc2626",
        });
        Assert.Equal(HttpStatusCode.Created, spatialBlockerResponse.StatusCode);
        var spatialBlockerId = (await spatialBlockerResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var spatiallyBlockedRestore = await SendWithCsrfAsync(firstClient, HttpMethod.Post, $"/api/territories/microregions/{microregionId}/restore", null);
        Assert.Equal(HttpStatusCode.Conflict, spatiallyBlockedRestore.StatusCode);
        using var archiveSpatialBlocker = await SendWithCsrfAsync(firstClient, HttpMethod.Post, $"/api/territories/microregions/{spatialBlockerId}/archive", null);
        Assert.Equal(HttpStatusCode.NoContent, archiveSpatialBlocker.StatusCode);

        using var restoreOriginal = await SendWithCsrfAsync(firstClient, HttpMethod.Post, $"/api/territories/microregions/{microregionId}/restore", null);
        Assert.Equal(HttpStatusCode.NoContent, restoreOriginal.StatusCode);

        using var restoredMap = await firstClient.GetAsync($"/api/territories/map?healthUnitId={firstUnitId}");
        var restoredMapJson = await restoredMap.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(restoredMapJson.GetProperty("features").EnumerateArray(), feature => feature.GetProperty("id").GetGuid() == microregionId);
        var finalVersions = await verificationDb.MicroregionVersions.AsNoTracking().Where(item => item.MicroregionId == microregionId).ToListAsync();
        Assert.Equal(5, finalVersions.Count);
        Assert.Contains(finalVersions, version => version.ChangeKind == "Restore" && !version.IsArchived);
    }

    [Fact]
    public async Task BoundariesAutoFitAndMicroregionCanBelongToMultipleNeighborhoods()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var password = "Territory!Fit123";
        var userName = $"territorio.fit.{suffix}";
        var seed = Guid.NewGuid().ToByteArray();
        var minX = -130d + seed[0] / 20d;
        var minY = -55d + seed[1] / 40d;
        Guid unitId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var unit = HealthUnit.Create($"TF-{suffix}", "UBS Encaixe Territorial");
            db.HealthUnits.Add(unit); await db.SaveChangesAsync(); unitId = unit.Id;
            await CreateManagerAsync(users, userName, password, unitId);
        }

        using var client = CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await SendWithCsrfAsync(client, HttpMethod.Post, "/api/auth/login", new { userName, password, portal = "main" })).StatusCode);

        using var firstNeighborhoodResponse = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/territories/neighborhoods", new
        {
            name = $"Bairro A {suffix}", geometry = PolygonGeometry(minX, minY, minX + 1, minY + 1), source = "Manual", color = "#3366AA",
        });
        Assert.Equal(HttpStatusCode.Created, firstNeighborhoodResponse.StatusCode);
        var firstNeighborhoodId = (await firstNeighborhoodResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var secondNeighborhoodResponse = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/territories/neighborhoods", new
        {
            name = $"Bairro B {suffix}", geometry = PolygonGeometry(minX + .9, minY, minX + 2, minY + 1), source = "Manual", color = "#D97706",
        });
        Assert.Equal(HttpStatusCode.Created, secondNeighborhoodResponse.StatusCode);
        var secondNeighborhoodJson = await secondNeighborhoodResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(secondNeighborhoodJson.GetProperty("adjustedToExistingBoundaries").GetBoolean());
        var secondNeighborhoodId = secondNeighborhoodJson.GetProperty("id").GetGuid();

        var firstMicroregionBody = new
        {
            code = $"FIT-A-{suffix}", name = "Área existente", healthUnitId = unitId,
            assignedAgentId = (Guid?)null, geometry = PolygonGeometry(minX + .1, minY + .1, minX + .8, minY + .9), source = "Manual", color = "#7C3AED",
        };
        using var firstMicroregionResponse = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/territories/microregions", firstMicroregionBody);
        Assert.Equal(HttpStatusCode.Created, firstMicroregionResponse.StatusCode);
        var firstMicroregionId = (await firstMicroregionResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var spanningBody = new
        {
            code = $"FIT-B-{suffix}", name = "Área em dois bairros", healthUnitId = unitId,
            assignedAgentId = (Guid?)null, geometry = PolygonGeometry(minX + .7, minY + .1, minX + 1.8, minY + .9), source = "Manual", color = "#DB2777",
        };
        using var previewResponse = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/territories/microregions/preview", spanningBody);
        previewResponse.EnsureSuccessStatusCode();
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(preview.GetProperty("adjustedToExistingBoundaries").GetBoolean());

        using var spanningResponse = await SendWithCsrfAsync(client, HttpMethod.Post, "/api/territories/microregions", spanningBody);
        Assert.Equal(HttpStatusCode.Created, spanningResponse.StatusCode);
        var spanningId = (await spanningResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<StuDbContext>();
        var firstBoundary = (await verificationDb.Microregions.AsNoTracking().SingleAsync(item => item.Id == firstMicroregionId)).Boundary;
        var spanning = await verificationDb.Microregions.AsNoTracking().SingleAsync(item => item.Id == spanningId);
        var links = await verificationDb.MicroregionNeighborhoods.AsNoTracking().Where(item => item.MicroregionId == spanningId).ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.Equal(new[] { firstNeighborhoodId, secondNeighborhoodId }.Order(), links.Select(item => item.NeighborhoodId).Order());
        Assert.Equal("#db2777", spanning.Color);
        Assert.Equal(0, firstBoundary.Intersection(spanning.Boundary).Area, 8);
        Assert.Equal(minX + .8, spanning.Boundary.EnvelopeInternal.MinX, 8);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private static async Task CreateManagerAsync(UserManager<ApplicationUser> users, string userName, string password, Guid unitId)
    {
        var user = ApplicationUser.Create(userName, "Gerente territorial", unitId, mustChangePassword: false);
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, SystemRoles.HealthUnitManager)).Succeeded);
    }

    private static async Task<HttpResponseMessage> SendWithCsrfAsync(HttpClient client, HttpMethod method, string path, object? body)
    {
        using var csrfResponse = await client.GetAsync("/api/auth/csrf"); csrfResponse.EnsureSuccessStatusCode();
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var request = new HttpRequestMessage(method, path); request.Headers.Add("X-STU-CSRF", csrf.GetProperty("token").GetString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static object PolygonGeometry(double minX, double minY, double maxX, double maxY) => new
    {
        type = "Polygon",
        coordinates = new[] { new[] { new[] { minX, minY }, new[] { maxX, minY }, new[] { maxX, maxY }, new[] { minX, maxY }, new[] { minX, minY } } },
    };
}
