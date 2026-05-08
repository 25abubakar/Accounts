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
        private readonly ApplicationDbContext      _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IWebHostEnvironment       _env;

        public PersonsController(ApplicationDbContext db, UserManager<IdentityUser> userManager, IWebHostEnvironment env)
        {
            _db = db; _userManager = userManager; _env = env;
        }

        // ── DTOs ─────────────────────────────────────────────────────────────

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

        // ── GET /api/persons/profiles ─────────────────────────────────────────

        [HttpGet("profiles")]
        public async Task<IActionResult> GetProfiles()
        {
            var orgNodes = await _db.OrganizationTree.AsNoTracking().ToListAsync();
            var persons  = await _db.Persons.AsNoTracking()
                .Include(p => p.Addresses)
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy)
                .OrderByDescending(p => p.CreatedDate)
                .ToListAsync();
            return Ok(persons.Select(p => MapToProfile(p, orgNodes)));
        }

        // ── GET /api/persons/{id}/profile ─────────────────────────────────────

        [HttpGet("{id:guid}/profile")]
        public async Task<IActionResult> GetProfile(Guid id)
        {
            var person = await _db.Persons.AsNoTracking()
                .Include(p => p.Addresses)
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy)
                .FirstOrDefaultAsync(p => p.PersonId == id);
            if (person == null) return NotFound(new { message = $"Person {id} not found." });
            var orgNodes = await _db.OrganizationTree.AsNoTracking().ToListAsync();
            return Ok(MapToProfile(person, orgNodes));
        }

        // ── GET /api/persons/org-tree ─────────────────────────────────────────

        [HttpGet("org-tree")]
        public async Task<IActionResult> GetOrgTree()
        {
            var all      = await _db.OrganizationTree.OrderBy(n => n.Name).ToListAsync();
            var byParent = all.ToLookup(n => n.ParentId);
            var roots = byParent[null].Select(c => new
            {
                id = c.Id, name = c.Name, label = c.Label, flagUrl = c.FlagUrl,
                children = byParent[c.Id].Select(co => new
                {
                    id = co.Id, name = co.Name, label = co.Label,
                    loginPrefix = ResolveCompanyPrefix(co),
                    children = byParent[co.Id].Select(b => new { id = b.Id, name = b.Name, label = b.Label }).ToList()
                }).ToList()
            }).ToList();
            return Ok(roots);
        }

        // ── GET /api/persons/preview-login-id?branchId=5 ─────────────────────

        [HttpGet("preview-login-id")]
        public async Task<IActionResult> PreviewLoginId([FromQuery] int branchId)
        {
            if (branchId <= 0) return BadRequest(new { message = "branchId is required." });
            var all    = await _db.OrganizationTree.ToListAsync();
            var branch = all.FirstOrDefault(n => n.Id == branchId);
            if (branch == null) return NotFound(new { message = $"Branch {branchId} not found." });
            var company = branch.ParentId.HasValue ? all.FirstOrDefault(n => n.Id == branch.ParentId) : null;
            var loginId = await GenerateLoginIdAsync(company ?? branch, all);
            return Ok(new { loginId, companyName = company?.Name ?? branch.Name });
        }

        // ── GET /api/persons ──────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var persons  = await _db.Persons.Include(p => p.Addresses).Include(p => p.Staff)
                .OrderByDescending(p => p.CreatedDate).ToListAsync();
            var orgNodes = await _db.OrganizationTree.ToListAsync();
            return Ok(persons.Select(p => MapToDto(p, orgNodes)));
        }

        // ── GET /api/persons/unassigned ───────────────────────────────────────

        [HttpGet("unassigned")]
        public async Task<IActionResult> GetUnassigned()
        {
            var persons  = await _db.Persons.Include(p => p.Addresses).Include(p => p.Staff)
                .Where(p => p.Staff == null).OrderByDescending(p => p.CreatedDate).ToListAsync();
            var orgNodes = await _db.OrganizationTree.ToListAsync();
            return Ok(persons.Select(p => MapToDto(p, orgNodes)));
        }

        // ── GET /api/persons/{id} ─────────────────────────────────────────────

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var person = await _db.Persons.Include(p => p.Addresses).Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.PersonId == id);
            if (person == null) return NotFound(new { message = $"Person {id} not found." });
            var orgNodes = await _db.OrganizationTree.ToListAsync();
            return Ok(MapToDto(person, orgNodes));
        }

        // ── POST /api/persons/register-raw ────────────────────────────────────

        [HttpPost("register-raw")]
        public async Task<IActionResult> RegisterRaw()
        {
            using var reader = new System.IO.StreamReader(Request.Body);
            return Ok(new { received = await reader.ReadToEndAsync() });
        }

        // ── POST /api/persons/register ────────────────────────────────────────

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterPersonDto? dto)
        {
            if (dto is null) return BadRequest(new { message = "Request body missing. Use /register-raw to debug." });
            if (string.IsNullOrWhiteSpace(dto.FullName)) return BadRequest(new { message = "FullName is required." });
            if (string.IsNullOrWhiteSpace(dto.Password)) return BadRequest(new { message = "Password is required." });
            if (dto.BranchId <= 0) return BadRequest(new { message = "BranchId is required." });

            var orgNodes = await _db.OrganizationTree.ToListAsync();
            var branch   = orgNodes.FirstOrDefault(n => n.Id == dto.BranchId);
            if (branch == null) return BadRequest(new { message = $"Branch {dto.BranchId} not found." });
            var company = branch.ParentId.HasValue ? orgNodes.FirstOrDefault(n => n.Id == branch.ParentId) : null;

            var loginId = await GenerateLoginIdAsync(company ?? branch, orgNodes);

            if (!string.IsNullOrWhiteSpace(dto.Email) && await _userManager.FindByEmailAsync(dto.Email) != null)
                return Conflict(new { message = $"Email '{dto.Email}' is already registered." });

            var identityUser = new IdentityUser
            {
                UserName = loginId,
                Email    = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email,
                EmailConfirmed = true
            };
            var createResult = await _userManager.CreateAsync(identityUser, dto.Password);
            if (!createResult.Succeeded)
                return BadRequest(new { message = string.Join("; ", createResult.Errors.Select(e => e.Description)) });

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

            var ca = dto.CurrentAddress;
            var pa = dto.PermanentAddress;
            if (ca != null) person.Addresses.Add(BuildAddress(ca, "Current", person.PersonId));
            if (pa != null && !AddressesAreEqual(ca, pa)) person.Addresses.Add(BuildAddress(pa, "Permanent", person.PersonId));

            _db.Persons.Add(person);
            try { await _db.SaveChangesAsync(); }
            catch { await _userManager.DeleteAsync(identityUser); throw; }

            var created = await _db.Persons.Include(p => p.Addresses).Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.PersonId == person.PersonId);
            return CreatedAtAction(nameof(GetById), new { id = person.PersonId }, MapToDto(created!, orgNodes));
        }

        // ── PUT /api/persons/{id} ─────────────────────────────────────────────

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePersonDto? dto)
        {
            if (dto is null) return BadRequest(new { message = "Request body missing." });
            if (string.IsNullOrWhiteSpace(dto.FullName)) return BadRequest(new { message = "FullName is required." });

            var person = await _db.Persons.Include(p => p.Addresses).Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.PersonId == id);
            if (person == null) return NotFound(new { message = $"Person {id} not found." });

            person.FullName      = dto.FullName.Trim();
            person.Phone         = dto.Phone?.Trim();
            person.Email         = dto.Email?.Trim();
            person.Gender        = dto.Gender?.Trim();
            person.DateOfBirth   = dto.DateOfBirth;
            person.MaritalStatus = dto.MaritalStatus?.Trim();

            if (!string.IsNullOrWhiteSpace(dto.Email))
            {
                var iu = await _userManager.FindByIdAsync(person.IdentityUserId);
                if (iu != null && !string.Equals(iu.Email, dto.Email.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    iu.Email = dto.Email.Trim(); iu.NormalizedEmail = dto.Email.Trim().ToUpperInvariant();
                    await _userManager.UpdateAsync(iu);
                }
            }

            UpsertAddress(person, "Current",   dto.CurrentAddress);
            UpsertAddress(person, "Permanent", dto.PermanentAddress);
            await _db.SaveChangesAsync();

            var orgNodes = await _db.OrganizationTree.ToListAsync();
            var updated  = await _db.Persons.Include(p => p.Addresses).Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.PersonId == id);
            return Ok(MapToDto(updated!, orgNodes));
        }

        // ── POST /api/persons/{id}/upload-photo ───────────────────────────────

        [HttpPost("{id:guid}/upload-photo")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadPhoto(Guid id, IFormFile photo)
        {
            var person = await _db.Persons.FindAsync(id);
            if (person == null) return NotFound(new { message = $"Person {id} not found." });
            if (photo == null || photo.Length == 0) return BadRequest(new { message = "No file uploaded." });

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext)) return BadRequest(new { message = "Only jpg, jpeg, png, webp allowed." });
            if (photo.Length > 5 * 1024 * 1024) return BadRequest(new { message = "Max 5 MB." });

            var dir = Path.Combine(_env.WebRootPath, "uploads", "persons");
            Directory.CreateDirectory(dir);

            if (!string.IsNullOrWhiteSpace(person.ProfilePhotoUrl))
            {
                var old = Path.Combine(_env.WebRootPath, person.ProfilePhotoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(old)) System.IO.File.Delete(old);
            }

            var fileName = $"person_{id:N}_{Guid.NewGuid():N}{ext}";
            using (var s = new FileStream(Path.Combine(dir, fileName), FileMode.Create))
                await photo.CopyToAsync(s);

            person.ProfilePhotoUrl = $"/uploads/persons/{fileName}";
            await _db.SaveChangesAsync();
            return Ok(new { photoUrl = person.ProfilePhotoUrl, fullUrl = $"{Request.Scheme}://{Request.Host}{person.ProfilePhotoUrl}" });
        }

        // ── DELETE /api/persons/{id} ──────────────────────────────────────────

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var person = await _db.Persons.FindAsync(id);
            if (person == null) return NotFound(new { message = $"Person {id} not found." });

            var iu = await _userManager.FindByIdAsync(person.IdentityUserId);
            if (iu != null) await _userManager.DeleteAsync(iu);

            if (!string.IsNullOrWhiteSpace(person.ProfilePhotoUrl))
            {
                var fp = Path.Combine(_env.WebRootPath, person.ProfilePhotoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(fp)) System.IO.File.Delete(fp);
            }

            _db.Persons.Remove(person);
            await _db.SaveChangesAsync();
            return Ok(new { message = $"Person '{person.FullName}' deleted." });
        }

        // ── HELPERS ───────────────────────────────────────────────────────────

        private async Task<string> GenerateLoginIdAsync(OrganizationTree node, List<OrganizationTree> all)
        {
            var prefix  = ResolveCompanyPrefix(node);
            var existing = await _db.Persons.CountAsync(p => p.LoginId.StartsWith(prefix));
            string id; int seq = 10001 + existing;
            do { id = $"{prefix}{seq}"; seq++; }
            while (await _db.Persons.AnyAsync(p => p.LoginId == id));
            return id;
        }

        private static string ResolveCompanyPrefix(OrganizationTree node)
        {
            if (!string.IsNullOrWhiteSpace(node.Code)) return node.Code.ToUpper().Trim();
            var words = node.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 0).ToArray();
            if (words.Length >= 2) return string.Concat(words.Take(4).Select(w => char.ToUpper(w[0])));
            return node.Name.Length >= 2 ? node.Name[..Math.Min(3, node.Name.Length)].ToUpper() : node.Name.ToUpper();
        }

        private static PersonAddress BuildAddress(AddressDto src, string type, Guid personId) =>
            new PersonAddress
            {
                AddressId = Guid.NewGuid(), PersonId = personId, AddressType = type,
                AddressLine = src.AddressLine?.Trim(), Country = src.Country?.Trim(),
                Province = src.Province?.Trim(), District = src.District?.Trim(),
                City = src.City?.Trim(), PostalCode = src.PostalCode?.Trim()
            };

        private void UpsertAddress(Person person, string type, AddressDto? dto)
        {
            if (dto == null) return;
            var ex = person.Addresses.FirstOrDefault(a => a.AddressType == type);
            if (ex != null)
            {
                ex.AddressLine = dto.AddressLine?.Trim(); ex.Country   = dto.Country?.Trim();
                ex.Province    = dto.Province?.Trim();    ex.District  = dto.District?.Trim();
                ex.City        = dto.City?.Trim();        ex.PostalCode = dto.PostalCode?.Trim();
            }
            else
            {
                person.Addresses.Add(new PersonAddress
                {
                    AddressId = Guid.NewGuid(), PersonId = person.PersonId, AddressType = type,
                    AddressLine = dto.AddressLine?.Trim(), Country = dto.Country?.Trim(),
                    Province = dto.Province?.Trim(), District = dto.District?.Trim(),
                    City = dto.City?.Trim(), PostalCode = dto.PostalCode?.Trim()
                });
            }
        }

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

        private static AddressResponseDto ToAddressResponse(PersonAddress? a) =>
            a == null ? new AddressResponseDto() : new AddressResponseDto
            {
                AddressLine = a.AddressLine, Country = a.Country, Province = a.Province,
                District = a.District, City = a.City, PostalCode = a.PostalCode
            };

        private static PersonDto MapToDto(Person p, List<OrganizationTree> org)
        {
            var branch  = p.BranchId.HasValue ? org.FirstOrDefault(n => n.Id == p.BranchId) : null;
            var company = branch?.ParentId.HasValue == true ? org.FirstOrDefault(n => n.Id == branch.ParentId) : null;
            var country = company?.ParentId.HasValue == true ? org.FirstOrDefault(n => n.Id == company.ParentId) : null;

            var cur  = p.Addresses.FirstOrDefault(a => a.AddressType == "Current");
            var perm = p.Addresses.FirstOrDefault(a => a.AddressType == "Permanent");
            var curDto  = ToAddressResponse(cur);
            var permDto = ToAddressResponse(perm ?? cur);

            bool same = perm == null ||
                (string.Equals(curDto.AddressLine, permDto.AddressLine, StringComparison.OrdinalIgnoreCase)
                 && string.Equals(curDto.Country,  permDto.Country,     StringComparison.OrdinalIgnoreCase)
                 && string.Equals(curDto.City,     permDto.City,        StringComparison.OrdinalIgnoreCase));

            return new PersonDto
            {
                PersonId = p.PersonId, LoginId = p.LoginId, FullName = p.FullName,
                Gender = p.Gender, DateOfBirth = p.DateOfBirth, MaritalStatus = p.MaritalStatus,
                Phone = p.Phone, Email = p.Email, PhotoUrl = p.ProfilePhotoUrl,
                IsHired = p.Staff != null, RegisteredAt = p.CreatedDate.ToString("o"),
                BranchId = p.BranchId, BranchName = branch?.Name, CompanyName = company?.Name, CountryName = country?.Name,
                CurrentAddress = curDto, PermanentAddress = permDto, SameAddress = same
            };
        }

        private static PersonProfileDto MapToProfile(Person p, List<OrganizationTree> org)
        {
            var branch  = p.BranchId.HasValue ? org.FirstOrDefault(n => n.Id == p.BranchId) : null;
            var company = branch?.ParentId.HasValue == true ? org.FirstOrDefault(n => n.Id == branch.ParentId) : null;
            var country = company?.ParentId.HasValue == true ? org.FirstOrDefault(n => n.Id == company.ParentId) : null;

            var parts    = p.FullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var initials = parts.Length >= 2
                ? $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[1][0])}"
                : p.FullName.Length >= 1 ? char.ToUpper(p.FullName[0]).ToString() : "?";

            var cur  = p.Addresses.FirstOrDefault(a => a.AddressType == "Current");
            var perm = p.Addresses.FirstOrDefault(a => a.AddressType == "Permanent");

            return new PersonProfileDto
            {
                PersonId = p.PersonId, LoginId = p.LoginId, FullName = p.FullName, Initials = initials,
                Gender = p.Gender, DateOfBirth = p.DateOfBirth, MaritalStatus = p.MaritalStatus,
                Phone = p.Phone, Email = p.Email, PhotoUrl = p.ProfilePhotoUrl, RegisteredAt = p.CreatedDate,
                BranchId = p.BranchId, BranchName = branch?.Name, CompanyName = company?.Name,
                CountryName = country?.Name, CountryFlag = country?.FlagUrl,
                IsHired = p.Staff != null, StaffId = p.Staff?.StaffId, JoiningDate = p.Staff?.JoiningDate,
                VacancyId = p.Staff?.VacancyId, VacancyCode = p.Staff?.Vacancy?.VacancyCode,
                JobTitle = p.Staff?.Vacancy?.JobTitle, Department = p.Staff?.Vacancy?.Department,
                CurrentAddress   = ToAddressResponse(cur),
                PermanentAddress = ToAddressResponse(perm ?? cur)
            };
        }
    }
}
