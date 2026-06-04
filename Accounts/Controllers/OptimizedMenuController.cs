using Accounts.Data;
using Accounts.DTOs;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers
{
    /// <summary>
    /// Optimized menu and permission API endpoints.
    /// NO N+1 queries - loads all data in 2-3 queries, resolves in-memory.
    /// </summary>
    [ApiController]
    [Route("api/v2/menu")]
    [Authorize]
    public class OptimizedMenuController : ControllerBase
    {
        private readonly OptimizedMenuService _menuService;
        private readonly ApplicationDbContext _db;

        public OptimizedMenuController(
            OptimizedMenuService menuService,
            ApplicationDbContext db)
        {
            _menuService = menuService;
            _db = db;
        }

        /// <summary>
        /// GET /api/v2/menu/session
        /// Returns sidebar menu tree + allowed permission IDs for the current user.
        /// SuperAdmin/Admin users see all menus without permission filtering.
        /// </summary>
        [HttpGet("session")]
        public async Task<IActionResult> GetMenuSession(
            [FromQuery] bool includeDetailedPermissions = false,
            CancellationToken cancellationToken = default)
        {
            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(identityUserId))
                return Unauthorized(new { message = "User not authenticated" });

            // Check if user is SuperAdmin or Admin (full access)
            var isFullAccess = User.IsInRole("SuperAdmin") || User.IsInRole("Admin");

            if (isFullAccess)
            {
                // SuperAdmin gets all menus
                var session = await _menuService.GetUserMenuSessionAsync(
                    Guid.Empty, 
                    includeDetailedPermissions, 
                    cancellationToken);
                
                return Ok(session);
            }

            // Regular user - look up their Staff record
            var person = await _db.Persons
                .AsNoTracking()
                .Include(p => p.Staff)
                .Where(p => p.IdentityUserId == identityUserId)
                .Select(p => new { StaffId = p.Staff != null ? p.Staff.StaffId : (Guid?)null })
                .FirstOrDefaultAsync(cancellationToken);

            if (person?.StaffId == null)
            {
                return Ok(new UserMenuSessionDto
                {
                    StaffId = null,
                    IsFullAccess = false,
                    Sidebar = new List<MenuResponseDto>(),
                    AllowedPermissionIds = new List<int>()
                });
            }

            var userSession = await _menuService.GetUserMenuSessionAsync(
                person.StaffId.Value,
                includeDetailedPermissions,
                cancellationToken);

            return Ok(userSession);
        }

        /// <summary>
        /// GET /api/v2/menu/check-access/{permissionId}
        /// Check if current user has access to a specific permission.
        /// </summary>
        [HttpGet("check-access/{permissionId:int}")]
        public async Task<IActionResult> CheckAccess(
            int permissionId,
            CancellationToken cancellationToken = default)
        {
            var staffId = await GetCurrentStaffIdAsync(cancellationToken);
            if (staffId == null)
                return Ok(new { hasAccess = false, message = "Staff not found" });

            var hasAccess = await _menuService.HasAccessAsync(
                staffId.Value, 
                permissionId, 
                cancellationToken);

            return Ok(new { hasAccess });
        }

        /// <summary>
        /// GET /api/v2/menu/check-access-by-key/{featureKey}
        /// Check access by FeatureKey (backward compatibility).
        /// </summary>
        [HttpGet("check-access-by-key/{featureKey}")]
        public async Task<IActionResult> CheckAccessByKey(
            string featureKey,
            CancellationToken cancellationToken = default)
        {
            var staffId = await GetCurrentStaffIdAsync(cancellationToken);
            if (staffId == null)
                return Ok(new { hasAccess = false, message = "Staff not found" });

            var hasAccess = await _menuService.HasAccessByKeyAsync(
                staffId.Value,
                featureKey,
                cancellationToken);

            return Ok(new { hasAccess, featureKey });
        }

        /// <summary>
        /// GET /api/v2/menu/my-permissions
        /// Get all allowed permission IDs and FeatureKeys for current user.
        /// </summary>
        [HttpGet("my-permissions")]
        public async Task<IActionResult> GetMyPermissions(
            CancellationToken cancellationToken = default)
        {
            var staffId = await GetCurrentStaffIdAsync(cancellationToken);
            if (staffId == null)
            {
                return Ok(new
                {
                    permissionIds = new List<int>(),
                    featureKeys = new List<string>()
                });
            }

            var featureKeys = await _menuService.GetAllowedFeatureKeysAsync(
                staffId.Value,
                cancellationToken);

            var permissionIds = await _db.Features
                .AsNoTracking()
                .Where(f => featureKeys.Contains(f.FeatureKey))
                .Select(f => f.PermissionId)
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                permissionIds,
                featureKeys
            });
        }

        /// <summary>
        /// Helper: Get current user's StaffId from Person record.
        /// </summary>
        private async Task<Guid?> GetCurrentStaffIdAsync(CancellationToken cancellationToken)
        {
            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(identityUserId))
                return null;

            // Check if SuperAdmin/Admin
            if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin"))
                return Guid.Empty; // Convention: Empty GUID for full access

            var person = await _db.Persons
                .AsNoTracking()
                .Include(p => p.Staff)
                .Where(p => p.IdentityUserId == identityUserId)
                .Select(p => p.Staff != null ? p.Staff.StaffId : (Guid?)null)
                .FirstOrDefaultAsync(cancellationToken);

            return person;
        }
    }
}
