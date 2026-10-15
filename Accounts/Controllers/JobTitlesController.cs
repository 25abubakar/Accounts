using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers
{
    /// <summary>
    /// Manages the normalized JobTitles lookup table.
    /// </summary>
    [ApiController]
    [Route("api/job-titles")]
    [Authorize]
    [Produces("application/json")]
    public class JobTitlesController : ControllerBase
    {
        private readonly JobTitleService _service;
        public JobTitlesController(JobTitleService service) => _service = service;

        /// <summary>
        /// Returns all job titles with their active vacancy count for UI.
        /// GET /api/job-titles
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll() =>
            Ok(await _service.GetAllWithCountAsync()); // 🌟 Ab Count bhi aayega!

        /// <summary>
        /// Upsert by name — finds existing (case-insensitive) or inserts new.
        /// POST /api/job-titles/upsert
        /// </summary>
        [HttpPost("upsert")]
        [Authorize(Roles = "SuperAdmin,Admin,TenantAdmin")]
        public async Task<IActionResult> Upsert([FromBody] UpsertJobTitleDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TitleName))
                return BadRequest(new { message = "TitleName is required." });

            var id = await _service.UpsertByNameAsync(dto.TitleName.Trim());
            var title = await _service.GetByIdAsync(id);
            return Ok(new { id, titleName = title?.TitleName });
        }

        /// <summary>
        /// Updates an existing job title.
        /// PUT /api/job-titles/{id}
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "SuperAdmin,Admin,TenantAdmin")]
        public async Task<IActionResult> Update(int id, [FromBody] UpsertJobTitleDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TitleName))
                return BadRequest(new { message = "TitleName is required." });

            var success = await _service.UpdateAsync(id, dto.TitleName.Trim());

            if (!success)
                return NotFound(new { message = "Job Title not found." });

            return Ok(new { id, titleName = dto.TitleName.Trim() });
        }

        /// <summary>
        /// Deletes a job title ONLY if it's not in use.
        /// DELETE /api/job-titles/{id}
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "SuperAdmin,Admin,TenantAdmin")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var success = await _service.DeleteAsync(id);

                if (!success)
                    return NotFound(new { message = "Job Title not found." });

                return Ok(new { message = "Job Title deleted successfully." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message }); // 🌟 Agar Delete nai ho sakta to error dega
            }
        }
    }

    public class UpsertJobTitleDto
    {
        public string TitleName { get; set; } = string.Empty;
    }
}