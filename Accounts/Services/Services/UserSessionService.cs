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
            bool isOrganizationCeo,
            bool includeNavigation,
            CancellationToken cancellationToken = default)
        {
            // ── Resolve ApplicationUser to get tenant flags ───────────────────
            var appUser = await _userManager.FindByIdAsync(identityUserId);

            var session = new UserSessionDto
            {
                IsFullAccess   = isFullAccess,
                IdentityUserId = identityUserId,
                DisplayName    = appUser?.UserName ?? appUser?.Email,
                Email          = appUser?.Email,
                TenantId       = appUser?.TenantId,
                IsSuperAdmin   = appUser?.IsSuperAdmin ?? false,
                IsTenantAdmin  = (appUser?.IsTenantAdmin ?? false) || isOrganizationCeo
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
                if (!includeNavigation)
                    return session;

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
                if (includeNavigation)
                {
                    session.Sidebar = await _rbac.GetFilteredSidebarAsync(Guid.Empty);
                    session.Permissions = await _db.Features.AsNoTracking()
                        .Select(f => f.FeatureKey).ToListAsync(cancellationToken);
                }

                var staffForAdmin = await GetPersonInfoAsync(identityUserId, cancellationToken);
                ApplyPersonInfo(session, staffForAdmin);

                var adminStaffId = session.StaffId?.ToString() ?? identityUserId;
                session.LoginInstructions = await _notes.GetLoginInstructionsAsync(
                    adminStaffId, identityUserId, cancellationToken);
                session.UnreadInstructionCount = session.LoginInstructions.Count(n => !n.IsRead);
                return session;
            }

            // ── Regular staff / tenant path ───────────────────────────────────
            var person = await GetPersonInfoAsync(identityUserId, cancellationToken);

            if (person == null)
            {
                session.Sidebar     = new List<object>();
                session.Permissions = new List<string>();
            }
            else
            {
                ApplyPersonInfo(session, person);

                if (includeNavigation)
                {
                    var hasDirectGrants = await _personAccess.HasPersonGrantsAsync(person.PersonId, cancellationToken);
                    if (hasDirectGrants)
                    {
                        session.Sidebar = await _personAccess.GetGrantedSidebarAsync(person.PersonId, cancellationToken);
                        session.Permissions = (await _personAccess.GetGrantedFeatureKeysAsync(person.PersonId, cancellationToken)).ToList();
                    }
                    else if (person.StaffId.HasValue)
                    {
                        session.Sidebar = await _rbac.GetFilteredSidebarAsync(person.StaffId.Value);
                        session.Permissions = (await _rbac.GetEffectivePermissionsAsync(person.StaffId.Value)).ToList();
                    }
                }
            }

            var noteStaffId = session.StaffId?.ToString() ?? identityUserId;
            session.LoginInstructions = await _notes.GetLoginInstructionsAsync(
                noteStaffId, identityUserId, cancellationToken);
            session.UnreadInstructionCount = session.LoginInstructions.Count(n => !n.IsRead);

            return session;
        }

        // ── Sidebar tree builder (same as RbacService.BuildFullTree) ─────────
        private Task<SessionPersonInfo?> GetPersonInfoAsync(
            string identityUserId,
            CancellationToken cancellationToken) =>
            _db.Persons.AsNoTracking()
                .Where(person => person.IdentityUserId == identityUserId)
                .Select(person => new SessionPersonInfo
                {
                    PersonId = person.PersonId,
                    FullName = person.FullName,
                    Email = person.Email,
                    ProfilePhotoUrl = person.ProfilePhotoUrl,
                    StaffId = person.Staff != null ? (Guid?)person.Staff.StaffId : null,
                    StaffLoginId = person.Staff != null
                        ? (person.Staff.LoginId ??
                           (person.Staff.Vacancy != null ? person.Staff.Vacancy.VacancyCode : null))
                        : null,
                    JobTitle = person.Staff != null && person.Staff.Vacancy != null
                        ? (person.Staff.Vacancy.JobTitleNav != null
                            ? person.Staff.Vacancy.JobTitleNav.TitleName
                            : person.Staff.Vacancy.JobTitle)
                        : null,
                    Department = person.Staff != null && person.Staff.Vacancy != null
                        ? (person.Staff.Vacancy.Department ??
                           (person.Staff.Vacancy.Organization != null
                               ? person.Staff.Vacancy.Organization.Name
                               : null))
                        : null
                })
                .FirstOrDefaultAsync(cancellationToken);

        private static void ApplyPersonInfo(UserSessionDto session, SessionPersonInfo? person)
        {
            if (person == null) return;
            session.PersonId = person.PersonId;
            session.StaffId = person.StaffId;
            session.DisplayName = person.FullName;
            session.Email = person.Email ?? session.Email;
            session.StaffLoginId = person.StaffLoginId;
            session.ProfilePhotoUrl = person.ProfilePhotoUrl;
            session.JobTitle = person.JobTitle;
            session.Department = person.Department;
        }

        private sealed class SessionPersonInfo
        {
            public Guid PersonId { get; set; }
            public Guid? StaffId { get; set; }
            public string FullName { get; set; } = string.Empty;
            public string? Email { get; set; }
            public string? StaffLoginId { get; set; }
            public string? ProfilePhotoUrl { get; set; }
            public string? JobTitle { get; set; }
            public string? Department { get; set; }
        }

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
