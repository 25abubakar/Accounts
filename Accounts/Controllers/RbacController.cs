using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/rbac")]
    [Produces("application/json")]
    public class RbacController : ControllerBase
    {
        private readonly RbacService          _rbac;
        private readonly ApplicationDbContext _db;

        public RbacController(RbacService rbac, ApplicationDbContext db)
        {
            _rbac = rbac;
            _db   = db;
        }

        private string? CurrentUserId =>
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        // ── HasAccess check ───────────────────────────────────────────────────

        /// <summary>
        /// Check if a staff member has access to a specific feature.
        /// Resolution: UserOverride → RoleDefault → Matrix → false
        /// </summary>
        [HttpGet("staff/{staffId:guid}/has-access/{featureKey}")]
        public async Task<IActionResult> HasAccess(Guid staffId, string featureKey) =>
            Ok(new
            {
                staffId,
                featureKey,
                hasAccess = await _rbac.HasAccessAsync(staffId, featureKey)
            });

        /// <summary>Get all effective permissions for a staff member</summary>
        [HttpGet("staff/{staffId:guid}/effective-permissions")]
        public async Task<IActionResult> GetEffectivePermissions(Guid staffId) =>
            Ok(new
            {
                staffId,
                permissions = await _rbac.GetEffectivePermissionsAsync(staffId)
            });

        // ── Department Matrix ─────────────────────────────────────────────────

        /// <summary>
        /// Get the full permission matrix for a department.
        /// Each cell shows: effectiveAccess, source (UserOverride/RoleDefault/Matrix/Denied), hasUserOverride.
        /// Optimized — no N+1 queries.
        /// </summary>
        [HttpGet("matrix/{deptId:int}")]
        public async Task<IActionResult> GetMatrix(int deptId) =>
            Ok(await _rbac.GetDepartmentMatrixAsync(deptId));

        // ── Role Permissions ──────────────────────────────────────────────────

        /// <summary>
        /// Get default permissions for a job title.
        /// Example: GET /api/rbac/roles/Agent/permissions?deptId=4
        /// </summary>
        [HttpGet("roles/{jobTitle}/permissions")]
        public async Task<IActionResult> GetRolePermissions(
            string jobTitle, [FromQuery] int? deptId) =>
            Ok(await _rbac.GetRolePermissionsAsync(jobTitle, deptId));

        /// <summary>
        /// Set default permissions for a job title in a department.
        /// Send a dictionary of featureKey → isAllowed.
        /// Example body: { "ATTENDANCE_VIEW": true, "EMPLOYEE_EDIT": false }
        /// </summary>
        [HttpPut("roles/{jobTitle}/permissions")]
        public async Task<IActionResult> SetRolePermissions(
            string jobTitle,
            [FromQuery] int? deptId,
            [FromBody] Dictionary<string, bool> permissions)
        {
            if (permissions == null || !permissions.Any())
                return BadRequest(new { message = "No permissions provided." });

            var count = await _rbac.SetRolePermissionsAsync(
                jobTitle, deptId, permissions, CurrentUserId);

            return Ok(new { message = $"{count} role permissions updated.", jobTitle, deptId });
        }

        // ── User Overrides ────────────────────────────────────────────────────

        /// <summary>Get all user-specific overrides for a staff member</summary>
        [HttpGet("staff/{staffId:guid}/overrides")]
        public async Task<IActionResult> GetOverrides(Guid staffId) =>
            Ok(await _rbac.GetUserOverridesAsync(staffId));

        /// <summary>
        /// Get the sidebar menu filtered by the logged-in user's permissions.
        /// Menu items the user can't access are removed. Empty groups are removed.
        /// </summary>
        [HttpGet("sidebar")]
        public async Task<IActionResult> GetFilteredSidebar()
        {
            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized(new { message = "Not authenticated." });

            // SuperAdmin sees everything
            if (User.IsInRole("SuperAdmin"))
            {
                var allMenus = await _rbac.GetFilteredSidebarAsync(Guid.Empty);
                // For SuperAdmin just return full menu from MenuService
                return Ok(allMenus);
            }

            var person = await _db.Persons
                .AsNoTracking()
                .Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId);

            if (person?.Staff == null)
                return Ok(new List<object>()); // not hired — empty sidebar

            var sidebar = await _rbac.GetFilteredSidebarAsync(person.Staff.StaffId);
            return Ok(sidebar);
        }

        /// <summary>
        /// Set a user-specific permission override with ALLOW / DENY / INHERIT.
        /// DENY short-circuits ALL other rules — even if role says ALLOW.
        /// Body: { "status": "DENY", "reason": "Suspended by manager" }
        /// </summary>
        [HttpPut("staff/{staffId:guid}/overrides/{featureKey}")]
        public async Task<IActionResult> SetOverride(
            Guid staffId, string featureKey, [FromBody] SetOverrideDto dto)
        {
            if (!Enum.TryParse<PermissionStatus>(dto.Status, true, out var status))
                return BadRequest(new { message = $"Invalid status '{dto.Status}'. Use ALLOW, DENY, or INHERIT." });

            var (ok, msg) = await _rbac.SetUserOverrideAsync(
                staffId, featureKey, status, CurrentUserId, dto.Reason);

            if (!ok) return msg.Contains("not found")
                ? NotFound(new { message = msg })
                : BadRequest(new { message = msg });

            return Ok(new { message = msg });
        }

        /// <summary>
        /// Remove a user-specific override — reverts to role default.
        /// </summary>
        [HttpDelete("staff/{staffId:guid}/overrides/{featureKey}")]
        public async Task<IActionResult> RemoveOverride(Guid staffId, string featureKey)
        {
            var (ok, msg) = await _rbac.RemoveUserOverrideAsync(staffId, featureKey);
            return ok ? Ok(new { message = msg }) : NotFound(new { message = msg });
        }
    }

    public class SetOverrideDto
    {
        /// <summary>ALLOW, DENY, or INHERIT</summary>
        public string  Status { get; set; } = "INHERIT";
        public string? Reason { get; set; }
    }
}
