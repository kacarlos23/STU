using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql;
using STU.Application.Security;
using STU.Domain.Auditing;
using STU.Domain.HealthUnits;
using STU.Domain.Operations;
using STU.Domain.Properties;
using STU.Domain.Territories;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace STU.IntegrationTests.Api;

public sealed class FamilyMigrationTests
{
    [Fact]
    public async Task ResetIsBlockedByDefaultAndPreservesTerritoryIdentitySettingsAndAppendOnlyAudit()
    {
        await using var container = new PostgreSqlBuilder("postgis/postgis:18-3.6").WithDatabase("stu_family_migration_test").WithUsername("stu_tests").WithPassword("test-only-password").Build();
        await container.StartAsync();
        await using var db = new StuDbContext(new DbContextOptionsBuilder<StuDbContext>().UseNpgsql(container.GetConnectionString(), o => o.UseNetTopologySuite()).Options);
        await db.GetService<IMigrator>().MigrateAsync("20260902201258_ActiveMicroregionIdentifiers");
        var unit = HealthUnit.Create("RESET", "UBS Preservada");
        var user = ApplicationUser.Create("reset.test", "Usuário preservado", unit.Id, mustChangePassword: false);
        var role = ApplicationRole.CreateCustom("reset-role", "Perfil preservado", null); role.Id = Guid.NewGuid();
        var gf = new GeometryFactory(new PrecisionModel(), 4326);
        var boundary = gf.CreateMultiPolygon([gf.CreatePolygon([new(-40,-18),new(-39,-18),new(-39,-17),new(-40,-17),new(-40,-18)])]);
        var neighborhood = Neighborhood.Create("Bairro preservado", boundary, TerritorySource.Manual);
        var micro = Microregion.Create("RESET-M", "Área preservada", unit.Id, null, boundary, TerritorySource.Manual);
        var tag = OperationalTag.Create(unit.Id, "Etiqueta preservada", "#abcdef");
        db.HealthUnits.Add(unit); db.Users.Add(user); db.Roles.Add(role); db.Neighborhoods.Add(neighborhood); db.Microregions.Add(micro);
        db.MicroregionNeighborhoods.Add(MicroregionNeighborhood.Create(micro.Id, neighborhood.Id)); db.OperationalTags.Add(tag); db.CoverageRules.Add(CoverageRule.Create(unit.Id, micro.Id, 90)); db.BackupSettings.Add(BackupSettings.CreateDefault());
        db.RoleClaims.Add(new IdentityRoleClaim<Guid> { RoleId = role.Id, ClaimType = StuClaimTypes.Permission, ClaimValue = StuPermissions.PropertiesView });
        var approval = AuditEntry.Create(user.Id, "reset.test", "Approve", "OnboardingReview", unit.Id.ToString(), "Aprovação anterior preservada", null, "{}", null); db.AuditEntries.Add(approval);
        await db.SaveChangesAsync();
        var propertyId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO properties ("Id","HealthUnitId","MicroregionId","Street","HouseNumber","FamilyNumber","Geometry","RegistrationStatus","Situation","ConcurrencyToken","CreatedAtUtc")
            VALUES ({propertyId},{unit.Id},{micro.Id},'Rua anterior','10','F-OLD',ST_SetSRID(ST_Point(-39.5,-17.5),4326),'Active','Occupied',gen_random_uuid(),now());
            INSERT INTO property_versions ("Id","PropertyId","VersionNumber","HealthUnitId","MicroregionId","Street","HouseNumber","FamilyNumber","Geometry","RegistrationStatus","Situation","IsArchived","ChangeKind","ChangedByUserId","ChangedAtUtc")
            SELECT gen_random_uuid(),"Id",1,"HealthUnitId","MicroregionId","Street","HouseNumber","FamilyNumber","Geometry","RegistrationStatus","Situation",false,'Create',{user.Id},now() FROM properties;
            INSERT INTO property_visits ("Id","PropertyId","HealthUnitId","AgentId","VisitedAtUtc","Type","Outcome","ObservedSituation","AccessDifficulty","ConcurrencyToken","CreatedAtUtc")
            VALUES (gen_random_uuid(),{propertyId},{unit.Id},{user.Id},now(),'Routine','Completed','Occupied',false,gen_random_uuid(),now());
            INSERT INTO property_tags ("PropertyId","TagId") VALUES ({propertyId},{tag.Id});
            INSERT INTO operation_jobs ("Id","HealthUnitId","CreatedByUserId","Kind","Status","Format","ParametersJson","RecordCount","ValidationErrorCount","AttemptCount","ProgressPercentage","ConcurrencyToken","CreatedAtUtc","SourceFileName")
            VALUES ({jobId},{unit.Id},{user.Id},'PropertyImport','AwaitingApproval','Csv',jsonb_build_object(),1,0,1,50,gen_random_uuid(),now(),'old.csv');
            """);
        // Both migration attempts target only this disposable container.
        var blocked = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
        Assert.Contains("bloqueada", blocked.MessageText);
        Assert.Equal(1, await ScalarAsync(db, "SELECT count(*) FROM properties"));
        Assert.Equal(1, await ScalarAsync(db, "SELECT count(*) FROM property_visits"));
        var storage = Path.Combine(Path.GetTempPath(), "stu-reset-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storage);
        await File.WriteAllTextAsync(Path.Combine(storage, "old.csv"), "arquivo operacional sintético");
        var intermediate = Path.Combine(storage, jobId.ToString("N") + ".geojson");
        await File.WriteAllTextAsync(intermediate, "intermediário sintético de operação interrompida");
        var orphanStage = Path.Combine(storage, jobId.ToString("N") + ".stage.json");
        await File.WriteAllTextAsync(orphanStage, "preparação sintética interrompida");
        await File.WriteAllTextAsync(Path.Combine(storage, "preserve.keep"), "arquivo que não pertence às operações");
        var preview = await RunToolAsync(container.GetConnectionString(), storage);
        Assert.Equal(0, preview.Code);
        using var report = JsonDocument.Parse(preview.Output);
        Assert.Equal(1, report.RootElement.GetProperty("targets").GetProperty("properties").GetInt64());
        var fingerprint = report.RootElement.GetProperty("fingerprint").GetString()!;
        var refused = await RunToolAsync(container.GetConnectionString(), storage, "--apply", "--database", "wrong-database", "--confirm", fingerprint, "--maintenance-confirmed", "--manifest", Path.Combine(storage, "manifest.json"));
        Assert.NotEqual(0, refused.Code); Assert.True(File.Exists(Path.Combine(storage, "old.csv")));
        var applied = await RunToolAsync(container.GetConnectionString(), storage, "--apply", "--database", "stu_family_migration_test", "--confirm", fingerprint, "--maintenance-confirmed", "--manifest", Path.Combine(storage, "manifest.json"));
        Assert.True(applied.Code == 0, applied.Output);
        Assert.False(File.Exists(Path.Combine(storage, "old.csv")));
        Assert.False(File.Exists(intermediate));
        Assert.False(File.Exists(orphanStage));
        Assert.True(File.Exists(Path.Combine(storage, "preserve.keep")));
        Assert.True(File.Exists(Path.Combine(storage, "manifest.json")));
        foreach (var table in new[] { "properties", "property_versions", "property_visits", "property_tags", "operation_jobs", "families", "family_property_links" })
            Assert.Equal(0, await ScalarAsync(db, $"SELECT count(*) FROM \"{table}\""));
        foreach (var table in new[] { "health_units", "neighborhoods", "microregions", "microregion_neighborhoods", "AspNetUsers", "AspNetRoles", "operational_tags", "coverage_rules", "backup_settings" })
            Assert.Equal(1, await ScalarAsync(db, $"SELECT count(*) FROM \"{table}\""));
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Id == approval.Id));
        Assert.True(await db.AuditEntries.AnyAsync(a => a.EntityType == "FamiliesReset"));
        Assert.True(await db.RoleClaims.AnyAsync(c => c.RoleId == role.Id && c.ClaimValue == StuPermissions.FamiliesView));
        Assert.True(await db.RoleClaims.AnyAsync(c => c.RoleId == role.Id && c.ClaimValue == StuPermissions.PropertiesView));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit_entries WHERE \"Id\"={approval.Id}"));
    }

    private static async Task<(int Code, string Output)> RunToolAsync(string connection, string storage, params string[] arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.StartInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "STU.FamilyReset.dll"));
        process.StartInfo.ArgumentList.Add("--storage"); process.StartInfo.ArgumentList.Add(storage);
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment["STU_FAMILY_RESET_CONNECTION"] = connection;
        process.Start(); var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        return (process.ExitCode, await output + await error);
    }

    private static async Task<long> ScalarAsync(StuDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection(); if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
