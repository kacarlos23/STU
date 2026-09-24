using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Npgsql;
using STU.Application.Security;
using STU.Domain.Families;
using STU.Domain.HealthUnits;
using STU.Domain.Properties;
using STU.Domain.Territories;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.IntegrationTests.Api;

[Collection(StuApiFixtureDefinition.Name)]
public sealed class FamilyEndpointTests(StuApiFactory factory)
{
    [Fact]
    public async Task FamilyWithoutPropertySupportsSearchPaginationVersionsAndReservedArchivedNumber()
    {
        var setup = await FamilyTestData.CreateAsync(factory);
        using var client = await setup.LoginAsync(factory);
        var family = await FamilyTestData.CreateFamilyAsync(client, " fam-01 ", " Ana-María D'Ávila ");
        var id = family.GetProperty("id").GetGuid();
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/families/{id}");
        Assert.Equal("FAM-01", detail.GetProperty("number").GetString());
        Assert.Equal("Ana-María D'Ávila", detail.GetProperty("responsibleName").GetString());
        Assert.Equal("unlinked", detail.GetProperty("state").GetString());
        Assert.Equal("noProperty", detail.GetProperty("coverageStatus").GetString());
        await FamilyTestData.CreateFamilyAsync(client, "FAM-02", "Outro responsável");
        var search = await client.GetFromJsonAsync<JsonElement>("/api/families?query=" + Uri.EscapeDataString("d'ávila"));
        Assert.Equal(1, search.GetProperty("total").GetInt32());
        var page = await client.GetFromJsonAsync<JsonElement>("/api/families?page=2&pageSize=1");
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        Assert.Equal("FAM-02", page.GetProperty("items")[0].GetProperty("number").GetString());
        using var stale = await FamilyTestData.SendAsync(client, HttpMethod.Put, $"/api/families/{id}", new { number = "FAM-01", responsibleName = "Outro", expectedVersion = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var archived = await FamilyTestData.SendAsync(client, HttpMethod.Post, $"/api/families/{id}/archive", new { expectedVersion = family.GetProperty("concurrencyToken").GetGuid() });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        using var duplicate = await FamilyTestData.SendAsync(client, HttpMethod.Post, "/api/families", new { number = "fam-01", responsibleName = "Outra família" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var archivedDetail = await client.GetFromJsonAsync<JsonElement>($"/api/families/{id}");
        using var restore = await FamilyTestData.SendAsync(client, HttpMethod.Post, $"/api/families/{id}/restore", new { expectedVersion = archivedDetail.GetProperty("concurrencyToken").GetGuid() });
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/families/{id}/history");
        Assert.Equal(3, history.GetProperty("versions").GetArrayLength());
        using var invalid = await FamilyTestData.SendAsync(client, HttpMethod.Post, "/api/families", new { number = "3", responsibleName = "" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var invalidNumber = await FamilyTestData.SendAsync(client, HttpMethod.Post, "/api/families", new { number = "F\t3", responsibleName = "Responsável" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidNumber.StatusCode);
        using var visit = await FamilyTestData.SendAsync(client, HttpMethod.Post, $"/api/families/{id}/visits", FamilyTestData.VisitBody(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, visit.StatusCode);
    }

    [Fact]
    public async Task MovingFamilyKeepsVisitsAtOriginalPropertyAndCoverageFollowsFamily()
    {
        var setup = await FamilyTestData.CreateAsync(factory);
        using var client = await setup.LoginAsync(factory);
        var family = await FamilyTestData.CreateFamilyAsync(client, "MOVE", "Responsável em mudança"); var id = family.GetProperty("id").GetGuid();
        await FamilyTestData.LinkAsync(client, id, setup.PropertyIds[0]);
        var before = await client.GetFromJsonAsync<JsonElement>($"/api/families/{id}");
        using var visit = await FamilyTestData.SendAsync(client, HttpMethod.Post, $"/api/families/{id}/visits", FamilyTestData.VisitBody(before.GetProperty("concurrencyToken").GetGuid()));
        Assert.Equal(HttpStatusCode.Created, visit.StatusCode);
        await FamilyTestData.LinkAsync(client, id, setup.PropertyIds[1]);
        var after = await client.GetFromJsonAsync<JsonElement>($"/api/families/{id}");
        Assert.Equal(setup.PropertyIds[1], after.GetProperty("currentProperty").GetProperty("id").GetGuid());
        Assert.Equal("covered", after.GetProperty("coverageStatus").GetString());
        var visits = await client.GetFromJsonAsync<JsonElement>($"/api/families/{id}/visits");
        Assert.Equal(setup.PropertyIds[0], visits[0].GetProperty("propertyId").GetGuid());
        var oldProperty = await client.GetFromJsonAsync<JsonElement>($"/api/properties/{setup.PropertyIds[0]}");
        Assert.Equal("noFamily", oldProperty.GetProperty("coverageStatus").GetString());
        Assert.Equal(JsonValueKind.Null, oldProperty.GetProperty("familyId").ValueKind);
        var dashboard = await client.GetFromJsonAsync<JsonElement>("/api/dashboard/summary");
        Assert.Equal(1, dashboard.GetProperty("activeFamilyIdentifiers").GetInt32());
        Assert.Equal(2, dashboard.GetProperty("propertiesWithoutFamily").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("coverage").GetProperty("covered").GetInt32());
        Assert.Equal(0, dashboard.GetProperty("coverageAlerts").GetInt32());
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/families/{id}/history");
        Assert.Equal(2, history.GetProperty("links").GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, history.GetProperty("links")[1].GetProperty("endedAtUtc").ValueKind);
        using var archive = await FamilyTestData.SendAsync(client, HttpMethod.Post, $"/api/families/{id}/archive", new { expectedVersion = after.GetProperty("concurrencyToken").GetGuid() });
        Assert.Equal(HttpStatusCode.Conflict, archive.StatusCode);
        using var propertyArchive = await FamilyTestData.SendAsync(client, HttpMethod.Post, $"/api/properties/{setup.PropertyIds[1]}/archive", null);
        Assert.Equal(HttpStatusCode.Conflict, propertyArchive.StatusCode);
        using var unlink = await FamilyTestData.SendAsync(client, HttpMethod.Post, $"/api/families/{id}/unlink", new { expectedVersion = after.GetProperty("concurrencyToken").GetGuid() });
        Assert.Equal(HttpStatusCode.OK, unlink.StatusCode);
        Assert.Single((await client.GetFromJsonAsync<JsonElement>($"/api/families/{id}/visits")).EnumerateArray());
    }

    [Fact]
    public async Task FailedMoveDoesNotEndCurrentLinkAndConcurrencyAllowsOnlyOneWinner()
    {
        var setup = await FamilyTestData.CreateAsync(factory);
        using var client = await setup.LoginAsync(factory);
        var first = await FamilyTestData.CreateFamilyAsync(client, "FIRST", "Primeira família"); var firstId = first.GetProperty("id").GetGuid();
        var second = await FamilyTestData.CreateFamilyAsync(client, "SECOND", "Segunda família"); var secondId = second.GetProperty("id").GetGuid();
        await FamilyTestData.LinkAsync(client, firstId, setup.PropertyIds[0]);
        await FamilyTestData.LinkAsync(client, secondId, setup.PropertyIds[1]);
        using var conflict = await FamilyTestData.LinkResponseAsync(client, firstId, setup.PropertyIds[1]);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var unchanged = await client.GetFromJsonAsync<JsonElement>($"/api/families/{firstId}");
        Assert.Equal(setup.PropertyIds[0], unchanged.GetProperty("currentProperty").GetProperty("id").GetGuid());
        using var peer = await setup.LoginAsync(factory);
        var destination = await client.GetFromJsonAsync<JsonElement>($"/api/properties/{setup.PropertyIds[2]}");
        var secondDetail = await client.GetFromJsonAsync<JsonElement>($"/api/families/{secondId}");
        var responses = await Task.WhenAll(
            FamilyTestData.SendAsync(client, HttpMethod.Post, $"/api/families/{firstId}/property", new { propertyId = setup.PropertyIds[2], expectedVersion = unchanged.GetProperty("concurrencyToken").GetGuid(), expectedPropertyVersion = destination.GetProperty("concurrencyToken").GetGuid() }),
            FamilyTestData.SendAsync(peer, HttpMethod.Post, $"/api/families/{secondId}/property", new { propertyId = setup.PropertyIds[2], expectedVersion = secondDetail.GetProperty("concurrencyToken").GetGuid(), expectedPropertyVersion = destination.GetProperty("concurrencyToken").GetGuid() }));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
        Assert.Equal(2, await db.FamilyPropertyLinks.CountAsync(l => l.HealthUnitId == setup.UnitId && l.EndedAtUtc == null));
        Assert.Equal(1, await db.FamilyPropertyLinks.CountAsync(l => l.PropertyId == setup.PropertyIds[2] && l.EndedAtUtc == null));
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task FamilyPermissionsScopeAntiforgeryAndPrivateMapAreEnforced()
    {
        var setup = await FamilyTestData.CreateAsync(factory);
        using var client = await setup.LoginAsync(factory);
        var family = await FamilyTestData.CreateFamilyAsync(client, "PRIVATE", "Nome privado sintético"); var id = family.GetProperty("id").GetGuid();
        await FamilyTestData.LinkAsync(client, id, setup.PropertyIds[0]);
        using var anonymous = FamilyTestData.Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/families/{id}")).StatusCode);
        using var noCsrf = await client.PostAsJsonAsync("/api/families", new { number = "CSRF", responsibleName = "Teste" });
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        var other = await FamilyTestData.CreateAsync(factory);
        using var otherClient = await other.LoginAsync(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/api/families/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.GetAsync($"/api/families?healthUnitId={setup.UnitId}")).StatusCode);
        using var reader = await setup.LoginAsync(factory, "properties-only");
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/families")).StatusCode);
        var property = await reader.GetFromJsonAsync<JsonElement>($"/api/properties/{setup.PropertyIds[0]}");
        Assert.Equal(JsonValueKind.Null, property.GetProperty("familyResponsibleName").ValueKind);
        var map = await reader.GetStringAsync("/api/properties/map");
        Assert.DoesNotContain("Nome privado", map);
        using var authorizedMap = await client.GetAsync("/api/properties/map");
        Assert.True(authorizedMap.Headers.CacheControl?.NoStore);
        Assert.Contains("Nome privado", await authorizedMap.Content.ReadAsStringAsync());
        using var familyReader = await setup.LoginAsync(factory, "family-reader");
        Assert.Equal(HttpStatusCode.OK, (await familyReader.GetAsync($"/api/families/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await FamilyTestData.SendAsync(familyReader, HttpMethod.Post, "/api/families", new { number = "NO", responsibleName = "Sem gestão" })).StatusCode);
        using var agent = await setup.LoginAsync(factory, "agent");
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync($"/api/families/{id}")).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
        Assert.False(await db.AuditEntries.AnyAsync(a => a.Summary.Contains("Nome privado")));
    }

    [Fact]
    public async Task DatabaseRejectsDuplicateLinksArchivedFamiliesCrossUnitAndHistoryRewrite()
    {
        var setup = await FamilyTestData.CreateAsync(factory);
        using var client = await setup.LoginAsync(factory);
        var family = await FamilyTestData.CreateFamilyAsync(client, "DB", "Família de teste"); var id = family.GetProperty("id").GetGuid();
        await FamilyTestData.LinkAsync(client, id, setup.PropertyIds[0]);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
        var link = await db.FamilyPropertyLinks.AsNoTracking().SingleAsync(l => l.FamilyId == id && l.EndedAtUtc == null);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE family_property_links SET \"PropertyId\"={setup.PropertyIds[1]} WHERE \"Id\"={link.Id}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM family_property_links WHERE \"Id\"={link.Id}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE families SET \"ArchivedAtUtc\"=now() WHERE \"Id\"={id}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE family_versions SET \"ResponsibleName\"='rewrite' WHERE \"FamilyId\"={id}"));
        db.FamilyPropertyLinks.Add(FamilyPropertyLink.Create(setup.UnitId, id, setup.PropertyIds[1], setup.ActorId));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        var other = await FamilyTestData.CreateAsync(factory);
        db.FamilyPropertyLinks.Add(FamilyPropertyLink.Create(other.UnitId, id, other.PropertyIds[0], other.ActorId));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        var archived = Family.Create(setup.UnitId, "ARCH", "Arquivada", setup.ActorId); archived.Archive(setup.ActorId); db.Families.Add(archived); await db.SaveChangesAsync();
        db.FamilyPropertyLinks.Add(FamilyPropertyLink.Create(setup.UnitId, archived.Id, setup.PropertyIds[2], setup.ActorId));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}

internal sealed record FamilyTestData(Guid UnitId, Guid MicroregionId, Guid ActorId, Guid[] PropertyIds, string Suffix)
{
    private const string Password = "Families!Test123";
    public static async Task<FamilyTestData> CreateAsync(StuApiFactory factory)
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var unit = HealthUnit.Create("F-" + suffix[..8], "UBS Famílias " + suffix[..8]); db.HealthUnits.Add(unit); await db.SaveChangesAsync();
        Guid actorId = default;
        foreach (var (kind, roleName, permissions) in new[] {
            ("manager", SystemRoles.HealthUnitManager, Array.Empty<string>()), ("agent", SystemRoles.HealthAgent, Array.Empty<string>()),
            ("properties-only", "properties-"+suffix, new[] { StuPermissions.PropertiesView }),
            ("family-reader", "families-"+suffix, new[] { StuPermissions.FamiliesView }) })
        {
            if (permissions.Length > 0) { var role = ApplicationRole.CreateCustom(roleName, roleName, null); Assert.True((await roles.CreateAsync(role)).Succeeded); foreach (var permission in permissions) Assert.True((await roles.AddClaimAsync(role, new Claim(StuClaimTypes.Permission, permission))).Succeeded); }
            var user = ApplicationUser.Create(kind + "." + suffix, "Usuário sintético", unit.Id, mustChangePassword: false);
            Assert.True((await users.CreateAsync(user, Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, roleName)).Succeeded);
            if (kind == "manager") actorId = user.Id;
        }
        var gf = new GeometryFactory(new PrecisionModel(), 4326);
        var boundary = gf.CreateMultiPolygon([gf.CreatePolygon([new(-40,-18),new(-39,-18),new(-39,-17),new(-40,-17),new(-40,-18)])]);
        var micro = Microregion.Create("M-"+suffix[..8], "Micro de famílias", unit.Id, null, boundary, TerritorySource.Manual);
        db.Microregions.Add(micro); db.CoverageRules.Add(CoverageRule.Create(unit.Id, micro.Id, 90));
        var properties = Enumerable.Range(1, 3).Select(i => HealthProperty.Create(unit.Id, micro.Id, "Rua sintética", i.ToString(System.Globalization.CultureInfo.InvariantCulture), null, null, gf.CreatePoint(new Coordinate(-39.5+i*.01,-17.5)), PropertyRegistrationStatus.Active, PropertySituation.Occupied)).ToArray();
        db.Properties.AddRange(properties); await db.SaveChangesAsync();
        return new(unit.Id, micro.Id, actorId, properties.Select(p => p.Id).ToArray(), suffix);
    }
    public async Task<HttpClient> LoginAsync(StuApiFactory factory, string kind = "manager")
    {
        var client = Client(factory); using var login = await SendAsync(client, HttpMethod.Post, "/api/auth/login", new { userName = kind + "." + Suffix, password = Password, portal = "main" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); return client;
    }
    public static HttpClient Client(StuApiFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf"); using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-STU-CSRF", csrf.GetProperty("token").GetString()); if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    public static async Task<JsonElement> CreateFamilyAsync(HttpClient client, string number, string responsibleName)
    {
        using var response = await SendAsync(client, HttpMethod.Post, "/api/families", new { number, responsibleName });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    public static async Task<HttpResponseMessage> LinkResponseAsync(HttpClient client, Guid familyId, Guid propertyId)
    {
        var family = await client.GetFromJsonAsync<JsonElement>($"/api/families/{familyId}"); var property = await client.GetFromJsonAsync<JsonElement>($"/api/properties/{propertyId}");
        return await SendAsync(client, HttpMethod.Post, $"/api/families/{familyId}/property", new { propertyId, expectedVersion = family.GetProperty("concurrencyToken").GetGuid(), expectedPropertyVersion = property.GetProperty("concurrencyToken").GetGuid() });
    }
    public static async Task LinkAsync(HttpClient client, Guid familyId, Guid propertyId) { using var response = await LinkResponseAsync(client, familyId, propertyId); Assert.Equal(HttpStatusCode.OK, response.StatusCode); }
    public static object VisitBody(Guid token) => new { visitedAtUtc = DateTimeOffset.UtcNow, type = "Routine", outcome = "Completed", observedSituation = "Occupied", accessDifficulty = false, note = "Visita sintética", expectedFamilyVersion = token };
}
