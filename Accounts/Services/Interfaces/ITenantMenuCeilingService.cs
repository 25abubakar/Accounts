namespace Accounts.Services.Interfaces;

/// <summary>
/// Resolves the maximum menu and CRUD capabilities granted by SuperAdmin to a tenant.
/// Every TenantAdmin operation and every staff delegation must remain inside this ceiling.
/// </summary>
public interface ITenantMenuCeilingService
{
    Task<IReadOnlySet<int>> GetAllowedPermissionIdsAsync(
        int tenantId,
        CancellationToken cancellationToken = default);

    Task<bool> AllowsFeatureAsync(
        int tenantId,
        string featureKey,
        CancellationToken cancellationToken = default);

    Task<bool> AllowsMenuAsync(
        int tenantId,
        int menuId,
        CancellationToken cancellationToken = default);

    Task<TenantDelegationValidation> ValidatePermissionIdsAsync(
        int tenantId,
        IEnumerable<int> permissionIds,
        CancellationToken cancellationToken = default);
}

public sealed record TenantDelegationValidation(
    bool IsValid,
    IReadOnlyList<int> InvalidPermissionIds);
