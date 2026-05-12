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

        // ── DTOs (kept here so frontend-facing types stay in one place) ───────

        public class AddressDto
        {
            public string? AddressLine { get; set; }
            public string? Country     { get; set; }
            public string? Province    { get; set; }
            public string? District    { get; set; }
            public string? City        { get; set; }
            public string? PostalCode  { get; set; }
        }

        public class AddressResponseDto
        {
            public string? AddressLine { get; set; }
            public string? Country     { get; set; }
            public string? Province    { get; set; }
            public string? District    { get; set; }
            public string? City        { get; set; }
            public string? PostalCode  { get; set; }
        }

        public class UpdatePersonDto
        {
            public string    FullName      { get; set; } = string.Empty;
            public string?   Phone         { get; set; }
            public string?   Email         { get; set; }
            public string?   Gender        { get; set; }
            public DateTime? DateOfBirth   { get; set; }
            public string?   MaritalStatus { get; set; }
            public AddressDto? CurrentAddress   { get; set; }
            public AddressDto? PermanentAddress { get; set; }
        }

        public class RegisterPersonDto
        {
            public string    FullName      { get; set; } = string.Empty;
            public string?   Phone         { get; set; }
            public string?   Email         { get; set; }
            public string?   Gender        { get; set; }
            public DateTime? DateOfBirth   { get; set; }
            public string?   MaritalStatus { get; set; }
            public int       BranchId      { get; set; }
            public string    Password      { get; set; } = string.Empty;

            [System.Text.Json.Serialization.JsonPropertyName("currentAddress")]
            public System.Text.Json.JsonElement? CurrentAddressRaw { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("permanentAddress")]
            public System.Text.Json.JsonElement? PermanentAddressRaw { get; set; }

            [System.Text.Json.Serialization.JsonIgnore]
            public AddressDto? CurrentAddress => ParseAddress(CurrentAddressRaw);

            [System.Text.Json.Serialization.JsonIgnore]
            public AddressDto? PermanentAddress => ParseAddress(PermanentAddressRaw);

            private static AddressDto? ParseAddress(System.Text.Json.JsonElement? raw)
            {
                if (raw is null) return null;
                var el = raw.Value;
                if (el.ValueKind == System.Text.Json.JsonValueKind.Object)
                    return System.Text.Json.JsonSerializer.Deserialize<AddressDto>(el.GetRawText());
                if (el.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var inner = el.GetString();
                    if (string.IsNullOrWhiteSpace(inner)) return null;
                    return System.Text.Json.JsonSerializer.Deserialize<AddressDto>(inner);
                }
                return null;
            }
        }

        public class PersonDto
        {
            public Guid      PersonId      { get; set; }
            public string    LoginId       { get; set; } = string.Empty;
            public string    FullName      { get; set; } = string.Empty;
            public string?   Gender        { get; set; }
            public DateTime? DateOfBirth   { get; set; }
            public string?   MaritalStatus { get; set; }
            public string?   Phone         { get; set; }
            public string?   Email         { get; set; }
            public string?   PhotoUrl      { get; set; }
            public bool      IsHired       { get; set; }
            public string    RegisteredAt  { get; set; } = string.Empty;
            public int?      BranchId      { get; set; }
            public string?   BranchName    { get; set; }
            public string?   CompanyName   { get; set; }
            public string?   CountryName   { get; set; }
            public AddressResponseDto CurrentAddress   { get; set; } = new();
            public AddressResponseDto PermanentAddress { get; set; } = new();
            public bool               SameAddress      { get; set; }
        }

        public class PersonProfileDto
        {
            public Guid      PersonId      { get; set; }
            public string    LoginId       { get; set; } = string.Empty;
            public string    FullName      { get; set; } = string.Empty;
            public string    Initials      { get; set; } = string.Empty;
            public string?   Gender        { get; set; }
            public DateTime? DateOfBirth   { get; set; }
            public string?   MaritalStatus { get; set; }
            public string?   Phone         { get; set; }
            public string?   Email         { get; set; }
            public string?   PhotoUrl      { get; set; }
            public DateTime  RegisteredAt  { get; set; }
            public int?      BranchId      { get; set; }
            public string?   BranchName    { get; set; }
            public string?   CompanyName   { get; set; }
            public string?   CountryName   { get; set; }
            public string?   CountryFlag   { get; set; }
            public bool      IsHired       { get; set; }
            public Guid?     StaffId       { get; set; }
            public DateTime? JoiningDate   { get; set; }
            public Guid?     VacancyId     { get; set; }
            public string?   VacancyCode   { get; set; }
            public string?   JobTitle      { get; set; }
            public string?   Department    { get; set; }
            public AddressResponseDto CurrentAddress   { get; set; } = new();
            public AddressResponseDto PermanentAddress { get; set; } = new();
        }

        // ── Endpoints ─────────────────────────────────────────────────────────

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

        /// <summary>Preview the login ID that will be generated for a branch</summary>
        [HttpGet("preview-login-id")]
        public async Task<IActionResult> PreviewLoginId([FromQuery] int branchId)
        {
            if (branchId <= 0) return BadRequest(new { message = "branchId is required." });
            var result = await _service.PreviewLoginIdAsync(branchId);
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

        /// <summary>Debug endpoint — returns raw request body</summary>
        [HttpPost("register-raw")]
        public async Task<IActionResult> RegisterRaw()
        {
            using var reader = new System.IO.StreamReader(Request.Body);
            return Ok(new { received = await reader.ReadToEndAsync() });
        }

        /// <summary>Register a new person with address and identity account</summary>
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterPersonDto? dto)
        {
            if (dto is null) return BadRequest(new { message = "Request body missing." });
            var (person, error, statusCode) = await _service.RegisterAsync(dto);
            if (error != null) return StatusCode(statusCode, new { message = error });
            return CreatedAtAction(nameof(GetById), new { id = person!.PersonId }, person);
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
    }
}
