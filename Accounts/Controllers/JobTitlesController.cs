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
    [Route("api/job-titles")]
    [Authorize]
    [Produces("application/json")]
    public class JobTitlesController : ControllerBase
    {
        private readonly JobTitleService _service;
        private readonly ITenantService _tenantService;
        private readonly ApplicationDbContext _db;
        private readonly RbacService _rbac;

        public JobTitlesController(JobTitleService service, ITenantService tenantService, ApplicationDbContext db, RbacService rbac)
        {
            _service = service;
            _tenantService = tenantService;
            _db = db;
            _rbac = rbac;
        }

        private async Task<bool> HasJobTitleActionAsync(string action)
        {
            if (_tenantService.IsTenantAdmin || User.IsInRole("Admin") || User.IsInRole("TenantAdmin"))
                return true;
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
        public async Task<IActionResult> GetAll()
        {
            // Keep the menu/page visible to Super Admin without exposing tenant data.
            if (_tenantService.IsSuperAdmin) return Ok(Array.Empty<JobTitleResponseDto>());
            if (!_tenantService.TenantId.HasValue) return Forbid();
            if (!await HasJobTitleActionAsync("VIEW")) return Forbid();
            return Ok(await _service.GetAllWithCountAsync());
        }

        [HttpPost("upsert")]
        public async Task<IActionResult> Upsert([FromBody] UpsertJobTitleDto dto)
        {
            if (!await HasJobTitleActionAsync("ADD")) return Forbid();
            if (string.IsNullOrWhiteSpace(dto.TitleName))
                return BadRequest(new { message = "TitleName is required." });

            var id = await _service.UpsertByNameAsync(dto.TitleName.Trim());
            var title = await _service.GetByIdAsync(id);
            return Ok(new { id, titleName = title?.TitleName });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpsertJobTitleDto dto)
        {
            if (!await HasJobTitleActionAsync("EDIT")) return Forbid();
            if (string.IsNullOrWhiteSpace(dto.TitleName))
                return BadRequest(new { message = "TitleName is required." });

            var success = await _service.UpdateAsync(id, dto.TitleName.Trim());
            return success
                ? Ok(new { id, titleName = dto.TitleName.Trim() })
                : NotFound(new { message = "Job Title not found." });
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            if (!await HasJobTitleActionAsync("DELETE")) return Forbid();
            try
            {
                var success = await _service.DeleteAsync(id);
                return success
                    ? Ok(new { message = "Job Title deleted successfully." })
                    : NotFound(new { message = "Job Title not found." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("{id:int}/attendance-scope")]
        public async Task<IActionResult> UpdateAttendanceScope(int id, [FromBody] UpdateAttendanceScopeDto dto)
        {
            if (!await HasJobTitleActionAsync("EDIT")) return Forbid();
            if (!Enum.IsDefined(dto.Scope)) return BadRequest(new { message = "Invalid attendance visibility scope." });
            var success = await _service.UpdateAttendanceScopeAsync(id, dto.Scope);
            return success ? Ok(new { id, attendanceVisibilityScope = dto.Scope }) : NotFound(new { message = "Job Title not found." });
        }
    }

    public class UpsertJobTitleDto
    {
        public string TitleName { get; set; } = string.Empty;
    }

    public sealed class UpdateAttendanceScopeDto
    {
        public Accounts.Models.AttendanceVisibilityScope Scope { get; set; }
    }
}
