using Accounts.Authorization;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/access")]
    [Authorize]
    [Produces("application/json")]
    public class AccessController : ControllerBase
    {
        private readonly IAccessService _service;
        public AccessController(IAccessService service) => _service = service;

        private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

        [HttpGet("features")]
        public async Task<IActionResult> GetFeatures() =>
            Ok(await _service.GetAllFeaturesAsync());

        [HttpGet("features/module/{module}")]
        public async Task<IActionResult> GetFeaturesByModule(string module) =>
            Ok(await _service.GetFeaturesByModuleAsync(module));

        [HttpGet("staff/{staffId:guid}/permissions")]
        public async Task<IActionResult> GetStaffPermissions(Guid staffId) =>
            Ok(await _service.GetStaffPermissionsAsync(staffId));

        [HasPermission("ACCESS_GROUP_VIEW")]
        [HttpGet("department/{deptId:int}/persons")]
        public async Task<IActionResult> GetDepartmentPersons(int deptId) =>
            Ok(await _service.GetDepartmentPersonsAsync(deptId));

        [HasPermission("ACCESS_GROUP_ASSIGN")]
        [HttpPut("staff/{staffId:guid}/feature/{*featureKey}")]
        [HttpPut("staff/{staffId:guid}/feature")]
        public async Task<IActionResult> TogglePermission(
            Guid staffId,
            string? featureKey,
            [FromQuery] string? key,
            [FromBody] ToggleDto dto)
        {
            var resolvedKey = string.IsNullOrWhiteSpace(featureKey)
                ? key
                : Uri.UnescapeDataString(featureKey.Trim()).Trim('/');
            if (string.IsNullOrWhiteSpace(resolvedKey))
                return BadRequest(new { message = "featureKey is required in route or query string." });

            (bool ok, string msg) = await _service.TogglePermissionAsync(staffId, resolvedKey, dto.HasAccess, CurrentUserId);
            if (!ok) return msg.Contains("not found") ? NotFound(new { message = msg }) : BadRequest(new { message = msg });
            return Ok(new { message = msg, staffId, featureKey = resolvedKey, hasAccess = dto.HasAccess });
        }

        [HasPermission("ACCESS_GROUP_ASSIGN")]
        [HttpPost("staff/{staffId:guid}/grant-all")]
        public async Task<IActionResult> GrantAll(Guid staffId, [FromQuery] int deptId = 0)
        {
            (int count, string msg) = await _service.GrantAllAsync(staffId, deptId, CurrentUserId);
            return Ok(new { granted = count, message = msg });
        }

        [HasPermission("ACCESS_GROUP_ASSIGN")]
        [HttpDelete("staff/{staffId:guid}/revoke-all")]
        public async Task<IActionResult> RevokeAll(Guid staffId)
        {
            (int count, string msg) = await _service.RevokeAllAsync(staffId, CurrentUserId);
            return Ok(new { revoked = count, message = msg });
        }
    }

    public class ToggleDto { public bool HasAccess { get; set; } }
}
