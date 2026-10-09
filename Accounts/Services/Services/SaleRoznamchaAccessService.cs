using System.Security.Claims;
using Accounts.Services.Interfaces;

namespace Accounts.Services.Services;

/// <summary>
/// Keeps Sale Roznamcha authorization and its database-backed permission
/// resolution outside API controllers.
/// </summary>
public sealed class SaleRoznamchaAccessService(
    ITenantService tenant,
    TenantPermissionService tenantPermissions) : ISaleRoznamchaAccessService
{
    public async Task<bool> CanAsync(
        ClaimsPrincipal user,
        string route,
        string action,
        CancellationToken ct = default)
    {
        if (!tenant.TenantId.HasValue) return false;
        return await tenantPermissions.HasMenuRouteAsync(user, [route], action, ct);
    }
}
