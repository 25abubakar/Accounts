using Accounts.Data;
using Accounts.DTOs;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers
{

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

        [HttpGet("session")]
        public async Task<IActionResult> GetMenuSession(
            [FromQuery] bool includeDetailedPermissions = false,
            CancellationToken cancellationToken = default)
        {
            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(identityUserId))
                return Unauthorized(new { message = "User not authenticated" });

            var isFullAccess = User.IsInRole("SuperAdmin") || User.IsInRole("Admin");

            if (isFullAccess)
            {
                var session = await _menuService.GetUserMenuSessionAsync(
                    Guid.Empty, 
                    includeDetailedPermissions, 
                    cancellationToken);
                
                return Ok(session);
            }

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
