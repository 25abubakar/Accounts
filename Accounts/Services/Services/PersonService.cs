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

            // Walk up to find the Company node (parent of branch)
            var company = branch.ParentId.HasValue
                ? all.FirstOrDefault(n => n.Id == branch.ParentId)
                : null;

            var companyNode = company ?? branch;
            var loginId     = await GenerateLoginIdAsync(companyNode);
            var password    = $"{loginId}@";

            // Preview email uses a placeholder name since we don't know the person yet
            var domain      = BuildCompanyDomain(companyNode.Name);
            var sampleEmail = $"firstname.lastname@{domain}";

            return new
            {
                loginId,
                password,
                generatedEmail = sampleEmail,
                emailDomain    = domain,
                companyName    = companyNode.Name,
                companyCode    = GetCompanyInitials(companyNode),
                branchName     = branch.Name
            };
        }

        public async Task<(PersonDto? Person, string? GeneratedLoginId, string? GeneratedPassword, string? Error, int StatusCode)> RegisterAsync(RegisterPersonDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.FullName)) return (null, null, null, "FullName is required.", 400);
            if (dto.BranchId <= 0) return (null, null, null, "BranchId is required.", 400);

            var orgNodes = await _db.OrganizationTree.ToListAsync();
            var branch   = orgNodes.FirstOrDefault(n => n.Id == dto.BranchId);
            if (branch == null) return (null, null, null, $"Branch {dto.BranchId} not found.", 400);

            // ── Walk up to Company node (parent of branch) ────────────────────
            var company     = branch.ParentId.HasValue ? orgNodes.FirstOrDefault(n => n.Id == branch.ParentId) : null;
            var companyNode = company ?? branch;

            // ── Auto-generate Login ID from Company initials only ─────────────
            // Format: [CompanyInitials][5-digit seq]  e.g. LT10001
            var loginId = await GenerateLoginIdAsync(companyNode);

            // ── Auto-generate Password = LoginId + "@"  e.g. LT10001@ ─────────
            var password = $"{loginId}@";

            // ── Auto-generate Email if not provided ───────────────────────────
            // Format: firstname.lastname@companyname.com  e.g. abubakar.khan@laltechnology.com
            // Collision: abubakar.khan.1@laltechnology.com, abubakar.khan.2@...
            var email = string.IsNullOrWhiteSpace(dto.Email)
                ? await GenerateEmailAsync(dto.FullName, companyNode)
                : dto.Email.Trim();

            // Check email uniqueness in Identity
            if (await _userManager.FindByEmailAsync(email) != null)
            {
                // If user provided an email that's taken, reject it
                if (!string.IsNullOrWhiteSpace(dto.Email))
                    return (null, null, null, $"Email '{email}' is already registered.", 409);

                // Auto-generated email collision — GenerateEmailAsync already handles this,
                // but double-check just in case of race condition
                email = await GenerateEmailAsync(dto.FullName, companyNode);
            }

            var identityUser = new IdentityUser
            {
                UserName       = loginId,
                Email          = email,
                EmailConfirmed = true
            };

            // Always use auto-generated password — ignore any password sent from frontend
            var createResult = await _userManager.CreateAsync(identityUser, password);
            if (!createResult.Succeeded)
                return (null, null, null, string.Join("; ", createResult.Errors.Select(e => e.Description)), 400);

            var person = new Person
            {
                PersonId       = Guid.NewGuid(),
                FullName       = dto.FullName.Trim(),
                Phone          = dto.Phone?.Trim(),
                Email          = email,                // always store the final email
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

            return (MapToDto(created!, orgNodes), loginId, password, null, 201);
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

        // ── Password Management ───────────────────────────────────────────────

        /// <summary>
        /// Employee changes their own password.
        /// Requires the current password to be correct.
        /// </summary>
        public async Task<(bool Success, string Message)> ChangePasswordAsync(
            Guid personId, string currentPassword, string newPassword)
        {
            var person = await _db.Persons.FindAsync(personId);
            if (person == null) return (false, $"Person {personId} not found.");

            var user = await _userManager.FindByIdAsync(person.IdentityUserId);
            if (user == null) return (false, "Identity account not found.");

            var result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
            if (!result.Succeeded)
                return (false, string.Join("; ", result.Errors.Select(e => e.Description)));

            return (true, "Password changed successfully.");
        }

        /// <summary>
        /// Admin resets password for any person — no current password needed.
        /// If newPassword is null, auto-generates a new one as LoginId@NewSeq.
        /// </summary>
        public async Task<(bool Success, string Message, string? NewPassword)> ResetPasswordAsync(
            Guid personId, string? newPassword = null)
        {
            var person = await _db.Persons.FindAsync(personId);
            if (person == null) return (false, $"Person {personId} not found.", null);

            var user = await _userManager.FindByIdAsync(person.IdentityUserId);
            if (user == null) return (false, "Identity account not found.", null);

            // If no password provided, generate one: LoginId@
            var password = string.IsNullOrWhiteSpace(newPassword)
                ? $"{person.LoginId}@"
                : newPassword;

            // Remove old password and set new one
            var token  = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, password);

            if (!result.Succeeded)
                return (false, string.Join("; ", result.Errors.Select(e => e.Description)), null);

            return (true, $"Password reset successfully for '{person.FullName}'.", password);
        }

        /// <summary>
        /// Resets password back to the default: LoginId@
        /// e.g. LT10001 → LT10001@
        /// </summary>
        public async Task<(bool Success, string Message, string? DefaultPassword)> ResetToDefaultPasswordAsync(
            Guid personId)
        {
            var person = await _db.Persons.FindAsync(personId);
            if (person == null) return (false, $"Person {personId} not found.", null);

            var defaultPassword = $"{person.LoginId}@";
            var (success, message, _) = await ResetPasswordAsync(personId, defaultPassword);

            return success
                ? (true, $"Password reset to default for '{person.FullName}'.", defaultPassword)
                : (false, message, null);
        }

        /// <summary>
        /// Previews the email that will be auto-generated for a given name + branch.
        /// Useful for frontend to show the user before submitting.
        /// </summary>
        public async Task<object?> PreviewEmailAsync(int branchId, string fullName)
        {
            var all    = await _db.OrganizationTree.ToListAsync();
            var branch = all.FirstOrDefault(n => n.Id == branchId);
            if (branch == null) return null;

            var company     = branch.ParentId.HasValue ? all.FirstOrDefault(n => n.Id == branch.ParentId) : null;
            var companyNode = company ?? branch;

            var email  = await GenerateEmailAsync(fullName, companyNode);
            var domain = BuildCompanyDomain(companyNode.Name);

            return new
            {
                generatedEmail = email,
                emailDomain    = domain,
                companyName    = companyNode.Name
            };
        }

        // ── Private Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Generates Login ID from COMPANY only (never branch or country).
        /// Format: [CompanyInitials][5-digit sequence starting at 10001]
        /// Example: "Lal Technology" → LT10001, LT10002, ...
        /// </summary>
        //private async Task<string> GenerateLoginIdAsync(OrganizationTree companyNode)
        //{
        //    // Always derive prefix from company name initials (first letter of each word)
        //    var prefix = GetCompanyInitials(companyNode);

        //    // Count existing persons whose LoginId starts with this prefix
        //    var existing = await _db.Persons.CountAsync(p => p.LoginId.StartsWith(prefix));
        //    int seq = 10001 + existing;
        //    string loginId;

        //    // Guarantee uniqueness even if there are gaps from deletions
        //    do
        //    {
        //        loginId = $"{prefix}{seq}";
        //        seq++;
        //    }
        //    while (await _db.Persons.AnyAsync(p => p.LoginId == loginId));

        //    return loginId;
        //}


        private async Task<string> GenerateLoginIdAsync(OrganizationTree companyNode)
        {
            // Always derive prefix from company name initials (first letter of each word)
            var prefix = GetCompanyInitials(companyNode);

            // We start looking for sequences starting at 10001
            int seq = 10001;
            string loginId;

            // Loop until we find a LoginId that does NOT exist in the Identity (AspNetUsers) table.
            // This perfectly prevents the "Username is already taken" error.
            do
            {
                loginId = $"{prefix}{seq}";
                seq++;
            }
            // We check _userManager instead of _db.Persons because AspNetUsers enforces the uniqueness
            while (await _userManager.FindByNameAsync(loginId) != null);

            return loginId;
        }

        /// <summary>
        /// Derives company initials from the company name.
        ///
        /// Priority:
        ///   1. Use stored Code field if set (e.g. "TS", "NS", "NT")
        ///   2. Split by spaces → first letter of each word  ("Lal Technology" → "LT")
        ///   3. Split CamelCase → first letter of each part  ("NetSolutions" → "NS")
        ///   4. Single word fallback → first 2 uppercase chars ("Tech" → "TE")
        ///
        /// To override: set the Code field on the org node (e.g. Code = "NT").
        /// </summary>
        private static string GetCompanyInitials(OrganizationTree node)
        {
            // 1. Use stored Code field if set — highest priority
            if (!string.IsNullOrWhiteSpace(node.Code))
                return node.Code.ToUpper().Trim();

            var name = node.Name.Trim();

            // 2. Space-separated words → "Lal Technology" = "LT"
            var spaceWords = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                 .Where(w => w.Length > 0)
                                 .ToArray();
            if (spaceWords.Length >= 2)
                return string.Concat(spaceWords.Select(w => char.ToUpper(w[0])));

            // 3. CamelCase split → "NetSolutions" = ["Net","Solutions"] = "NS"
            //                      "TechSoft"     = ["Tech","Soft"]     = "TS"
            var camelWords = SplitCamelCase(name);
            if (camelWords.Length >= 2)
                return string.Concat(camelWords.Select(w => char.ToUpper(w[0])));

            // 4. Single word fallback → first 2 chars → "Tech" = "TE"
            return name.Length >= 2 ? name[..2].ToUpper() : name.ToUpper();
        }

        /// <summary>
        /// Splits a CamelCase string into words.
        /// "NetSolutions" → ["Net", "Solutions"]
        /// "TechSoft"     → ["Tech", "Soft"]
        /// "LALTechnology"→ ["LAL", "Technology"]
        /// </summary>
        private static string[] SplitCamelCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return [input];

            var result = new List<string>();
            int start  = 0;

            for (int i = 1; i < input.Length; i++)
            {
                bool isUpper = char.IsUpper(input[i]);
                bool prevLower = char.IsLower(input[i - 1]);
                bool nextLower = i + 1 < input.Length && char.IsLower(input[i + 1]);

                // Start new word at uppercase letter after lowercase, or at start of new word
                if (isUpper && (prevLower || nextLower))
                {
                    result.Add(input[start..i]);
                    start = i;
                }
            }
            result.Add(input[start..]);
            return result.Where(w => w.Length > 0).ToArray();
        }

        // Keep for backward compat (used by GetOrgTreeAsync)
        private static string ResolveCompanyPrefix(OrganizationTree node) =>
            GetCompanyInitials(node);

        // ── Email Generation ──────────────────────────────────────────────────

        /// <summary>
        /// Auto-generates a unique company email for a person.
        ///
        /// Format:  firstname.lastname@companyname.com
        /// Example: "Muhammad Abubakar" + "Lal Technology" → abubakar.muhammad@laltechnology.com
        ///
        /// Collision handling (appends incrementing number to name part):
        ///   abubakar.muhammad@laltechnology.com      ← taken
        ///   abubakar.muhammad.1@laltechnology.com    ← taken
        ///   abubakar.muhammad.2@laltechnology.com    ← assigned ✓
        ///
        /// Rules:
        ///   - All lowercase
        ///   - Remove spaces, special chars (keep only a-z, 0-9, dot)
        ///   - Domain = company name lowercased, spaces removed + .com
        /// </summary>
        private async Task<string> GenerateEmailAsync(string fullName, OrganizationTree companyNode)
        {
            // ── 1. Build domain from company name ─────────────────────────────
            var domain = BuildCompanyDomain(companyNode.Name);

            // ── 2. Split full name into parts ─────────────────────────────────
            var parts = fullName.Trim()
                                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                .Select(SanitizeEmailPart)
                                .Where(p => p.Length > 0)
                                .ToArray();

            string namePart;
            if (parts.Length == 0)
            {
                namePart = "user";
            }
            else if (parts.Length == 1)
            {
                // Only one name → use it alone
                namePart = parts[0];
            }
            else
            {
                // firstname.lastname  (first word = first name, last word = last name)
                namePart = $"{parts[0]}.{parts[^1]}";
            }

            // ── 3. Find unique email with collision handling ───────────────────
            var baseEmail = $"{namePart}@{domain}";

            // Check both Identity (AspNetUsers) and Persons table
            if (!await EmailExistsAsync(baseEmail))
                return baseEmail;

            // Append incrementing number until unique
            int counter = 1;
            string candidate;
            do
            {
                candidate = $"{namePart}.{counter}@{domain}";
                counter++;
            }
            while (await EmailExistsAsync(candidate));

            return candidate;
        }

        /// <summary>
        /// Checks if an email already exists in either Identity or Persons table.
        /// </summary>
        private async Task<bool> EmailExistsAsync(string email) =>
            await _userManager.FindByEmailAsync(email) != null
            || await _db.Persons.AnyAsync(p => p.Email != null &&
               p.Email.ToLower() == email.ToLower());

        /// <summary>
        /// Builds the email domain from a company name.
        /// "Lal Technology"  → "laltechnology.com"
        /// "NetSolutions"    → "netsolutions.com"
        /// "Soft Vision Ltd" → "softvisionltd.com"
        /// </summary>
        private static string BuildCompanyDomain(string companyName) =>
            companyName
                .ToLower()
                .Replace(" ", "")           // remove spaces
                .Replace(".", "")           // remove dots
                .Replace(",", "")           // remove commas
                .Replace("'", "")           // remove apostrophes
                .Replace("-", "")           // remove hyphens
                + ".com";

        /// <summary>
        /// Sanitizes a name part for use in an email address.
        /// Keeps only a-z, 0-9. Removes everything else.
        /// "Abubakar" → "abubakar"
        /// "O'Brien"  → "obrien"
        /// "Jean-Luc" → "jeanluc"
        /// </summary>
        private static string SanitizeEmailPart(string part) =>
            new string(part.ToLower()
                           .Where(c => char.IsLetterOrDigit(c))
                           .ToArray());

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
