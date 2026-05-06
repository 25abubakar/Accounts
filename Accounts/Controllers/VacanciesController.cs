using Accounts.Data;
using Accounts.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class VacanciesController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        public VacanciesController(ApplicationDbContext db) => _db = db;

        // ─────────────────────────────────────────────────────────────
        // GET /api/vacancies
        // All vacancies with org info and employee
        // ─────────────────────────────────────────────────────────────
        /// <summary>Get all vacancies with organization info and assigned employee</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var list = await _db.Vacancies
                .Include(v => v.Organization).ThenInclude(o => o!.Parent).ThenInclude(p => p!.Parent)
                .Include(v => v.Staff)
                .OrderBy(v => v.VacancyId)
                .ToListAsync();

            return Ok(list.Select(MapToDto));
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/vacancies/{id}
        // ─────────────────────────────────────────────────────────────
        /// <summary>Get a single vacancy by ID</summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var v = await GetVacancyWithIncludes(id);
            if (v == null) return NotFound(new { message = $"Vacancy {id} not found." });
            return Ok(MapToDto(v));
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/vacancies/vacant
        // Only empty seats
        // ─────────────────────────────────────────────────────────────
        /// <summary>Get all vacant (unfilled) positions</summary>
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

        // ─────────────────────────────────────────────────────────────
        // GET /api/vacancies/filled
        // Only filled seats
        // ─────────────────────────────────────────────────────────────
        /// <summary>Get all filled positions with employee info</summary>
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

        // ─────────────────────────────────────────────────────────────
        // GET /api/vacancies/by-branch/{orgId}
        // All vacancies under a specific branch node
        // ─────────────────────────────────────────────────────────────
        /// <summary>Get all vacancies for a specific branch (OrganizationTree node)</summary>
        [HttpGet("by-branch/{orgId:int}")]
        public async Task<IActionResult> GetByBranch(int orgId)
        {
            var list = await _db.Vacancies
                .Include(v => v.Organization).ThenInclude(o => o!.Parent).ThenInclude(p => p!.Parent)
                .Include(v => v.Staff)
                .Where(v => v.OrganizationId == orgId)
                .ToListAsync();

            return Ok(list.Select(MapToDto));
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/vacancies/report
        // Full org → vacancy → employee report
        // ─────────────────────────────────────────────────────────────
        /// <summary>Full report: Country → Company → Branch → Vacancy → Employee</summary>
        [HttpGet("report")]
        public async Task<IActionResult> GetReport()
        {
            var list = await _db.Vacancies
                .Include(v => v.Organization).ThenInclude(o => o!.Parent).ThenInclude(p => p!.Parent)
                .Include(v => v.Staff)
                .OrderBy(v => v.Organization!.Parent!.Parent!.Name)
                .ThenBy(v => v.Organization!.Parent!.Name)
                .ThenBy(v => v.Organization!.Name)
                .ToListAsync();

            var report = list.Select(v =>
            {
                var branch  = v.Organization;
                var company = branch?.Parent;
                var country = company?.Parent;
                return new OrgVacancyReportDto
                {
                    Country      = country?.Name ?? "-",
                    Company      = company?.Name ?? "-",
                    Branch       = branch?.Name  ?? "-",
                    VacancyCode  = v.VacancyCode,
                    JobTitle     = v.JobTitle,
                    Department   = v.Department,
                    IsFilled     = v.IsFilled,
                    EmployeeName = v.Staff?.FullName,
                    EmployeeEmail = v.Staff?.Email,
                    JoiningDate  = v.Staff?.JoiningDate
                };
            });

            return Ok(report);
        }

        // ─────────────────────────────────────────────────────────────
        // POST /api/vacancies
        // Create a new vacancy (empty seat)
        // ─────────────────────────────────────────────────────────────
        /// <summary>Create a new vacancy (empty seat)</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateVacancyDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var orgNode = await _db.OrganizationTree.FindAsync(dto.OrganizationId);
            if (orgNode == null)
                return BadRequest(new { message = $"OrganizationTree node {dto.OrganizationId} not found." });

            if (orgNode.Label != "Branch")
                return BadRequest(new { message = "Vacancy must be linked to a Branch node (Label = 'Branch')." });

            if (await _db.Vacancies.AnyAsync(v => v.VacancyCode == dto.VacancyCode))
                return Conflict(new { message = $"VacancyCode '{dto.VacancyCode}' already exists." });

            var vacancy = new Vacancy
            {
                OrganizationId = dto.OrganizationId,
                VacancyCode    = dto.VacancyCode,
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

        // ─────────────────────────────────────────────────────────────
        // PUT /api/vacancies/{id}
        // Update vacancy details
        // ─────────────────────────────────────────────────────────────
        /// <summary>Update vacancy details (code, title, department, branch)</summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateVacancyDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var vacancy = await _db.Vacancies.FindAsync(id);
            if (vacancy == null) return NotFound(new { message = $"Vacancy {id} not found." });

            var orgNode = await _db.OrganizationTree.FindAsync(dto.OrganizationId);
            if (orgNode == null)
                return BadRequest(new { message = $"OrganizationTree node {dto.OrganizationId} not found." });

            if (orgNode.Label != "Branch")
                return BadRequest(new { message = "Vacancy must be linked to a Branch node." });

            vacancy.VacancyCode    = dto.VacancyCode;
            vacancy.JobTitle       = dto.JobTitle;
            vacancy.Department     = dto.Department;
            vacancy.OrganizationId = dto.OrganizationId;

            await _db.SaveChangesAsync();

            var updated = await GetVacancyWithIncludes(id);
            return Ok(MapToDto(updated!));
        }

        // ─────────────────────────────────────────────────────────────
        // DELETE /api/vacancies/{id}
        // Delete vacancy (only if not filled)
        // ─────────────────────────────────────────────────────────────
        /// <summary>Delete a vacancy — blocked if an employee is assigned</summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var vacancy = await _db.Vacancies.FindAsync(id);
            if (vacancy == null) return NotFound(new { message = $"Vacancy {id} not found." });

            if (vacancy.IsFilled)
                return BadRequest(new { message = "Cannot delete a filled vacancy. Remove the employee first." });

            _db.Vacancies.Remove(vacancy);
            await _db.SaveChangesAsync();
            return Ok(new { message = $"Vacancy '{vacancy.VacancyCode}' deleted." });
        }

        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────

        private async Task<Vacancy?> GetVacancyWithIncludes(int id) =>
            await _db.Vacancies
                .Include(v => v.Organization).ThenInclude(o => o!.Parent).ThenInclude(p => p!.Parent)
                .Include(v => v.Staff)
                .FirstOrDefaultAsync(v => v.VacancyId == id);

        private static VacancyDto MapToDto(Vacancy v)
        {
            var branch  = v.Organization;
            var company = branch?.Parent;
            var country = company?.Parent;

            return new VacancyDto
            {
                VacancyId      = v.VacancyId,
                OrganizationId = v.OrganizationId,
                BranchName     = branch?.Name  ?? "-",
                CompanyName    = company?.Name ?? "-",
                CountryName    = country?.Name ?? "-",
                VacancyCode    = v.VacancyCode,
                JobTitle       = v.JobTitle,
                Department     = v.Department,
                IsFilled       = v.IsFilled,
                CreatedDate    = v.CreatedDate,
                Employee       = v.Staff == null ? null : new StaffDto
                {
                    StaffId     = v.Staff.StaffId,
                    FullName    = v.Staff.FullName,
                    Email       = v.Staff.Email,
                    Phone       = v.Staff.Phone,
                    VacancyId   = v.Staff.VacancyId,
                    VacancyCode = v.VacancyCode,
                    JobTitle    = v.JobTitle,
                    BranchName  = branch?.Name,
                    CompanyName = company?.Name,
                    CountryName = country?.Name,
                    JoiningDate = v.Staff.JoiningDate
                }
            };
        }
    }
}
