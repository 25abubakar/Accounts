using Accounts.Data;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Services.Services;

/// <summary>
/// Super Admin → Tenant Admin ceiling.
/// Tenant Admin never bypasses checks; they only receive menus/CRUD granted in
/// <c>TenantMenuPermissions</c>.
/// </summary>
public sealed class TenantPermissionService
{
    private readonly ApplicationDbContext _db;

    public TenantPermissionService(ApplicationDbContext db) => _db = db;

    public static bool IsSuperAdmin(ClaimsPrincipal user) =>
        user.IsInRole("SuperAdmin") ||
        string.Equals(user.FindFirstValue(ITenantService.ClaimIsSuperAdmin), "true", StringComparison.OrdinalIgnoreCase);

    public static bool IsTenantAdmin(ClaimsPrincipal user) =>
        user.IsInRole("TenantAdmin") ||
        string.Equals(user.FindFirstValue(ITenantService.ClaimIsTenantAdmin), "true", StringComparison.OrdinalIgnoreCase);

    public static int? GetTenantId(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ITenantService.ClaimTenantId), out var tenantId) && tenantId > 0
            ? tenantId
            : null;

    public async Task<bool> HasFeatureAsync(
        ClaimsPrincipal user,
        string featureKey,
        string? httpMethod = null,
        CancellationToken cancellationToken = default)
    {
        if (IsSuperAdmin(user)) return true;
        if (!IsTenantAdmin(user)) return false;

        var tenantId = GetTenantId(user);
        if (!tenantId.HasValue || string.IsNullOrWhiteSpace(featureKey)) return false;
        var capability = ResolveCapability(featureKey, httpMethod);

        // MENU_{id}[_ACTION] can be resolved directly from the tenant ceiling.
        if (TryParseMenuFeature(featureKey, out var menuId, out var menuCapability))
        {
            capability = menuCapability;
            return await _db.TenantMenuPermissions.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(grant =>
                    grant.TenantId == tenantId.Value &&
                    grant.MenuId == menuId &&
                    grant.IsAllow &&
                    (capability == TenantCapability.Add ? grant.CanAdd :
                     capability == TenantCapability.Edit ? grant.CanEdit :
                     capability == TenantCapability.Delete ? grant.CanDelete :
                     grant.CanView), cancellationToken);
        }

        // Semantic keys (PERSON_EDIT, etc.) are allowed only when mapped to a
        // menu that remains inside the Super Admin ceiling.
        var menuIds = await (
            from feature in _db.Features.AsNoTracking()
            join mapping in _db.MenuPermissions.AsNoTracking()
                on feature.PermissionId equals mapping.PermissionId
            where feature.FeatureKey == featureKey
            select mapping.MenuId).ToListAsync(cancellationToken);
        if (menuIds.Count == 0) return false;

        return await _db.TenantMenuPermissions.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(grant =>
                menuIds.Contains(grant.MenuId) &&
                grant.TenantId == tenantId.Value &&
                grant.IsAllow &&
                (capability == TenantCapability.Add ? grant.CanAdd :
                 capability == TenantCapability.Edit ? grant.CanEdit :
                 capability == TenantCapability.Delete ? grant.CanDelete :
                 grant.CanView), cancellationToken);
    }

    public async Task<bool> HasMenuRouteAsync(
        ClaimsPrincipal user,
        IEnumerable<string> routes,
        string action,
        CancellationToken cancellationToken = default)
    {
        if (IsSuperAdmin(user)) return true;
        if (!IsTenantAdmin(user)) return false;

        var tenantId = GetTenantId(user);
        var normalizedRoutes = routes
            .Where(route => !string.IsNullOrWhiteSpace(route))
            .Select(route => route.Trim().ToLower())
            .Distinct()
            .ToArray();
        if (!tenantId.HasValue || normalizedRoutes.Length == 0) return false;

        var capability = ResolveCapability(action, null);
        var routeSet = normalizedRoutes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var menuIds = await _db.Menus.AsNoTracking()
            .Where(menu => menu.Route != null && menu.IsActive)
            .Select(menu => new { menu.Id, Route = menu.Route!.ToLower() })
            .ToListAsync(cancellationToken);
        var matchingMenuIds = menuIds
            .Where(menu => routeSet.Contains(menu.Route))
            .Select(menu => menu.Id)
            .ToList();
        if (matchingMenuIds.Count == 0) return false;

        return await _db.TenantMenuPermissions.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(grant =>
                matchingMenuIds.Contains(grant.MenuId) &&
                grant.TenantId == tenantId.Value &&
                grant.IsAllow &&
                (capability == TenantCapability.Add ? grant.CanAdd :
                 capability == TenantCapability.Edit ? grant.CanEdit :
                 capability == TenantCapability.Delete ? grant.CanDelete :
                 grant.CanView), cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, TenantCeilingBits>> GetCeilingAsync(
        int tenantId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.TenantMenuPermissions.IgnoreQueryFilters().AsNoTracking()
            .Where(grant => grant.TenantId == tenantId && grant.IsAllow)
            .Select(grant => new { grant.MenuId, grant.CanView, grant.CanAdd, grant.CanEdit, grant.CanDelete })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            row => row.MenuId,
            row => new TenantCeilingBits(row.CanView, row.CanAdd, row.CanEdit, row.CanDelete));
    }

    public static bool TryParseMenuFeature(string featureKey, out int menuId, out TenantCapability capability)
    {
        menuId = 0;
        capability = TenantCapability.View;
        var parts = featureKey.Trim().Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 ||
            !parts[0].Equals("MENU", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(parts[1], out menuId))
            return false;

        if (parts.Length == 2)
        {
            capability = TenantCapability.View;
            return true;
        }

        capability = parts[2].ToUpperInvariant() switch
        {
            "VIEW" or "READ" => TenantCapability.View,
            "ADD" or "CREATE" or "ASSIGN" => TenantCapability.Add,
            "EDIT" or "UPDATE" => TenantCapability.Edit,
            "DELETE" => TenantCapability.Delete,
            _ => TenantCapability.View
        };
        return true;
    }

    public static TenantCapability ResolveCapability(string featureKey, string? httpMethod)
    {
        var action = featureKey.Trim();
        if (action.Equals("DELETE", StringComparison.OrdinalIgnoreCase) ||
            action.EndsWith("_DELETE", StringComparison.OrdinalIgnoreCase))
            return TenantCapability.Delete;
        if (action.Equals("EDIT", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("UPDATE", StringComparison.OrdinalIgnoreCase) ||
            action.EndsWith("_EDIT", StringComparison.OrdinalIgnoreCase) ||
            action.EndsWith("_UPDATE", StringComparison.OrdinalIgnoreCase))
            return TenantCapability.Edit;
        if (action.Equals("ADD", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("CREATE", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("ASSIGN", StringComparison.OrdinalIgnoreCase) ||
            action.EndsWith("_ADD", StringComparison.OrdinalIgnoreCase) ||
            action.EndsWith("_CREATE", StringComparison.OrdinalIgnoreCase) ||
            action.EndsWith("_ASSIGN", StringComparison.OrdinalIgnoreCase))
            return TenantCapability.Add;
        if (action.Equals("VIEW", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("READ", StringComparison.OrdinalIgnoreCase) ||
            action.EndsWith("_VIEW", StringComparison.OrdinalIgnoreCase) ||
            action.EndsWith("_READ", StringComparison.OrdinalIgnoreCase))
            return TenantCapability.View;
        return httpMethod?.ToUpperInvariant() switch
        {
            "POST" => TenantCapability.Add,
            "PUT" or "PATCH" => TenantCapability.Edit,
            "DELETE" => TenantCapability.Delete,
            _ => TenantCapability.View
        };
    }

    private static bool Matches(Models.TenantMenuPermission grant, TenantCapability capability) =>
        capability switch
        {
            TenantCapability.Add => grant.CanAdd,
            TenantCapability.Edit => grant.CanEdit,
            TenantCapability.Delete => grant.CanDelete,
            _ => grant.CanView
        };

    public enum TenantCapability { View, Add, Edit, Delete }

    public readonly record struct TenantCeilingBits(bool View, bool Add, bool Edit, bool Delete);
}
