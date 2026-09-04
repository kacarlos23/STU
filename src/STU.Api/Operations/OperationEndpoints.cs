using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using STU.Application.Security;
using STU.Domain.Auditing;
using STU.Domain.Operations;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.Api.Operations;

public static class OperationEndpoints
{
    private const long MaxUploadBytes = 20 * 1024 * 1024;
    private static readonly string[] AllowedImportExtensions = [".csv", ".json", ".geojson"];

    public static IEndpointRouteBuilder MapOperationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var jobs = endpoints.MapGroup("/api/operations").WithTags("Operational workflows").RequireAuthorization(StuPolicies.OperationalWorkflows).RequireRateLimiting("api");
        jobs.MapGet("/jobs", GetJobsAsync);
        jobs.MapGet("/jobs/{id:guid}/download", DownloadAsync).RequireAuthorization(StuPolicies.ReportsExport);
        Secure(jobs.MapPost("/jobs/{id:guid}/retry", RetryAsync));
        Secure(jobs.MapPost("/imports/{id:guid}/approve", ApproveImportAsync)).RequireAuthorization(StuPolicies.TerritoryManage);
        Secure(jobs.MapPost("/exports", CreateExportAsync)).RequireAuthorization(StuPolicies.ReportsExport);

        var imports = endpoints.MapGroup("/api/operations/imports").WithTags("Operational workflows").RequireAuthorization(StuPolicies.TerritoryManage).RequireRateLimiting("api");
        Secure(imports.MapPost("", CreateImportAsync));

        var notifications = endpoints.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization(StuPolicies.PasswordChanged).RequireRateLimiting("api");
        notifications.MapGet("", GetNotificationsAsync);
        Secure(notifications.MapPost("/{id:guid}/read", MarkNotificationReadAsync));
        Secure(notifications.MapPost("/read-all", MarkAllNotificationsReadAsync));

        endpoints.MapGet("/api/operations/indicators", GetIndicatorsAsync).WithTags("Operational workflows").RequireAuthorization(StuPolicies.OperationalWorkflows).RequireRateLimiting("api");
        return endpoints;
    }

    private static RouteHandlerBuilder Secure(RouteHandlerBuilder route) => route.WithMetadata(new RequireAntiforgeryTokenAttribute(true));

    private static async Task<IResult> GetJobsAsync(Guid? healthUnitId, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var actor = await ActorAsync(http, users); var scope = Scope(actor, http.User, healthUnitId); if (scope.Error is not null) return scope.Error;
        var jobs = await (from job in db.OperationJobs.AsNoTracking() join user in db.Users.AsNoTracking() on job.CreatedByUserId equals user.Id
            where job.HealthUnitId == scope.UnitId orderby job.CreatedAtUtc descending
            select new { job.Id, job.Kind, job.Status, job.Format, job.OriginalFileName, job.RecordCount, job.ValidationErrorCount, job.ErrorSummary, job.AttemptCount, job.ProgressPercentage, job.CreatedAtUtc, job.StartedAtUtc, job.ApprovedAtUtc, job.CompletedAtUtc, job.ConcurrencyToken, createdByName = user.DisplayName, canDownload = job.Status == OperationJobStatus.Completed && job.ResultFileName != null }).Take(100).ToListAsync();
        return Results.Ok(jobs);
    }

    private static async Task<IResult> CreateExportAsync(CreateExportRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var actor = await ActorAsync(http, users); var scope = Scope(actor, http.User, request.HealthUnitId); if (scope.Error is not null) return scope.Error;
        if (!Enum.TryParse<OperationFileFormat>(request.Format, true, out var format)) return Validation("format", "Use Csv, GeoJson, Kml ou GeoPackage.");
        if (request.MicroregionId.HasValue && !await db.Microregions.AnyAsync(item => item.Id == request.MicroregionId && item.HealthUnitId == scope.UnitId && item.ArchivedAtUtc == null)) return Validation("microregionId", "Selecione uma microrregião ativa da UBS.");
        var parameters = JsonSerializer.Serialize(new { request.MicroregionId, request.Situation, request.FromUtc, request.ToUtc });
        var job = OperationJob.CreateExport(scope.UnitId, actor.Id, format, parameters); db.OperationJobs.Add(job);
        Audit(db, http, actor, "Create", "OperationJob", job.Id, $"Exportação {format} solicitada."); await db.SaveChangesAsync();
        return Results.Accepted($"/api/operations/jobs/{job.Id}", new { job.Id, job.Status });
    }

    private static async Task<IResult> CreateImportAsync(HttpRequest request, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db, IConfiguration configuration)
    {
        if (!request.HasFormContentType) return Validation("file", "Envie o arquivo em formulário multipart.");
        var form = await request.ReadFormAsync(); var file = form.Files.GetFile("file"); if (file is null || file.Length == 0) return Validation("file", "Selecione um arquivo CSV ou GeoJSON.");
        if (file.Length > MaxUploadBytes) return Results.Problem(statusCode: 413, title: "Arquivo muito grande", detail: "O limite por importação é 20 MB.");
        var extension = Path.GetExtension(Path.GetFileName(file.FileName)).ToLowerInvariant(); if (!AllowedImportExtensions.Contains(extension)) return Validation("file", "Use um arquivo .csv, .json ou .geojson.");
        if (!Guid.TryParse(form["healthUnitId"].FirstOrDefault(), out var requestedUnitId)) requestedUnitId = Guid.Empty;
        var actor = await ActorAsync(http, users); var scope = Scope(actor, http.User, requestedUnitId == Guid.Empty ? null : requestedUnitId); if (scope.Error is not null) return scope.Error;
        var storage = EnsureStorage(configuration); var storedName = $"{Guid.NewGuid():N}{extension}"; var fullPath = SafePath(storage, storedName);
        await using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await file.CopyToAsync(stream);
        var format = extension == ".csv" ? OperationFileFormat.Csv : OperationFileFormat.GeoJson;
        var original = Path.GetFileName(file.FileName); var job = OperationJob.CreateImport(scope.UnitId, actor.Id, format, storedName, original); db.OperationJobs.Add(job);
        Audit(db, http, actor, "Create", "OperationJob", job.Id, $"Importação preparada a partir de {original}."); await db.SaveChangesAsync();
        return Results.Accepted($"/api/operations/jobs/{job.Id}", new { job.Id, job.Status });
    }

    private static async Task<IResult> ApproveImportAsync(Guid id, Guid? healthUnitId, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var job = await db.OperationJobs.SingleOrDefaultAsync(item => item.Id == id && item.Kind == OperationJobKind.PropertyImport); if (job is null) return Results.NotFound();
        var actor = await ActorAsync(http, users); var scope = Scope(actor, http.User, healthUnitId ?? job.HealthUnitId); if (scope.Error is not null || scope.UnitId != job.HealthUnitId) return Results.Forbid();
        try { job.Approve(actor.Id); } catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
        Audit(db, http, actor, "Approve", "OperationJob", job.Id, $"Importação de {job.RecordCount} imóveis aprovada."); await db.SaveChangesAsync(); return Results.Accepted();
    }

    private static async Task<IResult> RetryAsync(Guid id, Guid? healthUnitId, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var job = await db.OperationJobs.SingleOrDefaultAsync(item => item.Id == id); if (job is null) return Results.NotFound(); var actor = await ActorAsync(http, users);
        var requiredPermission = job.Kind == OperationJobKind.PropertyImport ? StuPermissions.TerritoryManage : StuPermissions.ReportsExport;
        if (!HasPermission(http.User, requiredPermission)) return Results.Forbid();
        var scope = Scope(actor, http.User, healthUnitId ?? job.HealthUnitId); if (scope.Error is not null || scope.UnitId != job.HealthUnitId) return Results.Forbid();
        try { job.Retry(); } catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
        Audit(db, http, actor, "Retry", "OperationJob", job.Id, "Trabalho colocado novamente na fila."); await db.SaveChangesAsync(); return Results.Accepted();
    }

    private static async Task<IResult> DownloadAsync(Guid id, Guid? healthUnitId, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db, IConfiguration configuration)
    {
        var job = await db.OperationJobs.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id); if (job is null || job.Status != OperationJobStatus.Completed || job.ResultFileName is null) return Results.NotFound();
        var actor = await ActorAsync(http, users); var scope = Scope(actor, http.User, healthUnitId ?? job.HealthUnitId); if (scope.Error is not null || scope.UnitId != job.HealthUnitId) return Results.Forbid();
        var path = SafePath(EnsureStorage(configuration), job.ResultFileName); if (!File.Exists(path)) return Results.NotFound();
        return Results.File(path, ContentType(job.Format), $"stu-imoveis-{DateTime.UtcNow:yyyyMMdd}.{Extension(job.Format)}", enableRangeProcessing: true);
    }

    private static async Task<IResult> GetNotificationsAsync(bool unreadOnly, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var actor = await ActorAsync(http, users); var query = db.UserNotifications.AsNoTracking().Where(item => item.UserId == actor.Id); if (unreadOnly) query = query.Where(item => item.ReadAtUtc == null);
        var items = await query.OrderByDescending(item => item.CreatedAtUtc).Take(50).Select(item => new { item.Id, item.Kind, item.Title, item.Message, item.Link, item.ReadAtUtc, item.CreatedAtUtc }).ToListAsync();
        return Results.Ok(new { items, unreadCount = await db.UserNotifications.CountAsync(item => item.UserId == actor.Id && item.ReadAtUtc == null) });
    }

    private static async Task<IResult> MarkNotificationReadAsync(Guid id, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var actor = await ActorAsync(http, users); var item = await db.UserNotifications.SingleOrDefaultAsync(value => value.Id == id && value.UserId == actor.Id); if (item is null) return Results.NotFound(); item.MarkRead(); await db.SaveChangesAsync(); return Results.NoContent();
    }

    private static async Task<IResult> MarkAllNotificationsReadAsync(HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var actor = await ActorAsync(http, users); var items = await db.UserNotifications.Where(item => item.UserId == actor.Id && item.ReadAtUtc == null).ToListAsync(); foreach (var item in items) item.MarkRead(); await db.SaveChangesAsync(); return Results.NoContent();
    }

    private static async Task<IResult> GetIndicatorsAsync(Guid? healthUnitId, HttpContext http, UserManager<ApplicationUser> users, StuDbContext db)
    {
        var actor = await ActorAsync(http, users); var scope = Scope(actor, http.User, healthUnitId); if (scope.Error is not null) return scope.Error;
        var activeProperties = await db.Properties.CountAsync(item => item.HealthUnitId == scope.UnitId && item.ArchivedAtUtc == null);
        var drafts = await db.Properties.CountAsync(item => item.HealthUnitId == scope.UnitId && item.ArchivedAtUtc == null && item.RegistrationStatus == STU.Domain.Properties.PropertyRegistrationStatus.Draft);
        var unassigned = await db.Microregions.CountAsync(item => item.HealthUnitId == scope.UnitId && item.ArchivedAtUtc == null && item.AssignedAgentId == null);
        var pendingJobs = await db.OperationJobs.CountAsync(item => item.HealthUnitId == scope.UnitId && (item.Status == OperationJobStatus.Pending || item.Status == OperationJobStatus.Processing || item.Status == OperationJobStatus.AwaitingApproval));
        return Results.Ok(new { activeProperties, drafts, unassignedMicroregions = unassigned, pendingJobs });
    }

    private static string EnsureStorage(IConfiguration configuration) { var root = Path.GetFullPath(configuration["Operations:StoragePath"] ?? Path.Combine(AppContext.BaseDirectory, "operations-data")); Directory.CreateDirectory(root); return root; }
    private static string SafePath(string root, string fileName) { var name = Path.GetFileName(fileName); var path = Path.GetFullPath(Path.Combine(root, name)); if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Caminho de arquivo inválido."); return path; }
    private static string ContentType(OperationFileFormat format) => format switch { OperationFileFormat.Csv => "text/csv; charset=utf-8", OperationFileFormat.GeoJson => "application/geo+json", OperationFileFormat.Kml => "application/vnd.google-earth.kml+xml", _ => "application/geopackage+sqlite3" };
    private static string Extension(OperationFileFormat format) => format switch { OperationFileFormat.Csv => "csv", OperationFileFormat.GeoJson => "geojson", OperationFileFormat.Kml => "kml", _ => "gpkg" };
    private static ScopeResult Scope(ApplicationUser actor, System.Security.Claims.ClaimsPrincipal principal, Guid? requested) { if (principal.IsInRole(SystemRoles.GlobalAdministrator)) return requested.HasValue ? new(null, requested.Value) : new(Validation("healthUnitId", "Selecione uma UBS."), Guid.Empty); if (!actor.HealthUnitId.HasValue) return new(Results.Forbid(), Guid.Empty); if (requested.HasValue && requested != actor.HealthUnitId) return new(Results.Forbid(), Guid.Empty); return new(null, actor.HealthUnitId.Value); }
    private static bool HasPermission(System.Security.Claims.ClaimsPrincipal principal, string permission) => principal.IsInRole(SystemRoles.GlobalAdministrator) || principal.Claims.Any(claim => claim.Type == StuClaimTypes.Permission && (claim.Value == StuPermissions.All || claim.Value == permission));
    private static async Task<ApplicationUser> ActorAsync(HttpContext http, UserManager<ApplicationUser> users) => await users.GetUserAsync(http.User) ?? throw new InvalidOperationException("Usuário autenticado não encontrado.");
    private static void Audit(StuDbContext db, HttpContext http, ApplicationUser actor, string action, string entity, Guid id, string summary) => db.AuditEntries.Add(AuditEntry.Create(actor.Id, actor.UserName ?? actor.DisplayName, action, entity, id.ToString(), summary, null, null, http.Connection.RemoteIpAddress?.ToString()));
    private static IResult Validation(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { { field, [message] } });
    private sealed record ScopeResult(IResult? Error, Guid UnitId);
}
