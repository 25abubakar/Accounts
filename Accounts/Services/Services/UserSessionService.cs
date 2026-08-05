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
                        t.BrandingAssetType,
                        t.BrandingFileName,
                        t.BrandingUpdatedOnUtc,
                        HasBranding = t.BrandingContent != null,
                        OrganizationLabel = t.OrganizationNode != null ? t.OrganizationNode.Label : null
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                if (tenantContext != null)
                {
                    session.TenantOrganizationTreeId = tenantContext.OrganizationTreeId;
                    session.TenantName = tenantContext.TenantName;
                    session.TenantOrganizationLabel = tenantContext.OrganizationLabel;
                    session.TenantBrandingType = tenantContext.BrandingAssetType;
                    session.TenantBrandingFileName = tenantContext.BrandingFileName;
                    session.TenantBrandingUpdatedOnUtc = tenantContext.BrandingUpdatedOnUtc;
                    if (tenantContext.HasBranding)
                    {
                        var version = tenantContext.BrandingUpdatedOnUtc?.Ticks ?? 0;
                        session.TenantBrandingUrl = $"/api/tenant-branding/{tenantId}/content?v={version}";
                    }
                }
            }

            // ── Super Admin path ──────────────────────────────────────────────
            // Super Admin owns the master catalogue and receives every active
            // menu and feature. Tenant/staff ceilings apply only below this tier.
            if (session.IsSuperAdmin)
            {
                if (!includeNavigation)
                    return session;

                var allMenus = await _db.Menus.AsNoTracking()
                    .Where(m => m.IsActive)
                    .OrderBy(m => m.SortOrder)
                    .ToListAsync(cancellationToken);
                session.Sidebar = BuildFullTree(null, allMenus.ToLookup(menu => menu.ParentId));
                session.Permissions = await _db.Features.AsNoTracking()
                    .OrderBy(feature => feature.Module)
                    .ThenBy(feature => feature.FeatureKey)
                    .Select(feature => feature.FeatureKey)
                    .ToListAsync(cancellationToken);

                var adminStaffId = identityUserId;
                session.LoginInstructions = await _notes.GetLoginInstructionsAsync(
                    adminStaffId, identityUserId, cancellationToken);
                session.UnreadInstructionCount = session.LoginInstructions.Count(n => !n.IsRead);
                return session;
            }

            // TenantAdmin receives exactly the SuperAdmin-approved tenant menu
            // and CRUD ceiling. A Person/Staff record is not required.
            if (session.IsTenantAdmin && session.TenantId.HasValue)
            {
                session.IsFullAccess = false;
                var tenantAdminPerson = await GetPersonInfoAsync(
                    identityUserId,
                    cancellationToken);
                ApplyPersonInfo(session, tenantAdminPerson);
                if (includeNavigation)
                {
                    var grants = await _db.TenantMenuPermissions
                        .AsNoTracking()
                        .Where(grant => grant.TenantId == session.TenantId.Value
                                        && grant.IsAllow
                                        && grant.CanView)
                        .Select(grant => new
                        {
                            grant.MenuId,
                            grant.CanView,
                            grant.CanAdd,
                            grant.CanEdit,
                            grant.CanDelete
                        })
                        .ToListAsync(cancellationToken);

                    var menus = await _db.Menus.AsNoTracking()
                        .Where(menu => menu.IsActive)
                        .OrderBy(menu => menu.SortOrder)
                        .ToListAsync(cancellationToken);
                    var byId = menus.ToDictionary(menu => menu.Id);
                    var visibleIds = grants.Select(grant => grant.MenuId).ToHashSet();
                    foreach (var menuId in visibleIds.ToList())
                    {
                        var current = byId.GetValueOrDefault(menuId);
                        while (current?.ParentId != null &&
                               byId.TryGetValue(current.ParentId.Value, out var parent))
                        {
                            visibleIds.Add(parent.Id);
                            current = parent;
                        }
                    }

                    session.Sidebar = BuildFullTree(
                        null,
                        menus.Where(menu => visibleIds.Contains(menu.Id))
                            .ToLookup(menu => menu.ParentId));

                    var permissionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var grant in grants)
                    {
                        permissionKeys.Add($"MENU_{grant.MenuId}");
                        if (grant.CanView) permissionKeys.Add($"MENU_{grant.MenuId}_VIEW");
                        if (grant.CanAdd) permissionKeys.Add($"MENU_{grant.MenuId}_ADD");
                        if (grant.CanEdit) permissionKeys.Add($"MENU_{grant.MenuId}_EDIT");
                        if (grant.CanDelete) permissionKeys.Add($"MENU_{grant.MenuId}_DELETE");
                    }
                    session.Permissions = permissionKeys.OrderBy(key => key).ToList();
                }

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
            (from person in _db.Persons.AsNoTracking()
             where person.IdentityUserId == identityUserId
             join staff in _db.StaffDirectoryRows.AsNoTracking()
                on person.PersonId equals staff.PersonId into staffRows
             from staff in staffRows.DefaultIfEmpty()
             select new SessionPersonInfo
             {
                 PersonId = person.PersonId,
                 FullName = person.FullName,
                 Email = person.Email,
                 ProfilePhotoUrl = person.ProfilePhotoUrl,
                 StaffId = staff != null ? staff.StaffId : null,
                 StaffLoginId = staff != null ? staff.EmployeeId : null,
                 JobTitle = staff != null ? staff.Designation : null,
                 Department = staff != null ? staff.Department : null
             })
                .OrderBy(info => info.StaffId == null)
                .Select(person => new SessionPersonInfo
                {
                    PersonId = person.PersonId,
                    FullName = person.FullName,
                    Email = person.Email,
                    ProfilePhotoUrl = person.ProfilePhotoUrl,
                    StaffId = person.StaffId,
                    StaffLoginId = person.StaffLoginId,
                    JobTitle = person.JobTitle,
                    Department = person.Department
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
