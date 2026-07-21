using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Accounts.Controllers
{
    /// <summary>
    /// Positions/Vacancies API — accessible to Tenant Admins and Staff.
    /// Super Admin is blocked (no operational data access).
    /// Data is automatically scoped per tenant via EF Core Global Query Filters.
    /// </summary>
    [ApiController]
    [Route("api/positions")]
    [Authorize]
    [Produces("application/json")]
    public class VacanciesController : ControllerBase
    {
        private readonly IVacancyService              _service;

        public VacanciesController(IVacancyService service)
        {
            _service     = service;
        }

        private Task<bool> CallerIsSuperAdminAsync() => Task.FromResult(
            User.IsInRole("SuperAdmin") ||
            string.Equals(User.FindFirstValue(ITenantService.ClaimIsSuperAdmin), "true", StringComparison.OrdinalIgnoreCase));

        private Task<bool> CallerIsTenantAdminAsync() => Task.FromResult(
            User.IsInRole("CEO") ||
            string.Equals(User.FindFirstValue(ITenantService.ClaimIsTenantAdmin), "true", StringComparison.OrdinalIgnoreCase));

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            return Ok(await _service.GetAllAsync());
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            var v = await _service.GetByIdAsync(id);
            return v == null ? NotFound(new { message = $"Position {id} not found." }) : Ok(v);
        }

        [HttpGet("vacant")]
        public async Task<IActionResult> GetVacant()
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            return Ok(await _service.GetVacantAsync());
        }

        [HttpGet("filled")]
        public async Task<IActionResult> GetFilled()
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            return Ok(await _service.GetFilledAsync());
        }

        [HttpGet("by-node/{orgId:int}")]
        public async Task<IActionResult> GetByNode(int orgId)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            return Ok(await _service.GetByNodeAsync(orgId));
        }

        [HttpGet("report")]
        public async Task<IActionResult> GetReport()
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            return Ok(await _service.GetReportAsync());
        }

        [HttpGet("preview-code")]
        public async Task<IActionResult> PreviewCode([FromQuery] int organizationId, [FromQuery] string jobTitle)
        {
            if (organizationId <= 0 || string.IsNullOrWhiteSpace(jobTitle))
                return BadRequest(new { message = "organizationId and jobTitle are required." });
            var code = await _service.PreviewCodeAsync(organizationId, jobTitle);
            return code == null
                ? BadRequest(new { message = $"Organization node {organizationId} not found." })
                : Ok(new { vacancyCode = code });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateVacancyDto dto)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (await CallerIsTenantAdminAsync() && !dto.JobTitleId.HasValue)
                return BadRequest(new { message = "Tenant Admins must select an existing job title from the Job Titles catalog." });
            if (dto.VacancyCount <= 1)
            {
                var (vacancy, error) = await _service.CreateAsync(dto);
                if (error != null)
                    return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
                return CreatedAtAction(nameof(GetById), new { id = vacancy!.VacancyId }, vacancy);
            }
            var (created, errors) = await _service.CreateBulkAsync(dto);
            var list = created.ToList();
            return Ok(new { requested = dto.VacancyCount, created = list.Count, failed = errors.Count(), vacancies = list, errors = errors.Any() ? errors : null });
        }

        [HttpPost("bulk")]
        public async Task<IActionResult> CreateBulk([FromBody] CreateVacancyDto dto)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (dto.VacancyCount < 1) return BadRequest(new { message = "VacancyCount must be at least 1." });
            var (created, errors) = await _service.CreateBulkAsync(dto);
            var list = created.ToList();
            return Ok(new { requested = dto.VacancyCount, created = list.Count, failed = errors.Count(), vacancies = list, errors = errors.Any() ? errors : null });
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateVacancyDto dto)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (vacancy, error) = await _service.UpdateAsync(id, dto);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return Ok(vacancy);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            var (success, message) = await _service.DeleteAsync(id);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message });
        }
    }
}
