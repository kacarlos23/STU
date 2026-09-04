using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using STU.Application.Security;
using STU.Domain.Auditing;
using STU.Domain.Operations;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.Api.Backups;

public static class BackupEndpoints
{
    public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin/backups")
            .WithTags("Backups")
            .RequireAuthorization(StuPolicies.GlobalAdministration)
            .RequireRateLimiting("api");

        group.MapGet("/settings", GetSettingsAsync);
        RequireAntiforgery(group.MapPut("/settings", UpdateSettingsAsync));
        group.MapGet("/runs", GetRunsAsync);
        RequireAntiforgery(group.MapPost("/runs", RequestManualBackupAsync));
        group.MapGet("/runs/{id:guid}/download", DownloadAsync);
        return endpoints;
    }

    private static RouteHandlerBuilder RequireAntiforgery(RouteHandlerBuilder builder) =>
        builder.WithMetadata(new RequireAntiforgeryTokenAttribute(true));

    private static async Task<IResult> GetSettingsAsync(StuDbContext dbContext)
    {
        var settings = await GetOrCreateSettingsAsync(dbContext);
        return Results.Ok(ToSettingsResponse(settings));
    }

    private static async Task<IResult> UpdateSettingsAsync(
        UpdateBackupSettingsRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        StuDbContext dbContext)
    {
        if (request.LocalHour is < 0 or > 23)
        {
            return Validation("localHour", "A hora deve estar entre 0 e 23.");
        }

        if (request.RetentionCount is < 1 or > 52)
        {
            return Validation("retentionCount", "A retenção deve estar entre 1 e 52 cópias.");
        }

        var actor = await GetActorAsync(context, userManager);
        var settings = await GetOrCreateSettingsAsync(dbContext);
        var before = Snapshot(settings);
        settings.Update(request.Enabled, request.DayOfWeek, request.LocalHour, request.RetentionCount);
        var after = Snapshot(settings);
        dbContext.AuditEntries.Add(AuditEntry.Create(
            actor.Id,
            actor.UserName ?? actor.DisplayName,
            "Update",
            "BackupSettings",
            settings.Id.ToString(),
            "Rotina semanal de backups atualizada.",
            before,
            after,
            context.Connection.RemoteIpAddress?.ToString()));
        await dbContext.SaveChangesAsync();
        return Results.Ok(ToSettingsResponse(settings));
    }

    private static async Task<IResult> GetRunsAsync(StuDbContext dbContext, int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 100);
        var runs = await dbContext.BackupRuns
            .AsNoTracking()
            .OrderByDescending(item => item.RequestedAtUtc)
            .Take(limit)
            .Select(item => new
            {
                item.Id,
                item.Trigger,
                item.Status,
                item.RequestedAtUtc,
                item.StartedAtUtc,
                item.CompletedAtUtc,
                item.FileName,
                item.SizeBytes,
                item.Sha256,
                item.ErrorSummary,
                item.FilePrunedAtUtc,
                item.AttemptCount,
                canDownload = item.Status == BackupRunStatus.Completed && item.FileName != null && item.FilePrunedAtUtc == null,
            })
            .ToListAsync();
        return Results.Ok(runs);
    }

    private static async Task<IResult> RequestManualBackupAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        StuDbContext dbContext)
    {
        var actor = await GetActorAsync(context, userManager);
        var run = BackupRun.CreateManual(actor.Id);
        dbContext.BackupRuns.Add(run);
        dbContext.AuditEntries.Add(AuditEntry.Create(
            actor.Id,
            actor.UserName ?? actor.DisplayName,
            "Request",
            "BackupRun",
            run.Id.ToString(),
            "Backup manual solicitado.",
            null,
            JsonSerializer.Serialize(new { run.Id, run.Trigger, run.Status, run.RequestedAtUtc }),
            context.Connection.RemoteIpAddress?.ToString()));
        await dbContext.SaveChangesAsync();
        return Results.Accepted($"/api/admin/backups/runs/{run.Id}", new
        {
            run.Id,
            run.Trigger,
            run.Status,
            run.RequestedAtUtc,
        });
    }

    private static async Task<IResult> DownloadAsync(
        Guid id,
        StuDbContext dbContext,
        IConfiguration configuration)
    {
        var run = await dbContext.BackupRuns.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id);
        if (run is null) return Results.NotFound();
        if (run.Status != BackupRunStatus.Completed || run.FileName is null || run.FilePrunedAtUtc.HasValue)
        {
            return Results.Conflict(new { detail = "O arquivo deste backup não está disponível." });
        }

        var fileName = Path.GetFileName(run.FileName);
        if (!string.Equals(fileName, run.FileName, StringComparison.Ordinal))
        {
            return Results.Problem("Referência de arquivo inválida.", statusCode: StatusCodes.Status500InternalServerError);
        }

        var backupRoot = BackupStorage.GetRoot(configuration);
        var path = Path.GetFullPath(Path.Combine(backupRoot, fileName));
        if (!path.StartsWith(backupRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(path))
        {
            return Results.NotFound(new { detail = "O arquivo do backup não foi encontrado no armazenamento." });
        }

        return Results.File(path, "application/octet-stream", fileName, enableRangeProcessing: true);
    }

    private static async Task<BackupSettings> GetOrCreateSettingsAsync(StuDbContext dbContext)
    {
        var settings = await dbContext.BackupSettings.SingleOrDefaultAsync();
        if (settings is not null) return settings;
        settings = BackupSettings.CreateDefault();
        dbContext.BackupSettings.Add(settings);
        await dbContext.SaveChangesAsync();
        return settings;
    }

    private static object ToSettingsResponse(BackupSettings settings) => new
    {
        settings.Enabled,
        settings.DayOfWeek,
        settings.LocalHour,
        settings.RetentionCount,
        settings.TimeZoneId,
        settings.UpdatedAtUtc,
    };

    private static string Snapshot(BackupSettings settings) => JsonSerializer.Serialize(ToSettingsResponse(settings));

    private static async Task<ApplicationUser> GetActorAsync(HttpContext context, UserManager<ApplicationUser> userManager) =>
        await userManager.GetUserAsync(context.User)
        ?? throw new InvalidOperationException("Authenticated administrator was not found.");

    private static IResult Validation(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}

public static class BackupStorage
{
    public static string GetRoot(IConfiguration configuration)
    {
        var operationsRoot = configuration["Operations:StoragePath"];
        if (string.IsNullOrWhiteSpace(operationsRoot))
        {
            operationsRoot = Path.Combine(Path.GetTempPath(), "stu-operations");
        }

        var root = Path.GetFullPath(Path.Combine(operationsRoot, "backups"));
        Directory.CreateDirectory(root);
        return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
