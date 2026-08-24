using Accounts.Data;
using Accounts.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    public sealed class AccountScopeAccessService : IAccountScopeAccessService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<AccountScopeAccessService> _logger;

        public AccountScopeAccessService(ApplicationDbContext db, ILogger<AccountScopeAccessService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<AccountScopeAccessResult> ValidateAsync(
            string userId, CancellationToken cancellationToken = default)
        {
            if (_db.Database.IsSqlServer())
            {
                try
                {
                    var decisions = await _db.Database.SqlQueryRaw<AccountScopeValidationRow>(
                        "EXEC dbo.usp_AccountScope_ValidateAccess @UserId",
                        new SqlParameter("@UserId", userId))
                        .ToListAsync(cancellationToken);

                    var decision = decisions.FirstOrDefault();
                    if (decision == null)
                        return AccountScopeAccessResult.Denied("Unable to validate this account. Contact your administrator.");

                    return decision.IsAllowed
                        ? AccountScopeAccessResult.Allowed()
                        : new AccountScopeAccessResult(
                            false,
                            string.IsNullOrWhiteSpace(decision.Code) ? "ACCOUNT_SCOPE_DISABLED" : decision.Code,
                            string.IsNullOrWhiteSpace(decision.Message) ? "This account is disabled or locked." : decision.Message);
                }
                catch (SqlException exception) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(
                        "Account-scope validation was cancelled because the HTTP request ended.",
                        exception,
                        cancellationToken);
                }
                catch (SqlException exception)
                {
                    // The LINQ path below implements the same fail-closed checks. It
                    // keeps authentication available if the stored procedure is
                    // temporarily unavailable, has a deployment-version mismatch,
                    // or SQL Server terminates the command after retry exhaustion.
                    _logger.LogWarning(
                        exception,
                        "Stored-procedure account-scope validation failed; using the fail-closed EF validation path.");
                }
            }

            var user = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new
                {
                    u.IsSuperAdmin,
                    u.IsTenantAdmin,
                    u.TenantId,
                    u.LockoutEnabled,
                    u.LockoutEnd
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (user == null)
                return AccountScopeAccessResult.Denied("This account no longer exists.");

            if (user.IsSuperAdmin)
                return AccountScopeAccessResult.Allowed();

            if (!user.IsTenantAdmin && user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow)
                return AccountScopeAccessResult.Denied("This account is disabled or locked.");

            if (!user.TenantId.HasValue)
                return AccountScopeAccessResult.Denied("This account is not assigned to an active tenant.");

            var tenant = await _db.Tenants.AsNoTracking()
                .Where(t => t.Id == user.TenantId.Value)
                .Select(t => new
                {
                    t.IsActive,
                    TenantOrganizationTreeId = (int?)t.OrganizationTreeId
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (tenant == null || tenant.IsActive != true ||
                !tenant.TenantOrganizationTreeId.HasValue)
                return AccountScopeAccessResult.Denied("Your tenant is currently disabled. Contact your administrator.");

            var person = await _db.Persons.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.IdentityUserId == userId)
                .Select(p => new
                {
                    PersonIsActive = (bool?)p.IsActive,
                    EmployeeOrganizationId = p.Staff != null && p.Staff.Vacancy != null
                        ? (int?)p.Staff.Vacancy.OrganizationId
                        : null
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (!user.IsTenantAdmin && person?.PersonIsActive == false)
                return AccountScopeAccessResult.Denied("Your staff account is inactive. Contact your administrator.");

            // Load only the two relevant ancestor chains on SQL Server. The
            // previous request middleware copied the complete organization tree
            // for every API call, which became progressively slower as tenants
            // and departments were added.
            var nodes = await _db.OrganizationTree.AsNoTracking()
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
            var currentId = tenant.TenantOrganizationTreeId;

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
            currentId = person?.EmployeeOrganizationId;
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

        private sealed class OrganizationAccessNode
        {
            public int Id { get; set; }
            public int? ParentId { get; set; }
            public bool IsActive { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;
        }

        private sealed class AccountScopeValidationRow
        {
            public bool IsAllowed { get; set; }
            public string Code { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
        }
    }
}
