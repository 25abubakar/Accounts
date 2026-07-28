using Accounts.Data;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    public sealed class AccountScopeAccessService : IAccountScopeAccessService
    {
        private readonly ApplicationDbContext _db;

        public AccountScopeAccessService(ApplicationDbContext db) => _db = db;

        public async Task<AccountScopeAccessResult> ValidateAsync(
            string userId, CancellationToken cancellationToken = default)
        {
            var user = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new
                {
                    u.IsSuperAdmin,
                    u.IsTenantAdmin,
                    u.TenantId,
                    u.LockoutEnabled,
                    u.LockoutEnd,
                    PersonIsActive = _db.Persons.IgnoreQueryFilters()
                        .Where(p => p.IdentityUserId == u.Id)
                        .Select(p => (bool?)p.IsActive)
                        .FirstOrDefault(),
                    EmployeeOrganizationId = _db.Persons.IgnoreQueryFilters()
                        .Where(p => p.IdentityUserId == u.Id)
                        .Select(p => p.Staff != null && p.Staff.Vacancy != null
                            ? (int?)p.Staff.Vacancy.OrganizationId : null)
                        .FirstOrDefault(),
                    TenantExists = u.TenantId.HasValue && _db.Tenants
                        .Any(t => t.Id == u.TenantId.Value),
                    TenantIsActive = u.TenantId.HasValue
                        ? _db.Tenants.Where(t => t.Id == u.TenantId.Value)
                            .Select(t => (bool?)t.IsActive).FirstOrDefault()
                        : null,
                    TenantOrganizationTreeId = u.TenantId.HasValue
                        ? _db.Tenants.Where(t => t.Id == u.TenantId.Value)
                            .Select(t => (int?)t.OrganizationTreeId).FirstOrDefault()
                        : null
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (user == null)
                return AccountScopeAccessResult.Denied("This account no longer exists.");

            if (user.IsSuperAdmin)
                return AccountScopeAccessResult.Allowed();

            if (!user.IsTenantAdmin && user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow)
                return AccountScopeAccessResult.Denied("This account is disabled or locked.");

            if (!user.IsTenantAdmin && user.PersonIsActive == false)
                return AccountScopeAccessResult.Denied("Your staff account is inactive. Contact your administrator.");

            if (!user.TenantId.HasValue)
                return AccountScopeAccessResult.Denied("This account is not assigned to an active tenant.");

            if (!user.TenantExists || user.TenantIsActive != true ||
                !user.TenantOrganizationTreeId.HasValue)
                return AccountScopeAccessResult.Denied("Your tenant is currently disabled. Contact your administrator.");

            // Load only the two relevant ancestor chains on SQL Server. The
            // previous request middleware copied the complete organization tree
            // for every API call, which became progressively slower as tenants
            // and departments were added.
            var nodes = _db.Database.IsSqlServer()
                ? await LoadRelevantSqlServerNodesAsync(
                    user.TenantOrganizationTreeId.Value,
                    user.EmployeeOrganizationId,
                    cancellationToken)
                : await _db.OrganizationTree.AsNoTracking()
                    .Select(n => new OrganizationAccessNode
                    {
                        Id = n.Id,
                        ParentId = n.ParentId,
                        IsActive = n.IsActive,
                        Name = n.Name,
                        Label = n.Label
                    })
                    .ToListAsync(cancellationToken);
            var byId = nodes.ToDictionary(n => n.Id);
            var visited = new HashSet<int>();
            var currentId = user.TenantOrganizationTreeId;

            while (currentId.HasValue && byId.TryGetValue(currentId.Value, out var node) && visited.Add(node.Id))
            {
                if (!node.IsActive)
                    return AccountScopeAccessResult.Denied(
                        $"Access is disabled for {node.Name} ({node.Label}). Contact your administrator.");
                currentId = node.ParentId;
            }

            // Staff can sit below the tenant's Company node (Department/Branch/Team).
            // Validate that complete assignment chain as well so department switches
            // revoke both current sessions and future logins.
            currentId = user.EmployeeOrganizationId;
            visited.Clear();
            while (currentId.HasValue && byId.TryGetValue(currentId.Value, out var employeeNode) && visited.Add(employeeNode.Id))
            {
                if (!employeeNode.IsActive)
                    return AccountScopeAccessResult.Denied(
                        $"Access is disabled for {employeeNode.Name} ({employeeNode.Label}). Contact your administrator.");
                currentId = employeeNode.ParentId;
            }

            return AccountScopeAccessResult.Allowed();
        }

        private async Task<List<OrganizationAccessNode>> LoadRelevantSqlServerNodesAsync(
            int tenantOrganizationId,
            int? employeeOrganizationId,
            CancellationToken cancellationToken)
        {
            return await _db.Database.SqlQuery<OrganizationAccessNode>(
                $"""
                WITH Ancestors AS
                (
                    SELECT node.Id, node.ParentId, node.IsActive, node.Name, node.Label
                    FROM dbo.OrganizationTree AS node
                    WHERE node.Id = {tenantOrganizationId}
                       OR node.Id = {employeeOrganizationId}

                    UNION ALL

                    SELECT parent.Id, parent.ParentId, parent.IsActive, parent.Name, parent.Label
                    FROM dbo.OrganizationTree AS parent
                    INNER JOIN Ancestors AS child ON child.ParentId = parent.Id
                )
                SELECT DISTINCT Id, ParentId, IsActive, Name, Label
                FROM Ancestors
                """)
                .ToListAsync(cancellationToken);
        }

        private sealed class OrganizationAccessNode
        {
            public int Id { get; set; }
            public int? ParentId { get; set; }
            public bool IsActive { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;
        }
    }
}
