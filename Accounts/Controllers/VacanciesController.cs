using Accounts.Data;
using Accounts.Models;
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
        public VacanciesController(ApplicationDbContext db) => _db = db;

        // ─────────────────────────────────────────────────────────────
        // GET /api/positions
        // ─────────────────────────────────────────────────────────────
        /// <summary>Get all positions with organization info and assigned employee</summary>
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
        // GET /api/positions/{id}
        // ─────────────────────────────────────────────────────────────
        /// <summary>Get a single position by ID</summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var v = await GetVacancyWithIncludes(id);
            if (v == null) return NotFound(new { message = $"Position {id} not found." });
            return Ok(MapToDto(v));
        }

        // ─────────────────────────────────────────────────────────────
        // GET /api/positions/vacant
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
        // GET /api/positions/filled
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
        // GET /api/positions/by-node/{orgId}
        // All positions under any org node (not just branches)
        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// Get all positions attached to a specific organization node.
        /// Works for any node type — Company, Group, Branch, Department, etc.
        /// </summary>
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

        // ─────────────────────────────────────────────────────────────
        // GET /api/positions/report
        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// Full report: Organization path → Position → Employee.
        /// Works for any hierarchy depth — not limited to 3 levels.
        /// </summary>
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

        // ─────────────────────────────────────────────────────────────
        // POST /api/positions
        // Create a position under ANY org node
        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// Create a new position (empty seat) under any organization node.
        /// No longer restricted to Branch nodes — can be under Company, Group, Department, etc.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateVacancyDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            // Validate the org node exists — any label is accepted
            var orgNode = await _db.OrganizationTree.FindAsync(dto.OrganizationId);
            if (orgNode == null)
                return BadRequest(new { message = $"Organization node {dto.OrganizationId} not found." });

            if (await _db.Vacancies.AnyAsync(v => v.VacancyCode == dto.VacancyCode))
                return Conflict(new { message = $"Position code '{dto.VacancyCode}' already exists." });

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
        // PUT /api/positions/{id}
        // ─────────────────────────────────────────────────────────────
        /// <summary>
        /// Update position details. Can be moved to any org node — not restricted to Branch.
        /// </summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateVacancyDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var vacancy = await _db.Vacancies.FindAsync(id);
            if (vacancy == null) return NotFound(new { message = $"Position {id} not found." });

            // Validate the org node exists — any label is accepted
            var orgNode = await _db.OrganizationTree.FindAsync(dto.OrganizationId);
            if (orgNode == null)
                return BadRequest(new { message = $"Organization node {dto.OrganizationId} not found." });

            vacancy.VacancyCode    = dto.VacancyCode;
            vacancy.JobTitle       = dto.JobTitle;
            vacancy.Department     = dto.Department;
            vacancy.OrganizationId = dto.OrganizationId;

            await _db.SaveChangesAsync();

            var updated = await GetVacancyWithIncludes(id);
            return Ok(MapToDto(updated!));
        }

        // ─────────────────────────────────────────────────────────────
        // DELETE /api/positions/{id}
        // ─────────────────────────────────────────────────────────────
        /// <summary>Delete a position — blocked if an employee is assigned</summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var vacancy = await _db.Vacancies.FindAsync(id);
            if (vacancy == null) return NotFound(new { message = $"Position {id} not found." });

            if (vacancy.IsFilled)
                return BadRequest(new { message = "Cannot delete a filled position. Remove the employee first." });

            _db.Vacancies.Remove(vacancy);
            await _db.SaveChangesAsync();
            return Ok(new { message = $"Position '{vacancy.VacancyCode}' deleted." });
        }

        // ─────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────

        private async Task<Vacancy?> GetVacancyWithIncludes(int id) =>
            await _db.Vacancies
                .Include(v => v.Organization).ThenInclude(o => o!.Parent).ThenInclude(p => p!.Parent)
                .Include(v => v.Staff)
                .FirstOrDefaultAsync(v => v.VacancyId == id);

        /// <summary>
        /// Dynamically resolves the org path regardless of hierarchy depth.
        /// Returns (directNodeName, parentName, grandParentName).
        /// Works whether vacancy is under a Branch, Company, Group, or Country.
        /// </summary>
        private static (string node, string parent, string grandParent) ResolveOrgPath(
            OrganizationTree? org)
        {
            var node        = org?.Name        ?? "-";
            var parent      = org?.Parent?.Name ?? "-";
            var grandParent = org?.Parent?.Parent?.Name ?? "-";
            return (node, parent, grandParent);
        }

        private static VacancyDto MapToDto(Vacancy v)
        {
            // Dynamically resolve org path — works at any hierarchy level
            // node  = the org node the vacancy is directly attached to
            // p1    = its parent (one level up)
            // p2    = grandparent (two levels up)
            var node = v.Organization;
            var p1   = node?.Parent;
            var p2   = p1?.Parent;

            return new VacancyDto
            {
                VacancyId      = v.VacancyId,
                OrganizationId = v.OrganizationId,

                // Dynamic labels — show what's actually there, not assumed names
                BranchName  = node?.Name  ?? "-",
                CompanyName = p1?.Name    ?? "-",
                CountryName = p2?.Name    ?? "-",
                NodeLabel   = node?.Label ?? "-",

                VacancyCode = v.VacancyCode,
                JobTitle    = v.JobTitle,
                Department  = v.Department,
                IsFilled    = v.IsFilled,
                CreatedDate = v.CreatedDate,

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
