using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Accounts.Controllers
{
    /// <summary>
    /// Super Admin only — manages SaaS Tenants.
    ///
    /// POST /api/tenants        → Create a new tenant (atomic transaction)
    /// GET  /api/tenants        → List all tenants
    /// GET  /api/tenants/{id}   → Single tenant detail
    /// PUT  /api/tenants/{id}/toggle → Enable / disable a tenant
    /// </summary>
    [ApiController]
    [Route("api/tenants")]
    [Authorize(Roles = "SuperAdmin")]
    [Produces("application/json")]
    public class TenantController : ControllerBase
    {
        private readonly ApplicationDbContext        _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole>   _roleManager;
        private readonly IOrganizationService        _orgService;

        public TenantController(
            ApplicationDbContext         db,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole>    roleManager,
            IOrganizationService         orgService)
        {
            _db          = db;
            _userManager = userManager;
            _roleManager = roleManager;
            _orgService  = orgService;
        }

        // ── GET /api/tenants ──────────────────────────────────────────────────

        /// <summary>List all tenants with their org node info.</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var tenants = await _db.Tenants
                .AsNoTracking()
                .Include(t => t.OrganizationNode)
                .OrderBy(t => t.TenantName)
                .ToListAsync();

            var nodes = await _db.OrganizationTree.AsNoTracking().ToListAsync();
            var nodeById = nodes.ToDictionary(n => n.Id);
            bool IsHierarchyActive(int nodeId)
            {
                var visited = new HashSet<int>();
                int? current = nodeId;
                while (current.HasValue && nodeById.TryGetValue(current.Value, out var node) && visited.Add(node.Id))
                {
                    if (!node.IsActive) return false;
                    current = node.ParentId;
                }
                return true;
            }

            var response = tenants.Select(t => new
            {
                t.Id,
                t.TenantName,
                t.TenantCode,
                t.IsActive,
                effectiveIsActive = t.IsActive && IsHierarchyActive(t.OrganizationTreeId),
                t.CreatedOnUtc,
                t.OrganizationTreeId,
                orgNodeName = t.OrganizationNode?.Name,
                orgNodeLabel = t.OrganizationNode?.Label,
                parentOrgNodeId = t.OrganizationNode?.ParentId,
                childCompanyCount = nodes.Count(n =>
                    n.Label.Equals("Company", StringComparison.OrdinalIgnoreCase) &&
                    IsDescendantOf(n.Id, t.OrganizationTreeId, nodeById))
            }).ToList();

            return Ok(response);
        }

        // ── GET /api/tenants/{id} ─────────────────────────────────────────────

        /// <summary>Single tenant with granted menus and staff count.</summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var tenant = await _db.Tenants
                .AsNoTracking()
                .Include(t => t.OrganizationNode)
                .Include(t => t.MenuPermissions).ThenInclude(mp => mp.Menu)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tenant == null)
                return NotFound(new { message = $"Tenant {id} not found." });

            var staffCount = await _db.StaffVacancies
                .AsNoTracking()
                .Where(s => s.TenantId == id)
                .CountAsync();

            return Ok(new
            {
                tenant.Id,
                tenant.TenantName,
                tenant.TenantCode,
                tenant.IsActive,
                tenant.OrganizationTreeId,
                orgNodeName  = tenant.OrganizationNode?.Name,
                staffCount,
                grantedMenus = tenant.MenuPermissions.Select(mp => new
                {
                    menuId    = mp.MenuId,
                    menuTitle = mp.Menu?.Title,
                    mp.IsAllow,
                    mp.CanView,
                    mp.CanAdd,
                    mp.CanEdit,
                    mp.CanDelete
                })
            });
        }

        // ── POST /api/tenants ─────────────────────────────────────────────────

        /// <summary>
        /// Create a new Tenant atomically.
        ///
        /// Transaction steps (all-or-nothing):
        ///   1. Create a Company node in OrganizationTree (under the specified parent).
        ///   2. Insert a Tenants row pointing at that org node.
        ///   3. Create an ApplicationUser as the Tenant Admin with auto-generated credentials.
        ///   4. Assign the TenantAdmin role to the new user.
        ///   5. Write initial TenantMenuPermissions (if grantedMenuIds provided).
        ///
        /// Returns the new tenant, generated admin credentials, and org node.
        /// </summary>
        [HttpPost]
        [HttpPost("create")]
        public async Task<IActionResult> Create([FromBody] CreateTenantDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var creatorId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // ── Validate parent org node ──────────────────────────────────────
            if (dto.ParentOrgNodeId.HasValue)
            {
                var parent = await _db.OrganizationTree.FindAsync(dto.ParentOrgNodeId.Value);
                if (parent == null)
                    return BadRequest(new { message = $"Parent org node {dto.ParentOrgNodeId} not found." });
            }

            // ── Check TenantCode uniqueness ───────────────────────────────────
            var code = dto.TenantCode.Trim().ToUpper();
            if (await _db.Tenants.AnyAsync(t => t.TenantCode == code))
                return Conflict(new { message = $"TenantCode '{code}' is already in use." });

            var orgLabel = string.IsNullOrWhiteSpace(dto.OrgLabel) ? "Company" : dto.OrgLabel.Trim();
            if (!orgLabel.Equals("Company", StringComparison.OrdinalIgnoreCase)
                && !orgLabel.Equals("Group", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "OrgLabel must be 'Company' or 'Group'." });

            // ── Use the retry execution strategy required by SqlServerRetryingExecutionStrategy ──
            // EnableRetryOnFailure prevents direct BeginTransaction; we must wrap all
            // database work in strategy.ExecuteAsync so the engine can retry on transient faults.
            var strategy = _db.Database.CreateExecutionStrategy();

            // Capture return value from inside the strategy lambda
            IActionResult? result = null;

            await strategy.ExecuteAsync(async () =>
            {
                // Each invocation of ExecuteAsync starts fresh — track what we create
                int? createdOrgNodeId  = null;
                int? createdTenantId   = null;
                string? createdUserId  = null;

                await using var tx = await _db.Database.BeginTransactionAsync();
                try
                {
                    // ── STEP 1: Org node ──────────────────────────────────────
                    var (orgNode, _) = await _orgService.CreateAsync(new CreateOrgNodeDto
                    {
                        Name     = dto.CompanyName.Trim(),
                        Code     = code,
                        Label    = orgLabel,
                        ParentId = dto.ParentOrgNodeId,
                        FlagUrl  = null
                    });
                    createdOrgNodeId = orgNode.Id;

                    // ── STEP 2: Tenant row ────────────────────────────────────
                    var tenant = new Tenant
                    {
                        TenantName         = dto.CompanyName.Trim(),
                        TenantCode         = code,
                        OrganizationTreeId = orgNode.Id,
                        IsActive           = true,
                        CreatedOnUtc       = DateTime.UtcNow,
                        CreatedByUserId    = creatorId
                    };
                    _db.Tenants.Add(tenant);
                    await _db.SaveChangesAsync();
                    createdTenantId = tenant.Id;

                    // ── STEP 3: Credentials ───────────────────────────────────
                    var loginId  = await GenerateLoginIdAsync(code);
                    var password = !string.IsNullOrWhiteSpace(dto.AdminPassword)
                        ? dto.AdminPassword.Trim()
                        : $"{loginId}@";
                    var email    = dto.AdminEmail?.Trim() ?? $"admin@{code.ToLower()}.com";

                    // ── STEP 4: Tenant Admin user ─────────────────────────────
                    var adminUser = new ApplicationUser
                    {
                        UserName       = loginId,
                        Email          = email,
                        EmailConfirmed = true,
                        TenantId       = tenant.Id,
                        IsTenantAdmin  = true,
                        IsSuperAdmin   = false
                    };

                    var createResult = await _userManager.CreateAsync(adminUser, password);
                    if (!createResult.Succeeded)
                    {
                        await tx.RollbackAsync();
                        result = BadRequest(new
                        {
                            message = "Failed to create Tenant Admin account.",
                            errors  = createResult.Errors.Select(e => e.Description)
                        });
                        return;
                    }
                    createdUserId = adminUser.Id;

                    // ── STEP 5: Role + claims ─────────────────────────────────
                    if (!await _roleManager.RoleExistsAsync("TenantAdmin"))
                        await _roleManager.CreateAsync(new IdentityRole("TenantAdmin"));

                    await _userManager.AddToRoleAsync(adminUser, "TenantAdmin");
                    await _userManager.AddClaimsAsync(adminUser, new[]
                    {
                        new Claim(ITenantService.ClaimTenantId,      tenant.Id.ToString()),
                        new Claim(ITenantService.ClaimIsSuperAdmin,  "false"),
                        new Claim(ITenantService.ClaimIsTenantAdmin, "true")
                    });

                    // ── STEP 6: Initial menu permissions ─────────────────────
                    if (dto.GrantedMenuIds != null && dto.GrantedMenuIds.Any())
                    {
                        var validMenuIds = await _db.Menus
                            .Where(m => dto.GrantedMenuIds.Contains(m.Id) && m.IsActive)
                            .Select(m => m.Id)
                            .ToListAsync();

                        foreach (var menuId in validMenuIds)
                        {
                            _db.TenantMenuPermissions.Add(new TenantMenuPermission
                            {
                                TenantId        = tenant.Id,
                                MenuId          = menuId,
                                IsAllow         = true,
                                GrantedByUserId = creatorId,
                                GrantedOnUtc    = DateTime.UtcNow
                            });
                        }
                        await _db.SaveChangesAsync();
                    }

                    await tx.CommitAsync();

                    result = Ok(new
                    {
                        message = $"Tenant '{tenant.TenantName}' created successfully.",
                        tenant  = new
                        {
                            tenant.Id,
                            tenant.TenantName,
                            tenant.TenantCode,
                            tenant.OrganizationTreeId,
                            orgNodeId   = orgNode.Id,
                            orgNodeName = orgNode.Name
                        },
                        tenantAdmin = new
                        {
                            userId   = adminUser.Id,
                            loginId,
                            password,
                            email,
                            note = "Save these credentials — the password cannot be retrieved again."
                        }
                    });
                }
                catch
                {
                    try { await tx.RollbackAsync(); } catch { /* ignore */ }

                    // Clean up Identity user if it was created before the failure
                    if (createdUserId != null)
                    {
                        var u = await _userManager.FindByIdAsync(createdUserId);
                        if (u != null) await _userManager.DeleteAsync(u);
                    }

                    throw; // re-throw so strategy can log / retry
                }
            });

            return result ?? StatusCode(500, new { message = "Tenant creation failed unexpectedly." });
        }

        /// <summary>Enable or disable a tenant and its mapped Group/Company node.</summary>
        [HttpPut("{id:int}/status")]
        public async Task<IActionResult> SetStatus(int id, [FromBody] SetTenantStatusDto dto)
        {
            var tenant = await _db.Tenants
                .Include(t => t.OrganizationNode)
                .FirstOrDefaultAsync(t => t.Id == id);
            if (tenant == null) return NotFound(new { message = $"Tenant {id} not found." });

            var allNodes = await _db.OrganizationTree.ToListAsync();
            var nodeById = allNodes.ToDictionary(n => n.Id);
            var affectedTenantIds = new HashSet<int> { tenant.Id };
            var affectedCompanyCount = 0;

            tenant.IsActive = dto.IsActive;
            if (tenant.OrganizationNode != null)
                tenant.OrganizationNode.IsActive = dto.IsActive;

            var isGroup = tenant.OrganizationNode?.Label.Equals(
                "Group", StringComparison.OrdinalIgnoreCase) == true;

            if (!dto.IsActive && dto.DisableChildCompanies && isGroup)
            {
                var companyNodeIds = allNodes
                    .Where(n => n.Label.Equals("Company", StringComparison.OrdinalIgnoreCase)
                        && IsDescendantOf(n.Id, tenant.OrganizationTreeId, nodeById))
                    .Select(n => n.Id)
                    .ToHashSet();

                foreach (var node in allNodes.Where(n => companyNodeIds.Contains(n.Id)))
                    node.IsActive = false;

                var childTenants = await _db.Tenants
                    .Where(t => companyNodeIds.Contains(t.OrganizationTreeId))
                    .ToListAsync();
                foreach (var child in childTenants)
                {
                    child.IsActive = false;
                    affectedTenantIds.Add(child.Id);
                }
                affectedCompanyCount = companyNodeIds.Count;
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = dto.IsActive
                    ? $"{tenant.TenantName} has been activated. Users can sign in when all parent levels and their accounts are active."
                    : affectedCompanyCount > 0
                        ? $"{tenant.TenantName} and {affectedCompanyCount} compan{(affectedCompanyCount == 1 ? "y" : "ies")} have been disabled."
                        : $"{tenant.TenantName} has been disabled and its users have lost application access.",
                isActive = tenant.IsActive,
                affectedTenantIds,
                affectedCompanyCount
            });
        }

        /// <summary>Compatibility endpoint for older clients.</summary>
        [HttpPut("{id:int}/toggle")]
        public async Task<IActionResult> Toggle(int id)
        {
            var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            if (tenant == null) return NotFound(new { message = $"Tenant {id} not found." });
            return await SetStatus(id, new SetTenantStatusDto { IsActive = !tenant.IsActive });
        }

        // ── PUT /api/tenants/{id}/menus ───────────────────────────────────────

        /// <summary>
        /// Replace the full set of menus granted to a tenant.
        /// Send an array of menuIds to grant — all others will be revoked.
        /// </summary>
        [HttpPut("{id:int}/menus")]
        public async Task<IActionResult> SetMenus(int id, [FromBody] List<int> menuIds)
        {
            var tenant = await _db.Tenants
                .Include(t => t.MenuPermissions)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tenant == null) return NotFound(new { message = $"Tenant {id} not found." });

            var creatorId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Validate requested IDs and automatically include every active ancestor.
            // This keeps the tenant sidebar hierarchy intact even when the Super Admin
            // selects only a nested menu.
            var activeMenus = await _db.Menus
                .AsNoTracking()
                .Where(m => m.IsActive)
                .Select(m => new { m.Id, m.ParentId })
                .ToListAsync();
            var activeById = activeMenus.ToDictionary(m => m.Id);
            var validMenuIds = menuIds
                .Where(activeById.ContainsKey)
                .Distinct()
                .ToHashSet();

            foreach (var requestedId in validMenuIds.ToList())
            {
                var currentId = requestedId;
                var visited = new HashSet<int>();
                while (activeById.TryGetValue(currentId, out var current)
                       && current.ParentId.HasValue
                       && visited.Add(currentId))
                {
                    validMenuIds.Add(current.ParentId.Value);
                    currentId = current.ParentId.Value;
                }
            }

            // Remove all existing permissions and re-add
            _db.TenantMenuPermissions.RemoveRange(tenant.MenuPermissions);

            foreach (var menuId in validMenuIds.OrderBy(x => x))
            {
                _db.TenantMenuPermissions.Add(new TenantMenuPermission
                {
                    TenantId        = id,
                    MenuId          = menuId,
                    IsAllow         = true,
                    CanView         = true,
                    CanAdd          = true,
                    CanEdit         = true,
                    CanDelete       = true,
                    GrantedByUserId = creatorId,
                    GrantedOnUtc    = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message      = $"Menus updated for tenant '{tenant.TenantName}'.",
                grantedCount = validMenuIds.Count
            });
        }

        [HttpPut("{id:int}/menu-access")]
        public async Task<IActionResult> SetMenuAccess(int id, [FromBody] List<TenantMenuAccessDto> access)
        {
            var tenant = await _db.Tenants
                .Include(t => t.MenuPermissions)
                .FirstOrDefaultAsync(t => t.Id == id);
            if (tenant == null) return NotFound(new { message = $"Tenant {id} not found." });

            var activeMenus = await _db.Menus.AsNoTracking().Where(m => m.IsActive)
                .Select(m => new { m.Id, m.ParentId }).ToListAsync();
            var activeById = activeMenus.ToDictionary(m => m.Id);
            var requested = access
                .Where(a => activeById.ContainsKey(a.MenuId))
                .GroupBy(a => a.MenuId)
                .Select(g => g.Last())
                .ToDictionary(a => a.MenuId);

            // Parents are structural grants with View permission only.
            foreach (var item in requested.Values.ToList())
            {
                var currentId = item.MenuId;
                var visited = new HashSet<int>();
                while (activeById.TryGetValue(currentId, out var current) && current.ParentId.HasValue && visited.Add(currentId))
                {
                    if (!requested.ContainsKey(current.ParentId.Value))
                        requested[current.ParentId.Value] = new TenantMenuAccessDto { MenuId = current.ParentId.Value, CanView = true };
                    currentId = current.ParentId.Value;
                }
            }

            _db.TenantMenuPermissions.RemoveRange(tenant.MenuPermissions);
            var creatorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            _db.TenantMenuPermissions.AddRange(requested.Values.Select(a => new TenantMenuPermission
            {
                TenantId = id, MenuId = a.MenuId, IsAllow = true,
                CanView = a.CanView, CanAdd = a.CanAdd, CanEdit = a.CanEdit, CanDelete = a.CanDelete,
                GrantedByUserId = creatorId, GrantedOnUtc = DateTime.UtcNow
            }));
            await _db.SaveChangesAsync();
            return Ok(new { message = $"Menu access updated for tenant '{tenant.TenantName}'.", grantedCount = requested.Count });
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private async Task<string> GenerateLoginIdAsync(string prefix)
        {
            int seq = 10001;
            string loginId;
            do
            {
                loginId = $"{prefix}{seq}";
                seq++;
            }
            while (await _userManager.FindByNameAsync(loginId) != null);
            return loginId;
        }

        private static bool IsDescendantOf(
            int candidateId,
            int ancestorId,
            IReadOnlyDictionary<int, OrganizationTree> nodes)
        {
            var visited = new HashSet<int>();
            int? current = candidateId;
            while (current.HasValue && nodes.TryGetValue(current.Value, out var node) && visited.Add(node.Id))
            {
                if (node.ParentId == ancestorId) return true;
                current = node.ParentId;
            }
            return false;
        }
    }

    // ── Request DTO ───────────────────────────────────────────────────────────

    public sealed class TenantMenuAccessDto
    {
        public int MenuId { get; set; }
        public bool CanView { get; set; }
        public bool CanAdd { get; set; }
        public bool CanEdit { get; set; }
        public bool CanDelete { get; set; }
    }

    public sealed class SetTenantStatusDto
    {
        public bool IsActive { get; set; }
        public bool DisableChildCompanies { get; set; }
    }

    public class CreateTenantDto
    {
        /// <summary>Display name for the new company / tenant.</summary>
        [Required, MaxLength(150)]
        public string CompanyName { get; set; } = string.Empty;

        /// <summary>
        /// Short unique code used for login ID prefix (e.g. "LT" → LT10001).
        /// Must be 2-6 uppercase letters.
        /// </summary>
        [Required, MaxLength(20), MinLength(2)]
        public string TenantCode { get; set; } = string.Empty;

        /// <summary>Optional parent org node ID (e.g. a Country or Group).</summary>
        public int? ParentOrgNodeId { get; set; }

        /// <summary>Organization node label — Company or Group. Defaults to Company.</summary>
        [MaxLength(50)]
        public string? OrgLabel { get; set; }

        /// <summary>
        /// Optional custom email for the Tenant Admin account.
        /// Auto-generated as admin@{tenantcode}.com if omitted.
        /// </summary>
        [MaxLength(150), EmailAddress]
        public string? AdminEmail { get; set; }

        /// <summary>
        /// Optional custom password for the Tenant Admin account.
        /// Auto-generated as {loginId}@ if omitted.
        /// </summary>
        [MinLength(6)]
        public string? AdminPassword { get; set; }

        /// <summary>
        /// Optional list of Menu IDs to grant to this tenant immediately.
        /// Leave empty to grant no menus initially (add later via PUT /api/tenants/{id}/menus).
        /// </summary>
        public List<int>? GrantedMenuIds { get; set; }
    }
}
