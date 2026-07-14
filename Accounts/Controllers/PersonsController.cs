using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
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

        public PersonsController(
            IPersonService               service,
            UserManager<ApplicationUser> userManager)
        {
            _service     = service;
            _userManager = userManager;
        }

        private async Task<bool> CallerIsSuperAdminAsync()
        {
            var uid  = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = uid != null ? await _userManager.FindByIdAsync(uid) : null;
            return user?.IsSuperAdmin == true;
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
            return Ok(await _service.GetProfilesAsync());
        }

        [HttpGet("{id:guid}/profile")]
        public async Task<IActionResult> GetProfile(Guid id)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            var profile = await _service.GetProfileAsync(id);
            return profile == null ? NotFound(new { message = $"Person {id} not found." }) : Ok(profile);
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
            return Ok(await _service.GetAllAsync());
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
        public async Task<IActionResult> Register([FromBody] RegisterPersonDto? dto)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
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
            if (await CallerIsSuperAdminAsync()) return Forbid();
            if (dto is null) return BadRequest(new { message = "Request body missing." });
            var (person, error) = await _service.UpdateAsync(id, dto);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return Ok(person);
        }

        [HttpPost("{id:guid}/upload-photo")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadPhoto(Guid id, IFormFile photo)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var (photoUrl, fullUrl, error) = await _service.UploadPhotoAsync(id, photo, baseUrl);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return Ok(new { photoUrl, fullUrl });
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            var (success, message) = await _service.DeleteAsync(id);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message });
        }

        [HttpPost("{id:guid}/change-password")]
        public async Task<IActionResult> ChangePassword(Guid id, [FromBody] ChangePasswordDto dto)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
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
            if (await CallerIsSuperAdminAsync()) return Forbid();
            var (success, message, newPassword) = await _service.ResetPasswordAsync(id, dto?.NewPassword);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message, newPassword, note = "Share this password with the employee securely." });
        }

        [HttpPost("{id:guid}/reset-to-default-password")]
        public async Task<IActionResult> ResetToDefaultPassword(Guid id)
        {
            if (await CallerIsSuperAdminAsync()) return Forbid();
            var (success, message, defaultPassword) = await _service.ResetToDefaultPasswordAsync(id);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message, defaultPassword, note = "Password has been reset to the default (LoginId@)." });
        }
    }
}
