using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;
using STU.Domain.Auditing;
using STU.Domain.Operations;
using STU.Domain.Properties;
using STU.Infrastructure.Persistence;

namespace STU.Worker;

public sealed class OperationJobProcessor(StuDbContext db, IConfiguration configuration, ILogger<OperationJobProcessor> logger)
{
    private static readonly JsonSerializerOptions GeoJsonOptions = CreateGeoJsonOptions();
    private static readonly Action<ILogger, Guid, Exception?> LogJobFailure = LoggerMessage.Define<Guid>(
        LogLevel.Error, new EventId(2, "OperationJobFailure"), "Falha no trabalho operacional {JobId}.");
    private readonly string storageRoot = EnsureStorage(configuration);

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var stale = await db.OperationJobs.Where(item => item.Status == OperationJobStatus.Processing && item.StartedAtUtc < DateTimeOffset.UtcNow.AddMinutes(-15)).ToListAsync(cancellationToken);
        foreach (var interrupted in stale)
        {
            interrupted.RecoverInterrupted();
            if (interrupted.Status == OperationJobStatus.Failed)
            {
                db.UserNotifications.Add(UserNotification.Create(interrupted.CreatedByUserId, interrupted.HealthUnitId, NotificationKind.OperationFailed, "Trabalho operacional interrompido", interrupted.ErrorSummary ?? "A operação exige revisão manual.", "/operations"));
                await AddAuditAsync(interrupted, "Fail", interrupted.ErrorSummary ?? "Operação interrompida.", cancellationToken);
            }
        }
        if (stale.Count > 0) await db.SaveChangesAsync(cancellationToken);

        var job = await db.OperationJobs.Where(item => item.Status == OperationJobStatus.Pending).OrderBy(item => item.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (job is null) return false;
        try
        {
            job.Start(); await db.SaveChangesAsync(cancellationToken);
            if (job.Kind == OperationJobKind.PropertyExport) await ExportAsync(job, cancellationToken);
            else if (job.ApprovedAtUtc.HasValue) await CommitImportAsync(job, cancellationToken);
            else await ValidateImportAsync(job, cancellationToken);
        }
        catch (Exception exception)
        {
            LogJobFailure(logger, job.Id, exception);
            db.ChangeTracker.Clear(); job = await db.OperationJobs.SingleAsync(item => item.Id == job.Id, cancellationToken);
            job.Fail(exception is InvalidDataException or ImportValidationException ? exception.Message : "O processamento falhou. O trabalho pode ser repetido.", exception is ImportValidationException validation ? validation.ErrorCount : exception is InvalidDataException ? 1 : 0);
            db.UserNotifications.Add(UserNotification.Create(job.CreatedByUserId, job.HealthUnitId, NotificationKind.OperationFailed, "Trabalho operacional com falha", job.ErrorSummary ?? "Não foi possível concluir o trabalho.", "/operations"));
            await AddAuditAsync(job, "Fail", job.ErrorSummary ?? "Falha operacional.", cancellationToken); await db.SaveChangesAsync(cancellationToken);
        }
        return true;
    }

    private async Task ExportAsync(OperationJob job, CancellationToken cancellationToken)
    {
        var filters = JsonSerializer.Deserialize<ExportFilters>(job.ParametersJson, GeoJsonOptions) ?? new();
        var query = db.Properties.AsNoTracking().Where(item => item.HealthUnitId == job.HealthUnitId && item.ArchivedAtUtc == null);
        if (filters.MicroregionId.HasValue) query = query.Where(item => item.MicroregionId == filters.MicroregionId);
        if (Enum.TryParse<PropertySituation>(filters.Situation, true, out var situation)) query = query.Where(item => item.Situation == situation);
        var properties = await query.OrderBy(item => item.Street).ThenBy(item => item.HouseNumber).ToListAsync(cancellationToken);
        var ids = properties.Select(item => item.Id).ToArray();
        var visits = await db.PropertyVisits.AsNoTracking().Where(item => ids.Contains(item.PropertyId) && item.ArchivedAtUtc == null && (!filters.FromUtc.HasValue || item.VisitedAtUtc >= filters.FromUtc) && (!filters.ToUtc.HasValue || item.VisitedAtUtc <= filters.ToUtc)).ToListAsync(cancellationToken);
        var lastVisits = visits.GroupBy(item => item.PropertyId).ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.VisitedAtUtc).First());
        var microregions = await db.Microregions.AsNoTracking().Where(item => item.HealthUnitId == job.HealthUnitId).ToDictionaryAsync(item => item.Id, cancellationToken);
        var records = properties.Select(item => new ExportProperty(item, microregions.GetValueOrDefault(item.MicroregionId)?.Code ?? string.Empty, lastVisits.GetValueOrDefault(item.Id))).ToList();
        var baseName = $"{job.Id:N}"; string resultName;
        switch (job.Format)
        {
            case OperationFileFormat.Csv:
                resultName = baseName + ".csv"; await File.WriteAllTextAsync(SafePath(resultName), BuildCsv(records), new UTF8Encoding(true), cancellationToken); break;
            case OperationFileFormat.Kml:
                resultName = baseName + ".kml"; await File.WriteAllTextAsync(SafePath(resultName), BuildKml(records), new UTF8Encoding(false), cancellationToken); break;
            case OperationFileFormat.GeoPackage:
                var intermediate = baseName + ".geojson"; await File.WriteAllTextAsync(SafePath(intermediate), BuildGeoJson(records), cancellationToken);
                resultName = baseName + ".gpkg"; await ConvertToGeoPackageAsync(SafePath(intermediate), SafePath(resultName), cancellationToken); File.Delete(SafePath(intermediate)); break;
            default:
                resultName = baseName + ".geojson"; await File.WriteAllTextAsync(SafePath(resultName), BuildGeoJson(records), cancellationToken); break;
        }
        job.Complete(resultName, records.Count); db.UserNotifications.Add(UserNotification.Create(job.CreatedByUserId, job.HealthUnitId, NotificationKind.OperationCompleted, "Exportação concluída", $"O arquivo {job.Format} com {records.Count} imóvel(is) está pronto.", "/operations"));
        await AddAuditAsync(job, "Complete", $"Exportação {job.Format} concluída com {records.Count} imóvel(is).", cancellationToken); await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ValidateImportAsync(OperationJob job, CancellationToken cancellationToken)
    {
        if (job.SourceFileName is null) throw new InvalidDataException("Arquivo de origem não encontrado.");
        var rows = job.Format == OperationFileFormat.Csv ? await ReadCsvAsync(SafePath(job.SourceFileName), cancellationToken) : await ReadGeoJsonAsync(SafePath(job.SourceFileName), cancellationToken);
        if (rows.Count is 0 or > 10000) throw new InvalidDataException("O arquivo deve conter entre 1 e 10.000 imóveis.");
        var microregions = await db.Microregions.Where(item => item.HealthUnitId == job.HealthUnitId && item.ArchivedAtUtc == null).ToListAsync(cancellationToken);
        var byCode = microregions.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase); var existingFamilies = await db.Properties.AsNoTracking().Where(item => item.HealthUnitId == job.HealthUnitId && item.ArchivedAtUtc == null).Select(item => item.FamilyNumber).ToHashSetAsync(cancellationToken);
        var staged = new List<StagedProperty>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var errors = new List<string>();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index]; var line = index + 2; var family = row.FamilyNumber.Trim().ToUpperInvariant();
            if (!byCode.TryGetValue(row.MicroregionCode.Trim(), out var micro)) { errors.Add($"Linha {line}: microrregião não encontrada."); continue; }
            if (string.IsNullOrWhiteSpace(row.Street) || string.IsNullOrWhiteSpace(row.HouseNumber) || string.IsNullOrWhiteSpace(family)) { errors.Add($"Linha {line}: endereço e números são obrigatórios."); continue; }
            if (!seen.Add(family) || existingFamilies.Contains(family)) { errors.Add($"Linha {line}: número de família {family} duplicado."); continue; }
            if (row.Geometry is not (Point or Polygon or MultiPolygon) || !row.Geometry.IsValid || !micro.Boundary.Covers(row.Geometry)) { errors.Add($"Linha {line}: geometria inválida ou fora da microrregião."); continue; }
            if (!Enum.TryParse<PropertyRegistrationStatus>(row.RegistrationStatus, true, out var status) || !Enum.TryParse<PropertySituation>(row.Situation, true, out var situation)) { errors.Add($"Linha {line}: situação inválida."); continue; }
            staged.Add(new(micro.Id, row.Street.Trim(), row.HouseNumber.Trim(), family, Clean(row.PostalCode), Clean(row.Complement), row.Geometry, status, situation));
            if (errors.Count >= 30) break;
        }
        if (errors.Count > 0) throw new ImportValidationException(errors.Count, $"{errors.Count} erro(s) de validação. {string.Join(" ", errors.Take(10))}");
        var stagedName = $"{job.Id:N}.stage.json"; await File.WriteAllTextAsync(SafePath(stagedName), JsonSerializer.Serialize(staged, GeoJsonOptions), cancellationToken);
        job.AwaitApproval(stagedName, staged.Count); db.UserNotifications.Add(UserNotification.Create(job.CreatedByUserId, job.HealthUnitId, NotificationKind.Information, "Importação validada", $"{staged.Count} imóvel(is) aguardam sua aprovação.", "/operations"));
        await AddAuditAsync(job, "Validate", $"Importação validada com {staged.Count} imóvel(is), sem alterar dados oficiais.", cancellationToken); await db.SaveChangesAsync(cancellationToken);
    }

    private async Task CommitImportAsync(OperationJob job, CancellationToken cancellationToken)
    {
        if (job.StagedFileName is null) throw new InvalidDataException("Arquivo preparado não encontrado.");
        var staged = JsonSerializer.Deserialize<List<StagedProperty>>(await File.ReadAllTextAsync(SafePath(job.StagedFileName), cancellationToken), GeoJsonOptions) ?? [];
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var families = staged.Select(item => item.FamilyNumber).ToArray();
        if (await db.Properties.AnyAsync(item => item.HealthUnitId == job.HealthUnitId && item.ArchivedAtUtc == null && families.Contains(item.FamilyNumber), cancellationToken)) throw new InvalidDataException("Um número de família passou a ser utilizado após a validação. Envie o arquivo novamente.");
        var microregions = await db.Microregions.Where(item => item.HealthUnitId == job.HealthUnitId && item.ArchivedAtUtc == null).ToDictionaryAsync(item => item.Id, cancellationToken);
        foreach (var row in staged)
        {
            if (!microregions.TryGetValue(row.MicroregionId, out var micro) || !micro.Boundary.Covers(row.Geometry)) throw new InvalidDataException("O território mudou após a validação. Envie o arquivo novamente.");
            var property = HealthProperty.Create(job.HealthUnitId, row.MicroregionId, row.Street, row.HouseNumber, row.FamilyNumber, row.PostalCode, row.Complement, row.Geometry, row.RegistrationStatus, row.Situation);
            db.Properties.Add(property); db.PropertyVersions.Add(PropertyVersion.Capture(property, 1, "Import", job.ApprovedByUserId ?? job.CreatedByUserId));
        }
        job.Complete(null, staged.Count); db.UserNotifications.Add(UserNotification.Create(job.CreatedByUserId, job.HealthUnitId, NotificationKind.OperationCompleted, "Importação concluída", $"{staged.Count} imóvel(is) foram cadastrados.", "/operations"));
        await AddAuditAsync(job, "Complete", $"Importação atômica concluída com {staged.Count} imóvel(is).", cancellationToken); await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    private async Task AddAuditAsync(OperationJob job, string action, string summary, CancellationToken cancellationToken)
    {
        var actor = await db.Users.AsNoTracking().Where(item => item.Id == (job.ApprovedByUserId ?? job.CreatedByUserId)).Select(item => item.UserName ?? item.DisplayName).SingleAsync(cancellationToken);
        db.AuditEntries.Add(AuditEntry.Create(job.ApprovedByUserId ?? job.CreatedByUserId, actor, action, "OperationJob", job.Id.ToString(), summary, null, null, null));
    }

    private static async Task<List<ImportRow>> ReadCsvAsync(string path, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(path, cancellationToken); if (lines.Length < 2) return [];
        var headers = ParseCsvLine(lines[0]).Select(value => value.Trim().ToLowerInvariant()).ToArray(); var rows = new List<ImportRow>();
        foreach (var line in lines.Skip(1).Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var values = ParseCsvLine(line); string Get(string name) { var index = Array.IndexOf(headers, name); return index >= 0 && index < values.Count ? values[index] : string.Empty; }
            if (!double.TryParse(Get("longitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) || !double.TryParse(Get("latitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)) throw new InvalidDataException("CSV com longitude ou latitude inválida.");
            var point = new Point(longitude, latitude) { SRID = 4326 }; rows.Add(new(Get("microregioncode"), Get("street"), Get("housenumber"), Get("familynumber"), Get("postalcode"), Get("complement"), point, Default(Get("registrationstatus"), "Active"), Default(Get("situation"), "Occupied")));
        }
        return rows;
    }

    private static async Task<List<ImportRow>> ReadGeoJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken)); if (!document.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array) throw new InvalidDataException("GeoJSON deve ser uma FeatureCollection.");
        var rows = new List<ImportRow>(); foreach (var feature in features.EnumerateArray())
        {
            var geometry = JsonSerializer.Deserialize<Geometry>(feature.GetProperty("geometry").GetRawText(), GeoJsonOptions) ?? throw new InvalidDataException("Geometria GeoJSON inválida."); geometry.SRID = 4326; var properties = feature.GetProperty("properties");
            string Get(string name, string fallback = "") => properties.TryGetProperty(name, out var value) ? value.ToString() : fallback;
            rows.Add(new(Get("microregionCode"), Get("street"), Get("houseNumber"), Get("familyNumber"), Get("postalCode"), Get("complement"), geometry, Get("registrationStatus", "Active"), Get("situation", "Occupied")));
        }
        return rows;
    }

    private static string BuildCsv(List<ExportProperty> records)
    {
        var builder = new StringBuilder("microregionCode,street,houseNumber,familyNumber,postalCode,complement,situation,registrationStatus,lastVisitAtUtc,lastVisitOutcome,longitude,latitude\r\n");
        foreach (var row in records) { var point = row.Property.Geometry.Centroid; builder.AppendLine(string.Join(',', Csv(row.MicroregionCode), Csv(row.Property.Street), Csv(row.Property.HouseNumber), Csv(row.Property.FamilyNumber), Csv(row.Property.PostalCode), Csv(row.Property.Complement), row.Property.Situation, row.Property.RegistrationStatus, row.LastVisit?.VisitedAtUtc.ToString("O") ?? "", row.LastVisit?.Outcome.ToString() ?? "", point.X.ToString(CultureInfo.InvariantCulture), point.Y.ToString(CultureInfo.InvariantCulture))); }
        return builder.ToString();
    }

    private static string BuildGeoJson(List<ExportProperty> records) => JsonSerializer.Serialize(new { type = "FeatureCollection", features = records.Select(row => new { type = "Feature", id = row.Property.Id, geometry = row.Property.Geometry, properties = new { row.MicroregionCode, row.Property.Street, row.Property.HouseNumber, row.Property.FamilyNumber, situation = row.Property.Situation.ToString(), registrationStatus = row.Property.RegistrationStatus.ToString(), lastVisitAtUtc = row.LastVisit?.VisitedAtUtc } }) }, GeoJsonOptions);
    private static string BuildKml(List<ExportProperty> records) { var body = string.Join("", records.Select(row => $"<Placemark><name>{SecurityElement.Escape(row.Property.HouseNumber)} · F {SecurityElement.Escape(row.Property.FamilyNumber)}</name><description>{SecurityElement.Escape(row.Property.Street)}</description>{KmlGeometry(row.Property.Geometry)}</Placemark>")); return $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document>{body}</Document></kml>"; }
    private static string KmlGeometry(Geometry geometry) => geometry switch { Point point => $"<Point><coordinates>{point.X.ToString(CultureInfo.InvariantCulture)},{point.Y.ToString(CultureInfo.InvariantCulture)},0</coordinates></Point>", Polygon polygon => KmlPolygon(polygon), MultiPolygon multi => $"<MultiGeometry>{string.Concat(Enumerable.Range(0, multi.NumGeometries).Select(index => KmlPolygon((Polygon)multi.GetGeometryN(index))))}</MultiGeometry>", _ => "" };
    private static string KmlPolygon(Polygon polygon) { var coordinates = string.Join(' ', polygon.ExteriorRing.Coordinates.Select(point => $"{point.X.ToString(CultureInfo.InvariantCulture)},{point.Y.ToString(CultureInfo.InvariantCulture)},0")); return $"<Polygon><outerBoundaryIs><LinearRing><coordinates>{coordinates}</coordinates></LinearRing></outerBoundaryIs></Polygon>"; }
    private static async Task ConvertToGeoPackageAsync(string input, string output, CancellationToken cancellationToken) { var process = new Process { StartInfo = new("ogr2ogr") { UseShellExecute = false, RedirectStandardError = true } }; process.StartInfo.ArgumentList.Add("-f"); process.StartInfo.ArgumentList.Add("GPKG"); process.StartInfo.ArgumentList.Add(output); process.StartInfo.ArgumentList.Add(input); process.StartInfo.ArgumentList.Add("-nln"); process.StartInfo.ArgumentList.Add("properties"); process.StartInfo.ArgumentList.Add("-overwrite"); process.Start(); var error = await process.StandardError.ReadToEndAsync(cancellationToken); await process.WaitForExitAsync(cancellationToken); if (process.ExitCode != 0) throw new InvalidDataException($"GDAL não gerou o GeoPackage: {error[..Math.Min(error.Length, 500)]}"); }
    private string SafePath(string name) { var path = Path.GetFullPath(Path.Combine(storageRoot, Path.GetFileName(name))); if (!path.StartsWith(storageRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Caminho operacional inválido."); return path; }
    private static string EnsureStorage(IConfiguration configuration) { var root = Path.GetFullPath(configuration["Operations:StoragePath"] ?? Path.Combine(AppContext.BaseDirectory, "operations-data")); Directory.CreateDirectory(root); return root; }
    private static JsonSerializerOptions CreateGeoJsonOptions() { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); options.Converters.Add(new GeoJsonConverterFactory()); return options; }
    private static List<string> ParseCsvLine(string line) { var values = new List<string>(); var current = new StringBuilder(); var quoted = false; for (var index = 0; index < line.Length; index++) { var character = line[index]; if (character == '"') { if (quoted && index + 1 < line.Length && line[index + 1] == '"') { current.Append('"'); index++; } else quoted = !quoted; } else if (character == ',' && !quoted) { values.Add(current.ToString()); current.Clear(); } else current.Append(character); } values.Add(current.ToString()); return values; }
    private static string Csv(string? value) { var safe = value ?? string.Empty; if (safe.Length > 0 && "=+-@".Contains(safe[0])) safe = "'" + safe; return $"\"{safe.Replace("\"", "\"\"")}\""; }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Default(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    private sealed record ExportFilters(Guid? MicroregionId = null, string? Situation = null, DateTimeOffset? FromUtc = null, DateTimeOffset? ToUtc = null);
    private sealed record ExportProperty(HealthProperty Property, string MicroregionCode, PropertyVisit? LastVisit);
    private sealed record ImportRow(string MicroregionCode, string Street, string HouseNumber, string FamilyNumber, string? PostalCode, string? Complement, Geometry Geometry, string RegistrationStatus, string Situation);
    private sealed record StagedProperty(Guid MicroregionId, string Street, string HouseNumber, string FamilyNumber, string? PostalCode, string? Complement, Geometry Geometry, PropertyRegistrationStatus RegistrationStatus, PropertySituation Situation);
    private sealed class ImportValidationException(int errorCount, string message) : Exception(message) { public int ErrorCount { get; } = errorCount; }
}
