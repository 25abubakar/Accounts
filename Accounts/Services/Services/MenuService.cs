using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    public class MenuService : IMenuService
    {
        private readonly ApplicationDbContext _context;

        public MenuService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Menu> CreateMenuAsync(CreateMenuDto dto)
        {
            var menu = new Menu
            {
                Title     = dto.Title,
                Icon      = dto.Icon,
                Route     = dto.Route,
                ParentId  = dto.ParentId,
                SortOrder = dto.SortOrder,
                IsActive  = true
            };

            foreach (var role in dto.RequiredRoles)
                menu.MenuRoles.Add(new MenuRole { RoleName = role });

            _context.Menus.Add(menu);
            await _context.SaveChangesAsync();
            return menu;
        }

        public async Task<List<MenuTreeNodeDto>> GetSidebarTreeAsync(IEnumerable<string>? userRoles = null)
        {
            var query = _context.Menus
                .Include(m => m.MenuRoles)
                .Where(m => m.IsActive);

            // If roles are provided, filter: show menus with no role restriction OR matching a user role
            if (userRoles != null && userRoles.Any())
            {
                var roleList = userRoles.ToList();
                query = query.Where(m =>
                    !m.MenuRoles.Any() ||
                    m.MenuRoles.Any(r => roleList.Contains(r.RoleName)));
            }

            var allMenus = await query.OrderBy(m => m.SortOrder).ToListAsync();

            var lookup = allMenus.ToLookup(m => m.ParentId);

            return BuildTree(null, lookup);
        }

        public async Task<List<Menu>> GetAllAsync()
        {
            return await _context.Menus
                .Include(m => m.MenuRoles)
                .OrderBy(m => m.SortOrder)
                .ToListAsync();
        }

        public async Task<bool> DeactivateAsync(int id)
        {
            var menu = await _context.Menus.FindAsync(id);
            if (menu is null) return false;

            menu.IsActive = false;
            await _context.SaveChangesAsync();
            return true;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static List<MenuTreeNodeDto> BuildTree(int? parentId, ILookup<int?, Menu> lookup)
        {
            return lookup[parentId]
                .Select(m => new MenuTreeNodeDto
                {
                    Id        = m.Id,
                    Title     = m.Title,
                    Icon      = m.Icon,
                    Route     = m.Route,
                    SortOrder = m.SortOrder,
                    Roles     = m.MenuRoles.Select(r => r.RoleName).ToList(),
                    Children  = BuildTree(m.Id, lookup)
                })
                .ToList();
        }
    }
}
