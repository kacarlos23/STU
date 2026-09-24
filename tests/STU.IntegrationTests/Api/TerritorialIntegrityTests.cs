using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using STU.Application.Security;
using STU.Domain.HealthUnits;
using STU.Domain.Properties;
using STU.Domain.Territories;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.IntegrationTests.Api;

[Collection(StuApiFixtureDefinition.Name)]
public sealed class TerritorialIntegrityTests(StuApiFactory factory)
{
    private static int sequence;
    private const string Password = "Integrity!Test123";

    [Fact]
    public async Task PreviewListsDisplacedPropertyAndDirectSavePreservesOriginalRecords()
    {
        var seed = await SeedAsync();
        using var client = await LoginAsync(seed.UserName);
        var body = Body(seed, Geometry(seed.X, 20, seed.X + .4, 20.9));
        using var preview = await SendAsync(client, HttpMethod.Post, "/api/territories/microregions/preview", body);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var json = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("valid").GetBoolean());
        Assert.Equal(seed.PropertyId, Assert.Single(json.GetProperty("affectedProperties").EnumerateArray()).GetProperty("id").GetGuid());
        using var save = await SendAsync(client, HttpMethod.Put, $"/api/territories/microregions/{seed.MicroregionId}", body);
        Assert.Equal(HttpStatusCode.Conflict, save.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
        var micro = await db.Microregions.AsNoTracking().SingleAsync(x => x.Id == seed.MicroregionId);
        var property = await db.Properties.AsNoTracking().SingleAsync(x => x.Id == seed.PropertyId);
        Assert.Equal(seed.Token, micro.ConcurrencyToken);
        Assert.True(micro.Boundary.Covers(property.Geometry));
        Assert.False(await db.MicroregionVersions.AnyAsync(x => x.MicroregionId == seed.MicroregionId));
    }

    [Fact]
    public async Task ArchivedBoundaryRemainsEditableButCannotRestoreWithDisplacedActiveProperty()
    {
        var seed = await SeedAsync(archivedMicroregion: true);
        using var client = await LoginAsync(seed.UserName);
        using var save = await SendAsync(client, HttpMethod.Put, $"/api/territories/microregions/{seed.MicroregionId}", Body(seed, Geometry(seed.X, 20, seed.X + .4, 20.9)));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        using var restore = await SendAsync(client, HttpMethod.Post, $"/api/territories/microregions/{seed.MicroregionId}/restore");
        Assert.Equal(HttpStatusCode.Conflict, restore.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
        Assert.NotNull((await db.Microregions.FindAsync(seed.MicroregionId))!.ArchivedAtUtc);
    }

    [Fact]
    public async Task StalePreviewIsRejectedAndValidBoundaryWithPropertyOnEdgeCanBeSaved()
    {
        var seed = await SeedAsync();
        using var client = await LoginAsync(seed.UserName);
        using var stale = await SendAsync(client, HttpMethod.Post, "/api/territories/microregions/preview", Body(seed with { Token = Guid.NewGuid() }, Geometry(seed.X, 20, seed.X + .9, 20.9)));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var valid = await SendAsync(client, HttpMethod.Put, $"/api/territories/microregions/{seed.MicroregionId}", Body(seed, Geometry(seed.X, 20, seed.X + .8, 20.9)));
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    [Fact]
    public async Task GlobalAdministratorCannotMoveMicroregionOrPropertyAcrossUnitsWithoutTransferringDependencies()
    {
        var seed = await SeedAsync(global: true);
        using var client = await LoginAsync(seed.UserName, "admin");
        using var microSave = await SendAsync(client, HttpMethod.Put, $"/api/territories/microregions/{seed.MicroregionId}", Body(seed, Geometry(seed.X, 20, seed.X + .9, 20.9), seed.OtherUnitId));
        Assert.Equal(HttpStatusCode.Conflict, microSave.StatusCode);
        using var propertySave = await SendAsync(client, HttpMethod.Put, $"/api/properties/{seed.PropertyId}", new
        {
            microregionId = seed.OtherMicroregionId, street = "Rua de teste", houseNumber = "10", familyNumber = "F10",
            geometry = new { type = "Point", coordinates = new[] { seed.X + 1.4, 20.5 } },
            registrationStatus = "Active", situation = "Occupied", tagIds = Array.Empty<Guid>(), expectedVersion = seed.PropertyToken,
        });
        Assert.Equal(HttpStatusCode.Conflict, propertySave.StatusCode);
    }

    [Fact]
    public async Task ArchivedPropertyCannotRestoreOutsideItsCurrentBoundary()
    {
        var seed = await SeedAsync(archivedProperty: true);
        using var client = await LoginAsync(seed.UserName);
        using var save = await SendAsync(client, HttpMethod.Put, $"/api/territories/microregions/{seed.MicroregionId}", Body(seed, Geometry(seed.X, 20, seed.X + .4, 20.9)));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        using var restore = await SendAsync(client, HttpMethod.Post, $"/api/properties/{seed.PropertyId}/restore");
        Assert.Equal(HttpStatusCode.Conflict, restore.StatusCode);
    }

    [Fact]
    public async Task OnboardingSnapshotChangesWhenPropertyChangesWithoutChangingCounts()
    {
        var seed = await SeedAsync();
        using var client = await LoginAsync(seed.UserName);
        var before = await client.GetFromJsonAsync<JsonElement>("/api/onboarding/readiness");
        var repeat = await client.GetFromJsonAsync<JsonElement>("/api/onboarding/readiness");
        Assert.Equal(before.GetProperty("snapshotHash").GetString(), repeat.GetProperty("snapshotHash").GetString());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
            var property = (await db.Properties.FindAsync(seed.PropertyId))!;
            property.Update(property.MicroregionId, property.Street, "11", null, null, property.Geometry, property.RegistrationStatus, property.Situation);
            await db.SaveChangesAsync();
        }
        var after = await client.GetFromJsonAsync<JsonElement>("/api/onboarding/readiness");
        Assert.Equal(before.GetProperty("counts").GetRawText(), after.GetProperty("counts").GetRawText());
        Assert.NotEqual(before.GetProperty("snapshotHash").GetString(), after.GetProperty("snapshotHash").GetString());
    }

    [Fact]
    public async Task PropertyWithoutFamilyIsVisibleButExcludedFromCoverageInListMapAndDetail()
    {
        var seed = await SeedAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
            db.CoverageRules.Add(CoverageRule.Create(seed.UnitId, seed.MicroregionId, 30));
            await db.SaveChangesAsync();
        }
        using var client = await LoginAsync(seed.UserName);
        var list = await client.GetFromJsonAsync<JsonElement>("/api/properties?page=1&pageSize=20&includeArchived=false");
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal("noFamily", item.GetProperty("coverageStatus").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("lastVisitAtUtc").ValueKind);
        var map = await client.GetFromJsonAsync<JsonElement>("/api/properties/map");
        Assert.Equal("noFamily", Assert.Single(map.GetProperty("features").EnumerateArray()).GetProperty("properties").GetProperty("coverageStatus").GetString());
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/properties/{seed.PropertyId}");
        Assert.Equal("noFamily", detail.GetProperty("coverageStatus").GetString());
    }

    [Fact]
    public async Task NeighborhoodCannotShrinkPastActiveMicroregions()
    {
        var seed = await SeedAsync(global: true);
        Guid neighborhoodId;
        Guid token;
        string name;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
            neighborhoodId = await db.MicroregionNeighborhoods.Where(x => x.MicroregionId == seed.MicroregionId).Select(x => x.NeighborhoodId).SingleAsync();
            var neighborhood = (await db.Neighborhoods.FindAsync(neighborhoodId))!;
            token = neighborhood.ConcurrencyToken;
            name = neighborhood.Name;
        }
        using var client = await LoginAsync(seed.UserName, "admin");
        using var blocked = await SendAsync(client, HttpMethod.Put, $"/api/territories/neighborhoods/{neighborhoodId}", new { name, geometry = Geometry(seed.X, 20, seed.X + .4, 21), source = "Manual", expectedVersion = token });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        using var expanded = await SendAsync(client, HttpMethod.Put, $"/api/territories/neighborhoods/{neighborhoodId}", new { name, geometry = Geometry(seed.X, 20, seed.X + 2.2, 21), source = "Manual", expectedVersion = token });
        Assert.Equal(HttpStatusCode.OK, expanded.StatusCode);
    }

    [Fact]
    public async Task TerritoryOnlyRoleGetsImpactCountsWithoutPropertyDetails()
    {
        var seed = await SeedAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var user = (await users.FindByNameAsync(seed.UserName))!;
            var role = ApplicationRole.CreateCustom($"territory-only-{seed.MicroregionId}", "Somente território", null);
            Assert.True((await roles.CreateAsync(role)).Succeeded);
            Assert.True((await roles.AddClaimAsync(role, new System.Security.Claims.Claim(StuClaimTypes.Permission, StuPermissions.TerritoryManage))).Succeeded);
            Assert.True((await users.RemoveFromRoleAsync(user, SystemRoles.HealthUnitManager)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role.Name!)).Succeeded);
        }
        using var client = await LoginAsync(seed.UserName);
        using var response = await SendAsync(client, HttpMethod.Post, "/api/territories/microregions/preview", Body(seed, Geometry(seed.X, 20, seed.X + .4, 20.9)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, preview.GetProperty("affectedPropertyCount").GetInt32());
        Assert.False(preview.GetProperty("propertyDetailsVisible").GetBoolean());
        Assert.Empty(preview.GetProperty("affectedProperties").EnumerateArray());
    }

    [Fact]
    public async Task ArchivedAreaWithAnInvalidAgentCannotRestoreAndOnboardingFlagsInvalidAssignment()
    {
        var seed = await SeedAsync(archivedMicroregion: true);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
            var user = await db.Users.SingleAsync(x => x.UserName == seed.UserName);
            var micro = (await db.Microregions.FindAsync(seed.MicroregionId))!;
            micro.Update(micro.Code, micro.Name, micro.HealthUnitId, user.Id, micro.Boundary, micro.Source);
            await db.SaveChangesAsync();
        }
        using var client = await LoginAsync(seed.UserName);
        using var restore = await SendAsync(client, HttpMethod.Post, $"/api/territories/microregions/{seed.MicroregionId}/restore");
        Assert.Equal(HttpStatusCode.Conflict, restore.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
            (await db.Microregions.FindAsync(seed.MicroregionId))!.Restore();
            await db.SaveChangesAsync();
        }
        var report = await client.GetFromJsonAsync<JsonElement>("/api/onboarding/readiness");
        Assert.Contains(report.GetProperty("checks").EnumerateArray(), x => x.GetProperty("id").GetString() == "agent-assignments" && x.GetProperty("status").GetString() == "blocked");
    }

    [Fact]
    public async Task ThreeUnrelatedNeighborhoodsDoNotSatisfyTheApprovedPilotTerritory()
    {
        var seed = await SeedAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
            for (var index = 0; index < 2; index++)
            {
                var neighborhood = Neighborhood.Create($"Fora do piloto {Guid.NewGuid():N}", new Point(seed.X + .2, 20.2) { SRID = 4326 }, TerritorySource.Manual);
                db.Neighborhoods.Add(neighborhood);
                db.MicroregionNeighborhoods.Add(MicroregionNeighborhood.Create(seed.MicroregionId, neighborhood.Id));
            }
            await db.SaveChangesAsync();
        }
        using var client = await LoginAsync(seed.UserName);
        var report = await client.GetFromJsonAsync<JsonElement>("/api/onboarding/readiness");
        Assert.Equal(3, report.GetProperty("counts").GetProperty("neighborhoods").GetInt32());
        Assert.Contains(report.GetProperty("checks").EnumerateArray(), x => x.GetProperty("id").GetString() == "pilot-neighborhoods" && x.GetProperty("status").GetString() == "blocked");
    }

    private async Task<Seed> SeedAsync(bool global = false, bool archivedMicroregion = false, bool archivedProperty = false)
    {
        var x = 30d + Interlocked.Increment(ref sequence) * 3;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var unit = HealthUnit.Create($"INT-{suffix}", "UBS Integridade");
        var other = HealthUnit.Create($"OTH-{suffix}", "UBS Outra");
        db.HealthUnits.AddRange(unit, other);
        var neighborhood = Neighborhood.Create($"Integridade {suffix}", Polygon(x, 20, x + 2, 21), TerritorySource.Manual);
        var micro = Microregion.Create($"INT-{suffix}", $"Área {suffix}", unit.Id, null, new MultiPolygon([Polygon(x, 20, x + .9, 20.9)]) { SRID = 4326 }, TerritorySource.Manual);
        var otherMicro = Microregion.Create($"OTH-{suffix}", $"Outra {suffix}", other.Id, null, new MultiPolygon([Polygon(x + 1, 20, x + 1.9, 20.9)]) { SRID = 4326 }, TerritorySource.Manual);
        if (archivedMicroregion) micro.Archive();
        var property = HealthProperty.Create(unit.Id, micro.Id, "Rua de teste", "10", null, null, new Point(x + .8, 20.5) { SRID = 4326 }, PropertyRegistrationStatus.Active, PropertySituation.Occupied);
        if (archivedProperty) property.Archive();
        db.Neighborhoods.Add(neighborhood);
        db.Microregions.AddRange(micro, otherMicro);
        db.MicroregionNeighborhoods.Add(MicroregionNeighborhood.Create(micro.Id, neighborhood.Id));
        db.MicroregionNeighborhoods.Add(MicroregionNeighborhood.Create(otherMicro.Id, neighborhood.Id));
        db.Properties.Add(property);
        await db.SaveChangesAsync();
        var user = ApplicationUser.Create($"integrity.{suffix}", "Responsável de teste", global ? null : unit.Id, false);
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, global ? SystemRoles.GlobalAdministrator : SystemRoles.HealthUnitManager)).Succeeded);
        return new(user.UserName!, unit.Id, other.Id, micro.Id, otherMicro.Id, micro.Code, micro.Name, micro.ConcurrencyToken, property.Id, property.ConcurrencyToken, x);
    }

    private async Task<HttpClient> LoginAsync(string userName, string portal = "main")
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        using var login = await SendAsync(client, HttpMethod.Post, "/api/auth/login", new { userName, password = Password, portal });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }

    private static object Body(Seed seed, object geometry, Guid? unitId = null) => new { seed.Code, seed.Name, microregionId = seed.MicroregionId, healthUnitId = unitId ?? seed.UnitId, assignedAgentId = (Guid?)null, geometry, source = "Manual", color = "#123456", expectedVersion = seed.Token };
    private static Polygon Polygon(double x, double y, double xx, double yy) => new(new LinearRing([new(x, y), new(xx, y), new(xx, yy), new(x, yy), new(x, y)])) { SRID = 4326 };
    private static object Geometry(double x, double y, double xx, double yy) => new { type = "Polygon", coordinates = new[] { new[] { new[] { x, y }, new[] { xx, y }, new[] { xx, yy }, new[] { x, yy }, new[] { x, y } } } };
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-STU-CSRF", csrf.GetProperty("token").GetString());
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    private sealed record Seed(string UserName, Guid UnitId, Guid OtherUnitId, Guid MicroregionId, Guid OtherMicroregionId, string Code, string Name, Guid Token, Guid PropertyId, Guid PropertyToken, double X);
}
