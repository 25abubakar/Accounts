using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers
{
    /// <summary>
    /// Manages the normalized JobTitles lookup table.
    ///
    /// Creatable-select contract (frontend):
    ///   - If the user selects an existing title, send JobTitleId.
    ///   - If the user types a new title, send JobTitleName.
    ///   - The backend upserts and returns the stable Id.
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
        /// Returns all job titles for dropdown population.
        /// GET /api/job-titles
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll() =>
            Ok(await _service.GetAllAsync());

        /// <summary>
        /// Upsert by name — finds existing (case-insensitive) or inserts new.
        /// Returns { id, titleName }.
        ///
        /// Used by frontend creatable-select when user types a brand-new title.
        /// POST /api/job-titles/upsert
        /// </summary>
        [HttpPost("upsert")]
        [Authorize(Roles = "SuperAdmin,Admin")]
        public async Task<IActionResult> Upsert([FromBody] UpsertJobTitleDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TitleName))
                return BadRequest(new { message = "TitleName is required." });

            var id = await _service.UpsertByNameAsync(dto.TitleName.Trim());
            var title = await _service.GetByIdAsync(id);
            return Ok(new { id, titleName = title?.TitleName });
        }
    }

    public class UpsertJobTitleDto
    {
        public string TitleName { get; set; } = string.Empty;
    }
}
