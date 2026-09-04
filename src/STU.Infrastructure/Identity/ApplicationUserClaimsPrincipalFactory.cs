using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using STU.Application.Security;

namespace STU.Infrastructure.Identity;

public sealed class ApplicationUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>(
        userManager,
        roleManager,
        optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(
            StuClaimTypes.MustChangePassword,
            user.MustChangePassword ? "true" : "false"));

        if (user.HealthUnitId.HasValue)
        {
            identity.AddClaim(new Claim(
                StuClaimTypes.HealthUnitId,
                user.HealthUnitId.Value.ToString()));
        }

        return identity;
    }
}

