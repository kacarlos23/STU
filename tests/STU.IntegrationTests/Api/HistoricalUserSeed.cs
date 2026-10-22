using Microsoft.EntityFrameworkCore;
using STU.Infrastructure.Identity;
using STU.Infrastructure.Persistence;

namespace STU.IntegrationTests.Api;

internal static class HistoricalUserSeed
{
    // Migration fixtures must insert the historical shape, before newer profile columns exist.
    public static Task InsertAsync(StuDbContext db, ApplicationUser user)
    {
        if (user.Id == Guid.Empty) user.Id = Guid.NewGuid();
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AspNetUsers" ("Id", "UserName", "DisplayName", "HealthUnitId", "MustChangePassword",
                "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES ({user.Id}, {user.UserName}, {user.DisplayName}, {user.HealthUnitId}, {user.MustChangePassword},
                false, false, false, true, 0)
            """);
    }
}
