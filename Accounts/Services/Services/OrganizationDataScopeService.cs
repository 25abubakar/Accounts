using Accounts.Data;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class OrganizationDataScopeService(ApplicationDbContext db, ITenantService tenant)
    : IOrganizationDataScopeService
{
    public async Task<OrganizationDataScope> ResolveAsync(string identityUserId, CancellationToken cancellationToken = default)
    {
        if (!tenant.TenantId.HasValue || tenant.IsSuperAdmin)
            return Empty();

        var tenantId = tenant.TenantId.Value;
        var caller = await db.Persons.AsNoTracking()
            .Where(person => person.TenantId == tenantId && person.IdentityUserId == identityUserId && person.IsActive)
            .Select(person => new
            {
                person.PersonId,
                StaffId = person.Staff != null ? (Guid?)person.Staff.StaffId : null,
                OrganizationId = person.Staff != null && person.Staff.Vacancy != null
                    ? (int?)person.Staff.Vacancy.OrganizationId : null
            }).FirstOrDefaultAsync(cancellationToken);

        var tenantWide = tenant.IsTenantAdmin;
        int? rootId = tenantWide
            ? await db.Tenants.AsNoTracking().Where(item => item.Id == tenantId)
                .Select(item => (int?)item.OrganizationTreeId).FirstOrDefaultAsync(cancellationToken)
            : caller?.OrganizationId;
        if (!rootId.HasValue) return Empty();

        var nodes = await db.OrganizationTree.AsNoTracking()
            .Where(node => node.IsActive)
            .Select(node => new { node.Id, node.ParentId }).ToListAsync(cancellationToken);
        var organizationIds = new HashSet<int> { rootId.Value };
        var children = nodes.Where(node => node.ParentId.HasValue)
            .ToLookup(node => node.ParentId!.Value, node => node.Id);
        var queue = new Queue<int>(); queue.Enqueue(rootId.Value);
        while (queue.TryDequeue(out var parent))
            foreach (var child in children[parent])
                if (organizationIds.Add(child)) queue.Enqueue(child);

        var people = await db.Persons.AsNoTracking()
            .Where(person => person.TenantId == tenantId && person.IsActive &&
                (tenantWide || (person.Staff != null && person.Staff.Vacancy != null
                    && organizationIds.Contains(person.Staff.Vacancy.OrganizationId))))
            .Select(person => new { person.PersonId, StaffId = person.Staff != null ? (Guid?)person.Staff.StaffId : null })
            .ToListAsync(cancellationToken);

        return new OrganizationDataScope(tenantWide, rootId, organizationIds,
            people.Select(person => person.PersonId).ToHashSet(),
            people.Where(person => person.StaffId.HasValue).Select(person => person.StaffId!.Value).ToHashSet());
    }

    private static OrganizationDataScope Empty() => new(false, null,
        new HashSet<int>(), new HashSet<Guid>(), new HashSet<Guid>());
}
