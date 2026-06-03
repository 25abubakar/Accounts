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
    [Route("api/[controller]")]
    [Authorize]
    public class MenusController : ControllerBase
    {
        private readonly IMenuService         _menuService;
        private readonly ApplicationDbContext _db;
        private readonly RbacService          _rbac;

        public MenusController(
            IMenuService menuService,
            ApplicationDbContext db,
            RbacService rbac)
        {
            _menuService = menuService;
            _db          = db;
            _rbac        = rbac;
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
                .Include(m => m.MenuRoles)
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
                    RequiredPermissions = m.MenuRoles.Select(r => r.RoleName).ToList(),
                    IsPublic            = !m.MenuRoles.Any()
                })
                .ToListAsync();

            return Ok(menus);
        }

        // ── Delete ────────────────────────────────────────────────────────────

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
            var menu = await _db.Menus.Include(m => m.MenuRoles).FirstOrDefaultAsync(m => m.Id == id);
            if (menu == null) return NotFound(new { message = $"Menu {id} not found." });

            var validKeys   = await _db.Features.Select(f => f.FeatureKey).ToHashSetAsync();
            var invalidKeys = permissionKeys.Where(k => !validKeys.Contains(k)).ToList();

            _db.MenuRoles.RemoveRange(menu.MenuRoles);

            var toAdd = permissionKeys
                .Where(k => validKeys.Contains(k))
                .Distinct()
                .Select(k => new MenuRole { MenuId = id, RoleName = k })
                .ToList();

            _db.MenuRoles.AddRange(toAdd);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                menuId      = id,
                title       = menu.Title,
                permissions = toAdd.Select(r => r.RoleName).ToList(),
                invalidKeys = invalidKeys.Any() ? invalidKeys : null,
                message     = toAdd.Any()
                    ? $"Menu '{menu.Title}' now requires one of: {string.Join(", ", toAdd.Select(r => r.RoleName))}"
                    : $"Menu '{menu.Title}' is now public."
            });
        }

        [HasPermission("ACCESS_GROUP_ASSIGN")]
        [HttpPost("bulk-permissions")]
        public async Task<IActionResult> BulkSetPermissions([FromBody] List<MenuPermissionDto> items)
        {
            if (items == null || !items.Any())
                return BadRequest(new { message = "No items provided." });

            var validKeys = await _db.Features.Select(f => f.FeatureKey).ToHashSetAsync();
            int updated   = 0;
            var errors    = new List<string>();

            foreach (var item in items)
            {
                var menu = await _db.Menus.Include(m => m.MenuRoles)
                    .FirstOrDefaultAsync(m => m.Id == item.MenuId);

                if (menu == null) { errors.Add($"Menu {item.MenuId} not found."); continue; }

                _db.MenuRoles.RemoveRange(menu.MenuRoles);
                _db.MenuRoles.AddRange(item.PermissionKeys
                    .Where(k => validKeys.Contains(k)).Distinct()
                    .Select(k => new MenuRole { MenuId = item.MenuId, RoleName = k }));
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
        [AllowAnonymous]  // First-time setup — no auth required
        public async Task<IActionResult> SeedMenus()
        {
            // ── Menu tree definition ──────────────────────────────────────────
            // Mirrors STATIC_NAV from Sidebar.tsx exactly.
            // Permission keys: empty list = public (all authenticated users can see it)
            //                  non-empty  = user must hold at least one of these keys
            var definitions = new List<SeedMenuItem>
            {
                // Root items (no parent)
                new("Overview",             "LayoutDashboard",    "/dashboard",            null,                 1, new()),

                // Accounts & Groups (parent group)
                new("Accounts & Groups",    "Building2",          null,                    null,                 2, new()),
                new("Companies & Entities", "Briefcase",          "/groups/companies",     "Accounts & Groups",  1, new() { "DEPT_VIEW" }),
                new("Organization Chart",   "GitBranch",          "/organization",         "Accounts & Groups",  2, new() { "DEPT_VIEW" }),
                new("Partner Portals",      "Handshake",          "/groups/partners",      "Accounts & Groups",  3, new() { "DEPT_VIEW" }),

                // HR Management (parent group)
                new("HR Management",        "Users",              null,                    null,                 3, new()),
                new("Staff & Persons",      "UserCheck",          "/hr/staff",             "HR Management",      1, new() { "EMPLOYEE_VIEW", "PERSON_VIEW" }),
                new("Register Person",      "UserPlus",           "/hr/staff/register",    "HR Management",      2, new() { "PERSON_REGISTER" }),
                new("Positions",            "Briefcase",          "/hr/vacancies",         "HR Management",      3, new() { "VACANCY_VIEW" }),
                new("Reports",              "BarChart2",          "/hr/reports",           "HR Management",      4, new() { "EMPLOYEE_VIEW" }),

                // Access Control (parent group)
                new("Access Control",       "Shield",             null,                    null,                 4, new()),
                new("Access Groups",        "Lock",               "/access/groups",        "Access Control",     1, new() { "ACCESS_GROUP_VIEW" }),
                new("Group Matrix",         "Grid",               "/access/groups/matrix", "Access Control",     2, new() { "ACCESS_GROUP_VIEW" }),
                new("Dept Permissions",     "ShieldCheck",        "/access/dept",          "Access Control",     3, new() { "ACCESS_GROUP_VIEW" }),

                // Platform Settings (parent group)
                new("Platform Settings",    "Settings",           null,                    null,                 5, new()),
                new("General",              "SlidersHorizontal",  "/settings/general",     "Platform Settings",  1, new() { "ACCESS_GROUP_VIEW" }),
                new("Branding",             "Palette",            "/settings/branding",    "Platform Settings",  2, new() { "ACCESS_GROUP_VIEW" }),
                new("Email Templates",      "Mail",               "/settings/emails",      "Platform Settings",  3, new() { "ACCESS_GROUP_VIEW" }),
                new("Integrations",         "Plug",               "/settings/integrations","Platform Settings",  4, new() { "ACCESS_GROUP_VIEW" }),
                new("Menu Manager",         "Menu",               "/settings/menus",       "Platform Settings",  5, new() { "ACCESS_GROUP_EDIT" }),
                new("Seed Menus",           "Database",           "/settings/seed-menus",  "Platform Settings",  6, new() { "ACCESS_GROUP_EDIT" }),
            };

            // Valid feature keys (only save permissions that exist in Features table)
            var validKeys = await _db.Features.Select(f => f.FeatureKey).ToHashSetAsync();

            // Existing menus — for idempotency checks
            var existingByRoute = await _db.Menus
                .Where(m => m.Route != null)
                .ToDictionaryAsync(m => m.Route!, m => m.Id);

            var existingByTitle = await _db.Menus
                .ToDictionaryAsync(m => m.Title, m => m.Id);

            int added = 0, skipped = 0;

            // Pass 1: parent groups (no route, no parent)
            foreach (var item in definitions.Where(d => d.Route == null && d.ParentTitle == null))
            {
                if (existingByTitle.ContainsKey(item.Title)) { skipped++; continue; }

                var menu = new Menu
                {
                    Title     = item.Title,
                    Icon      = item.Icon,
                    SortOrder = item.SortOrder,
                    IsActive  = true
                };
                _db.Menus.Add(menu);
                await _db.SaveChangesAsync();
                existingByTitle[item.Title] = menu.Id;
                added++;
            }

            // Pass 2: leaf items (have a route)
            foreach (var item in definitions.Where(d => d.Route != null))
            {
                if (existingByRoute.ContainsKey(item.Route!)) { skipped++; continue; }

                int? parentId = null;
                if (item.ParentTitle != null && existingByTitle.TryGetValue(item.ParentTitle, out int pid))
                    parentId = pid;

                var menu = new Menu
                {
                    Title     = item.Title,
                    Icon      = item.Icon,
                    Route     = item.Route,
                    ParentId  = parentId,
                    SortOrder = item.SortOrder,
                    IsActive  = true
                };

                // Only attach permissions that actually exist in Features table
                foreach (var key in item.Permissions.Where(k => validKeys.Contains(k)))
                    menu.MenuRoles.Add(new MenuRole { RoleName = key });

                _db.Menus.Add(menu);
                await _db.SaveChangesAsync();
                added++;
            }

            return Ok(new
            {
                message = $"Seed complete. {added} menus added, {skipped} already existed.",
                added,
                skipped,
                nextStep = "Call GET /api/rbac/sidebar to get the filtered sidebar for the current user."
            });
        }
    }

    // ── DTOs ─────────────────────────────────────────────────────────────────

    public class MenuPermissionDto
    {
        public int          MenuId         { get; set; }
        public List<string> PermissionKeys { get; set; } = new();
    }

    /// <summary>Internal helper — not exposed via API.</summary>
    internal sealed record SeedMenuItem(
        string       Title,
        string       Icon,
        string?      Route,
        string?      ParentTitle,
        int          SortOrder,
        List<string> Permissions);
}
