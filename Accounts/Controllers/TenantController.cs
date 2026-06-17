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
                .Select(t => new
                {
                    t.Id,
                    t.TenantName,
                    t.TenantCode,
                    t.IsActive,
                    t.CreatedOnUtc,
                    t.OrganizationTreeId,
                    orgNodeName  = t.OrganizationNode != null ? t.OrganizationNode.Name : null,
                    orgNodeLabel = t.OrganizationNode != null ? t.OrganizationNode.Label : null
                })
                .ToListAsync();

            return Ok(tenants);
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
                    mp.IsAllow
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

        /// <summary>Enable or disable a tenant.</summary>
        [HttpPut("{id:int}/toggle")]
        public async Task<IActionResult> Toggle(int id)
        {
            var tenant = await _db.Tenants.FindAsync(id);
            if (tenant == null) return NotFound(new { message = $"Tenant {id} not found." });

            tenant.IsActive = !tenant.IsActive;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message  = $"Tenant '{tenant.TenantName}' is now {(tenant.IsActive ? "active" : "disabled")}.",
                isActive = tenant.IsActive
            });
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

            // Validate that all requested menu IDs exist
            var validMenuIds = await _db.Menus
                .Where(m => menuIds.Contains(m.Id) && m.IsActive)
                .Select(m => m.Id)
                .ToListAsync();

            // Remove all existing permissions and re-add
            _db.TenantMenuPermissions.RemoveRange(tenant.MenuPermissions);

            foreach (var menuId in validMenuIds)
            {
                _db.TenantMenuPermissions.Add(new TenantMenuPermission
                {
                    TenantId        = id,
                    MenuId          = menuId,
                    IsAllow         = true,
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
    }

    // ── Request DTO ───────────────────────────────────────────────────────────

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
