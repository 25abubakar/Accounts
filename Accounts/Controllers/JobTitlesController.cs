using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

        public JobTitlesController(JobTitleService service, ITenantService tenantService)
        {
            _service = service;
            _tenantService = tenantService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            // Keep the menu/page visible to Super Admin without exposing tenant data.
            if (_tenantService.IsSuperAdmin) return Ok(Array.Empty<JobTitleResponseDto>());
            if (!_tenantService.TenantId.HasValue) return Forbid();
            return Ok(await _service.GetAllWithCountAsync());
        }

        [HttpPost("upsert")]
        public async Task<IActionResult> Upsert([FromBody] UpsertJobTitleDto dto)
        {
            if (!_tenantService.IsTenantAdmin) return Forbid();
            if (string.IsNullOrWhiteSpace(dto.TitleName))
                return BadRequest(new { message = "TitleName is required." });

            var id = await _service.UpsertByNameAsync(dto.TitleName.Trim());
            var title = await _service.GetByIdAsync(id);
            return Ok(new { id, titleName = title?.TitleName });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpsertJobTitleDto dto)
        {
            if (!_tenantService.IsTenantAdmin) return Forbid();
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
            if (!_tenantService.IsTenantAdmin) return Forbid();
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
            if (!_tenantService.IsTenantAdmin) return Forbid();
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
