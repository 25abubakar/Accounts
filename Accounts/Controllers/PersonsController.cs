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
        private readonly ApplicationDbContext     _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IWebHostEnvironment      _env;

        public PersonsController(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            IWebHostEnvironment env)
        {
            _db          = db;
            _userManager = userManager;
            _env         = env;
        }

        // ═══════════════════════════════════════════════════════════════
        // DTOs
        // ═══════════════════════════════════════════════════════════════

        public class AddressDto
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
        {            // ── Personal Info ─────────────────────────────────────────
            public string    FullName      { get; set; } = string.Empty;
            public string?   Phone         { get; set; }
            public string?   Email         { get; set; }
            public string?   Gender        { get; set; }
            public DateTime? DateOfBirth   { get; set; }
            public string?   MaritalStatus { get; set; }

            // ── Org placement (required) ──────────────────────────────
            /// <summary>ID of the Branch node in OrganizationTree where this person is registered</summary>
            public int BranchId { get; set; }

            // ── System Access ─────────────────────────────────────────
            public string Password { get; set; } = string.Empty;

            // ── Addresses ─────────────────────────────────────────────
            // Accept as raw JsonElement so any shape (object, string, null) is tolerated
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

            // Org placement
            public int?    BranchId    { get; set; }
            public string? BranchName  { get; set; }
            public string? CompanyName { get; set; }
            public string? CountryName { get; set; }

            // Addresses — flat named fields instead of a raw array
            public PersonAddressDto? CurrentAddress   { get; set; }
            public PersonAddressDto? PermanentAddress { get; set; }

            /// <summary>
            /// True when both addresses exist and are identical.
            /// Frontend can use this to show "Same as current address" instead of repeating.
            /// </summary>
            public bool SameAddress { get; set; }
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

        // ═══════════════════════════════════════════════════════════════
        // GET /api/persons/org-tree
        // Returns the full org tree grouped for cascading dropdowns:
        // Countries → Companies → Branches
        // ═══════════════════════════════════════════════════════════════

        [HttpGet("org-tree")]
        public async Task<IActionResult> GetOrgTree()
        {
            var all = await _db.OrganizationTree.OrderBy(n => n.Name).ToListAsync();

            // Build a lookup by parentId
            var byParent = all.ToLookup(n => n.ParentId);

            // Root nodes (no parent) = Countries / top-level groups
            var roots = byParent[null].Select(country => new
            {
                id       = country.Id,
                name     = country.Name,
                code     = country.Code,
                label    = country.Label,
                flagUrl  = country.FlagUrl,
                children = byParent[country.Id].Select(company => new
                {
                    id       = company.Id,
                    name     = company.Name,
                    code     = company.Code,
                    label    = company.Label,
                    loginPrefix = ResolveCompanyPrefix(company),
                    children = byParent[company.Id].Select(branch => new
                    {
                        id    = branch.Id,
                        name  = branch.Name,
                        code  = branch.Code,
                        label = branch.Label
                    }).ToList()
                }).ToList()
            }).ToList();

            return Ok(roots);
        }

        // ═══════════════════════════════════════════════════════════════
        // GET /api/persons
        // ═══════════════════════════════════════════════════════════════

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var persons = await _db.Persons
                .Include(p => p.Addresses)
                .OrderByDescending(p => p.CreatedDate)
                .ToListAsync();

            var all = await _db.OrganizationTree.ToListAsync();
            return Ok(persons.Select(p => MapToDto(p, all)));
        }

        // ═══════════════════════════════════════════════════════════════
        // GET /api/persons/{id}
        // ═══════════════════════════════════════════════════════════════

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var person = await _db.Persons
                .Include(p => p.Addresses)
                .FirstOrDefaultAsync(p => p.PersonId == id);

            if (person == null)
                return NotFound(new { message = $"Person {id} not found." });

            var all = await _db.OrganizationTree.ToListAsync();
            return Ok(MapToDto(person, all));
        }

        // ═══════════════════════════════════════════════════════════════
        // GET /api/persons/preview-login-id?branchId=5
        // Returns the LoginId that WILL be assigned if you register now.
        // Frontend shows this on the "System Access" step.
        // ═══════════════════════════════════════════════════════════════

        [HttpGet("preview-login-id")]
        public async Task<IActionResult> PreviewLoginId([FromQuery] int branchId)
        {
            if (branchId <= 0)
                return BadRequest(new { message = "branchId is required." });

            var all = await _db.OrganizationTree.ToListAsync();
            var branch  = all.FirstOrDefault(n => n.Id == branchId);
            if (branch == null)
                return NotFound(new { message = $"Branch {branchId} not found." });

            var company = branch.ParentId.HasValue ? all.FirstOrDefault(n => n.Id == branch.ParentId) : null;
            var loginId = await GenerateLoginIdAsync(company ?? branch, all);

            return Ok(new
            {
                loginId,
                prefix      = ResolveCompanyPrefix(company ?? branch),
                branchName  = branch.Name,
                companyName = company?.Name
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // POST /api/persons/register-raw  (debug — echoes raw body)
        // ═══════════════════════════════════════════════════════════════

        [HttpPost("register-raw")]
        public async Task<IActionResult> RegisterRaw()
        {
            using var reader = new System.IO.StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            return Ok(new { received = body });
        }

        // ═══════════════════════════════════════════════════════════════
        // POST /api/persons/register
        // ═══════════════════════════════════════════════════════════════

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterPersonDto? dto)
        {
            if (dto is null)
                return BadRequest(new { message = "Request body is missing or malformed. Use POST /api/persons/register-raw to inspect what was received." });

            if (string.IsNullOrWhiteSpace(dto.FullName))
                return BadRequest(new { message = "FullName is required." });

            if (string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest(new { message = "Password is required." });

            if (dto.BranchId <= 0)
                return BadRequest(new { message = "BranchId is required. Select a branch from the organization tree." });

            // ── 1. Resolve org chain: Branch → Company → Country ─────
            var all = await _db.OrganizationTree.ToListAsync();

            var branch  = all.FirstOrDefault(n => n.Id == dto.BranchId);
            if (branch == null)
                return BadRequest(new { message = $"Branch with ID {dto.BranchId} not found in the organization tree." });

            var company = branch.ParentId.HasValue ? all.FirstOrDefault(n => n.Id == branch.ParentId) : null;
            var country = company?.ParentId.HasValue == true ? all.FirstOrDefault(n => n.Id == company.ParentId) : null;

            // ── 2. Generate LoginId from company code ─────────────────
            //    Format: {CompanyCode}{5-digit-sequence}
            //    e.g.  LT10291,  SA10021
            var loginId = await GenerateLoginIdAsync(company ?? branch, all);

            // ── 3. Check email uniqueness (if provided) ───────────────
            if (!string.IsNullOrWhiteSpace(dto.Email) &&
                await _userManager.FindByEmailAsync(dto.Email) != null)
                return Conflict(new { message = $"Email '{dto.Email}' is already registered." });

            // ── 4. Create Identity user (UserName = LoginId) ──────────
            var identityUser = new IdentityUser
            {
                UserName       = loginId,
                Email          = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email,
                EmailConfirmed = true
            };

            var createResult = await _userManager.CreateAsync(identityUser, dto.Password);
            if (!createResult.Succeeded)
                return BadRequest(new
                {
                    message = string.Join("; ", createResult.Errors.Select(e => e.Description))
                });

            // ── 5. Create Person record ───────────────────────────────
            var person = new Person
            {
                PersonId       = Guid.NewGuid(),
                FullName       = dto.FullName.Trim(),
                Phone          = dto.Phone?.Trim(),
                Email          = dto.Email?.Trim(),
                Gender         = dto.Gender?.Trim(),
                DateOfBirth    = dto.DateOfBirth,
                MaritalStatus  = dto.MaritalStatus?.Trim(),
                LoginId        = loginId,
                IdentityUserId = identityUser.Id,
                BranchId       = dto.BranchId,
                CreatedDate    = DateTime.UtcNow
            };

            // ── 6. Add addresses ──────────────────────────────────────
            var currentAddr   = dto.CurrentAddress;
            var permanentAddr = dto.PermanentAddress;

            if (currentAddr != null)
                person.Addresses.Add(MapAddress(currentAddr, "Current", person.PersonId));

            // Only save Permanent separately if it differs from Current.
            // If both are identical (or Permanent is null), mark Current as both.
            if (permanentAddr != null && !AddressesAreEqual(currentAddr, permanentAddr))
                person.Addresses.Add(MapAddress(permanentAddr, "Permanent", person.PersonId));

            _db.Persons.Add(person);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (Exception)
            {
                await _userManager.DeleteAsync(identityUser);
                throw;
            }

            var created = await _db.Persons
                .Include(p => p.Addresses)
                .FirstOrDefaultAsync(p => p.PersonId == person.PersonId);

            return CreatedAtAction(nameof(GetById), new { id = person.PersonId }, MapToDto(created!, all));
        }

        // ═══════════════════════════════════════════════════════════════
        // PUT /api/persons/{id}
        // Updates personal info + both addresses in one call.
        // ═══════════════════════════════════════════════════════════════

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePersonDto? dto)
        {
            if (dto is null)
                return BadRequest(new { message = "Request body is missing." });

            if (string.IsNullOrWhiteSpace(dto.FullName))
                return BadRequest(new { message = "FullName is required." });

            var person = await _db.Persons
                .Include(p => p.Addresses)
                .FirstOrDefaultAsync(p => p.PersonId == id);

            if (person == null)
                return NotFound(new { message = $"Person {id} not found." });

            // ── 1. Update personal fields ─────────────────────────────
            person.FullName      = dto.FullName.Trim();
            person.Phone         = dto.Phone?.Trim();
            person.Email         = dto.Email?.Trim();
            person.Gender        = dto.Gender?.Trim();
            person.DateOfBirth   = dto.DateOfBirth;
            person.MaritalStatus = dto.MaritalStatus?.Trim();

            // ── 2. Update email on Identity user too ──────────────────
            if (!string.IsNullOrWhiteSpace(dto.Email))
            {
                var identityUser = await _userManager.FindByIdAsync(person.IdentityUserId);
                if (identityUser != null && identityUser.Email != dto.Email.Trim())
                {
                    identityUser.Email          = dto.Email.Trim();
                    identityUser.NormalizedEmail = dto.Email.Trim().ToUpperInvariant();
                    await _userManager.UpdateAsync(identityUser);
                }
            }

            // ── 3. Upsert addresses ───────────────────────────────────
            UpsertAddress(person, "Current",   dto.CurrentAddress);
            UpsertAddress(person, "Permanent", dto.PermanentAddress);

            await _db.SaveChangesAsync();

            var all = await _db.OrganizationTree.ToListAsync();
            var updated = await _db.Persons
                .Include(p => p.Addresses)
                .FirstOrDefaultAsync(p => p.PersonId == id);

            return Ok(MapToDto(updated!, all));
        }

        // ── Upsert helper: update existing address row or insert new one ──
        private void UpsertAddress(Person person, string addressType, AddressDto? dto)
        {
            if (dto == null) return;

            var existing = person.Addresses.FirstOrDefault(a => a.AddressType == addressType);
            if (existing != null)
            {
                // Update in place
                existing.AddressLine = dto.AddressLine?.Trim();
                existing.Country     = dto.Country?.Trim();
                existing.Province    = dto.Province?.Trim();
                existing.District    = dto.District?.Trim();
                existing.City        = dto.City?.Trim();
                existing.PostalCode  = dto.PostalCode?.Trim();
            }
            else
            {
                // Insert new row
                person.Addresses.Add(new PersonAddress
                {
                    AddressId   = Guid.NewGuid(),
                    PersonId    = person.PersonId,
                    AddressType = addressType,
                    AddressLine = dto.AddressLine?.Trim(),
                    Country     = dto.Country?.Trim(),
                    Province    = dto.Province?.Trim(),
                    District    = dto.District?.Trim(),
                    City        = dto.City?.Trim(),
                    PostalCode  = dto.PostalCode?.Trim()
                });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // POST /api/persons/{id}/upload-photo
        // ═══════════════════════════════════════════════════════════════

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
                message         = "Photo uploaded successfully.",
                profilePhotoUrl = person.ProfilePhotoUrl,
                fullUrl         = $"{Request.Scheme}://{Request.Host}{person.ProfilePhotoUrl}"
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // DELETE /api/persons/{id}
        // ═══════════════════════════════════════════════════════════════

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var person = await _db.Persons.FindAsync(id);
            if (person == null)
                return NotFound(new { message = $"Person {id} not found." });

            var identityUser = await _userManager.FindByIdAsync(person.IdentityUserId);
            if (identityUser != null)
                await _userManager.DeleteAsync(identityUser);

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

        // ═══════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Generates a LoginId in the format {CompanyCode}{5-digit-sequence}.
        /// Company code is taken from the Code field of the company node,
        /// or derived from initials of the company name.
        /// Examples:  LT10001, LT10002 ... SA10001, SA10002
        /// </summary>
        private async Task<string> GenerateLoginIdAsync(
            OrganizationTree companyNode,
            List<OrganizationTree> all)
        {
            // Resolve the company code (initials of name if Code field is empty)
            string prefix = ResolveCompanyPrefix(companyNode);

            // Count existing persons whose LoginId starts with this prefix
            // to determine the next sequence number
            int existing = await _db.Persons
                .CountAsync(p => p.LoginId.StartsWith(prefix));

            string loginId;
            int seq = 10001 + existing;   // starts at 10001 so first ID is e.g. LT10001

            // Ensure uniqueness (handles gaps / deletions)
            do
            {
                loginId = $"{prefix}{seq}";
                seq++;
            }
            while (await _db.Persons.AnyAsync(p => p.LoginId == loginId));

            return loginId;
        }

        /// <summary>
        /// Returns the company prefix for LoginId generation.
        /// Uses stored Code if set, otherwise derives initials from the name.
        /// "Lal Technology"              → LT
        /// "Sierra Allergy Asthma Center"→ SA  (first two initials)
        /// "Pakistan"                    → PK
        /// </summary>
        private static string ResolveCompanyPrefix(OrganizationTree node)
        {
            // Use stored Code field if available (already set by admin)
            if (!string.IsNullOrWhiteSpace(node.Code))
                return node.Code.ToUpper().Trim();

            // Derive from name initials
            var words = node.Name
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 0)
                .ToArray();

            if (words.Length >= 2)
                // Take first letter of each word, max 4 chars
                return string.Concat(words.Take(4).Select(w => char.ToUpper(w[0])));

            // Single word — take first 2-3 chars
            return node.Name.Length >= 2
                ? node.Name[..Math.Min(3, node.Name.Length)].ToUpper()
                : node.Name.ToUpper();
        }

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

        private static PersonDto MapToDto(Person p, List<OrganizationTree> all)
        {
            // Resolve org chain from BranchId
            OrganizationTree? branch  = p.BranchId.HasValue ? all.FirstOrDefault(n => n.Id == p.BranchId) : null;
            OrganizationTree? company = branch?.ParentId.HasValue == true ? all.FirstOrDefault(n => n.Id == branch.ParentId) : null;
            OrganizationTree? country = company?.ParentId.HasValue == true ? all.FirstOrDefault(n => n.Id == company.ParentId) : null;

            // Map addresses to named fields
            var currentRow   = p.Addresses.FirstOrDefault(a => a.AddressType == "Current");
            var permanentRow = p.Addresses.FirstOrDefault(a => a.AddressType == "Permanent");

            var currentDto   = currentRow   != null ? MapAddressToDto(currentRow)   : null;
            var permanentDto = permanentRow != null ? MapAddressToDto(permanentRow) : null;

            // Detect same address: if only Current exists (Permanent was not saved because
            // they were identical), or if both exist but all fields match.
            bool sameAddress = permanentDto == null
                ? currentDto != null   // only current saved → they were the same
                : AddressDtosAreEqual(currentDto, permanentDto);

            // If same, expose permanent as a copy of current so frontend always has both fields
            if (sameAddress && permanentDto == null && currentDto != null)
                permanentDto = new PersonAddressDto
                {
                    AddressId   = currentDto.AddressId,
                    AddressType = "Permanent",
                    AddressLine = currentDto.AddressLine,
                    Country     = currentDto.Country,
                    Province    = currentDto.Province,
                    District    = currentDto.District,
                    City        = currentDto.City,
                    PostalCode  = currentDto.PostalCode
                };

            return new PersonDto
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
                BranchId        = p.BranchId,
                BranchName      = branch?.Name,
                CompanyName     = company?.Name,
                CountryName     = country?.Name,
                CurrentAddress   = currentDto,
                PermanentAddress = permanentDto,
                SameAddress      = sameAddress
            };
        }

        private static PersonAddressDto MapAddressToDto(PersonAddress a) => new PersonAddressDto
        {
            AddressId   = a.AddressId,
            AddressType = a.AddressType,
            AddressLine = a.AddressLine,
            Country     = a.Country,
            Province    = a.Province,
            District    = a.District,
            City        = a.City,
            PostalCode  = a.PostalCode
        };

        /// <summary>
        /// Compares two AddressDto (from request) field by field.
        /// Used to avoid saving duplicate Permanent row when it equals Current.
        /// </summary>
        private static bool AddressesAreEqual(AddressDto? a, AddressDto? b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            return string.Equals(a.AddressLine, b.AddressLine, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Country,     b.Country,     StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Province,    b.Province,    StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.District,    b.District,    StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.City,        b.City,        StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.PostalCode,  b.PostalCode,  StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Compares two PersonAddressDto (from DB) field by field.
        /// Used in MapToDto to set SameAddress flag.
        /// </summary>
        private static bool AddressDtosAreEqual(PersonAddressDto? a, PersonAddressDto? b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            return string.Equals(a.AddressLine, b.AddressLine, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Country,     b.Country,     StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Province,    b.Province,    StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.District,    b.District,    StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.City,        b.City,        StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.PostalCode,  b.PostalCode,  StringComparison.OrdinalIgnoreCase);
        }
    }
}
