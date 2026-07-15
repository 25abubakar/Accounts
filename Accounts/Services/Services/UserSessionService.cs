using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    public class UserSessionService : IUserSessionService
    {
        private readonly ApplicationDbContext        _db;
        private readonly RbacService                 _rbac;
        private readonly IPersonAccessService        _personAccess;
        private readonly IAppNoteService             _notes;
        private readonly UserManager<ApplicationUser> _userManager;

        public UserSessionService(
            ApplicationDbContext         db,
            RbacService                  rbac,
            IPersonAccessService         personAccess,
            IAppNoteService              notes,
            UserManager<ApplicationUser> userManager)
        {
            _db           = db;
            _rbac         = rbac;
            _personAccess = personAccess;
            _notes        = notes;
            _userManager  = userManager;
        }

        public async Task<UserSessionDto> GetSessionAsync(
            string identityUserId,
            bool isFullAccess,
            CancellationToken cancellationToken = default)
        {
            // ── Resolve ApplicationUser to get tenant flags ───────────────────
            var appUser = await _userManager.FindByIdAsync(identityUserId);

            var session = new UserSessionDto
            {
                IsFullAccess   = isFullAccess,
                IdentityUserId = identityUserId,
                TenantId       = appUser?.TenantId,
                IsSuperAdmin   = appUser?.IsSuperAdmin ?? false,
                IsTenantAdmin  = appUser?.IsTenantAdmin ?? false
            };

            if (appUser?.TenantId is int tenantId)
            {
                var tenantContext = await _db.Tenants.AsNoTracking()
                    .Where(t => t.Id == tenantId)
                    .Select(t => new
                    {
                        t.OrganizationTreeId,
                        t.TenantName,
                        OrganizationLabel = t.OrganizationNode != null ? t.OrganizationNode.Label : null
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                if (tenantContext != null)
                {
                    session.TenantOrganizationTreeId = tenantContext.OrganizationTreeId;
                    session.TenantName = tenantContext.TenantName;
                    session.TenantOrganizationLabel = tenantContext.OrganizationLabel;
                }
            }

            // ── Super Admin path ──────────────────────────────────────────────
            // Super Admins see only Organisation + Platform Settings menus.
            // They must NOT see HR/Staff/Notes operational menus.
            if (session.IsSuperAdmin)
            {
                // Load only menus whose routes belong to the SuperAdmin scope
                var allMenus = await _db.Menus.AsNoTracking()
                    .Where(m => m.IsActive)
                    .OrderBy(m => m.SortOrder)
                    .ToListAsync(cancellationToken);

                var lookup = allMenus.ToLookup(m => m.ParentId);

                // Super Admin allowed route prefixes
                var superAdminRoutes = new[]
                {
                    "/groups/", "/organization", "/settings/", "/tenants", "/dashboard"
                };

                bool IsSuperAdminMenu(Models.Menu m) =>
                    m.Route == null || // group headers — include if they have valid children
                    superAdminRoutes.Any(r => m.Route.StartsWith(r, StringComparison.OrdinalIgnoreCase));

                // Build restricted sidebar (only org + settings)
                var saMenus = allMenus
                    .Where(m => IsSuperAdminMenu(m))
                    .ToLookup(m => m.ParentId);

                session.Sidebar = BuildFullTree(null, saMenus);
                // Super Admin gets all feature keys for the platform-level settings UI
                session.Permissions = await _db.Features.AsNoTracking()
                    .Select(f => f.FeatureKey).ToListAsync(cancellationToken);

                var adminStaffId = identityUserId;
                session.LoginInstructions = await _notes.GetLoginInstructionsAsync(
                    adminStaffId, identityUserId, cancellationToken);
                session.UnreadInstructionCount = session.LoginInstructions.Count(n => !n.IsRead);
                return session;
            }

            // ── Legacy isFullAccess path (Admin role — not Super Admin) ───────
            if (isFullAccess)
            {
                session.Sidebar     = await _rbac.GetFilteredSidebarAsync(Guid.Empty);
                session.Permissions = await _db.Features.AsNoTracking()
                    .Select(f => f.FeatureKey).ToListAsync(cancellationToken);

                var staffForAdmin = await _db.Persons.AsNoTracking()
                    .Include(p => p.Staff)
                    .FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId, cancellationToken);
                session.StaffId  = staffForAdmin?.Staff?.StaffId;
                session.PersonId = staffForAdmin?.PersonId;

                var adminStaffId = session.StaffId?.ToString() ?? identityUserId;
                session.LoginInstructions = await _notes.GetLoginInstructionsAsync(
                    adminStaffId, identityUserId, cancellationToken);
                session.UnreadInstructionCount = session.LoginInstructions.Count(n => !n.IsRead);
                return session;
            }

            // ── Regular staff / tenant path ───────────────────────────────────
            var person = await _db.Persons.AsNoTracking()
                .Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId, cancellationToken);

            if (person == null)
            {
                session.Sidebar     = new List<object>();
                session.Permissions = new List<string>();
            }
            else
            {
                session.PersonId = person.PersonId;
                session.StaffId  = person.Staff?.StaffId;

                var hasDirectGrants = await _personAccess.HasPersonGrantsAsync(person.PersonId, cancellationToken);

                if (hasDirectGrants)
                {
                    session.Sidebar     = await _personAccess.GetGrantedSidebarAsync(person.PersonId, cancellationToken);
                    session.Permissions = (await _personAccess.GetGrantedFeatureKeysAsync(person.PersonId, cancellationToken)).ToList();
                }
                else if (person.Staff != null)
                {
                    session.Sidebar     = await _rbac.GetFilteredSidebarAsync(person.Staff.StaffId);
                    session.Permissions = (await _rbac.GetEffectivePermissionsAsync(person.Staff.StaffId)).ToList();
                }
                else
                {
                    session.Sidebar     = new List<object>();
                    session.Permissions = new List<string>();
                }
            }

            var noteStaffId = session.StaffId?.ToString() ?? identityUserId;
            session.LoginInstructions = await _notes.GetLoginInstructionsAsync(
                noteStaffId, identityUserId, cancellationToken);
            session.UnreadInstructionCount = session.LoginInstructions.Count(n => !n.IsRead);

            return session;
        }

        // ── Sidebar tree builder (same as RbacService.BuildFullTree) ─────────
        private static List<object> BuildFullTree(int? parentId, ILookup<int?, Models.Menu> lookup)
        {
            return lookup[parentId].Select(menu => (object)new
            {
                id        = menu.Id,
                title     = menu.Title,
                icon      = menu.Icon,
                route     = menu.Route,
                sortOrder = menu.SortOrder,
                children  = BuildFullTree(menu.Id, lookup)
            }).ToList();
        }
    }
}
