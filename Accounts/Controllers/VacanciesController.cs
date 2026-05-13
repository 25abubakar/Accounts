using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/positions")]
    [Produces("application/json")]
    public class VacanciesController : ControllerBase
    {
        private readonly IVacancyService _service;

        public VacanciesController(IVacancyService service) => _service = service;

        /// <summary>Get all positions with org info and assigned employee</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll() =>
            Ok(await _service.GetAllAsync());

        /// <summary>Get a single position by ID</summary>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var v = await _service.GetByIdAsync(id);
            return v == null ? NotFound(new { message = $"Position {id} not found." }) : Ok(v);
        }

        /// <summary>Get all vacant (unfilled) positions</summary>
        [HttpGet("vacant")]
        public async Task<IActionResult> GetVacant() =>
            Ok(await _service.GetVacantAsync());

        /// <summary>Get all filled positions with employee info</summary>
        [HttpGet("filled")]
        public async Task<IActionResult> GetFilled() =>
            Ok(await _service.GetFilledAsync());

        /// <summary>Get all positions attached to a specific organization node</summary>
        [HttpGet("by-node/{orgId:int}")]
        public async Task<IActionResult> GetByNode(int orgId) =>
            Ok(await _service.GetByNodeAsync(orgId));

        /// <summary>Full report: Organization → Position → Employee</summary>
        [HttpGet("report")]
        public async Task<IActionResult> GetReport() =>
            Ok(await _service.GetReportAsync());

        /// <summary>Preview the auto-generated position code before creating</summary>
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

        /// <summary>
        /// Create one or more positions in a single request.
        /// Set VacancyCount = 1 (default) for a single vacancy.
        /// Set VacancyCount = N to create N vacancies — the loop runs on the backend.
        /// Each vacancy gets a unique auto-incremented code.
        ///
        /// Example body (creates 5 Developer vacancies):
        /// {
        ///   "organizationId": 4,
        ///   "jobTitle": "Developer",
        ///   "department": "IT",
        ///   "vacancyCount": 5
        /// }
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateVacancyDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            // Single vacancy (default)
            if (dto.VacancyCount <= 1)
            {
                var (vacancy, error) = await _service.CreateAsync(dto);
                if (error != null)
                    return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
                return CreatedAtAction(nameof(GetById), new { id = vacancy!.VacancyId }, vacancy);
            }

            // Bulk — loop runs server-side
            var (created, errors) = await _service.CreateBulkAsync(dto);
            var createdList = created.ToList();

            return Ok(new
            {
                requested = dto.VacancyCount,
                created   = createdList.Count,
                failed    = errors.Count(),
                vacancies = createdList,
                errors    = errors.Any() ? errors : null
            });
        }

        /// <summary>
        /// Dedicated bulk endpoint — same as POST /api/positions with VacancyCount > 1.
        /// Useful when you always want the bulk response format.
        ///
        /// Example: create 10 Manager seats in one call.
        /// </summary>
        [HttpPost("bulk")]
        public async Task<IActionResult> CreateBulk([FromBody] CreateVacancyDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (dto.VacancyCount < 1)
                return BadRequest(new { message = "VacancyCount must be at least 1." });

            var (created, errors) = await _service.CreateBulkAsync(dto);
            var createdList = created.ToList();

            return Ok(new
            {
                requested = dto.VacancyCount,
                created   = createdList.Count,
                failed    = errors.Count(),
                vacancies = createdList,
                errors    = errors.Any() ? errors : null
            });
        }

        /// <summary>Update position details. VacancyCode is regenerated if job title or org changes.</summary>
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateVacancyDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (vacancy, error) = await _service.UpdateAsync(id, dto);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return Ok(vacancy);
        }

        /// <summary>Delete a position — blocked if an employee is assigned</summary>
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var (success, message) = await _service.DeleteAsync(id);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message });
        }
    }
}
