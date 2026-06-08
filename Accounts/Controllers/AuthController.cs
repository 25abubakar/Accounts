using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/auth")]
    [Produces("application/json")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _service;
        private readonly IUserSessionService _session;
        private readonly RbacService _rbac;
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public AuthController(
            IAuthService service,
            IUserSessionService session,
            RbacService rbac,
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager)
        {
            _service = service;
            _session = session;
            _rbac = rbac;
            _db = db;
            _userManager = userManager;
        }

        /// <summary>Register a new user with a role</summary>
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (success, message, response) = await _service.RegisterAsync(dto);
            if (!success)
            {
                response.Message = message;
                return message.Contains("already") ? Conflict(response) : BadRequest(response);
            }
            return Ok(response);
        }

        /// <summary>Login with username or email</summary>
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (success, statusCode, response) = await _service.LoginAsync(dto);
            if (!success)
                return StatusCode(statusCode, response);

            var user = await _userManager.FindByNameAsync(response.Username ?? dto.Username)
                      ?? await _userManager.FindByEmailAsync(dto.Username);

            if (user == null)
                return StatusCode(statusCode, response);

            var roles = await _userManager.GetRolesAsync(user);
            var isFullAccess = roles.Contains("SuperAdmin") || roles.Contains("Admin");
            var sessionData = await _session.GetSessionAsync(user.Id, isFullAccess);

            return Ok(new
            {
                response.Success,
                response.Message,
                response.Username,
                response.Email,
                roles,
                session = sessionData
            });
        }

        /// <summary>Logout the current user</summary>
        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await _service.LogoutAsync();
            return Ok(new { success = true, message = "Logged out successfully." });
        }

        /// <summary>
        /// Post-login bootstrap: sidebar, permissions, and login instructions.
        /// GET /api/auth/session
        /// </summary>
        [HttpGet("session")]
        [Authorize]
        public async Task<IActionResult> GetSession(CancellationToken ct)
        {
            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized(new { success = false, message = "Not authenticated." });

            var isFullAccess = User.IsInRole("SuperAdmin") || User.IsInRole("Admin");
            var session = await _session.GetSessionAsync(identityUserId, isFullAccess, ct);
            return Ok(new { success = true, data = session });
        }

        /// <summary>Assign a role to an existing user</summary>
        [HttpPost("assign-role")]
        [AllowAnonymous]
        public async Task<IActionResult> AssignRole([FromBody] AssignRoleDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (success, message, response) = await _service.AssignRoleAsync(dto);
            if (!success)
            {
                response.Message = message;
                return message.Contains("not found") ? NotFound(response) : BadRequest(response);
            }
            return Ok(response);
        }

        /// <summary>Get all system users with their roles</summary>
        [HttpGet("users")]
        [AllowAnonymous]
        public async Task<IActionResult> GetUsers() =>
            Ok(await _service.GetUsersAsync());

        /// <summary>
        /// Filtered sidebar + allowed permissions for the current user.
        ///
        /// Resolution path (3 layers only):
        ///   1. SuperAdmin / Admin → all menus, all features
        ///   2. Staff with a role  → RolePermissions + UserPermissionOverrides
        ///   3. Person with no staff record → empty (no access granted yet)
        ///
        /// GET /api/auth/my-menus
        /// </summary>
        [HttpGet("my-menus")]
        [Authorize]
        public async Task<IActionResult> GetMyMenus(CancellationToken ct)
        {
            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized(new { status = false, message = "Invalid token" });

            bool isFullAccess = User.IsInRole("SuperAdmin") || User.IsInRole("Admin");

            if (isFullAccess)
            {
                var allSidebar = await _rbac.GetFilteredSidebarAsync(Guid.Empty);
                var allFeatures = await _db.Features.AsNoTracking()
                    .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
                    .Select(f => new { f.PermissionId, f.FeatureKey, f.FeatureName, f.Module })
                    .ToListAsync(ct);

                return Ok(new
                {
                    status = true,
                    isFullAccess = true,
                    staffId = (Guid?)null,
                    menus = allSidebar,
                    permissions = allFeatures.Select(f => f.FeatureKey).ToList(),
                    permissionDetails = allFeatures
                });
            }

            var person = await _db.Persons
                .AsNoTracking()
                .Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId, ct);

            if (person == null)
                return Ok(new
                {
                    status = true,
                    isFullAccess = false,
                    staffId = (Guid?)null,
                    menus = new List<object>(),
                    permissions = new List<string>(),
                    permissionDetails = new List<object>()
                });

            if (person.Staff == null)
                return Ok(new
                {
                    status = true,
                    isFullAccess = false,
                    personId = person.PersonId,
                    staffId = (Guid?)null,
                    menus = new List<object>(),
                    permissions = new List<string>(),
                    message = "No staff record found. Ask admin to assign a position.",
                    permissionDetails = new List<object>()
                });

            // 3-layer RBAC: RolePermissions → UserOverrides → deny
            var staffId = person.Staff.StaffId;
            var allowedIds = await _rbac.GetEffectivePermissionIdsAsync(staffId);

            var features = await _db.Features.AsNoTracking()
                .Where(f => allowedIds.Contains(f.PermissionId))
                .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
                .Select(f => new { f.PermissionId, f.FeatureKey, f.FeatureName, f.Module })
                .ToListAsync(ct);

            var allMenus = await _db.Menus.AsNoTracking()
                .Include(m => m.MenuPermissions)
                .Where(m => m.IsActive)
                .OrderBy(m => m.SortOrder)
                .ToListAsync(ct);

            var lookup = allMenus.ToLookup(m => m.ParentId);
            var sidebar = BuildFilteredMenuTree(null, lookup, allowedIds);

            return Ok(new
            {
                status = true,
                isFullAccess = false,
                personId = person.PersonId,
                staffId,
                menus = sidebar,
                permissions = features.Select(f => f.FeatureKey).ToList(),
                permissionDetails = features,
                accessSource = "RolePermissions"
            });
        }

        private static List<object> BuildFilteredMenuTree(
            int? parentId,
            ILookup<int?, Menu> lookup,
            HashSet<int> allowedIds)
        {
            var result = new List<object>();
            foreach (var menu in lookup[parentId])
            {
                var requiredIds = menu.MenuPermissions.Select(mp => mp.PermissionId).ToList();

                bool hasRequiredPermissions = requiredIds.Any();
                bool userHasAccess = hasRequiredPermissions && requiredIds.Any(id => allowedIds.Contains(id));
                bool isFolder = string.IsNullOrWhiteSpace(menu.Route);

                // Default Deny logic: Unmapped pages are hidden. Folders are checked based on children.
                bool canSee = userHasAccess || (isFolder && !hasRequiredPermissions);

                if (!canSee) continue;

                var children = BuildFilteredMenuTree(menu.Id, lookup, allowedIds);

                // Hide empty folders
                if (!children.Any() && isFolder)
                    continue;

                result.Add(new
                {
                    id = menu.Id,
                    title = menu.Title,
                    icon = menu.Icon,
                    route = menu.Route,
                    sortOrder = menu.SortOrder,
                    children
                });
            }
            return result;
        }
    }
}