using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/staff-menu-access")]
    [Authorize]
    [Produces("application/json")]
    public class StaffMenuAccessController : ControllerBase
    {
        private readonly StaffMenuAccessService _service;

        public StaffMenuAccessController(StaffMenuAccessService service) => _service = service;

        private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

        [HttpGet("{staffId:guid}")]
        public async Task<IActionResult> GetAccessTree(Guid staffId) =>
            Ok(await _service.GetStaffAccessTreeAsync(staffId));

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

        [HttpDelete("{staffId:guid}/revoke/{menuId:int}")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> RevokeMenu(Guid staffId, int menuId)
        {
            var (ok, msg) = await _service.RevokeMenuAccessAsync(staffId, menuId);
            return ok
                ? Ok(new { message = msg })
                : NotFound(new { message = msg });
        }

        [HttpDelete("{staffId:guid}/clear")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> ClearAll(Guid staffId)
        {
            var count = await _service.ClearAllAccessAsync(staffId);
            return Ok(new { message = $"Cleared {count} menu access grant(s) for staff {staffId}.", count });
        }

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
