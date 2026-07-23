using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

namespace Accounts.Authorization;

/// <summary>
/// Legacy no-op kept so old DI registrations remain safe.
/// CEO is a job title only; it must not grant authorization privileges.
/// </summary>
public sealed class OrganizationCeoClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        return Task.FromResult(principal);
    }
}
