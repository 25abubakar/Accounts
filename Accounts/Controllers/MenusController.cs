using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MenusController : ControllerBase
    {
        private readonly IMenuService        _menuService;
        private readonly ApplicationDbContext _db;

        public MenusController(IMenuService menuService, ApplicationDbContext db)
        {
            _menuService = menuService;
            _db          = db;
        }

        // POST api/menus — create a new menu item
        [HttpPost]
        public async Task<IActionResult> CreateMenu([FromBody] CreateMenuDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var menu = await _menuService.CreateMenuAsync(dto);
            return CreatedAtAction(nameof(GetSidebarTree), new { }, menu);
        }

        // GET api/menus/sidebar-tree — dynamic tree for the sidebar (role-based, legacy)
        [HttpGet("sidebar-tree")]
        public async Task<IActionResult> GetSidebarTree()
        {
            var userRoles = User.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();

            var tree = await _menuService.GetSidebarTreeAsync(userRoles.Count > 0 ? userRoles : null);
            return Ok(tree);
        }

        // GET api/menus — flat list for admin management
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var menus = await _menuService.GetAllAsync();
            return Ok(menus);
        }

        // DELETE api/menus/{id} — soft-delete (deactivate)
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Deactivate(int id)
        {
            var success = await _menuService.DeactivateAsync(id);
            if (!success) return NotFound(new { message = $"Menu {id} not found." });
            return NoContent();
        }

        // ── Assign permission keys to a menu item ─────────────────────────────

        /// <summary>
        /// Assign required permission keys to a menu item.
        /// Users must have at least one of these keys to see the menu item.
        /// Send empty array [] to make the menu item public (visible to all).
        /// 
        /// Example: PUT /api/menus/5/permissions
        /// Body: ["EMPLOYEE_VIEW", "EMPLOYEE_EDIT"]
        /// </summary>
        [HttpPut("{id:int}/permissions")]
        public async Task<IActionResult> SetMenuPermissions(int id, [FromBody] List<string> permissionKeys)
        {
            var menu = await _db.Menus
                .Include(m => m.MenuRoles)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (menu == null)
                return NotFound(new { message = $"Menu {id} not found." });

            // Validate keys exist in Features table
            var validKeys = await _db.Features
                .Select(f => f.FeatureKey)
                .ToHashSetAsync();

            var invalidKeys = permissionKeys.Where(k => !validKeys.Contains(k)).ToList();

            // Remove all existing roles for this menu
            _db.MenuRoles.RemoveRange(menu.MenuRoles);

            // Add new permission keys (only valid ones)
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
                    : $"Menu '{menu.Title}' is now public (no permission required)."
            });
        }

        /// <summary>
        /// Get all menus with their assigned permission keys.
        /// Use this to see which menus need which permissions.
        /// </summary>
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
                    IsPublic = !m.MenuRoles.Any()
                })
                .ToListAsync();

            return Ok(menus);
        }

        /// <summary>
        /// Bulk assign permissions to multiple menus at once.
        /// Body: { "menuId": 1, "permissionKeys": ["EMPLOYEE_VIEW"] }[]
        /// </summary>
        [HttpPost("bulk-permissions")]
        public async Task<IActionResult> BulkSetPermissions([FromBody] List<MenuPermissionDto> items)
        {
            if (items == null || !items.Any())
                return BadRequest(new { message = "No items provided." });

            var validKeys = await _db.Features
                .Select(f => f.FeatureKey)
                .ToHashSetAsync();

            int updated = 0;
            var errors  = new List<string>();

            foreach (var item in items)
            {
                var menu = await _db.Menus
                    .Include(m => m.MenuRoles)
                    .FirstOrDefaultAsync(m => m.Id == item.MenuId);

                if (menu == null)
                {
                    errors.Add($"Menu {item.MenuId} not found.");
                    continue;
                }

                _db.MenuRoles.RemoveRange(menu.MenuRoles);

                var toAdd = item.PermissionKeys
                    .Where(k => validKeys.Contains(k))
                    .Distinct()
                    .Select(k => new MenuRole { MenuId = item.MenuId, RoleName = k })
                    .ToList();

                _db.MenuRoles.AddRange(toAdd);
                updated++;
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = $"{updated} menus updated.",
                errors  = errors.Any() ? errors : null
            });
        }
    }

    public class MenuPermissionDto
    {
        public int          MenuId         { get; set; }
        public List<string> PermissionKeys { get; set; } = new();
    }
}
