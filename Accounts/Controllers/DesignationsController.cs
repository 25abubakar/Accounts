using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Accounts.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/designations")]
    [Route("api/job-titles")]
    [Authorize]
    [Produces("application/json")]
    public class DesignationsController : ControllerBase
    {
        private readonly DesignationService _service;
        private readonly PlatformSettingsProvisioningService _provisioning;
        private readonly ITenantService _tenantService;
        private readonly ApplicationDbContext _db;
        private readonly RbacService _rbac;
        private readonly TenantPermissionService _tenantPermissions;

        public DesignationsController(
            DesignationService service,
            PlatformSettingsProvisioningService provisioning,
            ITenantService tenantService,
            ApplicationDbContext db,
            RbacService rbac,
            TenantPermissionService tenantPermissions)
        {
            _service = service;
            _provisioning = provisioning;
            _tenantService = tenantService;
            _db = db;
            _rbac = rbac;
            _tenantPermissions = tenantPermissions;
        }

        private async Task<bool> HasDesignationActionAsync(string action)
        {
            if (TenantPermissionService.IsSuperAdmin(User)) return true;
            if (TenantPermissionService.IsTenantAdmin(User))
                return await _tenantPermissions.HasMenuRouteAsync(User, ["/settings/types", "/settings/job-titles"], action);
            if (!_tenantService.TenantId.HasValue) return false;

            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(identityUserId)) return false;

            var staffId = await _db.Persons.AsNoTracking()
                .Where(person => person.IdentityUserId == identityUserId && person.Staff != null)
                .Select(person => (Guid?)person.Staff!.StaffId)
                .FirstOrDefaultAsync();
            if (!staffId.HasValue) return false;

            var menuIds = await _db.Menus.AsNoTracking()
                .Where(menu => menu.IsActive &&
                    (menu.Route == "/settings/types" || menu.Route == "/settings/job-titles"))
                .Select(menu => menu.Id)
                .ToListAsync();
            if (menuIds.Count == 0) return false;

            var normalizedAction = action.Trim().ToUpperInvariant();
            if (normalizedAction == "VIEW" && await _db.StaffMenuAccesses.AsNoTracking()
                    .AnyAsync(access => access.StaffId == staffId.Value && menuIds.Contains(access.MenuId) && access.IsAllow))
                return true;

            foreach (var menuId in menuIds)
            {
                if (normalizedAction == "VIEW" && await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId}"))
                    return true;
                if (await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId}_{normalizedAction}"))
                    return true;
            }
            return false;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(CancellationToken ct)
        {
            if (_tenantService.IsSuperAdmin) return Ok(Array.Empty<DesignationResponseDto>());
            if (!_tenantService.TenantId.HasValue) return Forbid();
            if (!await HasDesignationActionAsync("VIEW")) return Forbid();

            await _provisioning.EnsureTenantPlatformSettingsAsync(_tenantService.TenantId.Value, ct: ct);
            return Ok(await _service.GetAllWithCountAsync());
        }

        [HttpPost("upsert")]
        public async Task<IActionResult> Upsert([FromBody] UpsertDesignationDto dto)
        {
            if (!await HasDesignationActionAsync("ADD")) return Forbid();
            var name = dto.Name ?? dto.TitleName;
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { message = "Name is required." });

            var id = await _service.UpsertByNameAsync(name.Trim());
            var designation = await _service.GetByIdAsync(id);
            return Ok(new { id, name = designation?.Name, titleName = designation?.Name });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpsertDesignationDto dto)
        {
            if (!await HasDesignationActionAsync("EDIT")) return Forbid();
            var name = dto.Name ?? dto.TitleName;
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { message = "Name is required." });

            var success = await _service.UpdateAsync(id, name.Trim());
            return success
                ? Ok(new { id, name = name.Trim(), titleName = name.Trim() })
                : NotFound(new { message = "Designation not found." });
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            if (!await HasDesignationActionAsync("DELETE")) return Forbid();
            try
            {
                var success = await _service.DeleteAsync(id);
                return success
                    ? Ok(new { message = "Designation deleted successfully." })
                    : NotFound(new { message = "Designation not found." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("{id:int}/attendance-scope")]
        public async Task<IActionResult> UpdateAttendanceScope(int id, [FromBody] UpdateAttendanceScopeDto dto)
        {
            if (!await HasDesignationActionAsync("EDIT")) return Forbid();
            if (!Enum.IsDefined(dto.Scope)) return BadRequest(new { message = "Invalid attendance visibility scope." });
            var success = await _service.UpdateAttendanceScopeAsync(id, dto.Scope);
            return success ? Ok(new { id, attendanceVisibilityScope = dto.Scope }) : NotFound(new { message = "Designation not found." });
        }
    }

    public class UpsertDesignationDto
    {
        public string? Name { get; set; }
        public string? TitleName { get; set; }
    }

    public sealed class UpdateAttendanceScopeDto
    {
        public Accounts.Models.AttendanceVisibilityScope Scope { get; set; }
    }
}
