using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

namespace Accounts.Authorization;

/// <summary>
/// A tenant employee whose synchronized Identity role is CEO receives the
/// existing Admin authorization role for the lifetime of the current request.
/// Tenant and Super Admin claims are not changed, so all EF tenant filters stay
/// active and the CEO cannot enter platform-wide Super Admin scope.
/// </summary>
public sealed class OrganizationCeoClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated == true &&
            principal.IsInRole("CEO") &&
            !principal.IsInRole("Admin") &&
            principal.Identity is ClaimsIdentity identity)
        {
            identity.AddClaim(new Claim(identity.RoleClaimType, "Admin"));
        }

        return Task.FromResult(principal);
    }
}
