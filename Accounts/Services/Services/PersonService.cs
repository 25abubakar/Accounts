using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static Accounts.Controllers.PersonsController;

namespace Accounts.Services.Services
{
    public class PersonService : IPersonService
    {
        private readonly ApplicationDbContext      _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IWebHostEnvironment       _env;

        public PersonService(
            ApplicationDbContext      db,
            UserManager<IdentityUser> userManager,
            IWebHostEnvironment       env)
        {
            _db          = db;
            _userManager = userManager;
            _env         = env;
        }

        public async Task<IEnumerable<PersonDto>> GetAllAsync()
        {
            var persons  = await _db.Persons.Include(p => p.Addresses).Include(p => p.Staff)
                .OrderByDescending(p => p.CreatedDate).ToListAsync();
            var orgNodes = await _db.OrganizationTree.ToListAsync();
            return persons.Select(p => MapToDto(p, orgNodes));
        }

        public async Task<IEnumerable<PersonDto>> GetUnassignedAsync()
        {
            var persons  = await _db.Persons.Include(p => p.Addresses).Include(p => p.Staff)
                .Where(p => p.Staff == null).OrderByDescending(p => p.CreatedDate).ToListAsync();
            var orgNodes = await _db.OrganizationTree.ToListAsync();
            return persons.Select(p => MapToDto(p, orgNodes));
        }

        public async Task<PersonDto?> GetByIdAsync(Guid id)
        {
            var person = await _db.Persons.Include(p => p.Addresses).Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.PersonId == id);
            if (person == null) return null;
            var orgNodes = await _db.OrganizationTree.ToListAsync();
            return MapToDto(person, orgNodes);
        }

        public async Task<IEnumerable<PersonProfileDto>> GetProfilesAsync()
        {
            var orgNodes = await _db.OrganizationTree.AsNoTracking().ToListAsync();
            var persons  = await _db.Persons.AsNoTracking()
                .Include(p => p.Addresses)
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy)
                .OrderByDescending(p => p.CreatedDate).ToListAsync();
            return persons.Select(p => MapToProfile(p, orgNodes));
        }

        public async Task<PersonProfileDto?> GetProfileAsync(Guid id)
        {
            var person = await _db.Persons.AsNoTracking()
                .Include(p => p.Addresses)
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy)
                .FirstOrDefaultAsync(p => p.PersonId == id);
            if (person == null) return null;
            var orgNodes = await _db.OrganizationTree.AsNoTracking().ToListAsync();
            return MapToProfile(person, orgNodes);
        }

        public async Task<object> GetOrgTreeAsync()
        {
            var all      = await _db.OrganizationTree.OrderBy(n => n.Name).ToListAsync();
            var byParent = all.ToLookup(n => n.ParentId);
            return byParent[null].Select(c => new
            {
                id = c.Id, name = c.Name, label = c.Label, flagUrl = c.FlagUrl,
                children = byParent[c.Id].Select(co => new
                {
                    id = co.Id, name = co.Name, label = co.Label,
                    loginPrefix = ResolveCompanyPrefix(co),
                    children = byParent[co.Id].Select(b => new { id = b.Id, name = b.Name, label = b.Label }).ToList()
                }).ToList()
            }).ToList();
        }

        public async Task<object?> PreviewLoginIdAsync(int branchId)
        {
            var all    = await _db.OrganizationTree.ToListAsync();
            var branch = all.FirstOrDefault(n => n.Id == branchId);
            if (branch == null) return null;
            var company = branch.ParentId.HasValue ? all.FirstOrDefault(n => n.Id == branch.ParentId) : null;
            var loginId = await GenerateLoginIdAsync(company ?? branch, all);
            return new { loginId, companyName = company?.Name ?? branch.Name };
        }

        public async Task<(PersonDto? Person, string? Error, int StatusCode)> RegisterAsync(RegisterPersonDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.FullName)) return (null, "FullName is required.", 400);
            if (string.IsNullOrWhiteSpace(dto.Password)) return (null, "Password is required.", 400);
            if (dto.BranchId <= 0) return (null, "BranchId is required.", 400);

            var orgNodes = await _db.OrganizationTree.ToListAsync();
            var branch   = orgNodes.FirstOrDefault(n => n.Id == dto.BranchId);
            if (branch == null) return (null, $"Branch {dto.BranchId} not found.", 400);

            var company = branch.ParentId.HasValue ? orgNodes.FirstOrDefault(n => n.Id == branch.ParentId) : null;
            var loginId = await GenerateLoginIdAsync(company ?? branch, orgNodes);

            if (!string.IsNullOrWhiteSpace(dto.Email) && await _userManager.FindByEmailAsync(dto.Email) != null)
                return (null, $"Email '{dto.Email}' is already registered.", 409);

            var identityUser = new IdentityUser
            {
                UserName       = loginId,
                Email          = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email,
                EmailConfirmed = true
            };

            var createResult = await _userManager.CreateAsync(identityUser, dto.Password);
            if (!createResult.Succeeded)
                return (null, string.Join("; ", createResult.Errors.Select(e => e.Description)), 400);

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
            return (MapToDto(created!, orgNodes), null, 201);
        }

        public async Task<(PersonDto? Person, string? Error)> UpdateAsync(Guid id, UpdatePersonDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.FullName)) return (null, "FullName is required.");

            var person = await _db.Persons.Include(p => p.Addresses).Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.PersonId == id);
            if (person == null) return (null, $"Person {id} not found.");

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
                    iu.Email = dto.Email.Trim();
                    iu.NormalizedEmail = dto.Email.Trim().ToUpperInvariant();
                    await _userManager.UpdateAsync(iu);
                }
            }

            UpsertAddress(person, "Current",   dto.CurrentAddress);
            UpsertAddress(person, "Permanent", dto.PermanentAddress);
            await _db.SaveChangesAsync();

            var orgNodes = await _db.OrganizationTree.ToListAsync();
            var updated  = await _db.Persons.Include(p => p.Addresses).Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.PersonId == id);
            return (MapToDto(updated!, orgNodes), null);
        }

        public async Task<(string? PhotoUrl, string? FullUrl, string? Error)> UploadPhotoAsync(
            Guid id, IFormFile photo, string baseUrl)
        {
            var person = await _db.Persons.FindAsync(id);
            if (person == null) return (null, null, $"Person {id} not found.");
            if (photo == null || photo.Length == 0) return (null, null, "No file uploaded.");

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext)) return (null, null, "Only jpg, jpeg, png, webp allowed.");
            if (photo.Length > 5 * 1024 * 1024) return (null, null, "Max 5 MB.");

            var dir = Path.Combine(_env.WebRootPath, "uploads", "persons");
            Directory.CreateDirectory(dir);

            if (!string.IsNullOrWhiteSpace(person.ProfilePhotoUrl))
            {
                var old = Path.Combine(_env.WebRootPath,
                    person.ProfilePhotoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(old)) File.Delete(old);
            }

            var fileName = $"person_{id:N}_{Guid.NewGuid():N}{ext}";
            using (var s = new FileStream(Path.Combine(dir, fileName), FileMode.Create))
                await photo.CopyToAsync(s);

            person.ProfilePhotoUrl = $"/uploads/persons/{fileName}";
            await _db.SaveChangesAsync();
            return (person.ProfilePhotoUrl, $"{baseUrl}{person.ProfilePhotoUrl}", null);
        }

        public async Task<(bool Success, string Message)> DeleteAsync(Guid id)
        {
            var person = await _db.Persons.FindAsync(id);
            if (person == null) return (false, $"Person {id} not found.");

            var iu = await _userManager.FindByIdAsync(person.IdentityUserId);
            if (iu != null) await _userManager.DeleteAsync(iu);

            if (!string.IsNullOrWhiteSpace(person.ProfilePhotoUrl))
            {
                var fp = Path.Combine(_env.WebRootPath,
                    person.ProfilePhotoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fp)) File.Delete(fp);
            }

            _db.Persons.Remove(person);
            await _db.SaveChangesAsync();
            return (true, $"Person '{person.FullName}' deleted.");
        }

        // ── Private Helpers ───────────────────────────────────────────────────

        private async Task<string> GenerateLoginIdAsync(OrganizationTree node, List<OrganizationTree> all)
        {
            var prefix   = ResolveCompanyPrefix(node);
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
                AddressId   = Guid.NewGuid(), PersonId = personId, AddressType = type,
                AddressLine = src.AddressLine?.Trim(), Country  = src.Country?.Trim(),
                Province    = src.Province?.Trim(),    District = src.District?.Trim(),
                City        = src.City?.Trim(),        PostalCode = src.PostalCode?.Trim()
            };

        private void UpsertAddress(Person person, string type, AddressDto? dto)
        {
            if (dto == null) return;
            var ex = person.Addresses.FirstOrDefault(a => a.AddressType == type);
            if (ex != null)
            {
                ex.AddressLine = dto.AddressLine?.Trim(); ex.Country    = dto.Country?.Trim();
                ex.Province    = dto.Province?.Trim();    ex.District   = dto.District?.Trim();
                ex.City        = dto.City?.Trim();        ex.PostalCode = dto.PostalCode?.Trim();
            }
            else
            {
                person.Addresses.Add(BuildAddress(dto, type, person.PersonId));
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
                AddressLine = a.AddressLine, Country  = a.Country,  Province = a.Province,
                District    = a.District,    City     = a.City,     PostalCode = a.PostalCode
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
