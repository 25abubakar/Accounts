using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Accounts.Controllers
{
    /// <summary>
    /// 2-Tier RBAC endpoints.
    ///
    /// Tier 1 — StaffMenuAccess: which menus a staff member can open
    /// Tier 2 — AccessFeatures:  which CRUD operations are allowed per menu
    ///
    /// The primary GET endpoint returns a nested tree consumed at login.
    /// All write endpoints are Admin-only.
    /// </summary>
    [ApiController]
    [Route("api/staff-menu-access")]
    [Authorize]
    [Produces("application/json")]
    public class StaffMenuAccessController : ControllerBase
    {
        private readonly StaffMenuAccessService _service;

        public StaffMenuAccessController(StaffMenuAccessService service) => _service = service;

        private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

        // ─── READ ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the full nested permission tree for a staff member.
        /// One optimized joined query — no N+1.
        ///
        /// Response:
        /// {
        ///   staffId, menus: [ { menuId, menuTitle, route, isAllow,
        ///     features: [{ permissionId, featureKey, featureName, isAllow }]
        ///   }],
        ///   allowedFeatureKeys: ["MENU_8_VIEW", "MENU_8_ADD", ...]
        /// }
        ///
        /// GET /api/staff-menu-access/{staffId}
        /// </summary>
        [HttpGet("{staffId:guid}")]
        public async Task<IActionResult> GetAccessTree(Guid staffId) =>
            Ok(await _service.GetStaffAccessTreeAsync(staffId));

        // ─── GRANT ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Grant a staff member access to a single menu and all its feature keys.
        ///
        /// Body (optional — omit to grant all features as ALLOW):
        /// {
        ///   "isAllow": true,
        ///   "featureOverrides": [
        ///     { "permissionId": 42, "isAllow": false }   ← deny EDIT only
        ///   ]
        /// }
        ///
        /// POST /api/staff-menu-access/{staffId}/grant/{menuId}
        /// </summary>
        [HttpPost("{staffId:guid}/grant/{menuId:int}")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> GrantMenu(
            Guid staffId,
            int menuId,
            [FromBody] GrantMenuDto? dto)
        {
            var featureOverrides = dto?.FeatureOverrides?
                .Select(fo => (fo.PermissionId, fo.IsAllow));

            var (ok, msg, keys) = await _service.GrantMenuAccessAsync(
                staffId,
                menuId,
                dto?.IsAllow ?? true,
                CurrentUserId,
                featureOverrides);

            return ok
                ? Ok(new { message = msg, grantedFeatureKeys = keys, staffId, menuId })
                : BadRequest(new { message = msg });
        }

        /// <summary>
        /// Bulk-grant multiple menus at once (admin access wizard).
        ///
        /// Body: { "menuId": true/false }
        /// Example: { "8": true, "11": true, "17": false }
        ///
        /// POST /api/staff-menu-access/{staffId}/bulk-grant
        /// </summary>
        [HttpPost("{staffId:guid}/bulk-grant")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> BulkGrantMenus(
            Guid staffId,
            [FromBody] Dictionary<int, bool>? menuGrants)
        {
            if (menuGrants == null || menuGrants.Count == 0)
                return BadRequest(new { message = "No menu grants provided." });

            var (saved, skipped, message) =
                await _service.BulkGrantMenusAsync(staffId, menuGrants, CurrentUserId);

            return Ok(new { message, saved, skipped, staffId });
        }

        // ─── REVOKE ────────────────────────────────────────────────────────────

        /// <summary>
        /// Revoke a staff member's access to a menu (and all child features).
        /// DELETE /api/staff-menu-access/{staffId}/revoke/{menuId}
        /// </summary>
        [HttpDelete("{staffId:guid}/revoke/{menuId:int}")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> RevokeMenu(Guid staffId, int menuId)
        {
            var (ok, msg) = await _service.RevokeMenuAccessAsync(staffId, menuId);
            return ok
                ? Ok(new { message = msg })
                : NotFound(new { message = msg });
        }

        /// <summary>
        /// Remove ALL menu access grants for a staff member.
        /// DELETE /api/staff-menu-access/{staffId}/clear
        /// </summary>
        [HttpDelete("{staffId:guid}/clear")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> ClearAll(Guid staffId)
        {
            var count = await _service.ClearAllAccessAsync(staffId);
            return Ok(new { message = $"Cleared {count} menu access grant(s) for staff {staffId}.", count });
        }

        // ─── FEATURE TOGGLE ────────────────────────────────────────────────────

        /// <summary>
        /// Toggle a single feature flag inside an existing menu access grant.
        ///
        /// Body: { "isAllow": false }
        ///
        /// PATCH /api/staff-menu-access/{staffId}/menus/{menuId}/features/{permissionId}
        /// </summary>
        [HttpPatch("{staffId:guid}/menus/{menuId:int}/features/{permissionId:int}")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> SetFeature(
            Guid staffId,
            int menuId,
            int permissionId,
            [FromBody] SetFeatureDto dto)
        {
            var (ok, msg) = await _service.SetFeatureAccessAsync(
                staffId, menuId, permissionId, dto.IsAllow);

            return ok
                ? Ok(new { message = msg })
                : BadRequest(new { message = msg });
        }
    }

    // ── Request DTOs ──────────────────────────────────────────────────────────

    public class GrantMenuDto
    {
        public bool IsAllow { get; set; } = true;
        public List<FeatureOverrideItem>? FeatureOverrides { get; set; }
    }

    public class FeatureOverrideItem
    {
        public int  PermissionId { get; set; }
        public bool IsAllow      { get; set; }
    }

    public class SetFeatureDto
    {
        public bool IsAllow { get; set; }
    }
}
