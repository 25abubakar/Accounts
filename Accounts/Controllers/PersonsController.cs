using Accounts.Data;
using Accounts.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class PersonsController : ControllerBase
    {
        private readonly ApplicationDbContext    _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IWebHostEnvironment     _env;

        public PersonsController(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            IWebHostEnvironment env)
        {
            _db          = db;
            _userManager = userManager;
            _env         = env;
        }

        // ── DTOs ──────────────────────────────────────────────────────────────

        public class AddressDto
        {
            public string? AddressLine { get; set; }
            public string? Country     { get; set; }
            public string? Province    { get; set; }
            public string? District    { get; set; }
            public string? City        { get; set; }
            public string? PostalCode  { get; set; }
        }

        public class RegisterPersonDto
        {
            public string     FullName        { get; set; } = string.Empty;
            public string?    Phone           { get; set; }
            public string?    Email           { get; set; }
            public string?    Gender          { get; set; }
            public DateTime?  DateOfBirth     { get; set; }
            public string?    MaritalStatus   { get; set; }
            public string     LoginId         { get; set; } = string.Empty;
            public string     Password        { get; set; } = string.Empty;
            public AddressDto? CurrentAddress  { get; set; }
            public AddressDto? PermanentAddress { get; set; }
        }

        public class PersonDto
        {
            public Guid      PersonId        { get; set; }
            public string    FullName        { get; set; } = string.Empty;
            public string?   Phone           { get; set; }
            public string?   Email           { get; set; }
            public string?   Gender          { get; set; }
            public DateTime? DateOfBirth     { get; set; }
            public string?   MaritalStatus   { get; set; }
            public string?   ProfilePhotoUrl { get; set; }
            public string    LoginId         { get; set; } = string.Empty;
            public DateTime  CreatedDate     { get; set; }
            public IEnumerable<PersonAddressDto> Addresses { get; set; } = [];
        }

        public class PersonAddressDto
        {
            public Guid    AddressId   { get; set; }
            public string  AddressType { get; set; } = string.Empty;
            public string? AddressLine { get; set; }
            public string? Country     { get; set; }
            public string? Province    { get; set; }
            public string? District    { get; set; }
            public string? City        { get; set; }
            public string? PostalCode  { get; set; }
        }

        // ── GET /api/persons ──────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var persons = await _db.Persons
                .Include(p => p.Addresses)
                .OrderByDescending(p => p.CreatedDate)
                .ToListAsync();

            return Ok(persons.Select(MapToDto));
        }

        // ── GET /api/persons/{id} ─────────────────────────────────────────────

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var person = await _db.Persons
                .Include(p => p.Addresses)
                .FirstOrDefaultAsync(p => p.PersonId == id);

            if (person == null)
                return NotFound(new { message = $"Person {id} not found." });

            return Ok(MapToDto(person));
        }

        // ── POST /api/persons/register ────────────────────────────────────────
        /// <summary>
        /// Creates an Identity user (using LoginId as UserName) + a Person record
        /// + optional Current and Permanent address rows — all in one transaction.
        /// </summary>
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterPersonDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.FullName))
                return BadRequest(new { message = "FullName is required." });

            if (string.IsNullOrWhiteSpace(dto.LoginId))
                return BadRequest(new { message = "LoginId is required." });

            if (string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest(new { message = "Password is required." });

            // ── 1. Check LoginId uniqueness ───────────────────────────
            if (await _db.Persons.AnyAsync(p => p.LoginId == dto.LoginId))
                return Conflict(new { message = $"LoginId '{dto.LoginId}' is already taken." });

            // ── 2. Check email uniqueness (if provided) ───────────────
            if (!string.IsNullOrWhiteSpace(dto.Email) &&
                await _userManager.FindByEmailAsync(dto.Email) != null)
                return Conflict(new { message = $"Email '{dto.Email}' is already registered." });

            // ── 3. Create Identity user (UserName = LoginId) ──────────
            var identityUser = new IdentityUser
            {
                UserName       = dto.LoginId,
                Email          = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email,
                EmailConfirmed = true
            };

            var createResult = await _userManager.CreateAsync(identityUser, dto.Password);
            if (!createResult.Succeeded)
                return BadRequest(new
                {
                    message = string.Join("; ", createResult.Errors.Select(e => e.Description))
                });

            // ── 4. Create Person record ───────────────────────────────
            var person = new Person
            {
                PersonId       = Guid.NewGuid(),
                FullName       = dto.FullName.Trim(),
                Phone          = dto.Phone?.Trim(),
                Email          = dto.Email?.Trim(),
                Gender         = dto.Gender?.Trim(),
                DateOfBirth    = dto.DateOfBirth,
                MaritalStatus  = dto.MaritalStatus?.Trim(),
                LoginId        = dto.LoginId.Trim(),
                IdentityUserId = identityUser.Id,
                CreatedDate    = DateTime.UtcNow
            };

            // ── 5. Add addresses ──────────────────────────────────────
            if (dto.CurrentAddress != null)
                person.Addresses.Add(MapAddress(dto.CurrentAddress, "Current", person.PersonId));

            if (dto.PermanentAddress != null)
                person.Addresses.Add(MapAddress(dto.PermanentAddress, "Permanent", person.PersonId));

            _db.Persons.Add(person);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (Exception)
            {
                // Roll back the Identity user so we don't leave orphaned accounts
                await _userManager.DeleteAsync(identityUser);
                throw;
            }

            var created = await _db.Persons
                .Include(p => p.Addresses)
                .FirstOrDefaultAsync(p => p.PersonId == person.PersonId);

            return CreatedAtAction(nameof(GetById), new { id = person.PersonId }, MapToDto(created!));
        }

        // ── POST /api/persons/{id}/upload-photo ───────────────────────────────

        [HttpPost("{id:guid}/upload-photo")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadPhoto(Guid id, IFormFile photo)
        {
            var person = await _db.Persons.FindAsync(id);
            if (person == null)
                return NotFound(new { message = $"Person {id} not found." });

            if (photo == null || photo.Length == 0)
                return BadRequest(new { message = "No file uploaded." });

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext))
                return BadRequest(new { message = "Only jpg, jpeg, png, webp files are allowed." });

            if (photo.Length > 5 * 1024 * 1024)
                return BadRequest(new { message = "File size must be under 5 MB." });

            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "persons");
            Directory.CreateDirectory(uploadsDir);

            // Delete old photo if present
            if (!string.IsNullOrWhiteSpace(person.ProfilePhotoUrl))
            {
                var oldFile = Path.Combine(_env.WebRootPath,
                    person.ProfilePhotoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(oldFile))
                    System.IO.File.Delete(oldFile);
            }

            var fileName = $"person_{id:N}_{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
                await photo.CopyToAsync(stream);

            person.ProfilePhotoUrl = $"/uploads/persons/{fileName}";
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message        = "Photo uploaded successfully.",
                profilePhotoUrl = person.ProfilePhotoUrl,
                fullUrl        = $"{Request.Scheme}://{Request.Host}{person.ProfilePhotoUrl}"
            });
        }

        // ── DELETE /api/persons/{id} ──────────────────────────────────────────

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var person = await _db.Persons.FindAsync(id);
            if (person == null)
                return NotFound(new { message = $"Person {id} not found." });

            // Remove Identity user
            var identityUser = await _userManager.FindByIdAsync(person.IdentityUserId);
            if (identityUser != null)
                await _userManager.DeleteAsync(identityUser);

            // Remove photo file if present
            if (!string.IsNullOrWhiteSpace(person.ProfilePhotoUrl))
            {
                var filePath = Path.Combine(_env.WebRootPath,
                    person.ProfilePhotoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);
            }

            _db.Persons.Remove(person);
            await _db.SaveChangesAsync();

            return Ok(new { message = $"Person '{person.FullName}' deleted." });
        }

        // ── HELPERS ───────────────────────────────────────────────────────────

        private static PersonAddress MapAddress(AddressDto src, string type, Guid personId) =>
            new PersonAddress
            {
                AddressId   = Guid.NewGuid(),
                PersonId    = personId,
                AddressType = type,
                AddressLine = src.AddressLine?.Trim(),
                Country     = src.Country?.Trim(),
                Province    = src.Province?.Trim(),
                District    = src.District?.Trim(),
                City        = src.City?.Trim(),
                PostalCode  = src.PostalCode?.Trim()
            };

        private static PersonDto MapToDto(Person p) => new PersonDto
        {
            PersonId        = p.PersonId,
            FullName        = p.FullName,
            Phone           = p.Phone,
            Email           = p.Email,
            Gender          = p.Gender,
            DateOfBirth     = p.DateOfBirth,
            MaritalStatus   = p.MaritalStatus,
            ProfilePhotoUrl = p.ProfilePhotoUrl,
            LoginId         = p.LoginId,
            CreatedDate     = p.CreatedDate,
            Addresses       = p.Addresses.Select(a => new PersonAddressDto
            {
                AddressId   = a.AddressId,
                AddressType = a.AddressType,
                AddressLine = a.AddressLine,
                Country     = a.Country,
                Province    = a.Province,
                District    = a.District,
                City        = a.City,
                PostalCode  = a.PostalCode
            })
        };
    }
}
