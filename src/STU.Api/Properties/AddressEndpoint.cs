using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using STU.Application.Security;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.Api.Properties;

public static partial class PropertyEndpoints
{
    private static async Task<IResult> ReverseAddressAsync(Guid microregionId, double latitude, double longitude,
        HttpContext http, UserManager<ApplicationUser> users, StuDbContext db, IAddressLookup lookup)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180)
            return Validation("coordinates", "Informe coordenadas válidas.");
        var micro = await db.Microregions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == microregionId && x.ArchivedAtUtc == null);
        if (micro is null) return Results.NotFound();
        var actor = await ActorAsync(http, users);
        var scope = Scope(actor, http.User, micro.HealthUnitId);
        if (scope.Error is not null) return scope.Error;
        if (http.User.IsInRole(SystemRoles.HealthAgent) && micro.AssignedAgentId != actor.Id) return Results.Forbid();
        if (!micro.Boundary.Covers(new Point(longitude, latitude) { SRID = 4326 }))
            return Validation("coordinates", "Marque um ponto dentro da microrregião selecionada.");
        var result = await lookup.ReverseAsync(latitude, longitude, http.RequestAborted);
        if (result.Error == "busy")
        {
            http.Response.Headers.RetryAfter = "60";
            return Results.Problem(statusCode: 429, detail: "Consulta de endereço ocupada. Aguarde um momento ou preencha manualmente.");
        }
        if (result.Error is not null)
            return Results.Problem(statusCode: 503, detail: "Consulta de endereço indisponível. Você pode preencher os campos manualmente.");
        return Results.Ok(new { found = result.Address is not null, street = result.Address?.Street, postalCode = result.Address?.PostalCode });
    }
}
