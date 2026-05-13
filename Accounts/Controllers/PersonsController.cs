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

        // ── Queries ───────────────────────────────────────────────────────────

        /// <summary>Get all person profiles with full org + employment info</summary>
        [HttpGet("profiles")]
        public async Task<IActionResult> GetProfiles() =>
            Ok(await _service.GetProfilesAsync());

        /// <summary>Get a single person's full profile</summary>
        [HttpGet("{id:guid}/profile")]
        public async Task<IActionResult> GetProfile(Guid id)
        {
            var profile = await _service.GetProfileAsync(id);
            return profile == null ? NotFound(new { message = $"Person {id} not found." }) : Ok(profile);
        }

        /// <summary>Get org tree for registration form dropdowns</summary>
        [HttpGet("org-tree")]
        public async Task<IActionResult> GetOrgTree() =>
            Ok(await _service.GetOrgTreeAsync());

        /// <summary>
        /// Preview the login ID, password and email domain that will be
        /// auto-generated for a given branch.
        /// </summary>
        [HttpGet("preview-login-id")]
        public async Task<IActionResult> PreviewLoginId([FromQuery] int branchId)
        {
            if (branchId <= 0) return BadRequest(new { message = "branchId is required." });
            var result = await _service.PreviewLoginIdAsync(branchId);
            return result == null ? NotFound(new { message = $"Branch {branchId} not found." }) : Ok(result);
        }

        /// <summary>
        /// Preview the email that will be auto-generated for a given name + branch.
        /// Call this on the frontend as the user types their name.
        /// GET /api/persons/preview-email?branchId=4&amp;fullName=Abubakar+Khan
        /// </summary>
        [HttpGet("preview-email")]
        public async Task<IActionResult> PreviewEmail([FromQuery] int branchId, [FromQuery] string fullName)
        {
            if (branchId <= 0) return BadRequest(new { message = "branchId is required." });
            if (string.IsNullOrWhiteSpace(fullName)) return BadRequest(new { message = "fullName is required." });
            var result = await _service.PreviewEmailAsync(branchId, fullName);
            return result == null ? NotFound(new { message = $"Branch {branchId} not found." }) : Ok(result);
        }

        /// <summary>Get all persons</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll() =>
            Ok(await _service.GetAllAsync());

        /// <summary>Get persons not yet assigned to any vacancy</summary>
        [HttpGet("unassigned")]
        public async Task<IActionResult> GetUnassigned() =>
            Ok(await _service.GetUnassignedAsync());

        /// <summary>Get a single person by ID</summary>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var person = await _service.GetByIdAsync(id);
            return person == null ? NotFound(new { message = $"Person {id} not found." }) : Ok(person);
        }

        // ── Commands ──────────────────────────────────────────────────────────

        /// <summary>Debug endpoint — returns raw request body</summary>
        [HttpPost("register-raw")]
        public async Task<IActionResult> RegisterRaw()
        {
            using var reader = new System.IO.StreamReader(Request.Body);
            return Ok(new { received = await reader.ReadToEndAsync() });
        }

        /// <summary>
        /// Register a new person.
        /// LoginId, Password and Email are ALL AUTO-GENERATED — do not send them.
        /// The response includes all generated credentials (show once to admin).
        /// </summary>
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterPersonDto? dto)
        {
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

        /// <summary>Update person info and addresses</summary>
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePersonDto? dto)
        {
            if (dto is null) return BadRequest(new { message = "Request body missing." });
            var (person, error) = await _service.UpdateAsync(id, dto);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return Ok(person);
        }

        /// <summary>Upload person profile photo (multipart/form-data, field: photo, max 5MB)</summary>
        [HttpPost("{id:guid}/upload-photo")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadPhoto(Guid id, IFormFile photo)
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var (photoUrl, fullUrl, error) = await _service.UploadPhotoAsync(id, photo, baseUrl);
            if (error != null) return error.Contains("not found") ? NotFound(new { message = error }) : BadRequest(new { message = error });
            return Ok(new { photoUrl, fullUrl });
        }

        /// <summary>Delete a person and their identity account</summary>
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var (success, message) = await _service.DeleteAsync(id);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message });
        }

        // ── Password Management ───────────────────────────────────────────────

        /// <summary>
        /// Employee changes their own password (requires current password).
        /// </summary>
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

        /// <summary>
        /// Admin resets password — no current password needed.
        /// Leave NewPassword empty to auto-generate as LoginId@
        /// </summary>
        [HttpPost("{id:guid}/reset-password")]
        public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordDto? dto)
        {
            var (success, message, newPassword) = await _service.ResetPasswordAsync(id, dto?.NewPassword);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message, newPassword, note = "Share this password with the employee securely." });
        }

        /// <summary>
        /// Reset password back to default (LoginId@).
        /// e.g. LT10001 → LT10001@
        /// </summary>
        [HttpPost("{id:guid}/reset-to-default-password")]
        public async Task<IActionResult> ResetToDefaultPassword(Guid id)
        {
            var (success, message, defaultPassword) = await _service.ResetToDefaultPasswordAsync(id);
            if (!success) return message.Contains("not found") ? NotFound(new { message }) : BadRequest(new { message });
            return Ok(new { message, defaultPassword, note = "Password has been reset to the default (LoginId@)." });
        }
    }
}
