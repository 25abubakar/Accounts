using Accounts.Data;
using Accounts.Models;
using Accounts.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/positions")]
    [Produces("application/json")]
    public class VacanciesController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly VacancyCodeService   _codeService;

        public VacanciesController(ApplicationDbContext db, VacancyCodeService codeService)
        {
            _db          = db;
            _codeService = codeService;
        }

        // GET /api/positions
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var list = await _db.Vacancies
                .Include(v => v.Organization).ThenInclude(o => o!.Parent).ThenInclude(p => p!.Parent)
                .Include(v => v.Staff)
                .OrderBy(v => v.VacancyCode)
                .ToListAsync();

            return Ok(list.Select(MapToDto));
        }

        // GET /api/positions/{id}
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var v = await GetVacancyWithIncludes(id);
            if (v == null) return NotFound(new { message = $"Position {id} not found." });
            return Ok(MapToDto(v));
        }

        // GET /api/positions/vacant
        [HttpGet("vacant")]
        public async Task<IActionResult> GetVacant()
        {
            var list = await _db.Vacancies
                .Include(v => v.Organization).ThenInclude(o => o!.Parent).ThenInclude(p => p!.Parent)
                .Include(v => v.Staff)
                .Where(v => !v.IsFilled)
                .ToListAsync();

            return Ok(list.Select(MapToDto));
        }

        // GET /api/positions/filled
        [HttpGet("filled")]
        public async Task<IActionResult> GetFilled()
        {
            var list = await _db.Vacancies
                .Include(v => v.Organization).ThenInclude(o => o!.Parent).ThenInclude(p => p!.Parent)
                .Include(v => v.Staff)
                .Where(v => v.IsFilled)
                .ToListAsync();

            return Ok(list.Select(MapToDto));
        }

        // GET /api/positions/by-node/{orgId}
        [HttpGet("by-node/{orgId:int}")]
        public async Task<IActionResult> GetByNode(int orgId)
        {
            var list = await _db.Vacancies
                .Include(v => v.Organization).ThenInclude(o => o!.Parent).ThenInclude(p => p!.Parent)
                .Include(v => v.Staff)
                .Where(v => v.OrganizationId == orgId)
                .ToListAsync();

            return Ok(list.Select(MapToDto));
        }

        // GET /api/positions/report
        [HttpGet("report")]
        public async Task<IActionResult> GetReport()
        {
            var list = await _db.Vacancies
                .Include(v => v.Organization).ThenInclude(o => o!.Parent).ThenInclude(p => p!.Parent)
                .Include(v => v.Staff)
                .OrderBy(v => v.Organization!.Name)
                .ToListAsync();

            var report = list.Select(v =>
            {
                var (nodeName, parentName, grandParentName) = ResolveOrgPath(v.Organization);
                return new OrgVacancyReportDto
                {
                    Country       = grandParentName,
                    Company       = parentName,
                    Branch        = nodeName,
                    VacancyCode   = v.VacancyCode,
                    JobTitle      = v.JobTitle,
                    Department    = v.Department,
                    IsFilled      = v.IsFilled,
                    EmployeeName  = v.Staff?.FullName,
                    EmployeeEmail = v.Staff?.Email,
                    JoiningDate   = v.Staff?.JoiningDate
                };
            });

            return Ok(report);
        }

        // GET /api/positions/preview-code?organizationId=1&jobTitle=Manager
        /// <summary>
        /// Preview what vacancy code will be generated before creating.
        /// Useful for frontend to show the user the code before submitting.
        /// </summary>
        [HttpGet("preview-code")]
        public async Task<IActionResult> PreviewCode([FromQuery] int organizationId, [FromQuery] string jobTitle)
        {
            if (organizationId <= 0 || string.IsNullOrWhiteSpace(jobTitle))
                return BadRequest(new { message = "organizationId and jobTitle are required." });

            var orgNode = await _db.OrganizationTree.FindAsync(organizationId);
            if (orgNode == null)
                return BadRequest(new { message = $"Organization node {organizationId} not found." });

            var code = await _codeService.GenerateAsync(organizationId, jobTitle);
            return Ok(new { vacancyCode = code });
        }

        // POST /api/positions
        /// <summary>
        /// Create a new position. VacancyCode is AUTO-GENERATED — do NOT send it.
        /// Format: {CompanyCode}-{CityCode}-{JobCode}-{NN}  e.g. LT-KHI-MGR-01
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateVacancyDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var orgNode = await _db.OrganizationTree.FindAsync(dto.OrganizationId);
            if (orgNode == null)
                return BadRequest(new { message = $"Organization node {dto.OrganizationId} not found." });

            // Auto-generate vacancy code
            var vacancyCode = await _codeService.GenerateAsync(dto.OrganizationId, dto.JobTitle);

            var vacancy = new Vacancy
            {
                VacancyId      = Guid.NewGuid(),
                OrganizationId = dto.OrganizationId,
                VacancyCode    = vacancyCode,
                JobTitle       = dto.JobTitle,
                Department     = dto.Department,
                IsFilled       = false,
                CreatedDate    = DateTime.UtcNow
            };

            _db.Vacancies.Add(vacancy);
            await _db.SaveChangesAsync();

            var created = await GetVacancyWithIncludes(vacancy.VacancyId);
            return CreatedAtAction(nameof(GetById), new { id = vacancy.VacancyId }, MapToDto(created!));
        }

        // PUT /api/positions/{id}
        /// <summary>
        /// Update position. If JobTitle or OrganizationId changes, VacancyCode is regenerated.
        /// </summary>
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateVacancyDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var vacancy = await _db.Vacancies.FindAsync(id);
            if (vacancy == null) return NotFound(new { message = $"Position {id} not found." });

            var orgNode = await _db.OrganizationTree.FindAsync(dto.OrganizationId);
            if (orgNode == null)
                return BadRequest(new { message = $"Organization node {dto.OrganizationId} not found." });

            // Regenerate code if job title or org changed
            bool needsNewCode = vacancy.JobTitle != dto.JobTitle
                             || vacancy.OrganizationId != dto.OrganizationId;

            vacancy.JobTitle       = dto.JobTitle;
            vacancy.Department     = dto.Department;
            vacancy.OrganizationId = dto.OrganizationId;

            if (needsNewCode)
                vacancy.VacancyCode = await _codeService.GenerateAsync(dto.OrganizationId, dto.JobTitle);

            await _db.SaveChangesAsync();

            var updated = await GetVacancyWithIncludes(id);
            return Ok(MapToDto(updated!));
        }

        // DELETE /api/positions/{id}
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var vacancy = await _db.Vacancies.FindAsync(id);
            if (vacancy == null) return NotFound(new { message = $"Position {id} not found." });

            if (vacancy.IsFilled)
                return BadRequest(new { message = "Cannot delete a filled position. Remove the employee first." });

            _db.Vacancies.Remove(vacancy);
            await _db.SaveChangesAsync();
            return Ok(new { message = $"Position '{vacancy.VacancyCode}' deleted." });
        }

        // ── HELPERS ───────────────────────────────────────────────────────────

        private async Task<Vacancy?> GetVacancyWithIncludes(Guid id) =>
            await _db.Vacancies
                .Include(v => v.Organization).ThenInclude(o => o!.Parent).ThenInclude(p => p!.Parent)
                .Include(v => v.Staff)
                .FirstOrDefaultAsync(v => v.VacancyId == id);

        private static (string node, string parent, string grandParent) ResolveOrgPath(
            OrganizationTree? org)
        {
            var node        = org?.Name         ?? "-";
            var parent      = org?.Parent?.Name  ?? "-";
            var grandParent = org?.Parent?.Parent?.Name ?? "-";
            return (node, parent, grandParent);
        }

        private static VacancyDto MapToDto(Vacancy v)
        {
            var node = v.Organization;
            var p1   = node?.Parent;
            var p2   = p1?.Parent;

            return new VacancyDto
            {
                VacancyId      = v.VacancyId,
                OrganizationId = v.OrganizationId,
                BranchName     = node?.Name  ?? "-",
                CompanyName    = p1?.Name    ?? "-",
                CountryName    = p2?.Name    ?? "-",
                NodeLabel      = node?.Label ?? "-",
                VacancyCode    = v.VacancyCode,
                JobTitle       = v.JobTitle,
                Department     = v.Department,
                IsFilled       = v.IsFilled,
                CreatedDate    = v.CreatedDate,

                Employee = v.Staff == null ? null : new StaffDto
                {
                    StaffId     = v.Staff.StaffId,
                    FullName    = v.Staff.FullName,
                    Email       = v.Staff.Email,
                    Phone       = v.Staff.Phone,
                    PhotoUrl    = v.Staff.PhotoUrl,
                    VacancyId   = v.Staff.VacancyId,
                    VacancyCode = v.VacancyCode,
                    JobTitle    = v.JobTitle,
                    BranchName  = node?.Name,
                    CompanyName = p1?.Name,
                    CountryName = p2?.Name,
                    JoiningDate = v.Staff.JoiningDate
                }
            };
        }
    }
}
