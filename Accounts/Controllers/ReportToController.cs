using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
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
        private readonly RbacService _rbac;

        public ReportToController(ApplicationDbContext db, UserManager<ApplicationUser> users, ITenantService tenant, RbacService rbac)
        { _db = db; _users = users; _tenant = tenant; _rbac = rbac; }

        [HttpGet]
        public async Task<IActionResult> GetAll(CancellationToken ct)
        {
            if (!await HasReportToActionAsync("VIEW", ct)) return Forbid();

            var fullAccess = await IsAuthorizedAdminAsync();
            var visiblePersonIds = fullAccess
                ? null
                : await ResolveVisibleReportToPersonIdsAsync(includeSelf: true, ct);

            var rows = await _db.Persons.AsNoTracking()
                .Where(p => p.Staff != null)
                .Where(p => fullAccess || visiblePersonIds!.Contains(p.PersonId))
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
            if (!await HasReportToActionAsync("EDIT", ct)) return Forbid();
            var fullAccess = await IsAuthorizedAdminAsync();
            var visiblePersonIds = fullAccess
                ? null
                : await ResolveVisibleReportToPersonIdsAsync(includeSelf: true, ct);

            if (!fullAccess)
            {
                var callerPersonId = await CurrentPersonIdAsync(ct);
                if (callerPersonId == personId)
                    return Forbid();
                if (visiblePersonIds == null || !visiblePersonIds.Contains(personId))
                    return Forbid();
                if (!dto.ReportsToPersonId.HasValue)
                    return BadRequest(new { message = "Only an organization admin can remove a reporting manager." });
                if (!visiblePersonIds.Contains(dto.ReportsToPersonId.Value))
                    return BadRequest(new { message = "The selected reporting manager is outside your reporting hierarchy." });
            }

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
            if (user?.IsTenantAdmin == true || User.IsInRole("TenantAdmin")) return true;
            return user != null && (await _users.GetRolesAsync(user)).Any(r => r is "Admin" or "TenantAdmin");
        }

        private bool IsFullAccessClaimUser() =>
            User.IsInRole("Admin") ||
            User.IsInRole("TenantAdmin") ||
            string.Equals(User.FindFirstValue(ITenantService.ClaimIsTenantAdmin), "true", StringComparison.OrdinalIgnoreCase);

        private async Task<Guid?> CurrentStaffIdAsync(CancellationToken ct)
        {
            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(identityUserId)) return null;

            return await _db.Persons.AsNoTracking()
                .Where(person => person.IdentityUserId == identityUserId && person.Staff != null)
                .Select(person => (Guid?)person.Staff!.StaffId)
                .FirstOrDefaultAsync(ct);
        }

        private async Task<Guid?> CurrentPersonIdAsync(CancellationToken ct)
        {
            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(identityUserId)) return null;

            return await _db.Persons.AsNoTracking()
                .Where(person => person.IdentityUserId == identityUserId && person.Staff != null)
                .Select(person => (Guid?)person.PersonId)
                .FirstOrDefaultAsync(ct);
        }

        private async Task<bool> HasReportToActionAsync(string action, CancellationToken ct)
        {
            if (IsFullAccessClaimUser()) return true;
            if (!_tenant.TenantId.HasValue || _tenant.IsSuperAdmin) return false;

            var staffId = await CurrentStaffIdAsync(ct);
            if (!staffId.HasValue) return false;

            var normalizedAction = action.Trim().ToUpperInvariant();
            var reportRoutes = new[] { "/hr/process/report", "/hr/reports" };
            var menuIds = await _db.Menus.AsNoTracking()
                .Where(menu => menu.IsActive && menu.Route != null && reportRoutes.Contains(menu.Route))
                .Select(menu => menu.Id)
                .ToListAsync(ct);

            foreach (var menuId in menuIds)
            {
                if (normalizedAction == "VIEW" && await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId}"))
                    return true;
                if (await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId}_{normalizedAction}"))
                    return true;
            }

            return false;
        }

        private async Task<HashSet<Guid>> ResolveVisibleReportToPersonIdsAsync(bool includeSelf, CancellationToken ct)
        {
            var callerPersonId = await CurrentPersonIdAsync(ct)
                ?? throw new KeyNotFoundException("No active employee profile is linked to this account.");

            var people = await _db.Persons.AsNoTracking()
                .Where(person =>
                    person.Staff != null &&
                    person.IsActive &&
                    !_db.Users.Any(user => user.Id == person.IdentityUserId && (user.IsTenantAdmin || user.IsSuperAdmin)))
                .Select(person => new
                {
                    person.PersonId,
                    person.ReportsToPersonId
                })
                .ToListAsync(ct);

            var children = people
                .Where(person => person.ReportsToPersonId.HasValue)
                .ToLookup(person => person.ReportsToPersonId!.Value, person => person.PersonId);

            var visible = new HashSet<Guid>();
            if (includeSelf) visible.Add(callerPersonId);

            var pending = new Queue<Guid>();
            pending.Enqueue(callerPersonId);
            while (pending.TryDequeue(out var managerId))
            {
                foreach (var childId in children[managerId])
                {
                    if (visible.Add(childId))
                        pending.Enqueue(childId);
                }
            }

            return visible;
        }
    }

    public sealed class UpdateReportToDto { public Guid? ReportsToPersonId { get; set; } }
}
