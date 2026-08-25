using Accounts.DTOs;
using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Accounts.Idempotency;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers
{
    /// <summary>
    /// Persons API — accessible to Tenant Admins and Staff.
    /// Super Admin sees only Tenant Admin accounts (no company employee data).
    /// Data is automatically scoped per tenant via EF Core Global Query Filters.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [Produces("application/json")]
    public class PersonsController : ControllerBase
    {
        private readonly IPersonService               _service;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext         _db;
        private readonly RbacService                  _rbac;
        private readonly TenantPermissionService      _tenantPermissions;
        private readonly IOrganizationDataScopeService _dataScope;

        public PersonsController(
            IPersonService               service,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext         db,
            RbacService                  rbac,
            TenantPermissionService      tenantPermissions,
            IOrganizationDataScopeService dataScope)
        {
            _service     = service;
            _userManager = userManager;
            _db          = db;
            _rbac        = rbac;
            _tenantPermissions = tenantPermissions;
            _dataScope   = dataScope;
        }

        private Task<bool> CallerIsSuperAdminAsync() => Task.FromResult(
            User.IsInRole("SuperAdmin") ||
            string.Equals(User.FindFirstValue(ITenantService.ClaimIsSuperAdmin), "true", StringComparison.OrdinalIgnoreCase));

        private async Task<bool> HasStaffActionAsync(string action, params string[] semanticKeys)
        {
            if (TenantPermissionService.IsSuperAdmin(User)) return true;
            if (TenantPermissionService.IsTenantAdmin(User))
                return await _tenantPermissions.HasMenuRouteAsync(User, ["/hr/staff"], action);

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

        private async Task<bool> HasRegisterActionAsync()
        {
            if (TenantPermissionService.IsTenantAdmin(User))
                return await _tenantPermissions.HasMenuRouteAsync(User, ["/hr/staff/register"], "ADD");
            if (await HasStaffActionAsync("ADD", "PERSON_REGISTER")) return true;

            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(identityUserId)) return false;

            var staffId = await _db.Persons.AsNoTracking()
                .Where(person => person.IdentityUserId == identityUserId && person.Staff != null)
                .Select(person => (Guid?)person.Staff!.StaffId)
                .FirstOrDefaultAsync();
            if (!staffId.HasValue) return false;

            var registerMenuId = await _db.Menus.AsNoTracking()
                .Where(menu => menu.IsActive && menu.Route == "/hr/staff/register")
                .Select(menu => (int?)menu.Id)
                .FirstOrDefaultAsync();

            return registerMenuId.HasValue && await _rbac.HasAccessAsync(staffId.Value, $"MENU_{registerMenuId.Value}_ADD");
        }

        private async Task<bool> CanAccessPersonAsync(Guid personId)
        {
            var scope = await _dataScope.ResolveAsync(
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
                HttpContext.RequestAborted);
            if (scope.PersonIds.Contains(personId)) return true;

            return await _db.Persons.AsNoTracking().AnyAsync(person =>
                person.PersonId == personId &&
                (person.EmploymentStatus == "Fired" || person.EmploymentStatus == "Retired") &&
                (scope.IsTenantWide || (person.LastOrganizationId.HasValue &&
                    scope.OrganizationIds.Contains(person.LastOrganizationId.Value))),
                HttpContext.RequestAborted);
        }

        [HttpGet("profiles")]
        public async Task<IActionResult> GetProfiles()
        {
            if (await CallerIsSuperAdminAsync())
            {
                var tenantAdmins = await _userManager.Users
                    .AsNoTracking()
                    .Where(u => u.IsTenantAdmin)
                    .OrderBy(u => u.UserName)
                    .Select(u => new
                    {
                        identityUserId = u.Id,
                        fullName       = u.UserName,
                        email          = u.Email,
                        tenantId       = u.TenantId,
                        isTenantAdmin  = u.IsTenantAdmin,
                        note           = "Tenant Admin account"
                    })
                    .ToListAsync();
                return Ok(tenantAdmins);
            }
            var scope = await _dataScope.ResolveAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty, HttpContext.RequestAborted);
            return Ok((await _service.GetProfilesAsync()).Where(profile => scope.PersonIds.Contains(profile.PersonId)));
        }

        [HttpGet("{id:guid}/profile")]
        public async Task<IActionResult> GetProfile(Guid id)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await CanAccessPersonAsync(id)) return Forbid();
            var profile = await _service.GetProfileAsync(id);
            return profile == null ? NotFound(new { message = $"Person {id} not found." }) : Ok(profile);
        }

        [HttpGet("{id:guid}/hr-profile")]
        public async Task<IActionResult> GetHrProfile(Guid id)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await CanAccessPersonAsync(id)) return Forbid();
            var profile = await _service.GetHrProfileAsync(id);
            return profile == null ? NotFound(new { message = $"Person {id} not found." }) : Ok(profile);
        }

        [HttpPut("{id:guid}/hr-profile")]
        public async Task<IActionResult> UpdateHrProfile(Guid id, [FromBody] PersonHrProfileDto? dto)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasStaffActionAsync("EDIT", "PERSON_EDIT", "EMPLOYEE_EDIT")) return Forbid();
            if (!await CanAccessPersonAsync(id)) return Forbid();
            if (dto is null) return BadRequest(new { message = "Request body missing." });
            var (profile, error) = await _service.UpdateHrProfileAsync(id, dto);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return Ok(profile);
        }

        // ── Public helpers — needed by registration form ──────────────────────

        [HttpGet("org-tree")]
        public async Task<IActionResult> GetOrgTree() =>
            Ok(await _service.GetOrgTreeAsync());

        [HttpGet("preview-login-id")]
        public async Task<IActionResult> PreviewLoginId([FromQuery] int branchId)
        {
            if (branchId <= 0) return BadRequest(new { message = "branchId is required." });
            var result = await _service.PreviewLoginIdAsync(branchId);
            return result == null ? NotFound(new { message = $"Branch {branchId} not found." }) : Ok(result);
        }

        [HttpGet("preview-email")]
        public async Task<IActionResult> PreviewEmail([FromQuery] int branchId, [FromQuery] string fullName)
        {
            if (branchId <= 0) return BadRequest(new { message = "branchId is required." });
            if (string.IsNullOrWhiteSpace(fullName)) return BadRequest(new { message = "fullName is required." });
            var result = await _service.PreviewEmailAsync(branchId, fullName);
            return result == null ? NotFound(new { message = $"Branch {branchId} not found." }) : Ok(result);
        }

        // ── Protected endpoints ───────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            if (await CallerIsSuperAdminAsync())
            {
                var tenantAdmins = await _userManager.Users
                    .AsNoTracking()
                    .Where(u => u.IsTenantAdmin)
                    .OrderBy(u => u.UserName)
                    .Select(u => new
                    {
                        identityUserId = u.Id,
                        fullName       = u.UserName,
                        email          = u.Email,
                        tenantId       = u.TenantId,
                        isTenantAdmin  = u.IsTenantAdmin,
                        note           = "Tenant Admin account"
                    })
                    .ToListAsync();
                return Ok(tenantAdmins);
            }
            var scope = await _dataScope.ResolveAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty, HttpContext.RequestAborted);
            return Ok((await _service.GetAllAsync()).Where(person => scope.PersonIds.Contains(person.PersonId)));
        }

        [HttpGet("former")]
        public async Task<IActionResult> GetFormer()
        {
            if (await CallerIsSuperAdminAsync()) return Ok(Array.Empty<PersonDto>());
            if (!await HasStaffActionAsync("VIEW", "PERSON_VIEW", "EMPLOYEE_VIEW")) return Forbid();

            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var scope = await _dataScope.ResolveAsync(identityUserId, HttpContext.RequestAborted);
            return Ok(await _service.GetFormerAsync(scope.IsTenantWide, scope.OrganizationIds));
        }

        [HttpGet("unassigned")]
        public async Task<IActionResult> GetUnassigned()
        {
            if (await CallerIsSuperAdminAsync()) return Ok(new List<object>());
            return Ok(await _service.GetUnassignedAsync());
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(identityUserId)) return Unauthorized();
            var person = await _service.GetByIdentityUserIdAsync(identityUserId);
            return person == null
                ? NotFound(new { message = "No person profile is linked to this account." })
                : Ok(person);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await CanAccessPersonAsync(id)) return Forbid();
            var person = await _service.GetByIdAsync(id);
            return person == null ? NotFound(new { message = $"Person {id} not found." }) : Ok(person);
        }

        [HttpPost("register-raw")]
        public async Task<IActionResult> RegisterRaw()
        {
            using var reader = new System.IO.StreamReader(Request.Body);
            return Ok(new { received = await reader.ReadToEndAsync() });
        }

        [HttpPost("register")]
        [Idempotent]
        public async Task<IActionResult> Register([FromBody] RegisterPersonDto? dto)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasRegisterActionAsync()) return Forbid();
            if (dto is null) return BadRequest(new { message = "Request body missing." });
            var (person, loginId, password, error, statusCode) = await _service.RegisterAsync(dto);
            if (error != null) return StatusCode(statusCode, new { message = error });
            return CreatedAtAction(nameof(GetById), new { id = person!.PersonId }, new
            {
                person,
                generatedLoginId  = loginId,
                generatedPassword = password,
                generatedEmail    = person.Email,
                note = "Save these credentials — the password cannot be retrieved again."
            });
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePersonDto? dto)
        {
            if (!await CanAccessPersonAsync(id)) return Forbid();
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasStaffActionAsync("EDIT", "PERSON_EDIT", "EMPLOYEE_EDIT")) return Forbid();
            if (dto is null) return BadRequest(new { message = "Request body missing." });
            var (person, error) = await _service.UpdateAsync(id, dto);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return Ok(person);
        }

        [HttpPost("{id:guid}/upload-photo")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadPhoto(Guid id, IFormFile photo)
        {
            if (!await CanAccessPersonAsync(id)) return Forbid();
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasStaffActionAsync("EDIT", "PERSON_EDIT", "EMPLOYEE_EDIT")) return Forbid();
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var (photoUrl, fullUrl, error) = await _service.UploadPhotoAsync(id, photo, baseUrl);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return Ok(new { photoUrl, fullUrl });
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (!await CanAccessPersonAsync(id)) return Forbid();
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasStaffActionAsync("DELETE", "PERSON_DELETE", "EMPLOYEE_DELETE")) return Forbid();
            var (success, message) = await _service.DeleteAsync(id);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message });
        }

        [HttpPatch("{id:guid}/status")]
        public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetPersonStatusDto dto)
        {
            if (!await CanAccessPersonAsync(id)) return Forbid();
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasStaffActionAsync("DELETE", "PERSON_DELETE", "EMPLOYEE_DELETE")) return Forbid();
            var (success, message, isActive) = await _service.SetActiveAsync(id, dto.IsActive);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message, isActive });
        }

        [HttpPost("{id:guid}/change-password")]
        public async Task<IActionResult> ChangePassword(Guid id, [FromBody] ChangePasswordDto dto)
        {
            if (!await CanAccessPersonAsync(id)) return Forbid();
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasStaffActionAsync("EDIT", "PERSON_RESET_PASSWORD", "PERSON_EDIT")) return Forbid();
            if (string.IsNullOrWhiteSpace(dto.CurrentPassword))
                return BadRequest(new { message = "CurrentPassword is required." });
            if (string.IsNullOrWhiteSpace(dto.NewPassword))
                return BadRequest(new { message = "NewPassword is required." });
            var (success, message) = await _service.ChangePasswordAsync(id, dto.CurrentPassword, dto.NewPassword);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message });
        }

        [HttpPost("{id:guid}/reset-password")]
        public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordDto? dto)
        {
            if (!await CanAccessPersonAsync(id)) return Forbid();
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasStaffActionAsync("EDIT", "PERSON_RESET_PASSWORD", "PERSON_EDIT")) return Forbid();
            var (success, message, newPassword) = await _service.ResetPasswordAsync(id, dto?.NewPassword);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message, newPassword, note = "Share this password with the employee securely." });
        }

        [HttpPost("{id:guid}/reset-to-default-password")]
        public async Task<IActionResult> ResetToDefaultPassword(Guid id)
        {
            if (!await CanAccessPersonAsync(id)) return Forbid();
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (!await HasStaffActionAsync("EDIT", "PERSON_RESET_PASSWORD", "PERSON_EDIT")) return Forbid();
            var (success, message, defaultPassword) = await _service.ResetToDefaultPasswordAsync(id);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message, defaultPassword, note = "Password has been reset to the default (LoginId@)." });
        }
    }
}
