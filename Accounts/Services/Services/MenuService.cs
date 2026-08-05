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

        public async Task<Menu?> UpdateMenuAsync(int id, CreateMenuDto dto)
        {
            var menu = await _context.Menus.FirstOrDefaultAsync(m => m.Id == id);
            if (menu is null) return null;
            if (dto.ParentId == id) throw new InvalidOperationException("A menu cannot be its own parent.");

            if (dto.ParentId.HasValue)
            {
                if (!await _context.Menus.AnyAsync(m => m.Id == dto.ParentId.Value && m.IsActive))
                    throw new InvalidOperationException("The selected parent menu does not exist or is inactive.");

                var cursor = dto.ParentId;
                while (cursor.HasValue)
                {
                    if (cursor.Value == id)
                        throw new InvalidOperationException("A menu cannot be moved below one of its child menus.");
                    cursor = await _context.Menus.Where(m => m.Id == cursor.Value)
                        .Select(m => m.ParentId).FirstOrDefaultAsync();
                }
            }

            var route = string.IsNullOrWhiteSpace(dto.Route) ? null : dto.Route.Trim();
            if (route != null && !route.StartsWith('/')) route = "/" + route;
            if (route != null && await _context.Menus.AnyAsync(m => m.Id != id && m.IsActive && m.Route == route))
                throw new InvalidOperationException($"Another active menu already uses route '{route}'.");

            menu.Title = dto.Title.Trim();
            menu.Icon = string.IsNullOrWhiteSpace(dto.Icon) ? null : dto.Icon.Trim();
            menu.Route = route;
            menu.ParentId = dto.ParentId;
            menu.SortOrder = dto.SortOrder;
            await _context.SaveChangesAsync();
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

            // Link the complete CRUD bundle. TenantMenuCeilingService resolves
            // View/Add/Edit/Delete from MenuPermissions, so linking only the
            // top-level MENU_{id} makes every action appear granted in the UI
            // but causes the backend to discard it.
            var seededKeys = keysToSeed.Select(item => item.Item1).ToArray();
            var menuFeatures = await _context.Features
                .Where(feature => seededKeys.Contains(feature.FeatureKey))
                .Select(feature => new { feature.PermissionId })
                .ToListAsync();
            var linkedIds = await _context.MenuPermissions
                .Where(link => link.MenuId == menu.Id)
                .Select(link => link.PermissionId)
                .ToHashSetAsync();
            var missingLinks = menuFeatures
                .Where(feature => !linkedIds.Contains(feature.PermissionId))
                .Select(feature => new MenuPermission
                {
                    MenuId = menu.Id,
                    PermissionId = feature.PermissionId
                })
                .ToList();
            if (missingLinks.Count > 0)
            {
                _context.MenuPermissions.AddRange(missingLinks);
                await _context.SaveChangesAsync();
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
            var menu = await _context.Menus.FirstOrDefaultAsync(m => m.Id == id);
            if (menu is null) return false;

            var allMenus = await _context.Menus.ToListAsync();
            var childrenByParent = allMenus.ToLookup(m => m.ParentId);
            var pending = new Stack<int>();
            pending.Push(id);
            while (pending.Count > 0)
            {
                var currentId = pending.Pop();
                allMenus.First(m => m.Id == currentId).IsActive = false;
                foreach (var child in childrenByParent[currentId]) pending.Push(child.Id);
            }
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
