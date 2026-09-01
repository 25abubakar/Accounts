using Accounts.Authorization;
using Accounts.Data;
using Accounts.DTOs;
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
    [Route("api/menus")]
    [Authorize]
    public class MenusController : ControllerBase
    {
        private readonly IMenuService _menuService;
        private readonly ApplicationDbContext _db;
        private readonly RbacService _rbac;

        public MenusController(
            IMenuService menuService,
            ApplicationDbContext db,
            RbacService rbac)
        {
            _menuService = menuService;
            _db = db;
            _rbac = rbac;
        }

        // ── Create ────────────────────────────────────────────────────────────

        [HasPermission("ACCESS_GROUP_EDIT")]
        [HttpPost]
        public async Task<IActionResult> CreateMenu([FromBody] CreateMenuDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var menu = await _menuService.CreateMenuAsync(dto);
            return CreatedAtAction(nameof(GetSidebarTree), new { }, menu);
        }

        // ── Read ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Sidebar tree filtered by effective feature permissions.
        /// Prefer GET /api/rbac/sidebar or GET /api/auth/session for new frontend code.
        /// </summary>
        [HttpGet("sidebar-tree")]
        public async Task<IActionResult> GetSidebarTree()
        {
            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized(new { message = "Not authenticated." });

            if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin"))
                return Ok(await _rbac.GetFilteredSidebarAsync(Guid.Empty));

            var person = await _db.Persons
                .AsNoTracking()
                .Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId);

            if (person?.Staff == null)
                return Ok(new List<object>());

            return Ok(await _rbac.GetFilteredSidebarAsync(person.Staff.StaffId));
        }

        /// <summary>Flat list of all menus for admin management.</summary>
        [HasPermission("ACCESS_GROUP_VIEW")]
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var menus = await _menuService.GetAllAsync();
            return Ok(menus);
        }

        /// <summary>All menus with their assigned permission keys.</summary>
        [HasPermission("ACCESS_GROUP_VIEW")]
        [HttpGet("with-permissions")]
        public async Task<IActionResult> GetMenusWithPermissions()
        {
            var menus = await _db.Menus
                .AsNoTracking()
                .Include(m => m.MenuPermissions).ThenInclude(mp => mp.Feature)
                .OrderBy(m => m.SortOrder)
                .Select(m => new
                {
                    m.Id,
                    m.Title,
                    m.Icon,
                    m.Route,
                    m.ParentId,
                    m.SortOrder,
                    m.IsActive,
                    RequiredPermissions = m.MenuPermissions
                        .Where(mp => mp.Feature != null)
                        .Select(mp => mp.Feature!.FeatureKey).ToList(),
                    IsPublic = !m.MenuPermissions.Any()
                })
                .ToListAsync();

            return Ok(menus);
        }

        // ── Delete ────────────────────────────────────────────────────────────

        [HasPermission("ACCESS_GROUP_EDIT")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateMenu(int id, [FromBody] CreateMenuDto dto)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);
            if (string.IsNullOrWhiteSpace(dto.Title))
                return BadRequest(new { message = "Menu title is required." });

            try
            {
                var menu = await _menuService.UpdateMenuAsync(id, dto);
                if (menu is null) return NotFound(new { message = $"Menu {id} not found." });
                return Ok(new { menu.Id, menu.Title, menu.Icon, menu.Route, menu.ParentId, menu.SortOrder, menu.IsActive });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        [HasPermission("ACCESS_GROUP_EDIT")]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Deactivate(int id)
        {
            var success = await _menuService.DeactivateAsync(id);
            if (!success) return NotFound(new { message = $"Menu {id} not found." });
            return NoContent();
        }

        // ── Permission assignment ─────────────────────────────────────────────

        /// <summary>
        /// Assign required permission keys to a menu item.
        /// Send empty array [] to make it public (visible to all authenticated users).
        /// </summary>
        [HasPermission("ACCESS_GROUP_ASSIGN")]
        [HttpPut("{id:int}/permissions")]
        public async Task<IActionResult> SetMenuPermissions(int id, [FromBody] List<string> permissionKeys)
        {
            var menu = await _db.Menus.Include(m => m.MenuPermissions).FirstOrDefaultAsync(m => m.Id == id);
            if (menu == null) return NotFound(new { message = $"Menu {id} not found." });

            // Map FeatureKey strings to PermissionId integers
            var featureMap = await _db.Features
                .Where(f => permissionKeys.Contains(f.FeatureKey))
                .ToDictionaryAsync(f => f.FeatureKey, f => f.PermissionId);

            var invalidKeys = permissionKeys.Where(k => !featureMap.ContainsKey(k)).ToList();

            _db.MenuPermissions.RemoveRange(menu.MenuPermissions);

            var toAdd = permissionKeys
                .Where(k => featureMap.ContainsKey(k))
                .Distinct()
                .Select(k => new MenuPermission { MenuId = id, PermissionId = featureMap[k] })
                .ToList();

            _db.MenuPermissions.AddRange(toAdd);
            await _db.SaveChangesAsync();

            // Re-query to get feature keys for response
            var addedKeys = await _db.MenuPermissions
                .AsNoTracking()
                .Include(mp => mp.Feature)
                .Where(mp => mp.MenuId == id && mp.Feature != null)
                .Select(mp => mp.Feature!.FeatureKey)
                .ToListAsync();

            return Ok(new
            {
                menuId = id,
                title = menu.Title,
                permissions = addedKeys,
                invalidKeys = invalidKeys.Any() ? invalidKeys : null,
                message = toAdd.Any()
                    ? $"Menu '{menu.Title}' now requires one of: {string.Join(", ", addedKeys)}"
                    : $"Menu '{menu.Title}' is now public."
            });
        }

        [HasPermission("ACCESS_GROUP_ASSIGN")]
        [HttpPost("bulk-permissions")]
        public async Task<IActionResult> BulkSetPermissions([FromBody] List<MenuPermissionDto> items)
        {
            if (items == null || !items.Any())
                return BadRequest(new { message = "No items provided." });

            // Get all unique feature keys from all items
            var allKeys = items.SelectMany(i => i.PermissionKeys).Distinct().ToList();
            var featureMap = await _db.Features
                .Where(f => allKeys.Contains(f.FeatureKey))
                .ToDictionaryAsync(f => f.FeatureKey, f => f.PermissionId);

            int updated = 0;
            var errors = new List<string>();

            foreach (var item in items)
            {
                var menu = await _db.Menus.Include(m => m.MenuPermissions)
                    .FirstOrDefaultAsync(m => m.Id == item.MenuId);

                if (menu == null) { errors.Add($"Menu {item.MenuId} not found."); continue; }

                _db.MenuPermissions.RemoveRange(menu.MenuPermissions);
                _db.MenuPermissions.AddRange(item.PermissionKeys
                    .Where(k => featureMap.ContainsKey(k)).Distinct()
                    .Select(k => new MenuPermission { MenuId = item.MenuId, PermissionId = featureMap[k] }));
                updated++;
            }

            await _db.SaveChangesAsync();
            return Ok(new { message = $"{updated} menus updated.", errors = errors.Any() ? errors : null });
        }

        // ── Seed sidebar menu structure ───────────────────────────────────────

        /// <summary>
        /// Seeds the full sidebar menu structure into the Menus table.
        /// Replaces the hardcoded STATIC_NAV fallback in the frontend Sidebar.tsx.
        ///
        /// Idempotent — skips menus that already exist (matched by Title for groups,
        /// by Route for leaf items).
        ///
        /// After seeding, the frontend should call GET /api/rbac/sidebar which
        /// returns only the menus the current user has permission to see.
        ///
        /// SuperAdmin → sees all menus.
        /// Other users → see only menus whose RequiredPermissions they hold.
        ///
        /// POST /api/menus/seed
        /// </summary>
        [HttpPost("seed")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> SeedMenus()
        {
            // ── Menu tree definition ──────────────────────────────────────────
            // Mirrors STATIC_NAV from Sidebar.tsx exactly.
            // Permission keys: empty list = public (all authenticated users can see it)
            //                  non-empty  = user must hold at least one of these keys
            var definitions = new List<SeedMenuItem>
            {
                // Root items (no parent)
                new("Overview",             "LayoutDashboard",    "/dashboard",            null,                  1, new()),

                // Accounts & Groups (parent group)
                new("Accounts & Groups",    "Building2",          null,                    null,                  2, new()),
                new("Companies & Entities", "Briefcase",          "/groups/companies",     "Accounts & Groups",   1, new() { "DEPT_VIEW" }),
                new("Organization Chart",   "GitBranch",          "/organization",         "Accounts & Groups",   2, new() { "DEPT_VIEW" }),
                new("Partner Portals",      "Handshake",          "/groups/partners",      "Accounts & Groups",   3, new() { "DEPT_VIEW" }),
                new("Tenant Management",    "ShieldCheck",        "/tenants",              "Accounts & Groups",   4, new() { "ACCESS_GROUP_VIEW" }),

                // HR Management (parent group)
                new("HR Management",        "Users",              null,                    null,                  3, new()),
                new("Staff & Persons",      "UserCheck",          "/hr/staff",             "HR Management",       1, new() { "EMPLOYEE_VIEW", "PERSON_VIEW" }),
                new("Register Person",      "UserPlus",           "/hr/staff/register",    "HR Management",       2, new() { "PERSON_REGISTER" }),
                new("Positions",            "Briefcase",          "/hr/vacancies",         "HR Management",       3, new() { "VACANCY_VIEW" }),
                new("Process",              "Workflow",           null,                    "HR Management",       4, new() { "EMPLOYEE_VIEW" }),
                new("Reports",              "BarChart2",          "/hr/process/report",    "Process",             1, new() { "EMPLOYEE_VIEW" }),
                new("Task List",            "ListTodo",           "/hr/process/task-list", "Process",             2, new() { "EMPLOYEE_VIEW" }),

                // Access Control (parent group)
                new("Access Control",       "Shield",             null,                    null,                  4, new()),
                new("Admin Access",         "ShieldCheck",        "/access/admin",         "Access Control",      1, new() { "ACCESS_GROUP_VIEW", "ACCESS_GROUP_ASSIGN" }),
                new("Access Groups",        "Lock",               "/access/groups",        "Access Control",      2, new() { "ACCESS_GROUP_VIEW" }),
                new("Group Matrix",         "Grid",               "/access/groups/matrix", "Access Control",      3, new() { "ACCESS_GROUP_VIEW" }),
                new("Dept Permissions",     "ShieldCheck",        "/access/dept",          "Access Control",      4, new() { "ACCESS_GROUP_VIEW" }),

                // Platform Settings (parent group)
                new("Platform Settings",    "Settings",           null,                    null,                  5, new()),
                new("General",              "SlidersHorizontal",  "/settings/general",     "Platform Settings",   1, new() { "ACCESS_GROUP_VIEW" }),
                new("Branding",             "Palette",            "/settings/branding",    "Platform Settings",   2, new() { "ACCESS_GROUP_VIEW" }),
                new("Email Templates",      "Mail",               "/settings/emails",      "Platform Settings",   3, new() { "ACCESS_GROUP_VIEW" }),
                new("Integrations",         "Plug",               "/settings/integrations","Platform Settings",   4, new() { "ACCESS_GROUP_VIEW" }),
                new("Menu Manager",         "Menu",               "/settings/menus",       "Platform Settings",   5, new() { "ACCESS_GROUP_EDIT" }),
                new("Seed Menus",           "Database",           "/settings/seed-menus",  "Platform Settings",   6, new() { "ACCESS_GROUP_EDIT" }),
                new("Status",               "Palette",            "/settings/statuses",    "Platform Settings",   7, new()),
                new("Settings",             "SlidersHorizontal",  "/settings/configuration", "Platform Settings", 10, new()),
                new("Scale",                "BadgeDollarSign",    "/settings/scales",      "Platform Settings",   8, new()),

                // Library (parent group)
                new("Library",              "LibraryBig",         null,                        null,                 80, new()),
                new("Library Type",         "Tags",               "/library/types",           "Library",             1, new()),
                new("Library",              "Files",              "/library",                 "Library",             2, new()),
                new("File Converter",       "FileCog",            "/library/file-converter",  "Library",             3, new()),
                new("Generate Invoice",     "ReceiptText",        "/library/generate-invoice", "Library",             4, new()),

                // Accounts (parent group)
                new("Accounts",             "ReceiptText",        null,                         null,                 81, new()),
                new("Payment (ROZ)",        "DollarSign",         "/accounts/payment-roz",     "Accounts",            1, new()),
                new("Receipt (ROZ)",        "ReceiptText",        "/accounts/receipt-roz",     "Accounts",            2, new()),
                new("Roznamcha Update",     "FileCog",            "/accounts/roznamcha-update", "Accounts",            3, new()),
                new("Reports",              "BarChart3",          "/accounts/reports",         "Accounts",            4, new()),
                new("Report Filter",        "SlidersHorizontal",  "/accounts/report-filter",   "Accounts",            5, new()),
                new("Daily Updates",        "CalendarRange",      "/accounts/daily-updates",   "Accounts",            6, new()),
                new("Bank Account Report",  "Files",              "/accounts/bank-account-report", "Accounts",         7, new()),
                new("Show record",          "LayoutGrid",         "/accounts/show-record",     "Accounts",            8, new()),
            };

            // Valid feature keys (only save permissions that exist in Features table)
            var validKeys = await _db.Features.Select(f => f.FeatureKey).ToHashSetAsync();
            var featureMap = await _db.Features
                .Where(f => validKeys.Contains(f.FeatureKey))
                .ToDictionaryAsync(f => f.FeatureKey, f => f.PermissionId);

            var hrMenu = await _db.Menus
                .FirstOrDefaultAsync(m => m.ParentId == null && m.Title == "HR Management");

            if (hrMenu != null)
            {
                var legacyReports = await _db.Menus
                    .FirstOrDefaultAsync(m =>
                        m.ParentId == hrMenu.Id &&
                        (m.Title == "Reports" || m.Route == "/hr/reports"));

                var existingProcess = await _db.Menus
                    .FirstOrDefaultAsync(m =>
                        m.ParentId == hrMenu.Id &&
                        m.Title == "Process");

                if (legacyReports != null && existingProcess == null)
                {
                    legacyReports.Title = "Process";
                    legacyReports.Icon = "Workflow";
                    legacyReports.Route = null;
                    legacyReports.ParentId = hrMenu.Id;
                    legacyReports.SortOrder = 4;
                    legacyReports.IsActive = true;
                    await _db.SaveChangesAsync();
                }
                else if (legacyReports != null && existingProcess != null && legacyReports.Id != existingProcess.Id)
                {
                    legacyReports.Title = "Reports";
                    legacyReports.Icon = "BarChart2";
                    legacyReports.Route = "/hr/process/report";
                    legacyReports.ParentId = existingProcess.Id;
                    legacyReports.SortOrder = 1;
                    legacyReports.IsActive = true;
                    await _db.SaveChangesAsync();
                }
            }

            // Existing menus — for idempotency checks (Protected against DB duplicates)
            async Task<int?> ResolveParentIdAsync(string? parentTitle)
            {
                if (string.IsNullOrWhiteSpace(parentTitle)) return null;

                return await _db.Menus
                    .Where(menu => menu.IsActive && menu.Route == null && menu.Title == parentTitle)
                    .OrderBy(menu => menu.ParentId == null ? 0 : 1)
                    .ThenBy(menu => menu.SortOrder)
                    .Select(menu => (int?)menu.Id)
                    .FirstOrDefaultAsync();
            }

            int added = 0, skipped = 0;
            // Pass 1: parent groups (no route). This also supports nested groups such as HR Management > Process.
            foreach (var item in definitions.Where(d => d.Route == null))
            {
                var parentId = await ResolveParentIdAsync(item.ParentTitle);

                var existing = await _db.Menus
                    .FirstOrDefaultAsync(menu =>
                        menu.ParentId == parentId &&
                        menu.Route == null &&
                        menu.Title == item.Title);

                if (existing != null)
                {
                    existing.Icon = item.Icon;
                    existing.Route = null;
                    existing.ParentId = parentId;
                    existing.SortOrder = item.SortOrder;
                    existing.IsActive = true;
                    skipped++;
                    continue;
                }

                var menu = new Menu
                {
                    Title = item.Title,
                    Icon = item.Icon,
                    ParentId = parentId,
                    SortOrder = item.SortOrder,
                    IsActive = true
                };
                _db.Menus.Add(menu);
                added++;
                await _db.SaveChangesAsync();
            }

            // Pass 2: leaf items (have a route)
            foreach (var item in definitions.Where(d => d.Route != null))
            {
                var parentId = await ResolveParentIdAsync(item.ParentTitle);

                var existing = await _db.Menus
                    .Include(m => m.MenuPermissions)
                    .FirstOrDefaultAsync(m =>
                        m.Route == item.Route ||
                        (m.ParentId == parentId && m.Title == item.Title));

                if (existing != null)
                {
                    existing.Title = item.Title;
                    existing.Icon = item.Icon;
                    existing.Route = item.Route;
                    existing.ParentId = parentId;
                    existing.SortOrder = item.SortOrder;
                    existing.IsActive = true;

                    foreach (var key in item.Permissions.Where(k => featureMap.ContainsKey(k)))
                    {
                        var permissionId = featureMap[key];
                        if (existing.MenuPermissions.All(p => p.PermissionId != permissionId))
                            existing.MenuPermissions.Add(new MenuPermission { PermissionId = permissionId });
                    }
                    skipped++;
                    continue;
                }

                var menu = new Menu
                {
                    Title = item.Title,
                    Icon = item.Icon,
                    Route = item.Route,
                    ParentId = parentId,
                    SortOrder = item.SortOrder,
                    IsActive = true
                };

                // Only attach permissions that exist in Features table
                foreach (var key in item.Permissions.Where(k => featureMap.ContainsKey(k)))
                    menu.MenuPermissions.Add(new MenuPermission { PermissionId = featureMap[key] });

                _db.Menus.Add(menu);
                added++;
            }

            if (_db.ChangeTracker.HasChanges())
                await _db.SaveChangesAsync();

            return Ok(new
            {
                message = $"Seed complete. {added} menus added, {skipped} already existed.",
                added,
                skipped,
                nextStep = "Call POST /api/menus/sync-routes then GET /api/auth/session."
            });
        }

        /// <summary>
        /// Normalizes menu routes to lowercase paths expected by the React frontend.
        /// Safe to run multiple times.
        /// </summary>
        [HttpPost("sync-routes")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> SyncMenuRoutes()
        {
            var routeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["/ACCESS/GROUPS"] = "/access/groups",
                ["/Access/Groups"] = "/access/groups",
                ["/access/group"] = "/access/groups",
                ["/groups/hierarchy"] = "/organization",
                ["/groups/registration"] = "/hr/vacancies",
                ["/groups/staff"] = "/hr/staff",
                ["/staff/register"] = "/hr/staff/register",
                ["/hr/positions"] = "/hr/vacancies",
                ["/hr/reports"] = "/hr/process/report",
                ["/HR/REPORTS"] = "/hr/process/report",
                ["/Hr/Reports"] = "/hr/process/report"
            };

            var menus = await _db.Menus.Where(m => m.Route != null).ToListAsync();
            int updated = 0;

            foreach (var menu in menus)
            {
                var route = menu.Route!;
                var normalized = routeMap.TryGetValue(route, out var mapped)
                    ? mapped
                    : route.ToLowerInvariant();

                if (!string.Equals(route, normalized, StringComparison.Ordinal))
                {
                    menu.Route = normalized;
                    updated++;
                }
            }

            if (updated > 0)
                await _db.SaveChangesAsync();

            return Ok(new
            {
                message = $"Route sync complete. {updated} menu route(s) updated.",
                updated,
                routes = menus.Where(m => m.Route != null)
                    .OrderBy(m => m.SortOrder)
                    .Select(m => new { m.Id, m.Title, m.Route })
            });
        }
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    public class MenuPermissionDto
    {
        public int MenuId { get; set; }
        public List<string> PermissionKeys { get; set; } = new();
    }

    /// <summary>Internal helper — not exposed via API.</summary>
    internal sealed record SeedMenuItem(
        string Title,
        string Icon,
        string? Route,
        string? ParentTitle,
        int SortOrder,
        List<string> Permissions);
}

