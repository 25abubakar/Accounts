using Accounts.Authorization;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/access")]
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

        [HasPermission("ACCESS_GROUP_VIEW")]
        [HttpGet("groups")]
        public async Task<IActionResult> GetGroups() => 
            Ok(await _service.GetAllGroupsAsync());

        [HasPermission("ACCESS_GROUP_VIEW")]
        [HttpGet("groups/{id:int}")]
        public async Task<IActionResult> GetGroup(int id)
        {
            var g = await _service.GetGroupByIdAsync(id);
            return g == null ? NotFound(new { message = $"Group {id} not found." }) : Ok(g);
        }

        [HasPermission("ACCESS_GROUP_CREATE")]
        [HttpPost("groups")]
        public async Task<IActionResult> CreateGroup([FromBody] CreateGroupDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.GroupName)) 
                return BadRequest(new { message = "GroupName is required." });
                
            var group = await _service.CreateGroupAsync(dto.GroupName, dto.Description);
            // Assuming your dynamic object or entity contains GroupId
            dynamic dynamicGroup = group; 
            return CreatedAtAction(nameof(GetGroup), new { id = dynamicGroup.GroupId }, group);
        }

        [HasPermission("ACCESS_GROUP_EDIT")]
        [HttpPut("groups/{id:int}")]
        public async Task<IActionResult> UpdateGroup(int id, [FromBody] CreateGroupDto dto)
        {
            var ok = await _service.UpdateGroupAsync(id, dto.GroupName, dto.Description);
            return ok ? Ok(new { message = "Group updated." }) : NotFound(new { message = $"Group {id} not found." });
        }

        [HasPermission("ACCESS_GROUP_EDIT")]
        [HttpDelete("groups/{id:int}")]
        public async Task<IActionResult> DeleteGroup(int id)
        {
            var ok = await _service.DeleteGroupAsync(id);
            return ok ? Ok(new { message = "Group deactivated." }) : NotFound(new { message = $"Group {id} not found." });
        }

        [HasPermission("ACCESS_GROUP_ASSIGN")]
        [HttpPut("groups/{id:int}/features")]
        public async Task<IActionResult> SetGroupFeatures(int id, [FromBody] SetFeaturesDto dto)
        {
            (bool ok, string msg) = await _service.SetGroupFeaturesAsync(id, dto.FeatureKeys);

            if (ok)
            {
                await _service.SyncGroupToDeptMatrixAsync(id, CurrentUserId);
                return Ok(new { message = "Features updated and matrix synced." });
            }

            return NotFound(new { message = msg });
        }

        /// <summary>
        /// Manually sync group permissions to DepartmentAccessMatrix.
        /// This copies all group features to the matrix for every staff member in the group.
        /// </summary>
        [HasPermission("ACCESS_GROUP_ASSIGN")]
        [HttpPost("groups/{id:int}/sync")]
        public async Task<IActionResult> SyncGroupToMatrix(int id)
        {
            var (success, message, staffSynced, permissionsSynced) = 
                await _service.SyncGroupToDeptMatrixAsync(id, CurrentUserId);

            if (!success)
                return NotFound(new { message });

            return Ok(new 
            { 
                success, 
                message, 
                staffSynced, 
                permissionsSynced 
            });
        }

        [HasPermission("ACCESS_GROUP_VIEW")]
        [HttpGet("staff/{staffId:guid}/groups")]
        public async Task<IActionResult> GetStaffGroups(Guid staffId) => 
            Ok(await _service.GetStaffGroupsAsync(staffId));

        [HasPermission("ACCESS_GROUP_ASSIGN")]
        [HttpPost("staff/{staffId:guid}/groups/{groupId:int}")]
        public async Task<IActionResult> AssignGroup(Guid staffId, int groupId, [FromBody] AssignGroupDto? dto)
        {
            (bool ok, string msg) = await _service.AssignGroupToStaffAsync(staffId, groupId, CurrentUserId, dto?.Note);
            if (!ok) return msg.Contains("not found") ? NotFound(new { message = msg }) : BadRequest(new { message = msg });
            return Ok(new { message = msg });
        }

        [HasPermission("ACCESS_GROUP_ASSIGN")]
        [HttpDelete("staff/{staffId:guid}/groups/{groupId:int}")]
        public async Task<IActionResult> RemoveGroup(Guid staffId, int groupId)
        {
            (bool ok, string msg) = await _service.RemoveGroupFromStaffAsync(staffId, groupId);
            return ok ? Ok(new { message = msg }) : NotFound(new { message = msg });
        }

        [HttpGet("staff/{staffId:guid}/permissions")]
        public async Task<IActionResult> GetStaffPermissions(Guid staffId) =>
            Ok(await _service.GetStaffPermissionsAsync(staffId));

        [HasPermission("ACCESS_GROUP_VIEW")]
        [HttpGet("department/{deptId:int}/persons")]
        public async Task<IActionResult> GetDepartmentPersons(int deptId) =>
            Ok(await _service.GetDepartmentPersonsAsync(deptId));

        [HasPermission("ACCESS_GROUP_VIEW")]
        [HttpGet("department/{deptId:int}/matrix")]
        public async Task<IActionResult> GetMatrix(int deptId) =>
            Ok(await _service.GetDepartmentMatrixAsync(deptId));

        [HasPermission("ACCESS_GROUP_ASSIGN")]
        [HttpPost("department/{deptId:int}/matrix")]
        public async Task<IActionResult> SaveMatrix(int deptId, [FromBody] SaveMatrixDto dto)
        {
            if (dto.Items == null || !dto.Items.Any())
                return BadRequest(new { message = "No items provided." });

            var items = dto.Items
                .Where(i => Guid.TryParse(i.StaffId, out _) && !string.IsNullOrWhiteSpace(i.FeatureKey))
                .Select(i => new MatrixUpdateItem { StaffId = Guid.Parse(i.StaffId), FeatureKey = i.FeatureKey, HasAccess = i.HasAccess })
                .ToList();

            if (!items.Any()) 
                return BadRequest(new { message = "No valid items found. Check staffId format (must be GUID)." });

            (int count, string msg) = await _service.SaveDepartmentMatrixAsync(deptId, items, CurrentUserId);
            return Ok(new { updated = count, message = msg });
        }

        [HasPermission("ACCESS_GROUP_ASSIGN")]
        [HttpPut("staff/{staffId:guid}/feature/{featureKey}")]
        public async Task<IActionResult> TogglePermission(Guid staffId, string featureKey, [FromBody] ToggleDto dto)
        {
            (bool ok, string msg) = await _service.TogglePermissionAsync(staffId, featureKey, dto.HasAccess, CurrentUserId);
            if (!ok) return msg.Contains("not found") ? NotFound(new { message = msg }) : BadRequest(new { message = msg });
            return Ok(new { message = msg });
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

    // ── Request DTOs ──────────────────────────────────────────────────────────
    public class CreateGroupDto  { public string GroupName { get; set; } = string.Empty; public string? Description { get; set; } }
    public class SetFeaturesDto  { public List<string> FeatureKeys { get; set; } = new(); }
    public class AssignGroupDto  { public string? Note { get; set; } }
    public class SaveMatrixDto   { public List<MatrixItemDto> Items { get; set; } = new(); }
    public class MatrixItemDto   { public string StaffId { get; set; } = string.Empty; public string FeatureKey { get; set; } = string.Empty; public bool HasAccess { get; set; } }
    public class ToggleDto       { public bool HasAccess { get; set; } }
}