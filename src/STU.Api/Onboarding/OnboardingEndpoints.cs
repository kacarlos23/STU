using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using STU.Application.Security;
using STU.Domain.Auditing;
using STU.Domain.Operations;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.Api.Onboarding;

public static class OnboardingEndpoints
{
    private const double AreaTolerance = 0.0000000001;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] PilotNeighborhoods = ["Luiz Eduardo Magalhães", "Nova Teixeira", "Redenção"];

    public static IEndpointRouteBuilder MapOnboardingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/onboarding")
            .WithTags("Controlled onboarding")
            .RequireAuthorization(StuPolicies.PasswordChanged)
            .RequireRateLimiting("api");

        group.MapGet("/readiness", GetReadinessAsync);
        group.MapPost("/approve", ApproveAsync)
            .WithMetadata(new RequireAntiforgeryTokenAttribute(true));
        return endpoints;
    }

    private static async Task<IResult> GetReadinessAsync(
        Guid? healthUnitId,
        HttpContext http,
        UserManager<ApplicationUser> userManager,
        StuDbContext db,
        CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(healthUnitId, http, userManager, db, cancellationToken);
        if (scope.Error is not null) return scope.Error;
        return Results.Ok(await BuildReportAsync(scope.UnitId, db, cancellationToken));
    }

    private static async Task<IResult> ApproveAsync(
        ApprovalRequest request,
        HttpContext http,
        UserManager<ApplicationUser> userManager,
        StuDbContext db,
        CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(request.HealthUnitId, http, userManager, db, cancellationToken);
        if (scope.Error is not null) return scope.Error;

        var report = await BuildReportAsync(scope.UnitId, db, cancellationToken);
        if (!report.ReadyForApproval)
        {
            return Results.Conflict(new
            {
                title = "A UBS ainda possui pendências de pré-implantação.",
                blockers = report.Checks.Where(item => item.Status == "blocked").ToArray(),
            });
        }

        if (string.IsNullOrWhiteSpace(request.OffsiteDestination) || request.OffsiteDestination.Trim().Length is < 3 or > 160)
        {
            return Validation("offsiteDestination", "Informe onde a cópia externa criptografada foi armazenada, sem incluir credenciais.");
        }

        var backup = await db.BackupRuns.AsNoTracking()
            .Where(item => item.Status == BackupRunStatus.Completed && item.FilePrunedAtUtc == null)
            .OrderByDescending(item => item.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (backup?.Sha256 is null || !backup.Sha256.Equals(request.BackupSha256?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Validation("backupSha256", "O SHA-256 informado não corresponde ao backup íntegro mais recente.");
        }

        var actor = await userManager.GetUserAsync(http.User)
            ?? throw new InvalidOperationException("Usuário autenticado não encontrado.");
        var evidence = new
        {
            report.SnapshotHash,
            report.GeneratedAtUtc,
            HealthUnitId = scope.UnitId,
            BackupRunId = backup.Id,
            BackupSha256 = backup.Sha256,
            OffsiteDestination = request.OffsiteDestination.Trim(),
            Note = Clean(request.Note, 500),
        };
        db.AuditEntries.Add(AuditEntry.Create(
            actor.Id,
            actor.UserName ?? actor.DisplayName,
            "Approve",
            "OnboardingReview",
            scope.UnitId.ToString(),
            $"Pré-implantação da UBS {report.HealthUnit.Code} aprovada com evidência de backup externo.",
            null,
            JsonSerializer.Serialize(evidence, JsonOptions),
            http.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new
        {
            approved = true,
            report.SnapshotHash,
            approvedAtUtc = DateTimeOffset.UtcNow,
            approvedBy = actor.DisplayName,
        });
    }

    internal static async Task<ReadinessReport> BuildReportAsync(Guid unitId, StuDbContext db, CancellationToken cancellationToken)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var unit = await db.HealthUnits.AsNoTracking().SingleAsync(item => item.Id == unitId, cancellationToken);
        var microregions = await db.Microregions.AsNoTracking()
            .Where(item => item.HealthUnitId == unitId && item.ArchivedAtUtc == null)
            .OrderBy(item => item.Code)
            .ToListAsync(cancellationToken);
        var microregionIds = microregions.Select(item => item.Id).ToArray();
        var links = await db.MicroregionNeighborhoods.AsNoTracking()
            .Where(item => microregionIds.Contains(item.MicroregionId))
            .ToListAsync(cancellationToken);
        var neighborhoodIds = links.Select(item => item.NeighborhoodId).Distinct().ToArray();
        var neighborhoods = await db.Neighborhoods.AsNoTracking()
            .Where(item => neighborhoodIds.Contains(item.Id) && item.ArchivedAtUtc == null)
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);
        var activeNeighborhoodIds = neighborhoods.Select(item => item.Id).ToHashSet();
        var missingPilotNeighborhoods = PilotNeighborhoods.Where(expected => !neighborhoods.Any(item =>
            CultureInfo.InvariantCulture.CompareInfo.Compare(item.Name.Trim(), expected, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0)).ToArray();
        var properties = await db.Properties.AsNoTracking()
            .Where(item => item.HealthUnitId == unitId && item.ArchivedAtUtc == null)
            .ToListAsync(cancellationToken);

        var invalidNeighborhoods = neighborhoods.Count(item => item.Geometry.IsEmpty || !item.Geometry.IsValid || item.Geometry.SRID != 4326);
        var invalidMicroregions = microregions.Count(item => item.Boundary.IsEmpty || !item.Boundary.IsValid || item.Boundary.SRID != 4326);
        var orphanMicroregions = microregions.Count(item => !links.Any(link => link.MicroregionId == item.Id && activeNeighborhoodIds.Contains(link.NeighborhoodId)));
        var unassignedMicroregions = microregions.Count(item => item.AssignedAgentId == null);
        var overlapPairs = CountOverlaps(microregions.Select(item => (item.Id, Geometry: (Geometry)item.Boundary)).ToArray());
        var (gapArea, outsideArea) = CoverageDifference(neighborhoods.Select(item => item.Geometry), microregions.Select(item => (Geometry)item.Boundary));
        var boundaries = microregions.ToDictionary(item => item.Id, item => (Geometry)item.Boundary);
        var propertiesOutside = properties.Count(item => !boundaries.TryGetValue(item.MicroregionId, out var boundary) || !boundary.Covers(item.Geometry));
        var duplicateFamilies = properties.GroupBy(item => item.FamilyNumber, StringComparer.OrdinalIgnoreCase).Count(group => group.Count() > 1);

        var activeUsers = await db.Users.AsNoTracking()
            .Where(item => item.HealthUnitId == unitId && item.ArchivedAtUtc == null)
            .Select(item => new { item.Id, item.DisplayName, item.MustChangePassword })
            .ToListAsync(cancellationToken);
        var activeUserIds = activeUsers.Select(item => item.Id).ToArray();
        var memberships = await (from userRole in db.UserRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where activeUserIds.Contains(userRole.UserId) && role.ArchivedAtUtc == null
            select new { userRole.UserId, userRole.RoleId, role.Name }).ToListAsync(cancellationToken);
        var validAgentIds = memberships.Where(item => item.Name == SystemRoles.HealthAgent).Select(item => item.UserId).ToHashSet();
        unassignedMicroregions = microregions.Count(item => !item.AssignedAgentId.HasValue || !validAgentIds.Contains(item.AssignedAgentId.Value));
        var roleCounts = await (
            from userRole in db.UserRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where activeUserIds.Contains(userRole.UserId)
            group userRole by role.Name into grouped
            select new { Name = grouped.Key!, Count = grouped.Count() })
            .ToDictionaryAsync(item => item.Name, item => item.Count, cancellationToken);

        var pendingImports = await db.OperationJobs.AsNoTracking().CountAsync(item =>
            item.HealthUnitId == unitId &&
            item.Kind == OperationJobKind.PropertyImport &&
            (item.Status == OperationJobStatus.Pending || item.Status == OperationJobStatus.Processing || item.Status == OperationJobStatus.AwaitingApproval), cancellationToken);
        var latestImport = await db.OperationJobs.AsNoTracking()
            .Where(item => item.HealthUnitId == unitId && item.Kind == OperationJobKind.PropertyImport && item.Status == OperationJobStatus.Completed)
            .OrderByDescending(item => item.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var importedCount = latestImport?.RecordCount ?? 0;

        var backupSettings = await db.BackupSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var latestBackup = await db.BackupRuns.AsNoTracking()
            .Where(item => item.Status == BackupRunStatus.Completed && item.FilePrunedAtUtc == null)
            .OrderByDescending(item => item.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var recentBackup = latestBackup?.CompletedAtUtc >= generatedAt.AddDays(-7);

        var counts = new ReadinessCounts(
            neighborhoods.Count,
            microregions.Count,
            properties.Count,
            activeUsers.Count,
            activeUsers.Count(item => item.MustChangePassword),
            roleCounts.GetValueOrDefault(SystemRoles.HealthAgent),
            roleCounts.GetValueOrDefault(SystemRoles.Receptionist),
            roleCounts.GetValueOrDefault(SystemRoles.Doctor),
            roleCounts.GetValueOrDefault(SystemRoles.HealthUnitManager));
        var importSummary = new ImportCountSummary(Math.Max(0, properties.Count - importedCount), importedCount, properties.Count, pendingImports);
        var checks = new List<ReadinessCheck>
        {
            Check("neighborhood-count", "Três bairros vinculados", neighborhoods.Count == 3, $"{neighborhoods.Count} de 3 bairro(s) vinculados às microrregiões da UBS."),
            Check("pilot-neighborhoods", "Bairros previstos para o piloto", missingPilotNeighborhoods.Length == 0,
                missingPilotNeighborhoods.Length == 0 ? "Os três bairros aprovados estão vinculados." : $"Faltam vínculos com: {string.Join(", ", missingPilotNeighborhoods)}."),
            Check("microregions", "Microrregiões cadastradas", microregions.Count > 0, $"{microregions.Count} microrregião(ões) ativa(s)."),
            Check("properties", "Imóveis cadastrados para o ensaio", properties.Count > 0, $"{properties.Count} imóvel(is) não arquivado(s)."),
            Check("coordinate-system", "Geometrias válidas em SRID 4326", invalidNeighborhoods + invalidMicroregions == 0, $"{invalidNeighborhoods + invalidMicroregions} geometria(s) inválida(s) ou fora do SRID 4326."),
            Check("overlaps", "Sem sobreposição interna", overlapPairs == 0, $"{overlapPairs} par(es) de microrregiões com sobreposição de área."),
            Check("coverage-gaps", "Sem lacunas entre bairros e microrregiões", gapArea <= AreaTolerance && outsideArea <= AreaTolerance, $"Área sem cobertura: {gapArea:G6}; área fora dos bairros: {outsideArea:G6}."),
            Check("neighborhood-links", "Todas as microrregiões vinculadas a bairro", orphanMicroregions == 0, $"{orphanMicroregions} microrregião(ões) sem bairro ativo."),
            Check("agent-assignments", "Todas as microrregiões com agente ativo da UBS", unassignedMicroregions == 0, $"{unassignedMicroregions} microrregião(ões) sem agente ativo com função de agente de saúde nesta UBS."),
            Check("property-boundaries", "Imóveis dentro da microrregião", propertiesOutside == 0, $"{propertiesOutside} imóvel(is) fora do limite atribuído."),
            Check("family-numbers", "Números de família sem duplicidade", duplicateFamilies == 0, $"{duplicateFamilies} número(s) de família duplicado(s)."),
            Check("role-accounts", "Contas dos quatro perfis operacionais", counts.HealthAgents > 0 && counts.Receptionists > 0 && counts.Doctors > 0 && counts.Managers > 0, $"Agentes {counts.HealthAgents}; recepção {counts.Receptionists}; médicos {counts.Doctors}; gerentes {counts.Managers}."),
            Check("pending-imports", "Sem importação aguardando processamento ou aprovação", pendingImports == 0, $"{pendingImports} importação(ões) pendente(s)."),
            Check("backup-schedule", "Rotina semanal de backup ativa", backupSettings?.Enabled == true, backupSettings?.Enabled == true ? $"Rotina ativa: {backupSettings.DayOfWeek}, {backupSettings.LocalHour:00}h ({backupSettings.TimeZoneId})." : "Rotina semanal inativa ou não configurada."),
            Check("recent-backup", "Backup íntegro dos últimos sete dias", recentBackup, latestBackup is null ? "Nenhum backup concluído disponível." : $"Último backup em {latestBackup.CompletedAtUtc:O}."),
        };

        var hashPayload = JsonSerializer.Serialize(new
        {
            unit.Id,
            unit.Code,
            unit.Name,
            // Counts alone do not identify the reviewed records. Tokens also invalidate
            // approvals after an edit that leaves every count/check unchanged.
            NeighborhoodState = neighborhoods.OrderBy(item => item.Id).Select(item => new { item.Id, item.ConcurrencyToken }),
            MicroregionState = microregions.OrderBy(item => item.Id).Select(item => new { item.Id, item.ConcurrencyToken, item.AssignedAgentId }),
            LinkState = links.OrderBy(item => item.MicroregionId).ThenBy(item => item.NeighborhoodId).Select(item => new { item.MicroregionId, item.NeighborhoodId }),
            PropertyState = properties.OrderBy(item => item.Id).Select(item => new { item.Id, item.ConcurrencyToken }),
            UserState = activeUsers.OrderBy(item => item.Id),
            RoleState = memberships.OrderBy(item => item.UserId).ThenBy(item => item.RoleId),
            counts,
            importSummary,
            CheckState = checks.Select(item => new { item.Id, item.Status, item.Detail }),
            BackupId = latestBackup?.Id,
            BackupSha256 = latestBackup?.Sha256,
        }, JsonOptions);
        var snapshotHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashPayload))).ToLowerInvariant();
        var latestApproval = await LatestApprovalAsync(unitId, db, cancellationToken);

        return new ReadinessReport(
            generatedAt,
            snapshotHash,
            new HealthUnitSummary(unit.Id, unit.Code, unit.Name),
            counts,
            importSummary,
            new GeometrySummary(invalidNeighborhoods, invalidMicroregions, overlapPairs, orphanMicroregions, unassignedMicroregions, propertiesOutside, duplicateFamilies, gapArea, outsideArea),
            new BackupSummary(backupSettings?.Enabled == true, latestBackup?.Id, latestBackup?.CompletedAtUtc, latestBackup?.Sha256, latestBackup?.SizeBytes, recentBackup),
            checks.All(item => item.Status == "passed"),
            checks,
            latestApproval);
    }

    private static int CountOverlaps((Guid Id, Geometry Geometry)[] items)
    {
        var count = 0;
        for (var left = 0; left < items.Length; left++)
        for (var right = left + 1; right < items.Length; right++)
        {
            if (!items[left].Geometry.EnvelopeInternal.Intersects(items[right].Geometry.EnvelopeInternal)) continue;
            if (items[left].Geometry.Intersection(items[right].Geometry).Area > AreaTolerance) count++;
        }
        return count;
    }

    private static (double GapArea, double OutsideArea) CoverageDifference(IEnumerable<Geometry> neighborhoods, IEnumerable<Geometry> microregions)
    {
        var neighborhoodArray = neighborhoods.ToArray();
        var microregionArray = microregions.ToArray();
        if (neighborhoodArray.Length == 0 || microregionArray.Length == 0) return (0, 0);
        var neighborhoodUnion = UnaryUnionOp.Union(neighborhoodArray);
        var microregionUnion = UnaryUnionOp.Union(microregionArray);
        return (neighborhoodUnion.Difference(microregionUnion).Area, microregionUnion.Difference(neighborhoodUnion).Area);
    }

    private static async Task<ApprovalSummary?> LatestApprovalAsync(Guid unitId, StuDbContext db, CancellationToken cancellationToken)
    {
        var entry = await db.AuditEntries.AsNoTracking()
            .Where(item => item.EntityType == "OnboardingReview" && item.EntityId == unitId.ToString() && item.Action == "Approve")
            .OrderByDescending(item => item.OccurredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (entry is null) return null;
        string? hash = null;
        string? destination = null;
        if (!string.IsNullOrWhiteSpace(entry.AfterJson))
        {
            using var document = JsonDocument.Parse(entry.AfterJson);
            if (document.RootElement.TryGetProperty("snapshotHash", out var hashElement)) hash = hashElement.GetString();
            if (document.RootElement.TryGetProperty("offsiteDestination", out var destinationElement)) destination = destinationElement.GetString();
        }
        return new ApprovalSummary(entry.OccurredAtUtc, entry.ActorUserName, hash, destination);
    }

    private static async Task<ScopeResult> ResolveScopeAsync(
        Guid? requestedUnitId,
        HttpContext http,
        UserManager<ApplicationUser> userManager,
        StuDbContext db,
        CancellationToken cancellationToken)
    {
        var actor = await userManager.GetUserAsync(http.User);
        if (actor is null) return new(Results.Unauthorized(), Guid.Empty);
        var global = http.User.IsInRole(SystemRoles.GlobalAdministrator);
        var manager = http.User.IsInRole(SystemRoles.HealthUnitManager);
        if (!global && !manager) return new(Results.Forbid(), Guid.Empty);
        var unitId = global ? requestedUnitId : actor.HealthUnitId;
        if (!unitId.HasValue || (!global && requestedUnitId.HasValue && requestedUnitId != actor.HealthUnitId)) return new(Results.Forbid(), Guid.Empty);
        if (!await db.HealthUnits.AsNoTracking().AnyAsync(item => item.Id == unitId && item.ArchivedAtUtc == null, cancellationToken)) return new(Results.NotFound(), Guid.Empty);
        return new(null, unitId.Value);
    }

    private static ReadinessCheck Check(string id, string label, bool passed, string detail) => new(id, label, passed ? "passed" : "blocked", detail);
    private static string? Clean(string? value, int limit) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, limit)];
    private static IResult Validation(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private sealed record ScopeResult(IResult? Error, Guid UnitId);
    private sealed record ApprovalRequest(Guid? HealthUnitId, string? BackupSha256, string? OffsiteDestination, string? Note);
    internal sealed record HealthUnitSummary(Guid Id, string Code, string Name);
    internal sealed record ReadinessCounts(int Neighborhoods, int Microregions, int Properties, int ActiveUsers, int TemporaryPasswords, int HealthAgents, int Receptionists, int Doctors, int Managers);
    internal sealed record ImportCountSummary(int BeforeLatestImport, int LatestImported, int CurrentTotal, int PendingImports);
    internal sealed record GeometrySummary(int InvalidNeighborhoods, int InvalidMicroregions, int OverlapPairs, int OrphanMicroregions, int UnassignedMicroregions, int PropertiesOutsideBoundary, int DuplicateFamilyNumbers, double GapArea, double OutsideArea);
    internal sealed record BackupSummary(bool ScheduleEnabled, Guid? RunId, DateTimeOffset? CompletedAtUtc, string? Sha256, long? SizeBytes, bool Recent);
    internal sealed record ReadinessCheck(string Id, string Label, string Status, string Detail);
    internal sealed record ApprovalSummary(DateTimeOffset ApprovedAtUtc, string ApprovedBy, string? SnapshotHash, string? OffsiteDestination);
    internal sealed record ReadinessReport(DateTimeOffset GeneratedAtUtc, string SnapshotHash, HealthUnitSummary HealthUnit, ReadinessCounts Counts, ImportCountSummary ImportSummary, GeometrySummary Geometry, BackupSummary Backup, bool ReadyForApproval, IReadOnlyList<ReadinessCheck> Checks, ApprovalSummary? LatestApproval);
}
