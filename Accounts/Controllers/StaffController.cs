using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
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

        public StaffController(
            IStaffService               service,
            ApplicationDbContext        db)
        {
            _service     = service;
            _db          = db;
        }

        private Task<bool> CallerIsSuperAdminAsync() => Task.FromResult(
            User.IsInRole("SuperAdmin") ||
            string.Equals(User.FindFirstValue(ITenantService.ClaimIsSuperAdmin), "true", StringComparison.OrdinalIgnoreCase));

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
                return Ok(tenantAdmins);
            }
            return Ok(await _service.GetAllAsync());
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            var s = await _service.GetByIdAsync(id);
            return s == null ? NotFound(new { message = $"Employee {id} not found." }) : Ok(s);
        }

        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string q)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (string.IsNullOrWhiteSpace(q)) return BadRequest(new { message = "Query 'q' is required." });
            return Ok(await _service.SearchAsync(q));
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
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (staff, error) = await _service.HireAsync(vacancyId, dto);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return CreatedAtAction(nameof(GetById), new { id = staff!.StaffId }, staff);
        }

        [HttpPost("hire-person/{vacancyId:guid}")]
        public async Task<IActionResult> HirePerson(Guid vacancyId, [FromQuery] Guid personId)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            var (staff, error) = await _service.HirePersonAsync(vacancyId, personId);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return CreatedAtAction(nameof(GetById), new { id = staff!.StaffId }, staff);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStaffDto dto)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
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
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var (photoUrl, fullUrl, error) = await _service.UploadPhotoAsync(id, photo, baseUrl);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return Ok(new { message = "Photo uploaded successfully.", photoUrl, fullUrl });
        }

        [HttpDelete("{id:guid}/photo")]
        public async Task<IActionResult> DeletePhoto(Guid id)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            var (success, message) = await _service.DeletePhotoAsync(id);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message });
        }

        [HttpPut("{id:guid}/transfer")]
        public async Task<IActionResult> Transfer(Guid id, [FromBody] TransferStaffDto dto)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (staff, error) = await _service.TransferAsync(id, dto);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return Ok(staff);
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
