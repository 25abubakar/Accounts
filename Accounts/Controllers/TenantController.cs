using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;

namespace Accounts.Controllers
{
    /// Super Admin only — manages SaaS Tenants.
    ///
    /// POST /api/tenants        → Create a new tenant (atomic transaction)
    /// GET  /api/tenants        → List all tenants
    /// GET  /api/tenants/{id}   → Single tenant detail
    /// PUT  /api/tenants/{id}/toggle → Enable / disable a tenant
    [ApiController]
    [Route("api/tenants")]
    [Authorize(Roles = "SuperAdmin")]
    [Produces("application/json")]
    public class TenantController : ControllerBase
    {
        private readonly ApplicationDbContext        _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole>   _roleManager;
        private readonly IOrganizationService _orgService;
        private readonly PlatformSettingsProvisioningService _provisioning;

        public TenantController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IOrganizationService orgService,
            PlatformSettingsProvisioningService provisioning)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
            _orgService = orgService;
            _provisioning = provisioning;
        }

        // ── GET /api/tenants ──────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var tenants = await _db.Tenants
                .AsNoTracking()
                .Select(t => new
                {
                    t.Id, t.TenantName, t.TenantCode, t.IsActive, t.CreatedOnUtc,
                    t.OrganizationTreeId, t.BrandingAssetType, t.BrandingFileName,
                    t.BrandingUpdatedOnUtc, HasBranding = t.BrandingContent != null,
                    OrgNodeName = t.OrganizationNode != null ? t.OrganizationNode.Name : null,
                    OrgNodeLabel = t.OrganizationNode != null ? t.OrganizationNode.Label : null,
                    ParentOrgNodeId = t.OrganizationNode != null ? t.OrganizationNode.ParentId : null
                })
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
                orgNodeName = t.OrgNodeName,
                orgNodeLabel = t.OrgNodeLabel,
                parentOrgNodeId = t.ParentOrgNodeId,
                brandingAssetType = t.BrandingAssetType,
                brandingFileName = t.BrandingFileName,
                brandingUpdatedOnUtc = t.BrandingUpdatedOnUtc,
                brandingUrl = t.HasBranding
                    ? $"/api/tenant-branding/{t.Id}/content?v={t.BrandingUpdatedOnUtc?.Ticks ?? 0}"
                    : null,
                childCompanyCount = nodes.Count(n =>
                    n.Label.Equals("Company", StringComparison.OrdinalIgnoreCase) &&
                    IsDescendantOf(n.Id, t.OrganizationTreeId, nodeById))
            }).ToList();

            return Ok(response);
        }

        [HttpGet("admins")]
        public async Task<IActionResult> GetAdmins()
        {
            var adminUsers = await _db.Users
                .AsNoTracking()
                .Where(user => user.IsTenantAdmin && user.TenantId.HasValue)
                .Select(user => new
                {
                    user.Id,
                    user.UserName,
                    user.Email,
                    user.PhoneNumber,
                    user.TenantId,
                    user.LockoutEnd
                })
                .ToListAsync();

            var tenantIds = adminUsers
                .Select(user => user.TenantId!.Value)
                .Distinct()
                .ToList();

            var tenantsById = await _db.Tenants
                .AsNoTracking()
                .Where(tenant => tenantIds.Contains(tenant.Id))
                .Select(tenant => new
                {
                    tenant.Id,
                    tenant.TenantName,
                    tenant.IsActive,
                    tenant.CreatedOnUtc
                })
                .ToDictionaryAsync(tenant => tenant.Id);

            var now = DateTimeOffset.UtcNow;
            var response = adminUsers
                .Select(user =>
                {
                    tenantsById.TryGetValue(user.TenantId!.Value, out var tenant);
                    var accountIsUnlocked = !user.LockoutEnd.HasValue || user.LockoutEnd <= now;

                    return new
                    {

                        staffId = user.Id,
                        identityUserId = user.Id,
                        fullName = user.UserName ?? user.Email ?? "Tenant Admin",
                        email = user.Email ?? string.Empty,
                        phone = user.PhoneNumber ?? string.Empty,
                        vacancyId = string.Empty,
                        vacancyCode = string.Empty,
                        jobTitle = "Tenant Admin",
                        department = string.Empty,
                        companyName = tenant?.TenantName ?? "Tenant",
                        joiningDate = tenant?.CreatedOnUtc,
                        loginId = user.UserName,
                        tenantId = user.TenantId,
                        isTenantAdmin = true,
                        isActive = (tenant?.IsActive ?? false) && accountIsUnlocked
                    };
                })
                .OrderBy(admin => admin.companyName)
                .ThenBy(admin => admin.fullName)
                .ToList();

            return Ok(response);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var tenant = await _db.Tenants
                .AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => new
                {
                    t.Id, t.TenantName, t.TenantCode, t.IsActive,
                    t.OrganizationTreeId,
                    OrgNodeName = t.OrganizationNode != null ? t.OrganizationNode.Name : null,
                    t.BrandingAssetType, t.BrandingFileName, t.BrandingUpdatedOnUtc,
                    HasBranding = t.BrandingContent != null,
                    GrantedMenus = t.MenuPermissions.Select(mp => new
                    {
                        menuId = mp.MenuId,
                        menuTitle = mp.Menu != null ? mp.Menu.Title : null,
                        mp.IsAllow, mp.CanView, mp.CanAdd, mp.CanEdit, mp.CanDelete
                    }).ToList()
                })
                .FirstOrDefaultAsync();

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
                orgNodeName  = tenant.OrgNodeName,
                brandingAssetType = tenant.BrandingAssetType,
                brandingFileName = tenant.BrandingFileName,
                brandingUpdatedOnUtc = tenant.BrandingUpdatedOnUtc,
                brandingUrl = tenant.HasBranding
                    ? $"/api/tenant-branding/{tenant.Id}/content?v={tenant.BrandingUpdatedOnUtc?.Ticks ?? 0}"
                    : null,
                staffCount,
                grantedMenus = tenant.GrantedMenus
            });
        }

        [HttpPost]
        [HttpPost("create")]
        public async Task<IActionResult> Create([FromBody] CreateTenantDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var creatorId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (dto.ParentOrgNodeId.HasValue)
            {
                var parent = await _db.OrganizationTree.FindAsync(dto.ParentOrgNodeId.Value);
                if (parent == null)
                    return BadRequest(new { message = $"Parent org node {dto.ParentOrgNodeId} not found." });
            }

            var code = dto.TenantCode.Trim().ToUpper();
            if (await _db.Tenants.AnyAsync(t => t.TenantCode == code))
                return Conflict(new { message = $"TenantCode '{code}' is already in use." });

            var orgLabel = string.IsNullOrWhiteSpace(dto.OrgLabel) ? "Company" : dto.OrgLabel.Trim();
            if (!orgLabel.Equals("Company", StringComparison.OrdinalIgnoreCase)
                && !orgLabel.Equals("Group", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "OrgLabel must be 'Company' or 'Group'." });

            var strategy = _db.Database.CreateExecutionStrategy();

            IActionResult? result = null;

            await strategy.ExecuteAsync(async () =>
            {
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

                        await InsertTenantMenuGrantsAsync(
                            tenant.Id,
                            validMenuIds.Select(menuId => new TenantMenuAccessDto
                            {
                                MenuId = menuId,
                                CanView = true,
                                CanAdd = true,
                                CanEdit = true,
                                CanDelete = true
                            }),
                            creatorId);
                    }

                    await tx.CommitAsync();

                    await _provisioning.EnsureTenantPlatformSettingsAsync(tenant.Id, ct: default);

                    result = Ok(new
                    {
                        message = $"Tenant '{tenant.TenantName}' created successfully.",
                        tenant  = new
                        {
                            tenant.Id,
                            tenant.TenantName,
                            tenant.TenantCode,
                            tenant.OrganizationTreeId,
                            brandingAssetType = tenant.BrandingAssetType,
                            brandingFileName = tenant.BrandingFileName,
                            brandingUpdatedOnUtc = tenant.BrandingUpdatedOnUtc,
                            brandingUrl = (string?)null,
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

        [HttpPut("{id:int}/toggle")]
        public async Task<IActionResult> Toggle(int id)
        {
            var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            if (tenant == null) return NotFound(new { message = $"Tenant {id} not found." });
            return await SetStatus(id, new SetTenantStatusDto { IsActive = !tenant.IsActive });
        }

        // ── PUT /api/tenants/{id}/menus ───────────────────────────────────────
        [HttpPut("{id:int}/menus")]
        public async Task<IActionResult> SetMenus(int id, [FromBody] List<int> menuIds)
        {
            var tenant = await _db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tenant == null) return NotFound(new { message = $"Tenant {id} not found." });

            var creatorId = User.FindFirstValue(ClaimTypes.NameIdentifier);

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

            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var transaction = await _db.Database.BeginTransactionAsync();

                await _db.TenantMenuPermissions
                    .Where(p => p.TenantId == id)
                    .ExecuteDeleteAsync();

                await InsertTenantMenuGrantsAsync(
                    id,
                    validMenuIds.OrderBy(x => x).Select(menuId => new TenantMenuAccessDto
                    {
                        MenuId = menuId,
                        CanView = true,
                        CanAdd = true,
                        CanEdit = true,
                        CanDelete = true
                    }),
                    creatorId);

                await PruneRevokedStaffMenusAsync(id, validMenuIds);
                await transaction.CommitAsync();
            });

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
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id);
            if (tenant == null) return NotFound(new { message = $"Tenant {id} not found." });

            var activeMenus = await _db.Menus.AsNoTracking().Where(m => m.IsActive)
                .Select(m => new { m.Id, m.ParentId }).ToListAsync();
            var activeById = activeMenus.ToDictionary(m => m.Id);
            var requested = access
                .Where(a => activeById.ContainsKey(a.MenuId))
                .GroupBy(a => a.MenuId)
                .Select(g => NormalizeMenuAccess(g.Last()))
                .ToDictionary(a => a.MenuId);

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

            var creatorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var viewMenuIds = requested.Values.Where(a => a.CanView).Select(a => a.MenuId).ToArray();
            var grantsJson = JsonSerializer.Serialize(requested.Values
                .OrderBy(g => g.MenuId)
                .Select(g => new
                {
                    menuId = g.MenuId,
                    canView = g.CanView,
                    canAdd = g.CanAdd,
                    canEdit = g.CanEdit,
                    canDelete = g.CanDelete
                }));

            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                await using var transaction = await _db.Database.BeginTransactionAsync();

                await _db.TenantMenuPermissions
                    .Where(p => p.TenantId == id)
                    .ExecuteDeleteAsync();

                await InsertTenantMenuGrantsAsync(id, requested.Values, creatorId);

                await _db.StaffMenuAccesses
                    .Where(grant => grant.Staff != null && grant.Staff.TenantId == id && !viewMenuIds.Contains(grant.MenuId))
                    .ExecuteDeleteAsync();

                await _db.Database.ExecuteSqlRawAsync("""
                    DELETE af
                    FROM dbo.AccessFeatures af
                    INNER JOIN dbo.StaffMenuAccess sma ON sma.Id = af.StaffMenuAccessId
                    INNER JOIN dbo.StaffVacancy sv ON sv.StaffId = sma.StaffId
                    INNER JOIN dbo.Features f ON f.PermissionId = af.PermissionId
                    LEFT JOIN OPENJSON(@GrantsJson)
                    WITH (
                        MenuId int '$.menuId',
                        CanView bit '$.canView',
                        CanAdd bit '$.canAdd',
                        CanEdit bit '$.canEdit',
                        CanDelete bit '$.canDelete'
                    ) g ON g.MenuId = sma.MenuId
                    WHERE sv.TenantId = @TenantId
                      AND f.FeatureKey LIKE N'MENU_%'
                      AND (
                            g.MenuId IS NULL
                         OR (f.FeatureKey LIKE N'MENU_%_VIEW' AND ISNULL(g.CanView, 0) = 0)
                         OR (f.FeatureKey LIKE N'MENU_%_ADD' AND ISNULL(g.CanAdd, 0) = 0)
                         OR (f.FeatureKey LIKE N'MENU_%_EDIT' AND ISNULL(g.CanEdit, 0) = 0)
                         OR (f.FeatureKey LIKE N'MENU_%_DELETE' AND ISNULL(g.CanDelete, 0) = 0)
                      );
                    """,
                    new SqlParameter("@TenantId", id),
                    new SqlParameter("@GrantsJson", grantsJson));

                await transaction.CommitAsync();
            });

            return Ok(new
            {
                message = $"Menu access updated for tenant '{tenant.TenantName}'.",
                grantedCount = requested.Count,
                grantedMenus = requested.Values
                    .OrderBy(g => g.MenuId)
                    .Select(g => new
                    {
                        menuId = g.MenuId,
                        isAllow = true,
                        canView = g.CanView,
                        canAdd = g.CanAdd,
                        canEdit = g.CanEdit,
                        canDelete = g.CanDelete
                    })
            });
        }

        private async Task InsertTenantMenuGrantsAsync(
            int tenantId,
            IEnumerable<TenantMenuAccessDto> grants,
            string? grantedByUserId)
        {
            var payload = grants
                .Select(g => new
                {
                    menuId = g.MenuId,
                    canView = g.CanView,
                    canAdd = g.CanAdd,
                    canEdit = g.CanEdit,
                    canDelete = g.CanDelete
                })
                .ToArray();
            if (payload.Length == 0) return;

            await _db.Database.ExecuteSqlRawAsync("""
                INSERT INTO dbo.TenantMenuPermissions
                    (TenantId, MenuId, IsAllow, CanView, CanAdd, CanEdit, CanDelete, GrantedByUserId, GrantedOnUtc)
                SELECT
                    @TenantId,
                    g.MenuId,
                    CAST(1 AS bit),
                    g.CanView,
                    g.CanAdd,
                    g.CanEdit,
                    g.CanDelete,
                    @GrantedBy,
                    SYSUTCDATETIME()
                FROM OPENJSON(@GrantsJson)
                WITH (
                    MenuId int '$.menuId',
                    CanView bit '$.canView',
                    CanAdd bit '$.canAdd',
                    CanEdit bit '$.canEdit',
                    CanDelete bit '$.canDelete'
                ) g;
                """,
                new SqlParameter("@TenantId", tenantId),
                new SqlParameter("@GrantedBy", (object?)grantedByUserId ?? DBNull.Value),
                new SqlParameter("@GrantsJson", JsonSerializer.Serialize(payload)));
        }

        private async Task PruneRevokedStaffMenusAsync(int tenantId, IReadOnlySet<int> allowedMenuIds)
        {
            await _db.StaffMenuAccesses
                .Where(grant => grant.Staff != null &&
                    grant.Staff.TenantId == tenantId &&
                    !allowedMenuIds.Contains(grant.MenuId))
                .ExecuteDeleteAsync();
        }

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
        private static TenantMenuAccessDto NormalizeMenuAccess(TenantMenuAccessDto dto)
        {
            var canView = dto.CanView || dto.CanAdd || dto.CanEdit || dto.CanDelete;
            return new TenantMenuAccessDto
            {
                MenuId    = dto.MenuId,
                CanView   = canView,
                CanAdd    = canView && dto.CanAdd,
                CanEdit   = canView && dto.CanEdit,
                CanDelete = canView && dto.CanDelete,
            };
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
        [Required, MaxLength(150)]
        public string CompanyName { get; set; } = string.Empty;

        [Required, MaxLength(20), MinLength(2)]
        public string TenantCode { get; set; } = string.Empty;

        public int? ParentOrgNodeId { get; set; }
        [MaxLength(50)]
        public string? OrgLabel { get; set; }

        [MaxLength(150), EmailAddress]
        public string? AdminEmail { get; set; }

        [MinLength(6)]
        public string? AdminPassword { get; set; }

        public List<int>? GrantedMenuIds { get; set; }
    }
}
