using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using STU.Application.Security;
using STU.Domain.Territories;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.IntegrationTests.Api;

[Collection(StuApiFixtureDefinition.Name)]
public sealed class RegistrationEndpointTests(StuApiFactory factory)
{
    [Fact]
    public async Task FullHierarchyCreatesConsultationAddressAndFamilyWithoutCoordinates()
    {
        var setup = await Setup(); using var client = await setup.Data.LoginAsync(factory);
        var region = await CreateRegion(client, setup, "NEW");
        var agent = await Agent(setup.Data);
        var link = await Link(client, setup.Data.UnitId, region, agent);
        var snapshot = await Snapshot(client, setup.Data.UnitId);
        Assert.Single(snapshot.GetProperty("assignments").EnumerateArray().Where(a => a.GetProperty("id").GetGuid() == link));
        var address = await CreateAddress(client, setup, link, "F001");
        var propertyId = address.GetProperty("id").GetGuid(); var familyId = address.GetProperty("familyId").GetGuid();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
        var stored = await db.Properties.SingleAsync(p => p.Id == propertyId);
        Assert.Null(stored.Geometry); Assert.Equal(link, stored.MicroregionAgentId); Assert.Equal(setup.NeighborhoodId, stored.NeighborhoodId);
        Assert.Null((await db.Microregions.SingleAsync(m => m.Id == region)).Boundary);
        Assert.Equal(familyId, (await db.FamilyPropertyLinks.SingleAsync(l => l.PropertyId == propertyId && l.EndedAtUtc == null)).FamilyId);
        var map = await client.GetFromJsonAsync<JsonElement>("/api/properties/map");
        Assert.DoesNotContain(map.GetProperty("features").EnumerateArray(), feature => feature.GetProperty("id").GetGuid() == propertyId);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/families/{familyId}");
        Assert.Equal(propertyId, detail.GetProperty("currentProperty").GetProperty("id").GetGuid());
        Assert.Equal("Responsável de teste", detail.GetProperty("responsibleName").GetString());
    }

    [Fact]
    public async Task AssignmentScopeAndPrivateFamilyApisDoNotExposeOtherAgentsInSameRegion()
    {
        var setup = await Setup(); using var manager = await setup.Data.LoginAsync(factory);
        var region = await CreateRegion(manager, setup, "SCOPED");
        var firstAgent = await Agent(setup.Data);
        Guid secondAgent;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = ApplicationUser.Create("second." + setup.Data.Suffix, "Segundo agente", setup.Data.UnitId, false);
            Assert.True((await users.CreateAsync(user, "Families!Test123")).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, SystemRoles.HealthAgent)).Succeeded); secondAgent = user.Id;
        }
        var firstLink = await Link(manager, setup.Data.UnitId, region, firstAgent);
        var secondLink = await Link(manager, setup.Data.UnitId, region, secondAgent);
        var own = await CreateAddress(manager, setup, firstLink, "OWN");
        var hidden = await CreateAddress(manager, setup, secondLink, "HIDDEN");
        using var agentClient = await setup.Data.LoginAsync(factory, "agent");
        var snapshot = await Snapshot(agentClient, setup.Data.UnitId);
        Assert.Single(snapshot.GetProperty("assignments").EnumerateArray());
        Assert.Single(snapshot.GetProperty("addresses").EnumerateArray());
        Assert.Equal(own.GetProperty("id").GetGuid(), snapshot.GetProperty("addresses")[0].GetProperty("id").GetGuid());
        var hiddenFamily = hidden.GetProperty("familyId").GetGuid(); var hiddenProperty = hidden.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await agentClient.GetAsync($"/api/families/{hiddenFamily}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agentClient.GetAsync($"/api/properties/{hiddenProperty}")).StatusCode);
        var familyList = await agentClient.GetFromJsonAsync<JsonElement>("/api/families");
        Assert.DoesNotContain(familyList.GetProperty("items").EnumerateArray(), f => f.GetProperty("id").GetGuid() == hiddenFamily);
        var propertyList = await agentClient.GetFromJsonAsync<JsonElement>("/api/properties");
        Assert.DoesNotContain(propertyList.GetProperty("items").EnumerateArray(), p => p.GetProperty("id").GetGuid() == hiddenProperty);
        using var rejected = await FamilyTestData.SendAsync(agentClient, HttpMethod.Post, $"/api/registration/regions/{region}/agents", new { agentId = secondAgent });
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
    }

    [Fact]
    public async Task UnlinkIsAtomicRequiresRecipientAndKeepsFamilyResidenceAndVisits()
    {
        var setup = await Setup(); using var client = await setup.Data.LoginAsync(factory);
        var region = await CreateRegion(client, setup, "TRANSFER"); var agent = await Agent(setup.Data);
        var from = await Link(client, setup.Data.UnitId, region, agent);
        var address = await CreateAddress(client, setup, from, "TRANSFER"); var familyId = address.GetProperty("familyId").GetGuid(); var propertyId = address.GetProperty("id").GetGuid();
        var family = await client.GetFromJsonAsync<JsonElement>($"/api/families/{familyId}");
        using var visit = await FamilyTestData.SendAsync(client, HttpMethod.Post, $"/api/families/{familyId}/visits", FamilyTestData.VisitBody(family.GetProperty("concurrencyToken").GetGuid()));
        Assert.Equal(HttpStatusCode.Created, visit.StatusCode);
        var before = await Snapshot(client, setup.Data.UnitId);
        using var blocked = await Unlink(client, before, region, from, null);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        var agentTwo = await NewAgent(client, setup.Data.UnitId, region, "recipient." + setup.Data.Suffix);
        var to = agentTwo.GetProperty("id").GetGuid();
        before = await Snapshot(client, setup.Data.UnitId);
        using var moved = await Unlink(client, before, region, from, to);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
        Assert.NotNull((await db.MicroregionAgents.SingleAsync(a => a.Id == from)).EndedAtUtc);
        Assert.Equal(to, (await db.Properties.SingleAsync(p => p.Id == propertyId)).MicroregionAgentId);
        Assert.Equal(propertyId, (await db.FamilyPropertyLinks.SingleAsync(l => l.FamilyId == familyId && l.EndedAtUtc == null)).PropertyId);
        Assert.Equal(propertyId, (await db.PropertyVisits.SingleAsync(v => v.FamilyId == familyId)).PropertyId);
        Assert.True(await db.PropertyVersions.AnyAsync(v => v.PropertyId == propertyId && v.ChangeKind == "TransferAgent"));
        Assert.True(await db.AuditEntries.AnyAsync(a => a.EntityId == from.ToString() && a.Action == "UnlinkAgent"));
        using var stale = await Unlink(client, before, region, from, to);
        Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        var relink = await Link(client, setup.Data.UnitId, region, agent);
        Assert.NotEqual(from, relink);
    }

    [Fact]
    public async Task RegionNeighborhoodUnitScopeAntiforgeryAndDuplicateLinkAreEnforced()
    {
        var setup = await Setup(); using var client = await setup.Data.LoginAsync(factory);
        var region = await CreateRegion(client, setup, "SECURITY");
        using var noCsrf = await client.PostAsJsonAsync("/api/registration/regions", new { code = "NO-CSRF", name = "Teste", neighborhoodId = setup.NeighborhoodId });
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var anonymous = FamilyTestData.Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/registration")).StatusCode);
        var other = await Setup(); using var foreign = await other.Data.LoginAsync(factory);
        Assert.Equal(HttpStatusCode.Forbidden, (await foreign.GetAsync($"/api/registration?healthUnitId={setup.Data.UnitId}")).StatusCode);
        using var crossLink = await FamilyTestData.SendAsync(foreign, HttpMethod.Post, $"/api/registration/regions/{region}/agents", new { agentId = await Agent(other.Data) });
        Assert.Equal(HttpStatusCode.NotFound, crossLink.StatusCode);
        var link = await Link(client, setup.Data.UnitId, region, await Agent(setup.Data));
        using var duplicate = await FamilyTestData.SendAsync(client, HttpMethod.Post, $"/api/registration/regions/{region}/agents", new { agentId = await Agent(setup.Data), expectedVersion = RegionToken(await Snapshot(client, setup.Data.UnitId), region) });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var invalidRegion = await FamilyTestData.SendAsync(client, HttpMethod.Post, "/api/registration/regions", new { code = "INVALID", name = "Teste", neighborhoodId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, invalidRegion.StatusCode);
        var snapshot = await Snapshot(client, setup.Data.UnitId);
        using var badAddress = await FamilyTestData.SendAsync(client, HttpMethod.Post, "/api/registration/addresses", Body(link, Guid.NewGuid(), AssignmentToken(snapshot, link), "INVALID"));
        Assert.Equal(HttpStatusCode.BadRequest, badAddress.StatusCode);
    }

    [Fact]
    public async Task FamilyConflictRollsBackAddressAndAssignmentChanges()
    {
        var setup = await Setup(); using var client = await setup.Data.LoginAsync(factory);
        var region = await CreateRegion(client, setup, "ATOMIC"); var link = await Link(client, setup.Data.UnitId, region, await Agent(setup.Data));
        await CreateAddress(client, setup, link, "DUPLICATE");
        var snapshot = await Snapshot(client, setup.Data.UnitId);
        using var duplicate = await FamilyTestData.SendAsync(client, HttpMethod.Post, "/api/registration/addresses", Body(link, setup.NeighborhoodId, AssignmentToken(snapshot, link), "DUPLICATE"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var after = await Snapshot(client, setup.Data.UnitId);
        Assert.Equal(snapshot.GetProperty("addresses").GetArrayLength(), after.GetProperty("addresses").GetArrayLength());
        Assert.Equal(AssignmentToken(snapshot, link), AssignmentToken(after, link));
    }

    [Fact]
    public async Task NewAgentUsesExistingIdentityProvisioningAndExposesTemporaryPasswordOnce()
    {
        var setup = await Setup(); using var client = await setup.Data.LoginAsync(factory);
        var region = await CreateRegion(client, setup, "IDENTITY");
        var result = await NewAgent(client, setup.Data.UnitId, region, "new." + setup.Data.Suffix);
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("temporaryPassword").GetString()));
        var snapshot = await Snapshot(client, setup.Data.UnitId);
        Assert.DoesNotContain("temporaryPassword", snapshot.GetRawText(), StringComparison.Ordinal);
        using var scope = factory.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByNameAsync("new." + setup.Data.Suffix);
        Assert.NotNull(user); Assert.True(user.MustChangePassword); Assert.Equal(setup.Data.UnitId, user.HealthUnitId);
        Assert.True(await users.IsInRoleAsync(user, SystemRoles.HealthAgent));
    }

    private async Task<(FamilyTestData Data, Guid NeighborhoodId)> Setup()
    {
        var data = await FamilyTestData.CreateAsync(factory);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
        var polygon = new GeometryFactory(new PrecisionModel(), 4326).CreatePolygon([new(-40,-18), new(-39,-18), new(-39,-17), new(-40,-17), new(-40,-18)]);
        var neighborhood = Neighborhood.Create("Bairro " + data.Suffix, polygon, TerritorySource.Manual); db.Neighborhoods.Add(neighborhood); await db.SaveChangesAsync();
        return (data, neighborhood.Id);
    }
    private async Task<Guid> Agent(FamilyTestData setup)
    {
        using var scope = factory.Services.CreateScope(); return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync("agent." + setup.Suffix))!.Id;
    }
    private static async Task<JsonElement> Snapshot(HttpClient client, Guid unit) => await client.GetFromJsonAsync<JsonElement>($"/api/registration?healthUnitId={unit}");
    private static Guid RegionToken(JsonElement snapshot, Guid id) => snapshot.GetProperty("regions").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id).GetProperty("concurrencyToken").GetGuid();
    private static Guid AssignmentToken(JsonElement snapshot, Guid id) => snapshot.GetProperty("assignments").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id).GetProperty("concurrencyToken").GetGuid();
    private static async Task<Guid> CreateRegion(HttpClient client, (FamilyTestData Data, Guid NeighborhoodId) setup, string code)
    {
        using var response = await FamilyTestData.SendAsync(client, HttpMethod.Post, "/api/registration/regions", new { code, name = "Microrregião " + code, neighborhoodId = setup.NeighborhoodId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private static async Task<Guid> Link(HttpClient client, Guid unit, Guid region, Guid agent)
    {
        using var response = await FamilyTestData.SendAsync(client, HttpMethod.Post, $"/api/registration/regions/{region}/agents", new { agentId = agent, expectedVersion = RegionToken(await Snapshot(client, unit), region) });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
    private static async Task<JsonElement> NewAgent(HttpClient client, Guid unit, Guid region, string userName)
    {
        using var response = await FamilyTestData.SendAsync(client, HttpMethod.Post, $"/api/registration/regions/{region}/agents", new { name = "Novo agente", userName, expectedVersion = RegionToken(await Snapshot(client, unit), region) });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task<JsonElement> CreateAddress(HttpClient client, (FamilyTestData Data, Guid NeighborhoodId) setup, Guid assignment, string number)
    {
        using var response = await FamilyTestData.SendAsync(client, HttpMethod.Post, "/api/registration/addresses", Body(assignment, setup.NeighborhoodId, AssignmentToken(await Snapshot(client, setup.Data.UnitId), assignment), number));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static object Body(Guid assignment, Guid neighborhood, Guid version, string number) => new { assignmentId = assignment, street = "Rua sem coordenadas", houseNumber = "10", noNumber = false, neighborhoodId = neighborhood, familyNumber = number, familyResponsibleName = "Responsável de teste", expectedAssignmentVersion = version };
    private static Task<HttpResponseMessage> Unlink(HttpClient client, JsonElement snapshot, Guid region, Guid assignment, Guid? recipient) => FamilyTestData.SendAsync(client, HttpMethod.Post, $"/api/registration/assignments/{assignment}/unlink", new { recipientId = recipient, expectedVersion = AssignmentToken(snapshot, assignment), expectedRegionVersion = RegionToken(snapshot, region), expectedRecipientVersion = recipient.HasValue ? AssignmentToken(snapshot, recipient.Value) : (Guid?)null });
}
