using Accounts.Authorization;
using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class PersonsController : ControllerBase
    {
        private readonly IPersonService _service;

        public PersonsController(IPersonService service) => _service = service;

        [HasPermission("MENU_8_VIEW")]
        [HttpGet("profiles")]
        public async Task<IActionResult> GetProfiles() =>
            Ok(await _service.GetProfilesAsync());

        [HasPermission("MENU_8_VIEW")]
        [HttpGet("{id:guid}/profile")]
        public async Task<IActionResult> GetProfile(Guid id)
        {
            var profile = await _service.GetProfileAsync(id);
            return profile == null ? NotFound(new { message = $"Person {id} not found." }) : Ok(profile);
        }

        [HttpGet("org-tree")]   // public — needed for registration form
        public async Task<IActionResult> GetOrgTree() =>
            Ok(await _service.GetOrgTreeAsync());

        [HttpGet("preview-login-id")]   // public — needed for registration form
        public async Task<IActionResult> PreviewLoginId([FromQuery] int branchId)
        {
            if (branchId <= 0) return BadRequest(new { message = "branchId is required." });
            var result = await _service.PreviewLoginIdAsync(branchId);
            return result == null ? NotFound(new { message = $"Branch {branchId} not found." }) : Ok(result);
        }

        [HttpGet("preview-email")]   // public — needed for registration form
        public async Task<IActionResult> PreviewEmail([FromQuery] int branchId, [FromQuery] string fullName)
        {
            if (branchId <= 0) return BadRequest(new { message = "branchId is required." });
            if (string.IsNullOrWhiteSpace(fullName)) return BadRequest(new { message = "fullName is required." });
            var result = await _service.PreviewEmailAsync(branchId, fullName);
            return result == null ? NotFound(new { message = $"Branch {branchId} not found." }) : Ok(result);
        }

        [HasPermission("MENU_8_VIEW")]
        [HttpGet]
        public async Task<IActionResult> GetAll() =>
            Ok(await _service.GetAllAsync());

        [HasPermission("MENU_8_VIEW")]
        [HttpGet("unassigned")]
        public async Task<IActionResult> GetUnassigned() =>
            Ok(await _service.GetUnassignedAsync());

        [HasPermission("MENU_8_VIEW")]
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var person = await _service.GetByIdAsync(id);
            return person == null ? NotFound(new { message = $"Person {id} not found." }) : Ok(person);
        }

        [HttpPost("register-raw")]
        public async Task<IActionResult> RegisterRaw()
        {
            using var reader = new System.IO.StreamReader(Request.Body);
            return Ok(new { received = await reader.ReadToEndAsync() });
        }

        [HasPermission("MENU_10_ADD")]
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterPersonDto? dto)
        {
            if (dto is null) return BadRequest(new { message = "Request body missing." });
            var (person, loginId, password, error, statusCode) = await _service.RegisterAsync(dto);
            if (error != null) return StatusCode(statusCode, new { message = error });

            return CreatedAtAction(nameof(GetById), new { id = person!.PersonId }, new
            {
                person,
                generatedLoginId = loginId,
                generatedPassword = password,
                generatedEmail = person.Email,
                note = "Save these credentials — the password cannot be retrieved again."
            });
        }

        [HasPermission("MENU_8_EDIT")]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePersonDto? dto)
        {
            if (dto is null) return BadRequest(new { message = "Request body missing." });
            var (person, error) = await _service.UpdateAsync(id, dto);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return Ok(person);
        }

        [HttpPost("{id:guid}/upload-photo")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadPhoto(Guid id, IFormFile photo)
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var (photoUrl, fullUrl, error) = await _service.UploadPhotoAsync(id, photo, baseUrl);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return Ok(new { photoUrl, fullUrl });
        }

        [HasPermission("MENU_8_DELETE")]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var (success, message) = await _service.DeleteAsync(id);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message });
        }

        [HttpPost("{id:guid}/change-password")]
        public async Task<IActionResult> ChangePassword(Guid id, [FromBody] ChangePasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.CurrentPassword))
                return BadRequest(new { message = "CurrentPassword is required." });
            if (string.IsNullOrWhiteSpace(dto.NewPassword))
                return BadRequest(new { message = "NewPassword is required." });

            var (success, message) = await _service.ChangePasswordAsync(id, dto.CurrentPassword, dto.NewPassword);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message });
        }

        [HasPermission("MENU_8_EDIT")]
        [HttpPost("{id:guid}/reset-password")]
        public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordDto? dto)
        {
            var (success, message, newPassword) = await _service.ResetPasswordAsync(id, dto?.NewPassword);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message, newPassword, note = "Share this password with the employee securely." });
        }

        [HasPermission("MENU_8_EDIT")]
        [HttpPost("{id:guid}/reset-to-default-password")]
        public async Task<IActionResult> ResetToDefaultPassword(Guid id)
        {
            var (success, message, defaultPassword) = await _service.ResetToDefaultPasswordAsync(id);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message, defaultPassword, note = "Password has been reset to the default (LoginId@)." });
        }
    }
}