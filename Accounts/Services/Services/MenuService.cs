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

            _context.Menus.Add(menu);
            await _context.SaveChangesAsync();

            // Seed MENU_{id} feature keys and link them via MenuPermissions
            await SeedMenuFeaturesAsync(menu);

            return menu;
        }

        /// <summary>
        /// Seeds MENU_{id}, MENU_{id}_VIEW/_ADD/_EDIT/_DELETE into Features.
        /// Also links the MENU_{id} feature to this menu via MenuPermissions (int FK).
        /// Idempotent.
        /// </summary>
        private async Task SeedMenuFeaturesAsync(Menu menu)
        {
            var keysToSeed = new[]
            {
                ($"MENU_{menu.Id}",        menu.Title,               "Menu"),
                ($"MENU_{menu.Id}_VIEW",   $"{menu.Title} - View",   "Menu"),
                ($"MENU_{menu.Id}_ADD",    $"{menu.Title} - Add",    "Menu"),
                ($"MENU_{menu.Id}_EDIT",   $"{menu.Title} - Edit",   "Menu"),
                ($"MENU_{menu.Id}_DELETE", $"{menu.Title} - Delete", "Menu"),
            };

            var existingKeys = await _context.Features
                .Where(f => f.FeatureKey.StartsWith($"MENU_{menu.Id}"))
                .Select(f => f.FeatureKey)
                .ToHashSetAsync();

            var toAdd = keysToSeed
                .Where(k => !existingKeys.Contains(k.Item1))
                .Select(k => new Feature { FeatureKey = k.Item1, FeatureName = k.Item2, Module = k.Item3 })
                .ToList();

            if (toAdd.Count > 0)
            {
                _context.Features.AddRange(toAdd);
                await _context.SaveChangesAsync();
            }

            // Link MENU_{id} permission to this menu via MenuPermissions
            var menuFeature = await _context.Features
                .FirstOrDefaultAsync(f => f.FeatureKey == $"MENU_{menu.Id}");

            if (menuFeature != null)
            {
                var alreadyLinked = await _context.MenuPermissions
                    .AnyAsync(mp => mp.MenuId == menu.Id && mp.PermissionId == menuFeature.PermissionId);

                if (!alreadyLinked)
                {
                    _context.MenuPermissions.Add(new MenuPermission
                    {
                        MenuId       = menu.Id,
                        PermissionId = menuFeature.PermissionId
                    });
                    await _context.SaveChangesAsync();
                }
            }
        }

        public async Task<List<MenuTreeNodeDto>> GetSidebarTreeAsync(IEnumerable<string>? userRoles = null)
        {
            var allMenus = await _context.Menus
                .Include(m => m.MenuPermissions).ThenInclude(mp => mp.Feature)
                .Where(m => m.IsActive)
                .OrderBy(m => m.SortOrder)
                .ToListAsync();

            if (userRoles != null && userRoles.Any())
            {
                var roleSet = userRoles.ToHashSet();
                allMenus = allMenus.Where(m =>
                    !m.MenuPermissions.Any() ||
                    m.MenuPermissions.Any(mp => mp.Feature != null && roleSet.Contains(mp.Feature.FeatureKey))
                ).ToList();
            }

            var lookup = allMenus.ToLookup(m => m.ParentId);
            return BuildTree(null, lookup);
        }

        public async Task<List<Menu>> GetAllAsync()
        {
            return await _context.Menus
                .Include(m => m.MenuPermissions).ThenInclude(mp => mp.Feature)
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
                    Roles     = m.MenuPermissions
                        .Where(mp => mp.Feature != null)
                        .Select(mp => mp.Feature!.FeatureKey).ToList(),
                    Children  = BuildTree(m.Id, lookup)
                })
                .ToList();
        }
    }
}
