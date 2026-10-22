using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using STU.Domain.Families;
using STU.Domain.HealthUnits;
using STU.Domain.Properties;
using STU.Domain.Territories;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace STU.IntegrationTests.Api;

public sealed class RegistrationMigrationTests
{
    [Fact]
    public async Task UpgradePreservesExistingAddressesFamiliesVisitsAndGeographyAndBackfillsResponsibilities()
    {
        await using var container = new PostgreSqlBuilder("postgis/postgis:18-3.6").WithDatabase("registration_migration")
            .WithUsername("stu_tests").WithPassword("test-only-password").Build();
        await container.StartAsync();
        await using var db = new StuDbContext(new DbContextOptionsBuilder<StuDbContext>().UseNpgsql(container.GetConnectionString(), o => o.UseNetTopologySuite()).Options);
        await db.GetService<IMigrator>().MigrateAsync("20260923203301_IndependentFamilies");
        var unit = HealthUnit.Create("REG-UP", "UBS preservada");
        var agent = ApplicationUser.Create("migration.agent", "Agente preservado", unit.Id, false);
        agent.Id = Guid.NewGuid();
        db.HealthUnits.Add(unit); await db.SaveChangesAsync();
        await HistoricalUserSeed.InsertAsync(db, agent);
        var gf = new GeometryFactory(new PrecisionModel(), 4326);
        var boundary = gf.CreateMultiPolygon([gf.CreatePolygon([new(-40,-18),new(-39,-18),new(-39,-17),new(-40,-17),new(-40,-18)])]);
        var neighborhood = Neighborhood.Create("Bairro preservado", boundary, TerritorySource.Manual);
        var region = Microregion.Create("REG-UP", "Microrregião preservada", unit.Id, agent.Id, boundary, TerritorySource.Manual);
        var unassigned = Microregion.Create("REG-EMPTY", "Microrregião sem agente", unit.Id, null, boundary, TerritorySource.Manual);
        var family = Family.Create(unit.Id, "OLD-001", "Responsável preservado", agent.Id);
        db.Neighborhoods.Add(neighborhood); db.Microregions.AddRange(region, unassigned);
        db.MicroregionNeighborhoods.Add(MicroregionNeighborhood.Create(region.Id, neighborhood.Id));
        await db.SaveChangesAsync();
        db.Families.Add(family); db.FamilyVersions.Add(FamilyVersion.Capture(family, 1, "Create", agent.Id));
        await db.SaveChangesAsync();
        var propertyId = Guid.NewGuid(); var versionId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO properties ("Id","HealthUnitId","MicroregionId","Street","HouseNumber","Geometry","RegistrationStatus","Situation","ConcurrencyToken","CreatedAtUtc")
            VALUES ({propertyId},{unit.Id},{region.Id},'Rua preservada','10',ST_SetSRID(ST_Point(-39.5,-17.5),4326),'Active','Occupied',gen_random_uuid(),now());
            INSERT INTO property_versions ("Id","PropertyId","VersionNumber","HealthUnitId","MicroregionId","Street","HouseNumber","Geometry","RegistrationStatus","Situation","IsArchived","ChangeKind","ChangedByUserId","ChangedAtUtc")
            SELECT {versionId},"Id",1,"HealthUnitId","MicroregionId","Street","HouseNumber","Geometry","RegistrationStatus","Situation",false,'Create',{agent.Id},now() FROM properties WHERE "Id"={propertyId};
            """);
        var link = FamilyPropertyLink.Create(unit.Id, family.Id, propertyId, agent.Id);
        var visit = PropertyVisit.Create(family.Id, propertyId, unit.Id, agent.Id, DateTimeOffset.UtcNow.AddHours(-1), VisitType.Routine, VisitOutcome.Completed, PropertySituation.Occupied, false, "Histórico preservado");
        db.FamilyPropertyLinks.Add(link); db.PropertyVisits.Add(visit); await db.SaveChangesAsync();

        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        var property = await db.Properties.SingleAsync();
        var assignment = await db.MicroregionAgents.SingleAsync();
        Assert.Equal(propertyId, property.Id); Assert.Equal("Rua preservada", property.Street);
        Assert.Equal(gf.CreatePoint(new Coordinate(-39.5, -17.5)).AsText(), property.Geometry!.AsText());
        Assert.Equal(assignment.Id, property.MicroregionAgentId); Assert.Equal(agent.Id, assignment.AgentId);
        Assert.Equal(region.Id, assignment.MicroregionId); Assert.Equal(unit.Id, assignment.HealthUnitId);
        Assert.Equal(neighborhood.Id, property.NeighborhoodId); Assert.Null(assignment.EndedAtUtc);
        Assert.Equal(versionId, (await db.PropertyVersions.SingleAsync()).Id);
        Assert.Equal(family.Id, (await db.Families.SingleAsync()).Id);
        Assert.Equal(link.Id, (await db.FamilyPropertyLinks.SingleAsync()).Id);
        Assert.Equal(visit.Id, (await db.PropertyVisits.SingleAsync()).Id);
        Assert.Equal("Histórico preservado", (await db.PropertyVisits.SingleAsync()).Note);
        Assert.True(boundary.EqualsExact((await db.Microregions.SingleAsync(m => m.Id == region.Id)).Boundary));
        Assert.Null((await db.Microregions.SingleAsync(m => m.Id == unassigned.Id)).AssignedAgentId);
        var preserved = Assert.Single(await db.Users.ToListAsync());
        Assert.Equal(agent.DisplayName, preserved.DisplayName);
        Assert.Equal("light", preserved.AppearancePreference);
        Assert.Null(preserved.ProfilePhoto);
        Assert.Null(preserved.PasswordResetRequestedAtUtc);
    }
}
