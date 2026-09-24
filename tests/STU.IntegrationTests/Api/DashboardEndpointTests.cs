using STU.Domain.Families;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
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
public sealed class DashboardEndpointTests(StuApiFactory factory)
{
    private static readonly GeometryFactory GeometryFactory = new(new PrecisionModel(), 4326);

    [Fact]
    public async Task SummaryCalculatesRealTrendsAndNeverCrossesHealthUnitScope()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string password = "Dashboard!Test123";
        Guid firstUnitId;
        Guid secondUnitId;
        string managerName;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var first = HealthUnit.Create($"DA-{suffix}", "UBS Painel A");
            var second = HealthUnit.Create($"DB-{suffix}", "UBS Painel B");
            db.HealthUnits.AddRange(first, second);
            await db.SaveChangesAsync();
            firstUnitId = first.Id;
            secondUnitId = second.Id;
            managerName = $"painel.{suffix}";
            var manager = ApplicationUser.Create(managerName, "Gestor do painel", first.Id, mustChangePassword: false);
            Assert.True((await users.CreateAsync(manager, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(manager, SystemRoles.HealthUnitManager)).Succeeded);

            var firstMicroregion = Microregion.Create($"MR-{suffix}", "Área do painel", first.Id, manager.Id, Boundary(-47), TerritorySource.Manual, "#2F6BBD");
            var unassigned = Microregion.Create($"MU-{suffix}", "Área sem agente", first.Id, null, Boundary(-45), TerritorySource.Manual, "#E9A23B");
            var otherMicroregion = Microregion.Create($"MX-{suffix}", "Área externa", second.Id, null, Boundary(-43), TerritorySource.Manual);
            db.Microregions.AddRange(firstMicroregion, unassigned, otherMicroregion);
            await db.SaveChangesAsync();

            var property = HealthProperty.Create(first.Id, firstMicroregion.Id, "Rua do Painel", "10", null, null, GeometryFactory.CreatePoint(new Coordinate(-46.5, -23.5)), PropertyRegistrationStatus.Active, PropertySituation.Occupied);
            var otherProperty = HealthProperty.Create(second.Id, otherMicroregion.Id, "Rua Externa", "20", null, null, GeometryFactory.CreatePoint(new Coordinate(-42.5, -23.5)), PropertyRegistrationStatus.Active, PropertySituation.Occupied);
            db.Properties.AddRange(property, otherProperty);
            db.CoverageRules.Add(CoverageRule.Create(first.Id, firstMicroregion.Id, 60));
            var monthStart = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var family = Family.Create(first.Id, "F-10", "Responsável sintético A", manager.Id);
            var otherFamily = Family.Create(second.Id, "F-20", "Responsável sintético B", manager.Id);
            db.Families.AddRange(family, otherFamily);
            db.FamilyPropertyLinks.AddRange(FamilyPropertyLink.Create(first.Id, family.Id, property.Id, manager.Id), FamilyPropertyLink.Create(second.Id, otherFamily.Id, otherProperty.Id, manager.Id));
            await db.SaveChangesAsync();
            db.PropertyVisits.AddRange(
                PropertyVisit.Create(family.Id, property.Id, first.Id, manager.Id, monthStart.AddDays(1), VisitType.Routine, VisitOutcome.Completed, PropertySituation.Occupied, false, null),
                PropertyVisit.Create(family.Id, property.Id, first.Id, manager.Id, monthStart.AddMonths(-1).AddDays(1), VisitType.Routine, VisitOutcome.Completed, PropertySituation.Occupied, false, null),
                PropertyVisit.Create(otherFamily.Id, otherProperty.Id, second.Id, manager.Id, monthStart.AddDays(2), VisitType.Routine, VisitOutcome.Completed, PropertySituation.Occupied, false, null));
            await db.SaveChangesAsync();
        }

        using var client = CreateClient();
        await LoginAsync(client, managerName, password);
        var summary = await client.GetFromJsonAsync<JsonElement>($"/api/dashboard/summary?healthUnitId={firstUnitId}");
        Assert.Equal(1, summary.GetProperty("activeProperties").GetInt32());
        Assert.Equal(1, summary.GetProperty("activeFamilyIdentifiers").GetInt32());
        Assert.Equal(1, summary.GetProperty("visitsThisMonth").GetInt32());
        Assert.Equal(1, summary.GetProperty("visitsPreviousMonth").GetInt32());
        Assert.Equal(0, summary.GetProperty("visitsChangePercent").GetInt32());
        Assert.Equal(1, summary.GetProperty("unassignedMicroregions").GetInt32());
        Assert.Equal(1, summary.GetProperty("coverage").GetProperty("covered").GetInt32());
        Assert.Equal(2, summary.GetProperty("microregions").GetArrayLength());

        using var forbidden = await client.GetAsync($"/api/dashboard/summary?healthUnitId={secondUnitId}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private static MultiPolygon Boundary(double longitude)
    {
        var polygon = GeometryFactory.CreatePolygon([
            new(longitude, -24), new(longitude + 1, -24), new(longitude + 1, -23), new(longitude, -23), new(longitude, -24),
        ]);
        return GeometryFactory.CreateMultiPolygon([polygon]);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
    });

    private static async Task LoginAsync(HttpClient client, string userName, string password)
    {
        using var csrfResponse = await client.GetAsync("/api/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { userName, password, portal = "main" }),
        };
        request.Headers.Add("X-STU-CSRF", csrf.GetProperty("token").GetString());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
