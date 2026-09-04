using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Scalar.AspNetCore;
using STU.Api.Authentication;
using STU.Api.Administration;
using STU.Api.Dashboard;
using STU.Api.Territories;
using STU.Api.Properties;
using STU.Api.Errors;
using STU.Api.Operations;
using STU.Api.Backups;
using STU.Api.Monitoring;
using STU.Api.Onboarding;
using STU.Api.PilotRelease;
using STU.Api.HealthUnitUsers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NetTopologySuite.IO.Converters;
using STU.Application;
using STU.Application.Security;
using STU.Infrastructure;
using STU.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
builder.Services.AddExceptionHandler<DatabaseConflictExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseReadinessCheck>("database", failureStatus: HealthStatus.Unhealthy, tags: ["ready"])
    .AddCheck<StorageReadinessCheck>("operations-storage", failureStatus: HealthStatus.Unhealthy, tags: ["ready"])
    .AddCheck<WorkerReadinessCheck>("worker", failureStatus: HealthStatus.Unhealthy, tags: ["ready"]);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new GeoJsonConverterFactory());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddIdentityInfrastructure();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-STU-CSRF";
    options.Cookie.Name = "stu.csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(StuPolicies.PasswordChanged, policy =>
        policy.RequireAuthenticatedUser()
            .RequireClaim(StuClaimTypes.MustChangePassword, "false"));
    options.AddPolicy(StuPolicies.GlobalAdministration, policy =>
        policy.RequireAuthenticatedUser()
            .RequireRole(SystemRoles.GlobalAdministrator)
            .RequireClaim(StuClaimTypes.MustChangePassword, "false"));
    options.AddPolicy(StuPolicies.MapView, policy =>
        policy.RequireAuthenticatedUser()
            .RequireClaim(StuClaimTypes.MustChangePassword, "false")
            .RequireAssertion(context => HasPermission(context.User, StuPermissions.MapView)));
    options.AddPolicy(StuPolicies.TerritoryManage, policy =>
        policy.RequireAuthenticatedUser()
            .RequireClaim(StuClaimTypes.MustChangePassword, "false")
            .RequireAssertion(context => HasPermission(context.User, StuPermissions.TerritoryManage)));
    options.AddPolicy(StuPolicies.PropertiesView, policy => PermissionPolicy(policy, StuPermissions.PropertiesView));
    options.AddPolicy(StuPolicies.PropertiesManage, policy => PermissionPolicy(policy, StuPermissions.PropertiesManage));
    options.AddPolicy(StuPolicies.VisitsView, policy => PermissionPolicy(policy, StuPermissions.VisitsView));
    options.AddPolicy(StuPolicies.VisitsManage, policy => PermissionPolicy(policy, StuPermissions.VisitsManage));
    options.AddPolicy(StuPolicies.ReportsExport, policy => PermissionPolicy(policy, StuPermissions.ReportsExport));
    options.AddPolicy(StuPolicies.HealthUnitUsersManage, policy => PermissionPolicy(policy, StuPermissions.HealthUnitUsersManage));
    options.AddPolicy(StuPolicies.OperationalWorkflows, policy =>
        policy.RequireAuthenticatedUser()
            .RequireClaim(StuClaimTypes.MustChangePassword, "false")
            .RequireAssertion(context =>
                HasPermission(context.User, StuPermissions.ReportsExport) ||
                HasPermission(context.User, StuPermissions.TerritoryManage)));
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "stu.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("WebClients", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

builder.Services.AddRateLimiter(options =>
{
    var apiPermitLimit = builder.Configuration.GetValue("RateLimiting:ApiPermitLimit", 120);
    var loginPermitLimit = builder.Configuration.GetValue("RateLimiting:LoginPermitLimit", 5);
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("api", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = apiPermitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1),
            }));
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = loginPermitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(5),
            }));
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await DatabaseInitializer.InitializeAsync(app.Services, builder.Configuration);
}

if (args.Contains("--bootstrap-only", StringComparer.OrdinalIgnoreCase))
{
    return;
}

app.UseForwardedHeaders();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference("/docs");
}
else
{
    app.UseHsts();
}

if (builder.Configuration.GetValue("Https:EnableRedirection", true))
{
    app.UseHttpsRedirection();
}
app.UseCors("WebClients");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.Use(async (context, next) =>
{
    var requirement = context.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Antiforgery.IAntiforgeryMetadata>();
    var validation = context.Features.Get<Microsoft.AspNetCore.Antiforgery.IAntiforgeryValidationFeature>();
    if (requirement?.RequiresValidation == true && validation?.IsValid == false)
    {
        await Results.Problem(
                title: "ValidaÃ§Ã£o de seguranÃ§a necessÃ¡ria",
                detail: "Atualize a pÃ¡gina e tente novamente.",
                statusCode: StatusCodes.Status400BadRequest)
            .ExecuteAsync(context);
        return;
    }

    await next(context);
});

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
});

app.MapGet("/api/system/info", () => Results.Ok(new
    {
        name = "STU",
        description = "Sistema Territorial das UBS",
        status = "operations-hardening",
    }))
    .WithName("GetSystemInfo")
    .RequireRateLimiting("api");

app.MapAuthEndpoints();
app.MapDashboardEndpoints();
app.MapAdministrationEndpoints();
app.MapTerritoryEndpoints();
app.MapPropertyEndpoints();
app.MapOperationEndpoints();
app.MapBackupEndpoints();
app.MapMonitoringEndpoints();
app.MapOnboardingEndpoints();
app.MapPilotReleaseEndpoints();
app.MapHealthUnitUserEndpoints();

static bool HasPermission(System.Security.Claims.ClaimsPrincipal user, string permission) =>
    user.Claims.Any(claim => claim.Type == StuClaimTypes.Permission &&
        (claim.Value == permission || claim.Value == StuPermissions.All));

static void PermissionPolicy(Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder policy, string permission) =>
    policy.RequireAuthenticatedUser()
        .RequireClaim(StuClaimTypes.MustChangePassword, "false")
        .RequireAssertion(context => HasPermission(context.User, permission));

app.Run();

public partial class Program;
