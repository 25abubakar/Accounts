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
        private readonly IAuthService           _service;
        private readonly IUserSessionService    _session;
        private readonly RbacService            _rbac;
        private readonly IPersonAccessService   _personAccess;
        private readonly ApplicationDbContext   _db;
        private readonly UserManager<IdentityUser> _userManager;

        public AuthController(
            IAuthService service,
            IUserSessionService session,
            RbacService rbac,
            IPersonAccessService personAccess,
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager)
        {
            _service       = service;
            _session       = session;
            _rbac          = rbac;
            _personAccess  = personAccess;
            _db            = db;
            _userManager   = userManager;
        }

        /// <summary>Register a new user with a role (Manager / Developer / AssistantManager)</summary>
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

        /// <summary>Login with email and password</summary>
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (success, statusCode, response) = await _service.LoginAsync(dto);
            if (!success)
                return StatusCode(statusCode, response);

            // Cookie is set; load session using the user we just authenticated
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
        /// Post-login bootstrap: filtered sidebar, permissions, and admin instructions.
        /// Call immediately after successful login.
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

        // ── /api/auth/my-menus ────────────────────────────────────────────────

        /// <summary>
        /// Returns the filtered sidebar menu tree and allowed feature keys
        /// for the currently authenticated user — optimized for sub-0.5s response.
        ///
        /// How it works (fixed number of queries, no loops):
        ///   1. Resolve Person → StaffId (1 query)
        ///   2. Bulk-load user overrides, role permissions, matrix rows,
        ///      group features all at once (4–5 queries)
        ///   3. Resolve permissions 100% in-memory via HashSet lookups
        ///   4. Filter the Menus tree in-memory, return only visible items
        ///
        /// SuperAdmin / Admin bypass: see every menu, every feature key.
        /// Regular user: sees only what the admin has granted them.
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

            // ── SuperAdmin / Admin gets everything without any permission checks ──
            bool isFullAccess = User.IsInRole("SuperAdmin") || User.IsInRole("Admin");

            if (isFullAccess)
            {
                var allSidebar  = await _rbac.GetFilteredSidebarAsync(Guid.Empty);
                var allFeatures = await _db.Features.AsNoTracking()
                    .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
                    .Select(f => new { f.PermissionId, f.FeatureKey, f.FeatureName, f.Module })
                    .ToListAsync(ct);

                return Ok(new
                {
                    status       = true,
                    isFullAccess = true,
                    staffId      = (Guid?)null,
                    menus        = allSidebar,
                    permissions  = allFeatures.Select(f => f.FeatureKey).ToList(),
                    permissionDetails = allFeatures
                });
            }

            // ── Regular user — look up their Staff record ─────────────────────
            var person = await _db.Persons
                .AsNoTracking()
                .Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId, ct);

            // Not registered as a person at all (edge case)
            if (person == null)
                return Ok(new
                {
                    status       = true,
                    isFullAccess = false,
                    staffId      = (Guid?)null,
                    menus        = new List<object>(),
                    permissions  = new List<string>(),
                    permissionDetails = new List<object>()
                });

            // Direct admin grants (PersonMenus + PersonFeatures) — primary model
            if (await _personAccess.HasPersonGrantsAsync(person.PersonId, ct))
            {
                var sidebar = await _personAccess.GetGrantedSidebarAsync(person.PersonId, ct);
                var keys    = await _personAccess.GetGrantedFeatureKeysAsync(person.PersonId, ct);
                var allowedIds = await _personAccess.GetGrantedPermissionIdsAsync(person.PersonId, ct);
                var allowedFeatures = await _db.Features.AsNoTracking()
                    .Where(f => allowedIds.Contains(f.PermissionId))
                    .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
                    .Select(f => new { f.PermissionId, f.FeatureKey, f.FeatureName, f.Module })
                    .ToListAsync(ct);

                return Ok(new
                {
                    status       = true,
                    isFullAccess = false,
                    personId     = person.PersonId,
                    staffId      = person.Staff?.StaffId,
                    menus        = sidebar,
                    permissions  = keys,
                    permissionDetails = allowedFeatures,
                    accessSource = "PersonMenus"
                });
            }

            if (person.Staff == null)
                return Ok(new
                {
                    status       = true,
                    isFullAccess = false,
                    personId     = person.PersonId,
                    staffId      = (Guid?)null,
                    menus        = new List<object>(),
                    permissions  = new List<string>(),
                    message      = "No access granted yet. Ask admin to assign menu permissions.",
                    permissionDetails = new List<object>()
                });

            var staffId = person.Staff.StaffId;
            var legacyAllowedIds = await _rbac.GetEffectivePermissionIdsAsync(staffId);
            var legacyFeatures = await _db.Features.AsNoTracking()
                .Where(f => legacyAllowedIds.Contains(f.PermissionId))
                .OrderBy(f => f.Module).ThenBy(f => f.FeatureKey)
                .Select(f => new { f.PermissionId, f.FeatureKey, f.FeatureName, f.Module })
                .ToListAsync(ct);

            var allMenus = await _db.Menus.AsNoTracking()
                .Include(m => m.MenuPermissions)
                .Where(m => m.IsActive)
                .OrderBy(m => m.SortOrder)
                .ToListAsync(ct);

            var lookup  = allMenus.ToLookup(m => m.ParentId);
            var legacySidebar = BuildFilteredMenuTree(null, lookup, legacyAllowedIds);

            return Ok(new
            {
                status       = true,
                isFullAccess = false,
                personId     = person.PersonId,
                staffId,
                menus        = legacySidebar,
                permissions  = legacyFeatures.Select(f => f.FeatureKey).ToList(),
                permissionDetails = legacyFeatures,
                accessSource = "StaffRbac"
            });
        }

        /// <summary>
        /// Recursively builds sidebar tree, keeping only menus the user can see.
        /// Public menus (no MenuPermissions) are always included.
        /// Restricted menus require at least one matching PermissionId.
        /// Empty parent groups with no visible children are pruned.
        /// </summary>
        private static List<object> BuildFilteredMenuTree(
            int? parentId,
            ILookup<int?, Accounts.Models.Menu> lookup,
            HashSet<int> allowedIds)
        {
            var result = new List<object>();
            foreach (var menu in lookup[parentId])
            {
                var requiredIds = menu.MenuPermissions.Select(mp => mp.PermissionId).ToList();

                // Public = no permissions required; restricted = user needs ≥1 match
                bool canSee = !requiredIds.Any() || requiredIds.Any(id => allowedIds.Contains(id));
                if (!canSee) continue;

                var children = BuildFilteredMenuTree(menu.Id, lookup, allowedIds);

                // Prune empty parent groups
                if (!children.Any() && string.IsNullOrWhiteSpace(menu.Route) && lookup[menu.Id].Any())
                    continue;

                result.Add(new
                {
                    id        = menu.Id,
                    title     = menu.Title,
                    icon      = menu.Icon,
                    route     = menu.Route,
                    sortOrder = menu.SortOrder,
                    children
                });
            }
            return result;
        }
    }
}
