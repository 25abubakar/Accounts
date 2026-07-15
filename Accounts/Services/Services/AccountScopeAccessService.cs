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
                    u.TenantId,
                    u.LockoutEnabled,
                    u.LockoutEnd,
                    PersonIsActive = _db.Persons
                        .Where(p => p.IdentityUserId == u.Id)
                        .Select(p => (bool?)p.IsActive)
                        .FirstOrDefault()
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (user == null)
                return AccountScopeAccessResult.Denied("This account no longer exists.");

            if (user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow)
                return AccountScopeAccessResult.Denied("This account is disabled or locked.");

            if (user.PersonIsActive == false)
                return AccountScopeAccessResult.Denied("Your staff account is inactive. Contact your administrator.");

            if (user.IsSuperAdmin)
                return AccountScopeAccessResult.Allowed();

            if (!user.TenantId.HasValue)
                return AccountScopeAccessResult.Denied("This account is not assigned to an active tenant.");

            var tenant = await _db.Tenants.AsNoTracking()
                .Where(t => t.Id == user.TenantId.Value)
                .Select(t => new { t.IsActive, t.OrganizationTreeId, t.TenantName })
                .SingleOrDefaultAsync(cancellationToken);

            if (tenant == null || !tenant.IsActive)
                return AccountScopeAccessResult.Denied("Your tenant is currently disabled. Contact your administrator.");

            // One bounded query avoids an N+1 walk through the parent hierarchy.
            var nodes = await _db.OrganizationTree.AsNoTracking()
                .Select(n => new { n.Id, n.ParentId, n.IsActive, n.Name, n.Label })
                .ToListAsync(cancellationToken);
            var byId = nodes.ToDictionary(n => n.Id);
            var visited = new HashSet<int>();
            var currentId = (int?)tenant.OrganizationTreeId;

            while (currentId.HasValue && byId.TryGetValue(currentId.Value, out var node) && visited.Add(node.Id))
            {
                if (!node.IsActive)
                    return AccountScopeAccessResult.Denied(
                        $"Access is disabled for {node.Name} ({node.Label}). Contact your administrator.");
                currentId = node.ParentId;
            }

            return AccountScopeAccessResult.Allowed();
        }
    }
}
