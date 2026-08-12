using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/rbac")]
    [Authorize]
    [Produces("application/json")]
    public class RbacController : ControllerBase
    {
        private readonly RbacService          _rbac;
        private readonly ApplicationDbContext _db;
        private readonly IPersonAccessService _personAccess;

        public RbacController(RbacService rbac, ApplicationDbContext db, IPersonAccessService personAccess)
        {
            _rbac          = rbac;
            _db            = db;
            _personAccess  = personAccess;
        }

        private string? CurrentUserId =>
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        private bool IsSuperAdminUser =>
            User.IsInRole("SuperAdmin") ||
            string.Equals(User.FindFirstValue(ITenantService.ClaimIsSuperAdmin), "true", StringComparison.OrdinalIgnoreCase);

        private bool IsTenantAdminUser =>
            User.IsInRole("TenantAdmin") ||
            string.Equals(User.FindFirstValue(ITenantService.ClaimIsTenantAdmin), "true", StringComparison.OrdinalIgnoreCase);

        private async Task<int?> CurrentTenantIdAsync()
        {
            if (int.TryParse(User.FindFirstValue(ITenantService.ClaimTenantId), out var claimedTenantId))
                return claimedTenantId;

            var identityUserId = CurrentUserId;
            if (string.IsNullOrWhiteSpace(identityUserId)) return null;
            return await _db.Users.AsNoTracking()
                .OfType<ApplicationUser>()
                .Where(user => user.Id == identityUserId)
                .Select(user => user.TenantId)
                .FirstOrDefaultAsync();
        }

        private async Task<Guid?> CurrentStaffIdAsync()
        {
            var identityUserId = CurrentUserId;
            if (string.IsNullOrWhiteSpace(identityUserId)) return null;

            return await _db.Persons
                .AsNoTracking()
                .Where(person => person.IdentityUserId == identityUserId && person.Staff != null)
                .Select(person => (Guid?)person.Staff!.StaffId)
                .FirstOrDefaultAsync();
        }

        private async Task<bool> HasAccessControlPermissionAsync(params string[] actions)
        {
            if (IsSuperAdminUser) return true;

            var normalizedActions = actions
                .Where(action => !string.IsNullOrWhiteSpace(action))
                .Select(action => action.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var accessMenuIds = await _db.Menus.AsNoTracking()
                .Where(menu => menu.IsActive &&
                    (menu.Route == "/access/admin" ||
                     menu.Route == "/access/groups" ||
                     menu.Title == "Access Control" ||
                     menu.Title == "Access Groups" ||
                     menu.Title == "Dept Permissions"))
                .Select(menu => menu.Id)
                .ToListAsync();

            // Tenant Admin authority is the ceiling assigned by Super Admin.
            // It is not an unconditional bypass and does not require a Staff row.
            if (IsTenantAdminUser || User.IsInRole("Admin"))
            {
                var tenantId = await CurrentTenantIdAsync();
                if (!tenantId.HasValue) return false;

                var ceiling = await _db.TenantMenuPermissions.AsNoTracking()
                    .Where(permission => permission.TenantId == tenantId.Value &&
                        permission.IsAllow && accessMenuIds.Contains(permission.MenuId))
                    .Select(permission => new
                    {
                        permission.CanView,
                        permission.CanAdd,
                        permission.CanEdit,
                        permission.CanDelete
                    })
                    .ToListAsync();

                return normalizedActions.Any(action => action switch
                {
                    "VIEW" => ceiling.Any(permission => permission.CanView),
                    "ADD" => ceiling.Any(permission => permission.CanAdd),
                    "EDIT" => ceiling.Any(permission => permission.CanEdit),
                    "DELETE" => ceiling.Any(permission => permission.CanDelete),
                    _ => false
                });
            }

            var staffId = await CurrentStaffIdAsync();
            if (!staffId.HasValue) return false;

            var effectiveKeys = (await _rbac.GetEffectivePermissionsAsync(staffId.Value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var menuId in accessMenuIds)
            {
                if (normalizedActions.Contains("VIEW", StringComparer.OrdinalIgnoreCase) &&
                    effectiveKeys.Contains($"MENU_{menuId}")) return true;
                if (normalizedActions.Any(action => effectiveKeys.Contains($"MENU_{menuId}_{action}"))) return true;
            }

            return false;
        }

        // ── Admin: list all users with StaffId (for permission assignment UI) ─

        /// <summary>
        /// Returns registered persons with their StaffId.
        ///
        /// Business rule:
        ///   - Super Admin → returns ONLY Tenant Admin accounts (IsTenantAdmin=true)
        ///   - Admin / Tenant Admin → returns persons within their tenant scope
        ///   - Regular staff → forbidden (handled by [Authorize(Roles)])
        ///
        /// GET /api/rbac/users
        /// </summary>
        /// <summary>
        /// Returns the menu catalogue the current administrator is allowed to
        /// delegate. Tenant administrators receive only the menus granted to
        /// their tenant, including the CRUD ceiling from TenantMenuPermissions.
        /// </summary>
        [HttpGet("delegable-menus")]
        public async Task<IActionResult> GetDelegableMenus()
        {
            if (!await HasAccessControlPermissionAsync("VIEW"))
                return Forbid();

            if (IsSuperAdminUser)
            {
                var allMenus = await _db.Menus
                    .AsNoTracking()
                    .Where(menu => menu.IsActive)
                    .OrderBy(menu => menu.SortOrder)
                    .ThenBy(menu => menu.Title)
                    .Select(menu => new
                    {
                        menu.Id,
                        menu.Title,
                        menu.Icon,
                        menu.Route,
                        menu.ParentId,
                        menu.SortOrder,
                        CanView = true,
                        CanAdd = true,
                        CanEdit = true,
                        CanDelete = true
                    })
                    .ToListAsync();

                return Ok(allMenus);
            }

            var identityUserId = CurrentUserId;
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized();

            var tenantId = await _db.Users
                .AsNoTracking()
                .OfType<ApplicationUser>()
                .Where(user => user.Id == identityUserId)
                .Select(user => user.TenantId)
                .FirstOrDefaultAsync();

            if (!tenantId.HasValue)
                return Forbid();

            var menus = await (
                from grant in _db.TenantMenuPermissions.AsNoTracking()
                join menu in _db.Menus.AsNoTracking() on grant.MenuId equals menu.Id
                where grant.TenantId == tenantId.Value && grant.IsAllow && menu.IsActive
                orderby menu.SortOrder, menu.Title
                select new
                {
                    menu.Id,
                    menu.Title,
                    menu.Icon,
                    menu.Route,
                    menu.ParentId,
                    menu.SortOrder,
                    grant.CanView,
                    grant.CanAdd,
                    grant.CanEdit,
                    grant.CanDelete
                })
                .ToListAsync();

            return Ok(menus);
        }

        /// <summary>
        /// Returns the users that the current administrator can manage in the
        /// access-control screen.
        /// </summary>
        /// <summary>
        /// Returns the active tenant staff that can receive access grants. This
        /// catalogue is intentionally owned by RBAC so access administration is
        /// not coupled to the richer HR staff-directory query.
        /// </summary>
        [HttpGet("staff-catalog")]
        public async Task<IActionResult> GetStaffCatalog()
        {
            if (!await HasAccessControlPermissionAsync("VIEW"))
                return Forbid();

            if (IsSuperAdminUser)
                return BadRequest(new { message = "Super administrators delegate tenant access through tenant administrators." });

            // Resolve the scope from the authenticated account instead of
            // relying solely on the ambient tenant query filter. This keeps
            // the access catalogue fail-closed while also supporting tenant
            // admin accounts whose older authentication cookie does not yet
            // contain the tenant_id claim.
            var identityUserId = CurrentUserId;
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized();

            var tenantId = await _db.Users
                .AsNoTracking()
                .OfType<ApplicationUser>()
                .Where(user => user.Id == identityUserId)
                .Select(user => user.TenantId)
                .FirstOrDefaultAsync();

            if (!tenantId.HasValue)
                return Forbid();

            var staff = await _db.StaffVacancies
                // RBAC applies its own mandatory tenant predicate below. This
                // avoids an empty catalogue when a legacy session lacks the
                // newer tenant claim, without allowing cross-tenant rows.
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(row =>
                    row.TenantId == tenantId.Value &&
                    row.PersonId.HasValue &&
                    row.Person != null &&
                    row.Person.TenantId == tenantId.Value &&
                    row.Person.IsActive &&
                    row.Vacancy != null &&
                    row.Vacancy.TenantId == tenantId.Value)
                .Select(row => new
                {
                    row.StaffId,
                    row.PersonId,
                    FullName = row.Person!.FullName,
                    Email = row.Person.Email,
                    Phone = row.Person.Phone,
                    PhotoUrl = row.Person.ProfilePhotoUrl,
                    row.LoginId,
                    row.VacancyId,
                    VacancyCode = row.Vacancy != null ? row.Vacancy.VacancyCode : null,
                    JobTitleId = row.Vacancy != null ? row.Vacancy.JobTitleId : null,
                    // Use the stable vacancy value in this small catalogue.
                    // The richer HR endpoint can hydrate normalized
                    // designation navigation; RBAC must remain independent of
                    // that optional join so one malformed title cannot hide
                    // every staff member from access administration.
                    JobTitle = row.Vacancy != null ? row.Vacancy.JobTitle : null,
                    Department = row.Vacancy != null ? row.Vacancy.Department : null,
                    OrganizationId = row.Vacancy != null ? (int?)row.Vacancy.OrganizationId : null
                })
                .OrderBy(row => row.FullName)
                .ToListAsync();

            var jobTitleIds = staff
                .Where(row => row.JobTitleId.HasValue)
                .Select(row => row.JobTitleId!.Value)
                .Distinct()
                .ToArray();
            var jobTitleMap = await _db.JobTitles
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(title => title.TenantId == tenantId.Value && jobTitleIds.Contains(title.Id))
                .ToDictionaryAsync(title => title.Id, title => title.TitleName);

            var organizationNodes = await _db.OrganizationTree
                .AsNoTracking()
                .Select(node => new { node.Id, node.ParentId, node.Name, node.Label })
                .ToListAsync();
            var organizationMap = organizationNodes.ToDictionary(node => node.Id);

            string? FindOrganizationName(int? startId, params string[] labels)
            {
                if (!startId.HasValue) return null;
                var acceptedLabels = labels.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var currentId = startId;
                var guard = 0;
                while (currentId.HasValue && organizationMap.TryGetValue(currentId.Value, out var node) && guard++ < 100)
                {
                    if (acceptedLabels.Contains(node.Label)) return node.Name;
                    currentId = node.ParentId;
                }
                return null;
            }

            return Ok(staff.Select(row => new
            {
                staffId = row.StaffId,
                personId = row.PersonId,
                fullName = row.FullName,
                email = row.Email ?? string.Empty,
                phone = row.Phone ?? string.Empty,
                photoUrl = row.PhotoUrl,
                isActive = true,
                loginId = row.LoginId,
                vacancyId = row.VacancyId,
                vacancyCode = row.VacancyCode ?? string.Empty,
                jobTitle = row.JobTitleId.HasValue && jobTitleMap.TryGetValue(row.JobTitleId.Value, out var normalizedTitle)
                    ? normalizedTitle
                    : row.JobTitle ?? string.Empty,
                department = row.Department ?? FindOrganizationName(row.OrganizationId, "Department"),
                branchName = FindOrganizationName(row.OrganizationId, "Branch", "Office"),
                companyName = FindOrganizationName(row.OrganizationId, "Company"),
                countryName = FindOrganizationName(row.OrganizationId, "Country"),
                groupName = FindOrganizationName(row.OrganizationId, "Group"),
                joiningDate = (DateTime?)null
            }));
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers()
        {
            if (!await HasAccessControlPermissionAsync("VIEW"))
                return Forbid();

            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var appUser = await _db.Users
                .AsNoTracking()
                .OfType<ApplicationUser>()
                .FirstOrDefaultAsync(u => u.Id == identityUserId);

            // ── Super Admin: return only Tenant Admin accounts ────────────────
            if (appUser?.IsSuperAdmin == true)
            {
                var tenantAdmins = await _db.Users
                    .AsNoTracking()
                    .OfType<ApplicationUser>()
                    .Where(u => u.IsTenantAdmin)
                    .OrderBy(u => u.UserName)
                    .Select(u => new
                    {
                        identityUserId = u.Id,
                        userName       = u.UserName,
                        email          = u.Email,
                        tenantId       = u.TenantId,
                        isTenantAdmin  = u.IsTenantAdmin,
                        isSuperAdmin   = u.IsSuperAdmin,
                        isHired        = false,
                        staffId        = (Guid?)null,
                        fullName       = u.UserName,
                        loginId        = u.UserName
                    })
                    .ToListAsync();

                return Ok(tenantAdmins);
            }

            // ── Admin / Tenant Admin: return persons in their tenant scope ─────
            var persons = await _db.Persons
                .AsNoTracking()
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy)
                .OrderBy(p => p.FullName)
                .Select(p => new
                {
                    personId       = p.PersonId,
                    identityUserId = p.IdentityUserId,
                    fullName       = p.FullName,
                    email          = p.Email,
                    photoUrl       = p.ProfilePhotoUrl,
                    isHired        = p.Staff != null,
                    staffId        = p.Staff != null ? p.Staff.StaffId : (Guid?)null,
                    loginId        = p.Staff != null ? p.Staff.LoginId : null,
                    jobTitle       = p.Staff != null && p.Staff.Vacancy != null ? p.Staff.Vacancy.JobTitle : null
                })
                .ToListAsync();

            return Ok(persons);
        }

        [HttpGet("staff-access-overview")]
        public async Task<IActionResult> GetStaffAccessOverview()
        {
            if (!await HasAccessControlPermissionAsync("VIEW"))
                return Forbid();

            if (IsSuperAdminUser)
            {
                var tenantAdmins = await _db.Users
                    .AsNoTracking()
                    .OfType<ApplicationUser>()
                    .Where(user => user.IsTenantAdmin && user.TenantId.HasValue)
                    .Select(user => new { user.Id, TenantId = user.TenantId!.Value })
                    .ToListAsync();

                var tenantIds = tenantAdmins.Select(user => user.TenantId).Distinct().ToArray();
                var tenantGrants = await _db.TenantMenuPermissions
                    .AsNoTracking()
                    .Where(permission => tenantIds.Contains(permission.TenantId) && permission.IsAllow)
                    .Select(permission => new
                    {
                        permission.TenantId,
                        permission.MenuId,
                        permission.CanView,
                        permission.CanAdd,
                        permission.CanEdit,
                        permission.CanDelete
                    })
                    .ToListAsync();

                var grantsByTenant = tenantGrants.ToLookup(permission => permission.TenantId);
                var result = tenantAdmins.Select(user =>
                {
                    var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var grant in grantsByTenant[user.TenantId])
                    {
                        keys.Add($"MENU_{grant.MenuId}");
                        if (grant.CanView) keys.Add($"MENU_{grant.MenuId}_VIEW");
                        if (grant.CanAdd) keys.Add($"MENU_{grant.MenuId}_ADD");
                        if (grant.CanEdit) keys.Add($"MENU_{grant.MenuId}_EDIT");
                        if (grant.CanDelete) keys.Add($"MENU_{grant.MenuId}_DELETE");
                    }

                    return new { staffId = user.Id, allowedFeatureKeys = keys.OrderBy(key => key).ToArray() };
                });

                return Ok(result);
            }

            var overview = await _rbac.GetStaffAccessOverviewAsync();
            return Ok(overview.Select(item => new { staffId = item.Key, allowedFeatureKeys = item.Value }));
        }

        /// <summary>
        /// Get all current permissions for a staff member, with feature details.
        /// Reads from the new 2-tier RBAC (StaffMenuAccess + AccessFeatures).
        /// GET /api/rbac/staff/{staffId}/permissions-summary
        /// </summary>
        [HttpGet("staff/{staffId:guid}/permissions-summary")]
        public async Task<IActionResult> GetPermissionsSummary(Guid staffId)
        {
            if (!await HasAccessControlPermissionAsync("VIEW"))
                return Forbid();

            var effectiveKeys = (await _rbac.GetEffectivePermissionsAsync(staffId))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var allFeatures = await _db.Features.AsNoTracking()
                .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
                .ToListAsync();

            // Load menu grants and their feature overrides
            var menuGrants = await _db.StaffMenuAccesses
                .AsNoTracking()
                .Include(ma => ma.AccessFeatures)
                .Include(ma => ma.Menu)
                .Where(ma => ma.StaffId == staffId)
                .ToListAsync();

            // Build permission status map
            var allowSet = new HashSet<int>();
            var denySet  = new HashSet<int>();

            foreach (var grant in menuGrants.Where(g => g.IsAllow))
            {
                if (!grant.AccessFeatures.Any())
                {
                    // No feature rows means menu visibility only; CRUD/action features must be explicit.
                    var menuFeature = allFeatures.FirstOrDefault(f =>
                        string.Equals(f.FeatureKey, $"MENU_{grant.MenuId}", StringComparison.OrdinalIgnoreCase));
                    if (menuFeature != null) allowSet.Add(menuFeature.PermissionId);
                }
                else
                {
                    foreach (var af in grant.AccessFeatures)
                    {
                        if (af.IsAllow) allowSet.Add(af.PermissionId);
                        else            denySet.Add(af.PermissionId);
                    }
                }
            }

            var result = allFeatures.Select(f =>
            {
                string status = denySet.Contains(f.PermissionId) ? "DENY"
                              : allowSet.Contains(f.PermissionId) ? "ALLOW"
                              : "INHERIT";
                return new
                {
                    featureKey  = f.FeatureKey,
                    featureName = f.FeatureName,
                    module      = f.Module,
                    status = effectiveKeys.Contains(f.FeatureKey) ? "ALLOW" : "INHERIT",
                    hasOverride = effectiveKeys.Contains(f.FeatureKey)
                };
            });

            return Ok(new { staffId, permissions = result });
        }

        /// <summary>
        /// Bulk-save permissions for a user from the admin UI.
        /// Send { "featureKey": "ALLOW"|"DENY"|"INHERIT" } for each feature to update.
        /// POST /api/rbac/staff/{staffId}/bulk-overrides
        /// </summary>
        [HttpPost("staff/{staffId:guid}/bulk-overrides")]
        public async Task<IActionResult> BulkSetOverrides(
            Guid staffId,
            [FromBody] Dictionary<string, string> overrides)
        {
            if (!await HasAccessControlPermissionAsync("EDIT"))
                return Forbid();

            if (overrides == null || overrides.Count == 0)
                return BadRequest(new { message = "No overrides provided." });

            var (saved, skipped, message) = await _rbac.BulkApplyOverridesAsync(
                staffId, overrides, CurrentUserId);

            if (message == "Staff not found.")
                return NotFound(new { message });

            return Ok(new { message, saved, skipped });
        }

        [HttpPost("staff/bulk-overrides")]
        public async Task<IActionResult> BulkSetOverridesForStaff([FromBody] MultiStaffOverridesDto request)
        {
            if (!await HasAccessControlPermissionAsync("EDIT"))
                return Forbid();

            if (request.StaffIds == null || request.StaffIds.Count == 0)
                return BadRequest(new { message = "Select at least one staff member." });
            if (request.Overrides == null || request.Overrides.Count == 0)
                return BadRequest(new { message = "No overrides provided." });
            var result = await _rbac.BulkApplyOverridesToStaffAsync(request.StaffIds, request.Overrides, CurrentUserId);
            if (result.UsersUpdated == 0) return BadRequest(new { message = result.Message });
            return Ok(new { message = result.Message, usersUpdated = result.UsersUpdated, saved = result.Saved, skipped = result.Skipped });
        }

        /// <summary>
        /// Wipe all UserPermissionOverrides for a staff member (Revoke All — one fast DB trip).
        /// POST /api/rbac/staff/{staffId}/clear-overrides
        /// </summary>
        [HttpPost("staff/{staffId:guid}/clear-overrides")]
        public async Task<IActionResult> ClearStaffOverrides(Guid staffId)
        {
            if (!await HasAccessControlPermissionAsync("DELETE"))
                return Forbid();

            if (!await _db.StaffVacancies.AsNoTracking().AnyAsync(s => s.StaffId == staffId))
                return NotFound(new { message = "Staff not found." });

            var cleared = await _rbac.ClearStaffOverridesAsync(staffId);
            return Ok(new { message = $"Cleared {cleared} override(s).", cleared });
        }

        // ── HasAccess check ───────────────────────────────────────────────────

        /// <summary>
        /// Check if a staff member has access to a specific feature.
        /// Resolution: UserOverride → RoleDefault → Matrix → false
        /// </summary>
        [HttpGet("staff/{staffId:guid}/has-access/{*featureKey}")]
        public async Task<IActionResult> HasAccess(Guid staffId, string featureKey)
        {
            var key = ResolveFeatureKey(featureKey) ?? featureKey;
            return Ok(new
            {
                staffId,
                featureKey = key,
                hasAccess = await _rbac.HasAccessAsync(staffId, key)
            });
        }

        /// <summary>
        /// Get all effective permissions for a staff member.
        /// Returns both a flat list of allowed featureKeys (for UI checkbox hydration)
        /// and a detailed breakdown per feature (for admin/debug view).
        /// GET /api/rbac/staff/{staffId}/effective-permissions
        /// </summary>
        [HttpGet("staff/{staffId:guid}/effective-permissions")]
        public async Task<IActionResult> GetEffectivePermissions(Guid staffId)
        {
            var centrallyResolvedKeys = (await _rbac.GetEffectivePermissionsAsync(staffId))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var centrallyResolvedSet = centrallyResolvedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Load all menu grants with their feature-level flags
            var menuGrants = await _db.StaffMenuAccesses
                .AsNoTracking()
                .Include(ma => ma.AccessFeatures)
                .ThenInclude(af => af.Feature)
                .Where(ma => ma.StaffId == staffId && ma.IsAllow)
                .ToListAsync();

            // Compute the allowed PermissionId set in-memory (same logic as RbacService)
            var allowedPermIds = new HashSet<int>();
            var grantedMenuIds = menuGrants.Select(ma => ma.MenuId).ToHashSet();
            var grantedMenuFeatureKeys = grantedMenuIds.Select(menuId => $"MENU_{menuId}").ToArray();
            var menuFeatureIds = await _db.Features.AsNoTracking()
                .Where(feature => grantedMenuFeatureKeys.Contains(feature.FeatureKey))
                .Select(feature => new { feature.FeatureKey, feature.PermissionId })
                .ToListAsync();

            foreach (var grant in menuGrants)
            {
                if (!grant.AccessFeatures.Any())
                {
                    // No feature-level rows means menu visibility only; CRUD/action features must be explicit.
                    var feature = menuFeatureIds.FirstOrDefault(item =>
                        string.Equals(item.FeatureKey, $"MENU_{grant.MenuId}", StringComparison.OrdinalIgnoreCase));
                    if (feature != null) allowedPermIds.Add(feature.PermissionId);
                }
                else
                {
                    foreach (var af in grant.AccessFeatures.Where(af => af.IsAllow))
                        allowedPermIds.Add(af.PermissionId);
                    // IsAllow=false rows are explicit denies — do not add
                }
            }

            // Map PermissionIds back to FeatureKeys for the UI
            var allowedFeatureKeys = allowedPermIds.Count == 0
                ? new List<string>()
                : await _db.Features.AsNoTracking()
                    .Where(f => allowedPermIds.Contains(f.PermissionId))
                    .Select(f => f.FeatureKey)
                    .ToListAsync();
            allowedFeatureKeys = centrallyResolvedKeys;

            // Build detailed view for admin display
            var allFeatures = await _db.Features.AsNoTracking()
                .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
                .ToListAsync();

            var denySet = menuGrants
                .SelectMany(ma => ma.AccessFeatures)
                .Where(af => !af.IsAllow)
                .Select(af => af.PermissionId)
                .ToHashSet();

            var detailed = allFeatures.Select(f =>
            {
                bool hasAccess = centrallyResolvedSet.Contains(f.FeatureKey);
                string source  = denySet.Contains(f.PermissionId)    ? "MenuFeatureDeny"
                               : (hasAccess && menuGrants.Count > 0) ? "MenuGrant"
                               : hasAccess                            ? "RoleDefault"
                               :                                        "Denied";
                return new
                {
                    featureKey  = f.FeatureKey,
                    featureName = f.FeatureName,
                    module      = f.Module,
                    hasAccess,
                    source
                };
            }).ToList();

            return Ok(new
            {
                staffId,
                // Flat list → used by the frontend to hydrate checkboxes
                allowedFeatureKeys,
                // Detailed list → used by admin UI to show per-feature status
                detailed,
                // Summary counts
                totalAllowed = allowedFeatureKeys.Count,
                totalDenied  = denySet.Count,
                hasAnyGrant  = menuGrants.Count > 0
            });
        }

        // ── Department Matrix ─────────────────────────────────────────────────

        /// <summary>
        /// Get the full permission matrix for a department.
        /// Each cell shows: effectiveAccess, source (UserOverride/RoleDefault/Matrix/Denied), hasUserOverride.
        /// Optimized — no N+1 queries.
        /// </summary>
        [HttpGet("matrix/{deptId:int}")]
        public async Task<IActionResult> GetMatrix(int deptId)
        {
            if (!await HasAccessControlPermissionAsync("VIEW"))
                return Forbid();

            return Ok(await _rbac.GetDepartmentMatrixAsync(deptId));
        }

        // ── Role Permissions ──────────────────────────────────────────────────

        /// <summary>
        /// Get default permissions for a job title.
        /// Example: GET /api/rbac/roles/Agent/permissions?deptId=4
        /// </summary>
        [HttpGet("roles/{jobTitle}/permissions")]
        public async Task<IActionResult> GetRolePermissions(
            string jobTitle, [FromQuery] int? deptId)
        {
            if (!await HasAccessControlPermissionAsync("VIEW"))
                return Forbid();

            return Ok(await _rbac.GetRolePermissionsAsync(jobTitle, deptId));
        }

        /// <summary>
        /// Set default permissions for a job title in a department.
        /// Send a dictionary of featureKey → isAllowed.
        /// Example body: { "ATTENDANCE_VIEW": true, "EMPLOYEE_EDIT": false }
        /// </summary>
        [HttpPut("roles/{jobTitle}/permissions")]
        public async Task<IActionResult> SetRolePermissions(
            string jobTitle,
            [FromQuery] int? deptId,
            [FromBody] Dictionary<string, bool> permissions)
        {
            if (!await HasAccessControlPermissionAsync("EDIT"))
                return Forbid();

            if (permissions == null || !permissions.Any())
                return BadRequest(new { message = "No permissions provided." });

            // Check Features table is not empty first
            var featuresExist = await _db.Features.AnyAsync();
            if (!featuresExist)
                return BadRequest(new
                {
                    message = "Features table is empty. Seed features first via POST /api/rbac/seed-features.",
                    hint    = "RolePermissions.FeatureKey has a FK to Features table — the key must exist there first."
                });

            var (count, invalidKeys) = await _rbac.SetRolePermissionsAsync(
                jobTitle, deptId, permissions, CurrentUserId);

            if (count == 0 && invalidKeys.Any())
                return BadRequest(new
                {
                    message     = "No permissions saved. All provided FeatureKeys are invalid.",
                    invalidKeys,
                    hint        = "Use GET /api/access/features to see valid feature keys."
                });

            return Ok(new
            {
                message     = $"{count} role permissions saved for '{jobTitle}'.",
                jobTitle,
                deptId,
                saved       = count,
                invalidKeys = invalidKeys.Any() ? invalidKeys : null
            });
        }

        // ── User Overrides ────────────────────────────────────────────────────

        /// <summary>
        /// Get all menu grants and feature overrides for a staff member.
        /// Reads from StaffMenuAccess + AccessFeatures (2-tier RBAC).
        /// </summary>
        [HttpGet("staff/{staffId:guid}/overrides")]
        public async Task<IActionResult> GetOverrides(Guid staffId)
        {
            if (!await HasAccessControlPermissionAsync("VIEW"))
                return Forbid();

            var grants = await _db.StaffMenuAccesses
                .AsNoTracking()
                .Include(ma => ma.Menu)
                .Include(ma => ma.AccessFeatures).ThenInclude(af => af.Feature)
                .Where(ma => ma.StaffId == staffId)
                .Select(ma => new
                {
                    menuId      = ma.MenuId,
                    menuTitle   = ma.Menu != null ? ma.Menu.Title : $"Menu {ma.MenuId}",
                    isAllow     = ma.IsAllow,
                    grantedDate = ma.GrantedDate,
                    features    = ma.AccessFeatures.Select(af => new
                    {
                        permissionId = af.PermissionId,
                        featureKey   = af.Feature != null ? af.Feature.FeatureKey : string.Empty,
                        featureName  = af.Feature != null ? af.Feature.FeatureName : string.Empty,
                        module       = af.Feature != null ? af.Feature.Module : string.Empty,
                        isAllow      = af.IsAllow
                    }).ToList()
                })
                .ToListAsync();

            return Ok(grants);
        }

        /// <summary>
        /// Get the sidebar menu filtered by the logged-in user's permissions.
        /// Menu items the user can't access are removed. Empty groups are removed.
        /// </summary>
        [HttpGet("sidebar")]
        public async Task<IActionResult> GetFilteredSidebar()
        {
            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized(new { message = "Not authenticated." });

            if (IsSuperAdminUser)
            {
                var allMenus = await _rbac.GetFilteredSidebarAsync(Guid.Empty);
                return Ok(allMenus);
            }

            if (IsTenantAdminUser || User.IsInRole("Admin"))
            {
                var tenantId = await CurrentTenantIdAsync();
                if (!tenantId.HasValue) return Ok(new List<object>());
                return Ok(await _rbac.GetTenantSidebarAsync(tenantId.Value));
            }

            var person = await _db.Persons
                .AsNoTracking()
                .Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId);

            if (person == null)
                return Ok(new List<object>());

            if (await _personAccess.HasPersonGrantsAsync(person.PersonId))
                return Ok(await _personAccess.GetGrantedSidebarAsync(person.PersonId));

            if (person.Staff == null)
                return Ok(new List<object>());

            var sidebar = await _rbac.GetFilteredSidebarAsync(person.Staff.StaffId);
            return Ok(sidebar);
        }

        /// <summary>
        /// Set a user-specific permission override with ALLOW / DENY / INHERIT.
        /// featureKey in URL path (URL-encoded) or query ?featureKey=DEPT_VIEW
        /// </summary>
        [HttpPut("staff/{staffId:guid}/overrides/{*featureKey}")]
        [HttpPut("staff/{staffId:guid}/overrides")]
        public async Task<IActionResult> SetOverride(
            Guid staffId,
            string? featureKey,
            [FromQuery] string? key,
            [FromBody] SetOverrideDto dto)
        {
            if (!await HasAccessControlPermissionAsync("EDIT"))
                return Forbid();

            var resolvedKey = ResolveFeatureKey(featureKey ?? key);
            if (string.IsNullOrWhiteSpace(resolvedKey))
                return BadRequest(new { message = "featureKey is required in route or query string." });

            if (!Enum.TryParse<PermissionStatus>(dto.Status, true, out var status))
                return BadRequest(new { message = $"Invalid status '{dto.Status}'. Use ALLOW, DENY, or INHERIT." });

            var (ok, msg) = await _rbac.SetUserOverrideAsync(
                staffId, resolvedKey, status, CurrentUserId, dto.Reason);

            if (!ok) return msg.Contains("not found")
                ? NotFound(new { message = msg })
                : BadRequest(new { message = msg });

            return Ok(new { message = msg, staffId, featureKey = resolvedKey, status = status.ToString() });
        }

        /// <summary>
        /// Remove a user-specific override — reverts to role default.
        /// </summary>
        [HttpDelete("staff/{staffId:guid}/overrides/{*featureKey}")]
        [HttpDelete("staff/{staffId:guid}/overrides")]
        public async Task<IActionResult> RemoveOverride(
            Guid staffId,
            string? featureKey,
            [FromQuery] string? key)
        {
            if (!await HasAccessControlPermissionAsync("DELETE"))
                return Forbid();

            var resolvedKey = ResolveFeatureKey(featureKey ?? key);
            if (string.IsNullOrWhiteSpace(resolvedKey))
                return BadRequest(new { message = "featureKey is required in route or query string." });

            var (ok, msg) = await _rbac.RemoveUserOverrideAsync(staffId, resolvedKey);
            return ok
                ? Ok(new { message = msg, staffId, featureKey = resolvedKey })
                : NotFound(new { message = msg });
        }

        private static string? ResolveFeatureKey(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return Uri.UnescapeDataString(raw.Trim()).Trim('/');
        }

        // ── Menu bundle access (admin grants sidebar section + child features) ─

        /// <summary>
        /// Menu tree with all permission keys per item (for admin access UI).
        /// </summary>
        [HttpGet("menu-permissions")]
        public async Task<IActionResult> GetMenuPermissionTree()
        {
            if (!await HasAccessControlPermissionAsync("VIEW"))
                return Forbid();

            return Ok(await _rbac.GetMenuPermissionTreeAsync());
        }

        [HttpPost("staff/{staffId:guid}/grant-menu/{menuId:int}")]
        public async Task<IActionResult> GrantMenuAccess(
            Guid staffId, int menuId, [FromBody] GrantMenuAccessDto? dto)
        {
            if (!await HasAccessControlPermissionAsync("ADD"))
                return Forbid();

            var personId = await _db.StaffVacancies.AsNoTracking()
                .Where(s => s.StaffId == staffId)
                .Select(s => s.PersonId)
                .FirstOrDefaultAsync();

            if (!personId.HasValue)
                return BadRequest(new { message = "Staff is not linked to a person record." });

            var (ok, msg, menuIds, keys) = await _personAccess.GrantMenuAsync(
                personId.Value, menuId, CurrentUserId, dto?.Reason);

            return ok
                ? Ok(new { message = msg, grantedMenuIds = menuIds, grantedFeatureKeys = keys, menuId, staffId, personId })
                : BadRequest(new { message = msg });
        }

        /// <summary>Grant menu + features by PersonId (preferred — saves PersonMenus + PersonFeatures).</summary>
        [HttpPost("persons/{personId:guid}/grant-menu/{menuId:int}")]
        public async Task<IActionResult> GrantMenuToPerson(
            Guid personId, int menuId, [FromBody] GrantMenuAccessDto? dto)
        {
            if (!await HasAccessControlPermissionAsync("ADD"))
                return Forbid();

            var (ok, msg, menuIds, keys) = await _personAccess.GrantMenuAsync(
                personId, menuId, CurrentUserId, dto?.Reason);

            return ok
                ? Ok(new { message = msg, grantedMenuIds = menuIds, grantedFeatureKeys = keys, personId, menuId })
                : BadRequest(new { message = msg });
        }

        /// <summary>View menus and features granted to a person.</summary>
        [HttpGet("persons/{personId:guid}/access")]
        public async Task<IActionResult> GetPersonAccess(Guid personId)
        {
            if (!await HasAccessControlPermissionAsync("VIEW"))
                return Forbid();

            return Ok(await _personAccess.GetPersonAccessSummaryAsync(personId));
        }

        /// <summary>
        /// Revoke menu-bundle (PersonMenus + PersonFeatures).
        /// </summary>
        [HttpPost("staff/{staffId:guid}/revoke-menu/{menuId:int}")]
        public async Task<IActionResult> RevokeMenuAccess(Guid staffId, int menuId)
        {
            if (!await HasAccessControlPermissionAsync("DELETE"))
                return Forbid();

            var personId = await _db.StaffVacancies.AsNoTracking()
                .Where(s => s.StaffId == staffId)
                .Select(s => s.PersonId)
                .FirstOrDefaultAsync();

            if (!personId.HasValue)
                return BadRequest(new { message = "Staff is not linked to a person record." });

            var (ok, msg) = await _personAccess.RevokeMenuAsync(personId.Value, menuId);
            return ok
                ? Ok(new { message = msg, menuId, staffId, personId })
                : BadRequest(new { message = msg });
        }

        [HttpPost("persons/{personId:guid}/revoke-menu/{menuId:int}")]
        public async Task<IActionResult> RevokeMenuFromPerson(Guid personId, int menuId)
        {
            if (!await HasAccessControlPermissionAsync("DELETE"))
                return Forbid();

            var (ok, msg) = await _personAccess.RevokeMenuAsync(personId, menuId);
            return ok ? Ok(new { message = msg, personId, menuId }) : BadRequest(new { message = msg });
        }

        /// <summary>
        /// Preview feature keys that would be granted for a menu subtree.
        /// </summary>
        [HttpGet("menus/{menuId:int}/feature-keys")]
        public async Task<IActionResult> GetMenuFeatureKeys(int menuId)
        {
            if (!await HasAccessControlPermissionAsync("VIEW"))
                return Forbid();

            return Ok(new { menuId, featureKeys = await _rbac.GetMenuFeatureKeysAsync(menuId) });
        }

        // ── Seed Features ─────────────────────────────────────────────────────

        /// <summary>
        /// Seed MENU_{id}, MENU_{id}_VIEW/ADD/EDIT/DELETE into Features for every active menu.
        /// Also seeds static system feature keys (DEPT_VIEW, EMPLOYEE_VIEW, etc.).
        /// Safe to call multiple times — idempotent.
        /// POST /api/rbac/seed-features
        /// </summary>
        [HttpPost("seed-features")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> SeedFeatures()
        {
            // ── 1. Seed MENU_{id} keys from the Menus table ───────────────────
            var (menuAdded, menuSkipped) = await _rbac.SeedMenuFeaturesAsync();

            // ── 2. Seed static system feature keys ────────────────────────────
            var staticFeatures = new List<Feature>
            {
                // Organization
                new() { FeatureKey = "DEPT_VIEW",             FeatureName = "View Department",          Module = "Organization" },
                new() { FeatureKey = "DEPT_VIEW_ALL",         FeatureName = "View All Departments",     Module = "Organization" },
                new() { FeatureKey = "DEPT_CREATE",           FeatureName = "Create Department",        Module = "Organization" },
                new() { FeatureKey = "DEPT_EDIT",             FeatureName = "Edit Department",          Module = "Organization" },
                new() { FeatureKey = "DEPT_DELETE",           FeatureName = "Delete Department",        Module = "Organization" },
                // Vacancy
                new() { FeatureKey = "VACANCY_VIEW",          FeatureName = "View Vacancies",           Module = "Vacancy" },
                new() { FeatureKey = "VACANCY_CREATE",        FeatureName = "Create Vacancy",           Module = "Vacancy" },
                new() { FeatureKey = "VACANCY_EDIT",          FeatureName = "Edit Vacancy",             Module = "Vacancy" },
                new() { FeatureKey = "VACANCY_DELETE",        FeatureName = "Delete Vacancy",           Module = "Vacancy" },
                new() { FeatureKey = "VACANCY_ASSIGN",        FeatureName = "Assign Staff to Vacancy",  Module = "Vacancy" },
                // Employee
                new() { FeatureKey = "EMPLOYEE_VIEW",         FeatureName = "View Employees",           Module = "Employee" },
                new() { FeatureKey = "EMPLOYEE_VIEW_ALL",     FeatureName = "View All Employees",       Module = "Employee" },
                new() { FeatureKey = "EMPLOYEE_EDIT",         FeatureName = "Edit Employee",            Module = "Employee" },
                new() { FeatureKey = "EMPLOYEE_DELETE",       FeatureName = "Delete Employee",          Module = "Employee" },
                new() { FeatureKey = "EMPLOYEE_TRANSFER",     FeatureName = "Transfer Employee",        Module = "Employee" },
                // Person
                new() { FeatureKey = "PERSON_VIEW",           FeatureName = "View Persons",             Module = "Person" },
                new() { FeatureKey = "PERSON_VIEW_ALL",       FeatureName = "View All Persons",         Module = "Person" },
                new() { FeatureKey = "PERSON_REGISTER",       FeatureName = "Register Person",          Module = "Person" },
                new() { FeatureKey = "PERSON_EDIT",           FeatureName = "Edit Person",              Module = "Person" },
                new() { FeatureKey = "PERSON_DELETE",         FeatureName = "Delete Person",            Module = "Person" },
                new() { FeatureKey = "PERSON_RESET_PASSWORD", FeatureName = "Reset Person Password",   Module = "Person" },
                // Access Groups
                new() { FeatureKey = "ACCESS_GROUP_VIEW",     FeatureName = "View Access Groups",       Module = "Access" },
                new() { FeatureKey = "ACCESS_GROUP_CREATE",   FeatureName = "Create Access Group",      Module = "Access" },
                new() { FeatureKey = "ACCESS_GROUP_EDIT",     FeatureName = "Edit Access Group",        Module = "Access" },
                new() { FeatureKey = "ACCESS_GROUP_DELETE",   FeatureName = "Delete Access Group",      Module = "Access" },
                new() { FeatureKey = "ACCESS_GROUP_ASSIGN",   FeatureName = "Assign Group to Staff",    Module = "Access" },
                // Location
                new() { FeatureKey = "LOCATION_VIEW",         FeatureName = "View Locations",           Module = "Location" },
                new() { FeatureKey = "LOCATION_MANAGE",       FeatureName = "Manage Locations",         Module = "Location" },
            };

            var existingKeys = await _db.Features.Select(f => f.FeatureKey).ToHashSetAsync();
            var staticToAdd  = staticFeatures.Where(f => !existingKeys.Contains(f.FeatureKey)).ToList();

            if (staticToAdd.Any())
            {
                _db.Features.AddRange(staticToAdd);
                await _db.SaveChangesAsync();
            }

            // ── 3. Link menus to their MENU_{id} features ─────────────────────
            var linkCount = await LinkMenusToFeaturesAsync();

            return Ok(new
            {
                message      = "Seed complete.",
                menuFeatures = new { added = menuAdded, skipped = menuSkipped },
                staticFeatures = new { added = staticToAdd.Count, skipped = existingKeys.Count },
                menuPermissionsLinked = linkCount,
                totalFeatures  = existingKeys.Count + menuAdded + staticToAdd.Count,
                nextStep       = linkCount > 0 
                    ? "Menus are now linked to features. Grant access via: PUT /api/rbac/staff/{staffId}/overrides/{featureKey}" 
                    : "Use POST /api/rbac/link-menus-to-features to establish menu-feature relationships."
            });
        }

        /// <summary>
        /// Links active menus to their corresponding MENU_{id} features in MenuPermissions table.
        /// This is required for menus to show up in the sidebar after permissions are granted.
        /// POST /api/rbac/link-menus-to-features
        /// </summary>
        [HttpPost("link-menus-to-features")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> LinkMenusToFeatures()
        {
            var count = await LinkMenusToFeaturesAsync();
            return Ok(new
            {
                message = $"Linked {count} menus to their features in MenuPermissions table.",
                count,
                nextStep = count > 0 
                    ? "Users will now see menus they have permission for." 
                    : "All menus are already linked. If users still don't see menus, check UserPermissionOverrides table."
            });
        }

        /// <summary>
        /// Helper method to link menus to features via MenuPermissions table.
        /// Ensures that when admin grants MENU_{id} permission, the menu actually appears.
        /// </summary>
        private async Task<int> LinkMenusToFeaturesAsync()
        {
            // Get all active menus
            var activeMenus = await _db.Menus
                .Where(m => m.IsActive)
                .Select(m => m.Id)
                .ToListAsync();

            // Get Features table lookup: MENU_id → PermissionId
            var menuFeatures = await _db.Features
                .Where(f => f.FeatureKey.StartsWith("MENU_") && !f.FeatureKey.Contains("_VIEW") 
                         && !f.FeatureKey.Contains("_ADD") && !f.FeatureKey.Contains("_EDIT") 
                         && !f.FeatureKey.Contains("_DELETE"))
                .ToDictionaryAsync(f => f.FeatureKey, f => f.PermissionId);

            // Get existing MenuPermissions to avoid duplicates
            var existingLinks = await _db.MenuPermissions
                .Select(mp => new { mp.MenuId, mp.PermissionId })
                .ToHashSetAsync();

            var added = 0;
            foreach (var menuId in activeMenus)
            {
                var featureKey = $"MENU_{menuId}";
                if (menuFeatures.TryGetValue(featureKey, out int permissionId))
                {
                    // Check if link already exists
                    if (!existingLinks.Contains(new { MenuId = menuId, PermissionId = permissionId }))
                    {
                        _db.MenuPermissions.Add(new MenuPermission
                        {
                            MenuId = menuId,
                            PermissionId = permissionId
                        });
                        added++;
                    }
                }
            }

            if (added > 0)
            {
                await _db.SaveChangesAsync();
            }

            return added;
        }
    }

    public class SetOverrideDto
    {
        /// <summary>ALLOW, DENY, or INHERIT</summary>
        public string  Status { get; set; } = "INHERIT";
        public string? Reason { get; set; }
    }

    public sealed class MultiStaffOverridesDto
    {
        public List<Guid> StaffIds { get; set; } = new();
        public Dictionary<string, string> Overrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

