using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/report-to")]
    [Authorize]
    public sealed class ReportToController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ITenantService _tenant;

        public ReportToController(ApplicationDbContext db, UserManager<ApplicationUser> users, ITenantService tenant)
        { _db = db; _users = users; _tenant = tenant; }

        [HttpGet]
        public async Task<IActionResult> GetAll(CancellationToken ct)
        {
            if (!await IsAuthorizedAdminAsync()) return Forbid();
            var rows = await _db.Persons.AsNoTracking()
                .Where(p => p.Staff != null)
                .Select(p => new
                {
                    p.PersonId, p.FullName, p.ProfilePhotoUrl, p.IsActive,
                    StaffId = p.Staff!.StaffId,
                    EmployeeId = p.Staff.LoginId,
                    Department = p.Staff.Vacancy != null
                        ? (p.Staff.Vacancy.Organization != null && p.Staff.Vacancy.Organization.Label == "Department"
                            ? p.Staff.Vacancy.Organization.Name : p.Staff.Vacancy.Department) : null,
                    Designation = p.Staff.Vacancy != null
                        ? (p.Staff.Vacancy.JobTitleNav != null ? p.Staff.Vacancy.JobTitleNav.TitleName : p.Staff.Vacancy.JobTitle) : null,
                    p.ReportsToPersonId,
                    ReportsToName = p.ReportsToPerson != null ? p.ReportsToPerson.FullName : null,
                    ReportsToDepartment = p.ReportsToPerson != null && p.ReportsToPerson.Staff != null && p.ReportsToPerson.Staff.Vacancy != null
                        ? (p.ReportsToPerson.Staff.Vacancy.Organization != null && p.ReportsToPerson.Staff.Vacancy.Organization.Label == "Department"
                            ? p.ReportsToPerson.Staff.Vacancy.Organization.Name : p.ReportsToPerson.Staff.Vacancy.Department) : null,
                    ReportsToDesignation = p.ReportsToPerson != null && p.ReportsToPerson.Staff != null && p.ReportsToPerson.Staff.Vacancy != null
                        ? (p.ReportsToPerson.Staff.Vacancy.JobTitleNav != null ? p.ReportsToPerson.Staff.Vacancy.JobTitleNav.TitleName : p.ReportsToPerson.Staff.Vacancy.JobTitle) : null
                }).OrderBy(x => x.FullName).ToListAsync(ct);
            return Ok(rows);
        }

        [HttpPut("{personId:guid}")]
        public async Task<IActionResult> Update(Guid personId, [FromBody] UpdateReportToDto dto, CancellationToken ct)
        {
            if (!await IsAuthorizedAdminAsync()) return Forbid();
            if (dto.ReportsToPersonId == personId)
                return BadRequest(new { message = "A staff member cannot report to themselves." });

            var people = await _db.Persons.Where(p => p.Staff != null).ToListAsync(ct);
            var person = people.SingleOrDefault(p => p.PersonId == personId);
            if (person == null) return NotFound(new { message = "Staff member not found." });
            if (dto.ReportsToPersonId.HasValue && people.All(p => p.PersonId != dto.ReportsToPersonId.Value))
                return BadRequest(new { message = "The selected reporting manager is not available in this tenant." });

            if (dto.ReportsToPersonId.HasValue)
            {
                var byId = people.ToDictionary(p => p.PersonId);
                var visited = new HashSet<Guid> { personId };
                var current = dto.ReportsToPersonId;
                while (current.HasValue && byId.TryGetValue(current.Value, out var manager))
                {
                    if (!visited.Add(manager.PersonId))
                        return BadRequest(new { message = "This assignment would create a circular reporting relationship." });
                    current = manager.ReportsToPersonId;
                }
            }

            person.ReportsToPersonId = dto.ReportsToPersonId;
            await _db.SaveChangesAsync(ct);
            var managerName = dto.ReportsToPersonId.HasValue
                ? people.First(p => p.PersonId == dto.ReportsToPersonId.Value).FullName : "no reporting manager";
            return Ok(new { message = $"{person.FullName} now reports to {managerName}." });
        }

        private async Task<bool> IsAuthorizedAdminAsync()
        {
            if (!_tenant.TenantId.HasValue || _tenant.IsSuperAdmin) return false;
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = id == null ? null : await _users.FindByIdAsync(id);
            if (user?.IsTenantAdmin == true) return true;
            return user != null && (await _users.GetRolesAsync(user)).Any(r => r is "Admin" or "TenantAdmin");
        }
    }

    public sealed class UpdateReportToDto { public Guid? ReportsToPersonId { get; set; } }
}
