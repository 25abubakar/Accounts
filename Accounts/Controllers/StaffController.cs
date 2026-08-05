using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers
{
    /// <summary>
    /// Staff/Employees API — accessible to Tenant Admins and Staff.
    /// Super Admin sees only Tenant Admin accounts (no company employee data).
    /// Data is automatically scoped per tenant via EF Core Global Query Filters.
    /// </summary>
    [ApiController]
    [Route("api/employees")]
    [Authorize]
    [Produces("application/json")]
    public class StaffController : ControllerBase
    {
        private readonly IStaffService               _service;
        private readonly ApplicationDbContext        _db;
        private readonly RbacService                 _rbac;
        private readonly IOrganizationDataScopeService _dataScope;

        public StaffController(
            IStaffService               service,
            ApplicationDbContext        db,
            RbacService                 rbac,
            IOrganizationDataScopeService dataScope)
        {
            _service     = service;
            _db          = db;
            _rbac        = rbac;
            _dataScope   = dataScope;
        }

        private Task<bool> CallerIsSuperAdminAsync() => Task.FromResult(
            User.IsInRole("SuperAdmin") ||
            string.Equals(User.FindFirstValue(ITenantService.ClaimIsSuperAdmin), "true", StringComparison.OrdinalIgnoreCase));

        private async Task<bool> HasStaffActionAsync(string action, params string[] semanticKeys)
        {
            if (User.IsInRole("Admin") || User.IsInRole("TenantAdmin") ||
                string.Equals(User.FindFirstValue(ITenantService.ClaimIsTenantAdmin), "true", StringComparison.OrdinalIgnoreCase))
                return true;

            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(identityUserId)) return false;

            var staffId = await _db.Persons.AsNoTracking()
                .Where(person => person.IdentityUserId == identityUserId && person.Staff != null)
                .Select(person => (Guid?)person.Staff!.StaffId)
                .FirstOrDefaultAsync();
            if (!staffId.HasValue) return false;

            var staffMenuId = await _db.Menus.AsNoTracking()
                .Where(menu => menu.IsActive && menu.Route == "/hr/staff")
                .Select(menu => (int?)menu.Id)
                .FirstOrDefaultAsync();

            if (staffMenuId.HasValue && await _rbac.HasAccessAsync(staffId.Value, $"MENU_{staffMenuId.Value}_{action}"))
                return true;

            foreach (var key in semanticKeys)
                if (await _rbac.HasAccessAsync(staffId.Value, key)) return true;

            return false;
        }

        private async Task<OrganizationDataScope> CurrentDataScopeAsync() =>
            await _dataScope.ResolveAsync(
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
                HttpContext.RequestAborted);

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            // Super Admin: returns Tenant Admin accounts (not company employees)
            if (await CallerIsSuperAdminAsync())
            {
                var tenantAdmins = await _db.Users
                    .AsNoTracking()
                    .OfType<ApplicationUser>()
                    .Where(u => u.IsTenantAdmin)
                    .OrderBy(u => u.UserName)
                    .Select(u => new
                    {
                        staffId        = u.Id,
                        identityUserId = u.Id,
                        loginId        = u.UserName,
                        fullName       = u.UserName,
                        email          = u.Email,
                        phone          = "",
                        vacancyId      = "",
                        vacancyCode    = "",
                        jobTitle       = "Tenant Admin",
                        department     = "",
                        joiningDate    = (DateTime?)null,
                        isTenantAdmin  = u.IsTenantAdmin,
                        tenantId       = u.TenantId,
                        note           = "Tenant Admin account"
                    })
                    .ToListAsync();

                var tenantIds = tenantAdmins
                    .Where(admin => admin.tenantId.HasValue)
                    .Select(admin => admin.tenantId!.Value)
                    .Distinct()
                    .ToArray();

                var tenants = await _db.Tenants
                    .AsNoTracking()
                    .Where(tenant => tenantIds.Contains(tenant.Id))
                    .Select(tenant => new
                    {
                        tenant.Id,
                        tenant.TenantName,
                        tenant.OrganizationTreeId
                    })
                    .ToListAsync();

                var organizationNodes = await _db.OrganizationTree
                    .AsNoTracking()
                    .Select(node => new
                    {
                        node.Id,
                        node.ParentId,
                        node.Name,
                        node.Label
                    })
                    .ToListAsync();

                var tenantMap = tenants.ToDictionary(tenant => tenant.Id);
                var organizationMap = organizationNodes.ToDictionary(node => node.Id);

                string? FindOrgName(int organizationTreeId, params string[] labels)
                {
                    var labelSet = labels.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var guard = 0;
                    int? currentId = organizationTreeId;
                    while (currentId.HasValue && organizationMap.TryGetValue(currentId.Value, out var node) && guard++ < 100)
                    {
                        if (labelSet.Contains(node.Label))
                            return node.Name;
                        currentId = node.ParentId;
                    }
                    return null;
                }

                var enrichedTenantAdmins = tenantAdmins.Select(admin =>
                {
                    var tenant = admin.tenantId.HasValue && tenantMap.TryGetValue(admin.tenantId.Value, out var foundTenant)
                        ? foundTenant
                        : null;

                    var countryName = tenant == null ? null : FindOrgName(tenant.OrganizationTreeId, "Country");
                    var groupName = tenant == null ? null : FindOrgName(tenant.OrganizationTreeId, "Group");
                    var branchName = tenant == null ? null : FindOrgName(tenant.OrganizationTreeId, "Branch");

                    return new
                    {
                        admin.staffId,
                        admin.identityUserId,
                        admin.loginId,
                        admin.fullName,
                        admin.email,
                        admin.phone,
                        admin.vacancyId,
                        admin.vacancyCode,
                        admin.jobTitle,
                        admin.department,
                        branchName,
                        companyName = tenant?.TenantName,
                        countryName,
                        groupName,
                        admin.joiningDate,
                        admin.isTenantAdmin,
                        admin.tenantId,
                        admin.note
                    };
                });

                return Ok(enrichedTenantAdmins);
            }
            var scope = await _dataScope.ResolveAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty, HttpContext.RequestAborted);
            return Ok((await _service.GetAllAsync()).Where(staff => staff.PersonId.HasValue && scope.PersonIds.Contains(staff.PersonId.Value)));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            var s = await _service.GetByIdAsync(id);
            var scope = await _dataScope.ResolveAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty, HttpContext.RequestAborted);
            if (s != null && (!s.PersonId.HasValue || !scope.PersonIds.Contains(s.PersonId.Value))) return Forbid();
            return s == null ? NotFound(new { message = $"Employee {id} not found." }) : Ok(s);
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string q)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (string.IsNullOrWhiteSpace(q)) return BadRequest(new { message = "Query 'q' is required." });
            var scope = await _dataScope.ResolveAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty, HttpContext.RequestAborted);
            return Ok((await _service.SearchAsync(q)).Where(staff => staff.PersonId.HasValue && scope.PersonIds.Contains(staff.PersonId.Value)));
        }

        [HttpGet("by-login/{loginOrEmail}")]
        public async Task<IActionResult> GetByLogin(string loginOrEmail)
        {
            if (await CallerIsSuperAdminAsync()) return Ok(new { });

            var staff = await _db.StaffVacancies
                .AsNoTracking()
                .Include(s => s.Person)
                .Include(s => s.Vacancy)
                    .ThenInclude(v => v!.JobTitleNav)
                .Include(s => s.Vacancy)
                    .ThenInclude(v => v!.Organization)
                    .ThenInclude(o => o!.Parent)
                    .ThenInclude(p => p!.Parent)
                .Where(s => s.LoginId == loginOrEmail || (s.Person != null && s.Person.Email == loginOrEmail))
                .FirstOrDefaultAsync();

            if (staff == null) return NotFound(new { message = "Staff not found." });
            var scope = await _dataScope.ResolveAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty, HttpContext.RequestAborted);
            if (!scope.StaffIds.Contains(staff.StaffId)) return Forbid();

            var branch  = staff.Vacancy?.Organization;
            var company = branch?.Parent;
            var country = company?.Parent;

            return Ok(new
            {
                staffId     = staff.StaffId,
                loginId     = staff.LoginId ?? staff.Vacancy?.VacancyCode,
                fullName    = staff.Person?.FullName ?? "-",
                email       = staff.Person?.Email,
                phone       = staff.Person?.Phone,
                photoUrl    = staff.Person?.ProfilePhotoUrl,
                vacancyId   = staff.VacancyId,
                vacancyCode = staff.Vacancy?.VacancyCode,
                jobTitle    = staff.Vacancy?.ResolvedJobTitle,
                department  = staff.Vacancy?.Department ?? branch?.Name,
                branchName  = branch?.Name,
                companyName = company?.Name,
                countryName = country?.Name,
                joiningDate = DateTime.UtcNow
            });
        }

        [HttpPost("hire/{vacancyId:guid}")]
        public async Task<IActionResult> Hire(Guid vacancyId, [FromBody] HireStaffDto dto)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasStaffActionAsync("ADD", "PERSON_REGISTER")) return Forbid();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (staff, error) = await _service.HireAsync(vacancyId, dto);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return CreatedAtAction(nameof(GetById), new { id = staff!.StaffId }, staff);
        }

        [HttpPost("hire-person/{vacancyId:guid}")]
        public async Task<IActionResult> HirePerson(Guid vacancyId, [FromQuery] Guid personId)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasStaffActionAsync("ADD", "PERSON_REGISTER")) return Forbid();
            var scope = await CurrentDataScopeAsync();
            var targetOrganizationId = await _db.Vacancies.AsNoTracking()
                .Where(vacancy => vacancy.VacancyId == vacancyId)
                .Select(vacancy => (int?)vacancy.OrganizationId).FirstOrDefaultAsync();
            if (!scope.PersonIds.Contains(personId) || !targetOrganizationId.HasValue || !scope.OrganizationIds.Contains(targetOrganizationId.Value)) return Forbid();
            var (staff, error) = await _service.HirePersonAsync(vacancyId, personId);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return CreatedAtAction(nameof(GetById), new { id = staff!.StaffId }, staff);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStaffDto dto)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasStaffActionAsync("EDIT", "EMPLOYEE_EDIT", "PERSON_EDIT")) return Forbid();
            if (!(await CurrentDataScopeAsync()).StaffIds.Contains(id)) return Forbid();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (staff, error) = await _service.UpdateAsync(id, dto);
            if (error != null) return NotFound(new { message = error });
            return Ok(staff);
        }

        [HttpPost("{id:guid}/upload-photo")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadPhoto(Guid id, IFormFile photo)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasStaffActionAsync("EDIT", "EMPLOYEE_EDIT", "PERSON_EDIT")) return Forbid();
            if (!(await CurrentDataScopeAsync()).StaffIds.Contains(id)) return Forbid();
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var (photoUrl, fullUrl, error) = await _service.UploadPhotoAsync(id, photo, baseUrl);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return Ok(new { message = "Photo uploaded successfully.", photoUrl, fullUrl });
        }

        [HttpDelete("{id:guid}/photo")]
        public async Task<IActionResult> DeletePhoto(Guid id)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasStaffActionAsync("EDIT", "EMPLOYEE_EDIT", "PERSON_EDIT")) return Forbid();
            if (!(await CurrentDataScopeAsync()).StaffIds.Contains(id)) return Forbid();
            var (success, message) = await _service.DeletePhotoAsync(id);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message });
        }

        [HttpPut("{id:guid}/transfer")]
        public async Task<IActionResult> Transfer(Guid id, [FromBody] TransferStaffDto dto)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasStaffActionAsync("EDIT", "EMPLOYEE_TRANSFER", "EMPLOYEE_EDIT")) return Forbid();
            var scope = await CurrentDataScopeAsync();
            var targetOrganizationId = await _db.Vacancies.AsNoTracking().Where(vacancy => vacancy.VacancyId == dto.NewVacancyId)
                .Select(vacancy => (int?)vacancy.OrganizationId).FirstOrDefaultAsync();
            if (!scope.StaffIds.Contains(id) || !targetOrganizationId.HasValue || !scope.OrganizationIds.Contains(targetOrganizationId.Value)) return Forbid();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (staff, error) = await _service.TransferAsync(id, dto);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return Ok(staff);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasStaffActionAsync("DELETE", "EMPLOYEE_DELETE", "PERSON_DELETE")) return Forbid();
            if (!(await CurrentDataScopeAsync()).StaffIds.Contains(id)) return Forbid();
            var (success, message) = await _service.DeleteAsync(id);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message });
        }
    }
}
