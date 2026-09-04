using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using STU.Application.Security;
using STU.Domain.HealthUnits;
using STU.Domain.Operations;
using STU.Domain.Properties;
using STU.Domain.Territories;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.PilotData;

public sealed class PilotDatasetService(
    StuDbContext db,
    UserManager<ApplicationUser> userManager)
{
    private const string HealthUnitCode = "UBS-CARGA-PILOTO";
    private const string IsolationHealthUnitCode = "UBS-ISOLAMENTO-PILOTO";

    private static readonly (string Name, string Color)[] TagDefinitions =
    [
        ("Difícil acesso", "#dc2626"),
        ("Visita recusada", "#d97706"),
        ("Imóvel abandonado", "#7c3aed"),
        ("Acesso por escadaria", "#2563eb"),
        ("Cadastro para revisão", "#0891b2"),
        ("Referência comunitária", "#65a30d"),
    ];

    public async Task<PilotDatasetManifest> SeedAsync(
        string accountPassword,
        string? manifestPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountPassword);
        if (await db.HealthUnits.AnyAsync(item => item.Code == HealthUnitCode, cancellationToken))
        {
            throw new InvalidOperationException("A base sintética já foi carregada. Execute 'clean' antes de gerar novamente.");
        }

        var stopwatch = Stopwatch.StartNew();
        var blueprint = new PilotDatasetBlueprint();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var healthUnit = HealthUnit.Create(HealthUnitCode, "UBS Sintética de Carga");
        var isolationHealthUnit = HealthUnit.Create(IsolationHealthUnitCode, "UBS Sintética de Isolamento");
        db.HealthUnits.AddRange(healthUnit, isolationHealthUnit);
        await db.SaveChangesAsync(cancellationToken);

        var accounts = await CreateAccountsAsync(
            blueprint.Accounts,
            healthUnit.Id,
            isolationHealthUnit.Id,
            accountPassword);
        var managers = accounts.Where(item => item.Spec.Role == SystemRoles.HealthUnitManager).ToArray();
        var agents = accounts.Where(item => item.Spec.Role == SystemRoles.HealthAgent).ToArray();
        var actorId = managers[0].User.Id;

        var tags = TagDefinitions
            .Select(definition => OperationalTag.Create(healthUnit.Id, definition.Name, definition.Color))
            .ToArray();
        db.OperationalTags.AddRange(tags);

        var neighborhoods = blueprint.Neighborhoods
            .Select(spec => (Spec: spec, Entity: Neighborhood.Create(
                spec.Name,
                spec.Geometry,
                TerritorySource.Manual,
                $"STU-PILOT-BAIRRO-{spec.Index + 1:D2}",
                spec.Color)))
            .ToArray();
        db.Neighborhoods.AddRange(neighborhoods.Select(item => item.Entity));
        db.NeighborhoodVersions.AddRange(neighborhoods.Select(item =>
            NeighborhoodVersion.Capture(item.Entity, 1, "SyntheticSeed", actorId)));

        var microregions = blueprint.Microregions
            .Select(spec => (Spec: spec, Entity: Microregion.Create(
                spec.Code,
                spec.Name,
                healthUnit.Id,
                agents[spec.Index % agents.Length].User.Id,
                spec.Boundary,
                TerritorySource.Manual,
                spec.Color)))
            .ToArray();
        db.Microregions.AddRange(microregions.Select(item => item.Entity));

        foreach (var microregion in microregions)
        {
            var neighborhoodIds = neighborhoods
                .Where(neighborhood => neighborhood.Entity.Geometry.Intersection(microregion.Entity.Boundary).Area > 0)
                .Select(neighborhood => neighborhood.Entity.Id)
                .ToArray();

            db.MicroregionNeighborhoods.AddRange(neighborhoodIds.Select(neighborhoodId =>
                MicroregionNeighborhood.Create(microregion.Entity.Id, neighborhoodId)));
            db.MicroregionVersions.Add(MicroregionVersion.Capture(
                microregion.Entity,
                neighborhoodIds,
                1,
                "SyntheticSeed",
                actorId));
            db.CoverageRules.Add(CoverageRule.Create(healthUnit.Id, microregion.Entity.Id, 30 + microregion.Spec.Index % 4 * 15));
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();

        var visitCount = 0;
        var tagLinkCount = 0;
        var pendingProperties = 0;
        foreach (var microregion in microregions)
        {
            var agentId = agents[microregion.Spec.Index % agents.Length].User.Id;
            foreach (var propertySpec in PilotDatasetBlueprint.GetProperties(microregion.Spec))
            {
                var property = HealthProperty.Create(
                    healthUnit.Id,
                    microregion.Entity.Id,
                    propertySpec.Street,
                    propertySpec.HouseNumber,
                    propertySpec.FamilyNumber,
                    propertySpec.PostalCode,
                    propertySpec.Index % 12 == 0 ? "Fundos" : null,
                    propertySpec.Geometry,
                    propertySpec.Index % 20 == 0 ? PropertyRegistrationStatus.Draft : PropertyRegistrationStatus.Active,
                    SituationFor(propertySpec.Index));
                db.Properties.Add(property);
                db.PropertyVersions.Add(PropertyVersion.Capture(property, 1, "SyntheticSeed", agentId));

                AddVisit(property, agentId, propertySpec.Index, 0);
                visitCount++;
                if (propertySpec.Index % 2 == 0)
                {
                    AddVisit(property, agentId, propertySpec.Index, 1);
                    visitCount++;
                }

                if (propertySpec.Index % 3 == 0)
                {
                    db.PropertyTags.Add(PropertyTag.Create(property.Id, tags[propertySpec.Index % tags.Length].Id));
                    tagLinkCount++;
                }

                if (propertySpec.Index % 10 == 0)
                {
                    db.PropertyTags.Add(PropertyTag.Create(property.Id, tags[(propertySpec.Index + 1) % tags.Length].Id));
                    tagLinkCount++;
                }

                pendingProperties++;
                if (pendingProperties == 250)
                {
                    await db.SaveChangesAsync(cancellationToken);
                    db.ChangeTracker.Clear();
                    pendingProperties = 0;
                }
            }
        }

        if (pendingProperties > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }

        db.UserNotifications.AddRange(accounts.Select((account, index) => UserNotification.Create(
            account.User.Id,
            healthUnit.Id,
            index % 4 == 0 ? NotificationKind.Assignment : NotificationKind.Information,
            "Ambiente sintético preparado",
            "Esta conta pertence exclusivamente ao ensaio de carga do STU.",
            "/overview")));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        stopwatch.Stop();

        var status = await GetStatusAsync(cancellationToken);
        var manifest = new PilotDatasetManifest(
            DateTimeOffset.UtcNow,
            stopwatch.Elapsed,
            status,
            visitCount,
            tagLinkCount,
            accounts.Select(item => new PilotManifestAccount(item.Spec.UserName, item.Spec.Role)).ToArray());

        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            var fullPath = Path.GetFullPath(manifestPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(
                fullPath,
                JsonSerializer.Serialize(manifest, JsonOptions),
                cancellationToken);
        }

        return manifest;

        void AddVisit(HealthProperty property, Guid agentId, int propertyIndex, int sequence)
        {
            var observedSituation = SituationFor(propertyIndex + sequence);
            var visit = PropertyVisit.Create(
                property.Id,
                healthUnit.Id,
                agentId,
                PilotDatasetBlueprint.ReferenceDate.AddDays(-((propertyIndex * 3 + sequence * 17) % 120)),
                sequence == 0 ? VisitType.Routine : VisitType.FollowUp,
                propertyIndex % 11 == 0 ? VisitOutcome.NoAnswer : VisitOutcome.Completed,
                observedSituation,
                propertyIndex % 17 == 0,
                propertyIndex % 29 == 0 ? "Retornar em horário alternativo." : null);
            db.PropertyVisits.Add(visit);
        }
    }

    public async Task<PilotDatasetStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var databaseSize = await GetDatabaseSizeAsync(cancellationToken);
        return new PilotDatasetStatus(
            await db.HealthUnits.CountAsync(
                item => item.Code == HealthUnitCode || item.Code == IsolationHealthUnitCode,
                cancellationToken),
            await db.Neighborhoods.CountAsync(item => item.ExternalReference != null && item.ExternalReference.StartsWith("STU-PILOT-"), cancellationToken),
            await db.Microregions.CountAsync(item => item.Code.StartsWith("MR-"), cancellationToken),
            await db.Properties.CountAsync(item => item.HealthUnitId == db.HealthUnits.Where(unit => unit.Code == HealthUnitCode).Select(unit => unit.Id).FirstOrDefault(), cancellationToken),
            await db.PropertyVisits.CountAsync(item => item.HealthUnitId == db.HealthUnits.Where(unit => unit.Code == HealthUnitCode).Select(unit => unit.Id).FirstOrDefault(), cancellationToken),
            await userManager.Users.CountAsync(item => item.UserName != null && item.UserName.StartsWith("pilot."), cancellationToken),
            await db.OperationalTags.CountAsync(item => item.HealthUnitId == db.HealthUnits.Where(unit => unit.Code == HealthUnitCode).Select(unit => unit.Id).FirstOrDefault(), cancellationToken),
            await db.UserNotifications.CountAsync(item => item.HealthUnitId == db.HealthUnits.Where(unit => unit.Code == HealthUnitCode).Select(unit => unit.Id).FirstOrDefault(), cancellationToken),
            databaseSize);
    }

    public async Task CleanAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                audit_entries,
                operation_jobs,
                user_notifications,
                property_tags,
                property_visits,
                property_versions,
                properties,
                coverage_rules,
                operational_tags,
                microregion_versions,
                microregion_neighborhoods,
                microregions,
                neighborhood_versions,
                neighborhoods,
                backup_runs,
                backup_settings,
                system_heartbeats,
                health_units,
                "AspNetUserClaims",
                "AspNetUserLogins",
                "AspNetUserRoles",
                "AspNetUserTokens",
                "AspNetUsers"
            RESTART IDENTITY CASCADE;
            """,
            cancellationToken);
    }

    private async Task<(PilotAccountSpec Spec, ApplicationUser User)[]> CreateAccountsAsync(
        IReadOnlyList<PilotAccountSpec> specifications,
        Guid healthUnitId,
        Guid isolationHealthUnitId,
        string password)
    {
        var result = new List<(PilotAccountSpec, ApplicationUser)>(specifications.Count);
        foreach (var spec in specifications)
        {
            var user = ApplicationUser.Create(
                spec.UserName,
                spec.DisplayName,
                spec.IsIsolationAccount ? isolationHealthUnitId : healthUnitId);
            user.CompleteTemporaryPasswordChange();
            EnsureSucceeded(await userManager.CreateAsync(user, password), $"criar {spec.UserName}");
            EnsureSucceeded(await userManager.AddToRoleAsync(user, spec.Role), $"atribuir {spec.Role} a {spec.UserName}");
            result.Add((spec, user));
        }

        return [.. result];
    }

    private async Task<long> GetDatabaseSizeAsync(CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_database_size(current_database())";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static PropertySituation SituationFor(int index) => (index % 20) switch
    {
        0 => PropertySituation.Abandoned,
        1 or 2 => PropertySituation.Vacant,
        3 => PropertySituation.Commercial,
        _ => PropertySituation.Occupied,
    };

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        throw new InvalidOperationException($"Não foi possível {operation}: {string.Join(", ", result.Errors.Select(error => error.Description))}");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}

public sealed record PilotDatasetStatus(
    int HealthUnits,
    int Neighborhoods,
    int Microregions,
    int Properties,
    int Visits,
    int Users,
    int Tags,
    int Notifications,
    long DatabaseSizeBytes);

public sealed record PilotDatasetManifest(
    DateTimeOffset GeneratedAtUtc,
    TimeSpan SeedDuration,
    PilotDatasetStatus Counts,
    int GeneratedVisits,
    int PropertyTagLinks,
    IReadOnlyList<PilotManifestAccount> Accounts);

public sealed record PilotManifestAccount(string UserName, string Role);
