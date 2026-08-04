namespace Accounts.Services.Interfaces;

/// <summary>
/// Validates that organization resources remain within a tenant's Company/Group subtree.
/// </summary>
public interface IOrganizationScopeService
{
    Task<bool> IsWithinTenantSubtreeAsync(
        int tenantId,
        int organizationId,
        CancellationToken cancellationToken = default);

    Task<bool> IsWithinSubtreeAsync(
        int rootOrganizationId,
        int organizationId,
        CancellationToken cancellationToken = default);
}
