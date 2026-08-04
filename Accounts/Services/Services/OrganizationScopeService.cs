using Accounts.Data;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class OrganizationScopeService : IOrganizationScopeService
{
    private readonly ApplicationDbContext _db;

    public OrganizationScopeService(ApplicationDbContext db) => _db = db;

    public async Task<bool> IsWithinTenantSubtreeAsync(
        int tenantId,
        int organizationId,
        CancellationToken cancellationToken = default)
    {
        var rootId = await _db.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Id == tenantId && tenant.IsActive)
            .Select(tenant => (int?)tenant.OrganizationTreeId)
            .FirstOrDefaultAsync(cancellationToken);

        return rootId.HasValue &&
               await IsWithinSubtreeAsync(rootId.Value, organizationId, cancellationToken);
    }

    public async Task<bool> IsWithinSubtreeAsync(
        int rootOrganizationId,
        int organizationId,
        CancellationToken cancellationToken = default)
    {
        if (rootOrganizationId <= 0 || organizationId <= 0)
            return false;
        if (rootOrganizationId == organizationId)
            return true;

        var parents = await _db.OrganizationTree
            .AsNoTracking()
            .Select(node => new { node.Id, node.ParentId })
            .ToDictionaryAsync(node => node.Id, node => node.ParentId, cancellationToken);

        var current = organizationId;
        var visited = new HashSet<int>();
        while (visited.Add(current) && parents.TryGetValue(current, out var parentId) && parentId.HasValue)
        {
            if (parentId.Value == rootOrganizationId)
                return true;
            current = parentId.Value;
        }

        return false;
    }
}
