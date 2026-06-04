using Accounts.Data;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    /// <summary>
    /// Backward compatibility adapter for old RbacService consumers.
    /// Wraps OptimizedMenuService to provide the same API surface while using
    /// the new optimized implementation under the hood.
    /// 
    /// MIGRATION STRATEGY:
    /// 1. Update Program.cs to register this adapter as RbacService
    /// 2. Old code continues to work without changes
    /// 3. Gradually migrate consumers to use OptimizedMenuService directly
    /// 4. Eventually remove this adapter once all code migrated
    /// </summary>
    public class RbacServiceAdapter
    {
        private readonly ApplicationDbContext _db;
        private readonly OptimizedMenuService _optimizedService;

        public RbacServiceAdapter(
            ApplicationDbContext db,
            OptimizedMenuService optimizedService)
        {
            _db = db;
            _optimizedService = optimizedService;
        }

        /// <summary>
        /// Check if staff has access to a specific feature (by key).
        /// Uses OptimizedMenuService under the hood.
        /// </summary>
        public async Task<bool> HasAccessAsync(Guid staffId, string featureKey)
        {
            return await _optimizedService.HasAccessByKeyAsync(staffId, featureKey);
        }

        /// <summary>
        /// Get all effective permission keys for a staff member.
        /// Returns FeatureKeys for backward compatibility with old code.
        /// </summary>
        public async Task<IEnumerable<string>> GetEffectivePermissionsAsync(Guid staffId)
        {
            return await _optimizedService.GetAllowedFeatureKeysAsync(staffId);
        }

        /// <summary>
        /// Get effective permissions with detailed info (hasAccess + source).
        /// Maps from new PermissionDto to old format.
        /// </summary>
        public async Task<IEnumerable<object>> GetEffectivePermissionsDetailedAsync(Guid staffId)
        {
            var session = await _optimizedService.GetUserMenuSessionAsync(
                staffId, 
                includeDetailedPermissions: true);

            if (session.DetailedPermissions == null)
                return Array.Empty<object>();

            return session.DetailedPermissions.Select(p => new
            {
                featureKey = p.FeatureKey,
                featureName = p.FeatureName,
                module = p.Module,
                hasAccess = p.HasAccess,
                source = p.Source
            }).ToList();
        }

        /// <summary>
        /// Get filtered sidebar for a staff member.
        /// Uses OptimizedMenuService under the hood.
        /// </summary>
        public async Task<List<object>> GetFilteredSidebarAsync(Guid staffId)
        {
            var session = await _optimizedService.GetUserMenuSessionAsync(staffId);
            
            // Convert MenuResponseDto to anonymous objects for compatibility
            return ConvertMenuTree(session.Sidebar);
        }

        private List<object> ConvertMenuTree(List<DTOs.MenuResponseDto> menus)
        {
            return menus.Select(m => new
            {
                id = m.Id,
                title = m.Title,
                icon = m.Icon,
                route = m.Route,
                sortOrder = m.SortOrder,
                children = ConvertMenuTree(m.Children)
            }).ToList<object>();
        }

        /// <summary>
        /// Seed MENU_{id} features for all active menus.
        /// Creates Feature records with auto-generated PermissionId.
        /// </summary>
        public async Task<(int Added, int Skipped)> SeedMenuFeaturesAsync()
        {
            var menus = await _db.Menus.AsNoTracking()
                .Where(m => m.IsActive)
                .ToListAsync();

            var existingKeys = await _db.Features
                .Select(f => f.FeatureKey)
                .ToHashSetAsync();

            var toAdd = new List<Feature>();

            foreach (var menu in menus)
            {
                var keys = new[]
                {
                    ($"MENU_{menu.Id}",        menu.Title,               "Menu"),
                    ($"MENU_{menu.Id}_VIEW",   $"{menu.Title} - View",   "Menu"),
                    ($"MENU_{menu.Id}_ADD",    $"{menu.Title} - Add",    "Menu"),
                    ($"MENU_{menu.Id}_EDIT",   $"{menu.Title} - Edit",   "Menu"),
                    ($"MENU_{menu.Id}_DELETE", $"{menu.Title} - Delete", "Menu"),
                };

                foreach (var (key, name, module) in keys)
                {
                    if (!existingKeys.Contains(key))
                    {
                        toAdd.Add(new Feature
                        {
                            FeatureKey = key,
                            FeatureName = name,
                            Module = module
                            // PermissionId auto-generated by IDENTITY
                        });
                    }
                }
            }

            if (toAdd.Count > 0)
            {
                _db.Features.AddRange(toAdd);
                await _db.SaveChangesAsync();
            }

            return (toAdd.Count, menus.Count * 5 - toAdd.Count);
        }

        /// <summary>
        /// Get department matrix (bulk permission view for dept).
        /// NOTE: This is a complex method - recommend migrating consumers
        /// to a dedicated OptimizedMatrixService instead of using this adapter.
        /// </summary>
        public async Task<object> GetDepartmentMatrixAsync(int deptId)
        {
            // For now, just return a placeholder
            // Full implementation would be too complex for an adapter
            throw new NotImplementedException(
                "GetDepartmentMatrixAsync is too complex for the adapter. " +
                "Please create a dedicated OptimizedMatrixService or migrate " +
                "consumers to use a new optimized endpoint.");
        }
    }
}
