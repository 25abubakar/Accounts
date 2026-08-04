using Accounts.Data;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

/// <summary>
/// Server-side source of truth for the SuperAdmin -> tenant delegation boundary.
/// </summary>
public sealed class TenantMenuCeilingService : ITenantMenuCeilingService
{
    private readonly ApplicationDbContext _db;

    public TenantMenuCeilingService(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlySet<int>> GetAllowedPermissionIdsAsync(
        int tenantId,
        CancellationToken cancellationToken = default)
    {
        var grants = await _db.TenantMenuPermissions
            .AsNoTracking()
            .Where(grant => grant.TenantId == tenantId && grant.IsAllow && grant.CanView)
            .Select(grant => new TenantMenuGrant(
                grant.MenuId,
                grant.CanView,
                grant.CanAdd,
                grant.CanEdit,
                grant.CanDelete))
            .ToListAsync(cancellationToken);

        if (grants.Count == 0)
            return new HashSet<int>();

        var grantByMenu = grants.ToDictionary(grant => grant.MenuId);
        var menuIds = grantByMenu.Keys.ToArray();
        var linkedFeatures = await _db.MenuPermissions
            .AsNoTracking()
            .Where(link => menuIds.Contains(link.MenuId) && link.Feature != null)
            .Select(link => new
            {
                link.MenuId,
                link.PermissionId,
                FeatureKey = link.Feature!.FeatureKey
            })
            .ToListAsync(cancellationToken);

        return linkedFeatures
            .Where(link => IsActionAllowed(grantByMenu[link.MenuId], link.FeatureKey))
            .Select(link => link.PermissionId)
            .ToHashSet();
    }

    public async Task<bool> AllowsFeatureAsync(
        int tenantId,
        string featureKey,
        CancellationToken cancellationToken = default)
    {
        if (tenantId <= 0 || string.IsNullOrWhiteSpace(featureKey))
            return false;

        var normalizedKey = featureKey.Trim();
        var matches = await (
            from link in _db.MenuPermissions.AsNoTracking()
            join feature in _db.Features.AsNoTracking()
                on link.PermissionId equals feature.PermissionId
            join grant in _db.TenantMenuPermissions.AsNoTracking()
                on link.MenuId equals grant.MenuId
            where grant.TenantId == tenantId
                  && grant.IsAllow
                  && feature.FeatureKey == normalizedKey
            select new TenantMenuGrant(
                grant.MenuId,
                grant.CanView,
                grant.CanAdd,
                grant.CanEdit,
                grant.CanDelete))
            .ToListAsync(cancellationToken);

        return matches.Any(grant => IsActionAllowed(grant, normalizedKey));
    }

    public Task<bool> AllowsMenuAsync(
        int tenantId,
        int menuId,
        CancellationToken cancellationToken = default) =>
        _db.TenantMenuPermissions
            .AsNoTracking()
            .AnyAsync(
                grant => grant.TenantId == tenantId
                         && grant.MenuId == menuId
                         && grant.IsAllow
                         && grant.CanView,
                cancellationToken);

    public async Task<TenantDelegationValidation> ValidatePermissionIdsAsync(
        int tenantId,
        IEnumerable<int> permissionIds,
        CancellationToken cancellationToken = default)
    {
        var requested = permissionIds.Distinct().ToArray();
        if (requested.Length == 0)
            return new TenantDelegationValidation(true, Array.Empty<int>());

        var allowed = await GetAllowedPermissionIdsAsync(tenantId, cancellationToken);
        var invalid = requested.Where(permissionId => !allowed.Contains(permissionId)).ToArray();
        return new TenantDelegationValidation(invalid.Length == 0, invalid);
    }

    private static bool IsActionAllowed(TenantMenuGrant grant, string featureKey)
    {
        if (!grant.CanView)
            return false;

        var action = ResolveAction(featureKey);
        return action switch
        {
            MenuAction.Add => grant.CanAdd,
            MenuAction.Edit => grant.CanEdit,
            MenuAction.Delete => grant.CanDelete,
            _ => grant.CanView
        };
    }

    private static MenuAction ResolveAction(string featureKey)
    {
        var key = featureKey.ToUpperInvariant();

        if (HasAnySuffix(key, "_DELETE", "_REMOVE", "_PURGE"))
            return MenuAction.Delete;

        if (HasAnySuffix(
                key,
                "_ADD",
                "_CREATE",
                "_REGISTER",
                "_HIRE",
                "_IMPORT",
                "_UPLOAD"))
            return MenuAction.Add;

        if (HasAnySuffix(
                key,
                "_EDIT",
                "_UPDATE",
                "_ASSIGN",
                "_MANAGE",
                "_APPROVE",
                "_REJECT",
                "_DECIDE",
                "_CONFIGURE",
                "_TOGGLE"))
            return MenuAction.Edit;

        return MenuAction.View;
    }

    private static bool HasAnySuffix(string value, params string[] suffixes) =>
        suffixes.Any(suffix => value.EndsWith(suffix, StringComparison.Ordinal));

    private sealed record TenantMenuGrant(
        int MenuId,
        bool CanView,
        bool CanAdd,
        bool CanEdit,
        bool CanDelete);

    private enum MenuAction
    {
        View,
        Add,
        Edit,
        Delete
    }
}
