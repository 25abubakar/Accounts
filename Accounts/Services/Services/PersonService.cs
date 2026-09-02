using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Accounts.Services.Services
{
    public class PersonService : IPersonService
    {
        private readonly ApplicationDbContext      _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment       _env;
        private readonly ITenantService            _tenantService;

        public PersonService(
            ApplicationDbContext         db,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment          env,
            ITenantService               tenantService)
        {
            _db            = db;
            _userManager   = userManager;
            _env           = env;
            _tenantService = tenantService;
        }

        public async Task<IEnumerable<PersonDto>> GetAllAsync()
        {
            var persons  = await _db.Persons.Include(p => p.Addresses)
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy).ThenInclude(v => v!.DesignationNav)
                .OrderByDescending(p => p.CreatedDate).ToListAsync();
            var orgNodes = await _db.OrganizationTree.ToListAsync();
            return persons.Select(p => MapToDto(p, orgNodes));
        }

        public async Task<IEnumerable<PersonDto>> GetFormerAsync(bool tenantWide, IReadOnlySet<int> visibleOrganizationIds)
        {
            var visibleIds = visibleOrganizationIds.ToArray();
            var persons = await _db.Persons.AsNoTracking()
                .Include(p => p.Addresses)
                .Where(p => (p.EmploymentStatus == "Fired" || p.EmploymentStatus == "Retired") &&
                    (tenantWide || (p.LastOrganizationId.HasValue && visibleIds.Contains(p.LastOrganizationId.Value))))
                .OrderByDescending(p => p.TerminationDateUtc)
                .ThenBy(p => p.FullName)
                .ToListAsync();
            var orgNodes = await _db.OrganizationTree.AsNoTracking().ToListAsync();
            return persons.Select(p => MapToDto(p, orgNodes));
        }

        public async Task<IEnumerable<PersonDto>> GetUnassignedAsync()
        {
            var persons  = await _db.Persons.Include(p => p.Addresses)
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy).ThenInclude(v => v!.DesignationNav)
                .Where(p => p.Staff == null).OrderByDescending(p => p.CreatedDate).ToListAsync();
            var orgNodes = await _db.OrganizationTree.ToListAsync();
            return persons.Select(p => MapToDto(p, orgNodes));
        }

        public async Task<PersonDto?> GetByIdAsync(Guid id)
        {
            var person = await _db.Persons.Include(p => p.Addresses)
                .Include(p => p.Contacts)
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy).ThenInclude(v => v!.DesignationNav)
                .FirstOrDefaultAsync(p => p.PersonId == id);
            if (person == null) return null;
            var orgNodes = await _db.OrganizationTree.ToListAsync();
            return MapToDto(person, orgNodes);
        }

        public async Task<PersonDto?> GetByIdentityUserIdAsync(string identityUserId)
        {
            var person = await _db.Persons.AsNoTracking()
                .Include(p => p.Addresses)
                .Include(p => p.Contacts)
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy).ThenInclude(v => v!.DesignationNav)
                .FirstOrDefaultAsync(p => p.IdentityUserId == identityUserId);
            if (person == null) return null;
            var orgNodes = await _db.OrganizationTree.AsNoTracking().ToListAsync();
            return MapToDto(person, orgNodes);
        }

        public async Task<IEnumerable<PersonProfileDto>> GetProfilesAsync()
        {
            var orgNodes = await _db.OrganizationTree.AsNoTracking().ToListAsync();
            var persons  = await _db.Persons.AsNoTracking()
                .Include(p => p.Addresses)
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy).ThenInclude(v => v!.DesignationNav)
                .OrderByDescending(p => p.CreatedDate).ToListAsync();
            return persons.Select(p => MapToProfile(p, orgNodes));
        }

        public async Task<PersonProfileDto?> GetProfileAsync(Guid id)
        {
            var person = await _db.Persons.AsNoTracking()
                .Include(p => p.Addresses)
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy).ThenInclude(v => v!.DesignationNav)
                .FirstOrDefaultAsync(p => p.PersonId == id);
            if (person == null) return null;
            var orgNodes = await _db.OrganizationTree.AsNoTracking().ToListAsync();
            return MapToProfile(person, orgNodes);
        }

        public async Task<PersonHrProfileDto?> GetHrProfileAsync(Guid id)
        {
            var profile = await _db.PersonHrProfileReadRows.AsNoTracking()
                .FirstOrDefaultAsync(x => x.PersonId == id);
            if (profile == null) return null;

            var educations = await _db.PersonEducations.AsNoTracking()
                .Where(x => x.PersonId == id)
                .OrderBy(x => x.SortOrder)
                .ToListAsync();
            var experiences = await _db.PersonExperiences.AsNoTracking()
                .Where(x => x.PersonId == id)
                .OrderBy(x => x.SortOrder)
                .ToListAsync();

            return MapHrProfile(profile, educations, experiences);
        }

        public async Task<(PersonHrProfileDto? Profile, string? Error)> UpdateHrProfileAsync(Guid id, PersonHrProfileDto dto)
        {
            var person = await _db.Persons
                .Include(p => p.Contacts)
                .Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.PersonId == id);
            if (person == null) return (null, $"Person {id} not found.");
            if (string.IsNullOrWhiteSpace(dto.FullName)) return (null, "FullName is required.");

            var (firstName, middleName, lastName) = SplitFullName(dto.FullName);
            person.FirstName = firstName;
            person.MiddleName = middleName;
            person.LastName = lastName;
            person.FullName = dto.FullName.Trim();
            person.Phone = dto.Phone?.Trim();
            person.Email = dto.Email?.Trim();
            person.PersonalEmail = dto.PersonalEmail?.Trim();
            person.Gender = dto.Gender?.Trim();
            person.DateOfBirth = dto.DateOfBirth;
            person.MaritalStatus = dto.MaritalStatus?.Trim();
            person.ShiftStartTime = string.IsNullOrWhiteSpace(dto.ShiftStartTime) ? person.ShiftStartTime : dto.ShiftStartTime.Trim();
            person.ShiftEndTime = string.IsNullOrWhiteSpace(dto.ShiftEndTime) ? person.ShiftEndTime : dto.ShiftEndTime.Trim();
            person.TimeZoneId = string.IsNullOrWhiteSpace(dto.TimeZoneId) ? person.TimeZoneId : dto.TimeZoneId.Trim();

            UpsertContact(person, "Phone", person.Phone, true);
            UpsertContact(person, "Email", person.Email, true);
            UpsertContact(person, "PersonalEmail", person.PersonalEmail, false);
            UpsertContact(person, "Emergency", dto.EmergencyContactNo?.Trim(), true);

            var profile = await _db.PersonHrProfiles.FirstOrDefaultAsync(x => x.PersonId == id);
            if (profile == null)
            {
                profile = new PersonHrProfile
                {
                    PersonId = id,
                    TenantId = person.TenantId,
                    CreatedDate = DateTime.UtcNow
                };
                _db.PersonHrProfiles.Add(profile);
            }

            profile.CnicOrLicense = dto.CnicOrLicense?.Trim();
            profile.Nationality = dto.Nationality?.Trim();
            profile.Race = dto.Race?.Trim();
            profile.Language = dto.Language?.Trim();
            profile.BloodGroup = dto.BloodGroup?.Trim();
            profile.Disability = dto.Disability?.Trim();
            profile.PoliceStation = dto.PoliceStation?.Trim();
            profile.EmergencyContactNo = dto.EmergencyContactNo?.Trim();
            profile.MedicalFrom = dto.MedicalFrom;
            profile.MedicalTo = dto.MedicalTo;
            profile.Treatment = dto.Treatment?.Trim();
            profile.DiagnosisDisease = dto.DiagnosisDisease?.Trim();
            profile.Doctor = dto.Doctor?.Trim();
            profile.DoctorContactNo = dto.DoctorContactNo?.Trim();
            profile.BankName = dto.BankName?.Trim();
            profile.BankBranchName = dto.BankBranchName?.Trim();
            profile.BankBranchCode = dto.BankBranchCode?.Trim();
            profile.SwiftCode = dto.SwiftCode?.Trim();
            profile.AccountTitle = dto.AccountTitle?.Trim();
            profile.AccountNo = dto.AccountNo?.Trim();
            profile.IbanNo = dto.IbanNo?.Trim();
            profile.BankBranchContactNo = dto.BankBranchContactNo?.Trim();
            profile.TaxNumber = dto.TaxNumber?.Trim();
            profile.PaymentMode = dto.PaymentMode?.Trim();
            profile.InductionType = dto.InductionType?.Trim();
            profile.JoiningDate = dto.JoiningDate;
            profile.TrainingFrom = dto.TrainingFrom;
            profile.TrainingTo = dto.TrainingTo;
            profile.ProbationFrom = dto.ProbationFrom;
            profile.ProbationTo = dto.ProbationTo;
            profile.ContractFrom = dto.ContractFrom;
            profile.ContractTo = dto.ContractTo;
            profile.WorkingDays = dto.WorkingDays?.Trim();
            profile.WorkingHours = dto.WorkingHours?.Trim();
            profile.TimingFrom = dto.TimingFrom?.Trim();
            profile.TimingTo = dto.TimingTo?.Trim();
            profile.PostingPerHour = dto.PostingPerHour;
            profile.PostingPerDay = dto.PostingPerDay;
            profile.PromotionFrom = dto.PromotionFrom;
            profile.PromotionTo = dto.PromotionTo;
            var scaleName = dto.Scale?.Trim();
            SalaryScale? selectedScale = null;
            if (!string.IsNullOrWhiteSpace(scaleName))
            {
                selectedScale = await _db.SalaryScales.AsNoTracking()
                    .FirstOrDefaultAsync(scale => scale.TenantId == person.TenantId && scale.IsActive && scale.ScaleName == scaleName);
                if (selectedScale == null) return (null, "The selected salary scale is not active or does not belong to this company.");
            }
            var workingDays = decimal.TryParse(dto.WorkingDays, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedDays) && parsedDays > 0 ? parsedDays : 26m;
            var workingHours = decimal.TryParse(dto.WorkingHours, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedHours) && parsedHours > 0 ? parsedHours : ResolveWorkingHours(dto.TimingFrom, dto.TimingTo);
            var basicSalary = selectedScale?.BasicSalary ?? dto.BasicSalary;
            var perDay = basicSalary.HasValue ? decimal.Round(basicSalary.Value / workingDays, 2, MidpointRounding.AwayFromZero) : dto.AccountsPerDay;
            var perHour = perDay.HasValue ? decimal.Round(perDay.Value / workingHours, 2, MidpointRounding.AwayFromZero) : dto.AccountsPerHour;

            profile.Scale = scaleName;
            profile.ScaleDate = dto.ScaleDate;
            profile.BasicSalary = basicSalary;
            profile.IncrementSalary = selectedScale?.YearlyIncrement ?? dto.IncrementSalary;
            profile.MaxSalary = selectedScale?.MaximumSalary ?? dto.MaxSalary;
            profile.CurrentPay = selectedScale?.BasicSalary ?? dto.CurrentPay;
            profile.AccountsPerDay = perDay;
            profile.AccountsPerHour = perHour;
            profile.LeaveFrom = dto.LeaveFrom;
            profile.LeaveTo = dto.LeaveTo;
            profile.LeaveEntitled = dto.LeaveEntitled;
            profile.LeaveAvailed = dto.LeaveAvailed;
            profile.ModifiedDate = DateTime.UtcNow;

            var oldEducations = await _db.PersonEducations.Where(x => x.PersonId == id).ToListAsync();
            var oldExperiences = await _db.PersonExperiences.Where(x => x.PersonId == id).ToListAsync();
            _db.PersonEducations.RemoveRange(oldEducations);
            _db.PersonExperiences.RemoveRange(oldExperiences);

            var educationRows = dto.Educations
                .Where(x => !string.IsNullOrWhiteSpace(x.EducationLevel) || !string.IsNullOrWhiteSpace(x.DegreeTitle) || !string.IsNullOrWhiteSpace(x.Institute))
                .Select((x, index) => new PersonEducation
                {
                    TenantId = person.TenantId,
                    PersonId = id,
                    EducationLevel = x.EducationLevel?.Trim(),
                    DegreeTitle = x.DegreeTitle?.Trim(),
                    Institute = x.Institute?.Trim(),
                    PassingYear = x.PassingYear?.Trim(),
                    Grade = x.Grade?.Trim(),
                    SortOrder = index + 1
                });
            var experienceRows = dto.Experiences
                .Where(x => !string.IsNullOrWhiteSpace(x.CompanyName) || !string.IsNullOrWhiteSpace(x.Role))
                .Select((x, index) => new PersonExperience
                {
                    TenantId = person.TenantId,
                    PersonId = id,
                    CompanyName = x.CompanyName?.Trim(),
                    Role = x.Role?.Trim(),
                    StartDate = x.StartDate,
                    EndDate = x.EndDate,
                    Summary = x.Summary?.Trim(),
                    SortOrder = index + 1
                });
            _db.PersonEducations.AddRange(educationRows);
            _db.PersonExperiences.AddRange(experienceRows);

            await _db.SaveChangesAsync();
            return (await GetHrProfileAsync(id), null);
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

            // Walk up the full ancestor chain to find the Company node by label
            OrganizationTree companyNode;
            try   { companyNode = FindCompanyNode(all, branch); }
            catch { return null; }

            var loginId     = await GenerateLoginIdAsync(companyNode);
            var password    = $"{loginId}@";
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

            // ── Walk up the full ancestor chain to find the Company node by label ─
            OrganizationTree companyNode;
            try   { companyNode = FindCompanyNode(orgNodes, branch); }
            catch (InvalidOperationException ex) { return (null, null, null, ex.Message, 400); }

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

            var identityUser = new ApplicationUser
            {
                UserName       = loginId,
                Email          = email,
                EmailConfirmed = true,
                TenantId       = _tenantService.TenantId   // stamp tenant on the Identity user too
            };

            // Always use auto-generated password — ignore any password sent from frontend
            var createResult = await _userManager.CreateAsync(identityUser, password);
            if (!createResult.Succeeded)
                return (null, null, null, string.Join("; ", createResult.Errors.Select(e => e.Description)), 400);

            // ── Stamp tenant claims on the new user so they work on first login ──
            if (_tenantService.TenantId.HasValue)
            {
                await _userManager.AddClaimsAsync(identityUser, new[]
                {
                    new System.Security.Claims.Claim(ITenantService.ClaimTenantId,      _tenantService.TenantId.Value.ToString()),
                    new System.Security.Claims.Claim(ITenantService.ClaimIsSuperAdmin,  "false"),
                    new System.Security.Claims.Claim(ITenantService.ClaimIsTenantAdmin, "false"),
                });
            }

            // ── Split FullName into FirstName, MiddleName, LastName ────────────
            var (firstName, middleName, lastName) = SplitFullName(dto.FullName);

            var person = new Person
            {
                PersonId       = Guid.NewGuid(),
                TenantId       = _tenantService.RequiredTenantId,  // ← stamp tenant
                FirstName      = firstName,
                MiddleName     = middleName,
                LastName       = lastName,
                FullName       = dto.FullName.Trim(),
                Phone          = dto.Phone?.Trim(),
                Email          = email,
                PersonalEmail  = dto.PersonalEmail?.Trim(),
                Gender         = dto.Gender?.Trim(),
                DateOfBirth    = dto.DateOfBirth,
                MaritalStatus  = dto.MaritalStatus?.Trim(),
                ShiftStartTime = string.IsNullOrWhiteSpace(dto.ShiftStartTime) ? "09:00" : dto.ShiftStartTime.Trim(),
                ShiftEndTime   = string.IsNullOrWhiteSpace(dto.ShiftEndTime) ? "18:00" : dto.ShiftEndTime.Trim(),
                TimeZoneId     = string.IsNullOrWhiteSpace(dto.TimeZoneId) ? "Asia/Karachi" : dto.TimeZoneId.Trim(),
                IdentityUserId = identityUser.Id,
                CreatedDate    = DateTime.UtcNow
            };

            var ca = dto.CurrentAddress;
            var pa = dto.PermanentAddress;
            if (ca != null) person.Addresses.Add(BuildAddress(ca, "Current", person.PersonId));
            if (pa != null && !AddressesAreEqual(ca, pa)) person.Addresses.Add(BuildAddress(pa, "Permanent", person.PersonId));

            // ── Insert into PersonContacts (normalized) ────────────────────────
            if (!string.IsNullOrWhiteSpace(email))
            {
                person.Contacts.Add(new PersonContact
                {
                    PersonId     = person.PersonId,
                    ContactType  = "Email",
                    ContactValue = email,
                    IsPrimary    = true,
                    CreatedDate  = DateTime.UtcNow
                });
            }

            var personalEmailValue = string.IsNullOrWhiteSpace(dto.PersonalEmail)
                ? null
                : dto.PersonalEmail.Trim();
            person.PersonalEmail = personalEmailValue;
            var personalEmail = person.Contacts.FirstOrDefault(c => c.ContactType == "PersonalEmail");
            if (personalEmailValue == null)
            {
                if (personalEmail != null) _db.PersonContacts.Remove(personalEmail);
            }
            else if (personalEmail != null)
            {
                personalEmail.ContactValue = personalEmailValue;
            }
            else
            {
                person.Contacts.Add(new PersonContact
                {
                    PersonId = person.PersonId,
                    ContactType = "PersonalEmail",
                    ContactValue = personalEmailValue,
                    IsPrimary = false,
                    CreatedDate = DateTime.UtcNow
                });
            }

            if (!string.IsNullOrWhiteSpace(dto.Phone))
            {
                person.Contacts.Add(new PersonContact
                {
                    PersonId     = person.PersonId,
                    ContactType  = "Phone",
                    ContactValue = dto.Phone.Trim(),
                    IsPrimary    = true,
                    CreatedDate  = DateTime.UtcNow
                });
            }

            _db.Persons.Add(person);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Remove the failed person graph from the tracker before cleaning
                // up the Identity account created earlier in this request.
                _db.ChangeTracker.Clear();
                await _userManager.DeleteAsync(identityUser);
                return (null, null, null, "Registration could not be saved. Please verify the supplied details and try again.", 400);
            }

            var created = await _db.Persons.Include(p => p.Addresses)
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy).ThenInclude(v => v!.DesignationNav)
                .FirstOrDefaultAsync(p => p.PersonId == person.PersonId);

            return (MapToDto(created!, orgNodes), loginId, password, null, 201);
        }

        public async Task<(PersonDto? Person, string? Error)> UpdateAsync(Guid id, UpdatePersonDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.FullName)) return (null, "FullName is required.");

            var person = await _db.Persons
                .Include(p => p.Addresses)
                .Include(p => p.Contacts)
                .Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.PersonId == id);
            if (person == null) return (null, $"Person {id} not found.");

            // ── Split FullName into FirstName, MiddleName, LastName ────────────
            var (firstName, middleName, lastName) = SplitFullName(dto.FullName);

            person.FirstName     = firstName;
            person.MiddleName    = middleName;
            person.LastName      = lastName;
            person.FullName      = dto.FullName.Trim(); // Keep for backward compat
            person.Gender        = dto.Gender?.Trim();
            person.DateOfBirth   = dto.DateOfBirth;
            person.MaritalStatus = dto.MaritalStatus?.Trim();

            // ── Upsert Email in PersonContacts ─────────────────────────────────
            if (!string.IsNullOrWhiteSpace(dto.Email))
            {
                var primaryEmail = person.Contacts.FirstOrDefault(c => c.ContactType == "Email" && c.IsPrimary);
                if (primaryEmail != null)
                {
                    primaryEmail.ContactValue = dto.Email.Trim();
                }
                else
                {
                    person.Contacts.Add(new PersonContact
                    {
                        PersonId     = person.PersonId,
                        ContactType  = "Email",
                        ContactValue = dto.Email.Trim(),
                        IsPrimary    = true,
                        CreatedDate  = DateTime.UtcNow
                    });
                }
                // Sync legacy Email column for backward compat
                person.Email = dto.Email.Trim();

                // Update Identity email
                var iu = await _userManager.FindByIdAsync(person.IdentityUserId);
                if (iu != null && !string.Equals(iu.Email, dto.Email.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    iu.Email           = dto.Email.Trim();
                    iu.NormalizedEmail = dto.Email.Trim().ToUpperInvariant();
                    await _userManager.UpdateAsync(iu);
                }
            }

            var personalEmailValue = string.IsNullOrWhiteSpace(dto.PersonalEmail)
                ? null
                : dto.PersonalEmail.Trim();
            person.PersonalEmail = personalEmailValue;
            var personalEmail = person.Contacts.FirstOrDefault(c => c.ContactType == "PersonalEmail");
            if (personalEmailValue == null)
            {
                if (personalEmail != null) _db.PersonContacts.Remove(personalEmail);
            }
            else if (personalEmail != null)
            {
                personalEmail.ContactValue = personalEmailValue;
            }
            else
            {
                person.Contacts.Add(new PersonContact
                {
                    PersonId = person.PersonId,
                    ContactType = "PersonalEmail",
                    ContactValue = personalEmailValue,
                    IsPrimary = false,
                    CreatedDate = DateTime.UtcNow
                });
            }

            // ── Upsert Phone in PersonContacts ─────────────────────────────────
            if (!string.IsNullOrWhiteSpace(dto.Phone))
            {
                var primaryPhone = person.Contacts.FirstOrDefault(c => c.ContactType == "Phone" && c.IsPrimary);
                if (primaryPhone != null)
                {
                    primaryPhone.ContactValue = dto.Phone.Trim();
                }
                else
                {
                    person.Contacts.Add(new PersonContact
                    {
                        PersonId     = person.PersonId,
                        ContactType  = "Phone",
                        ContactValue = dto.Phone.Trim(),
                        IsPrimary    = true,
                        CreatedDate  = DateTime.UtcNow
                    });
                }
                // Sync legacy Phone column for backward compat
                person.Phone = dto.Phone.Trim();
            }

            UpsertAddress(person, "Current",   dto.CurrentAddress);
            UpsertAddress(person, "Permanent", dto.PermanentAddress);
            await _db.SaveChangesAsync();

            var orgNodes = await _db.OrganizationTree.ToListAsync();
            var updated  = await _db.Persons.Include(p => p.Addresses)
                .Include(p => p.Staff).ThenInclude(s => s!.Vacancy).ThenInclude(v => v!.DesignationNav)
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
            var person = await _db.Persons
                .Include(p => p.Staff)
                .FirstOrDefaultAsync(p => p.PersonId == id);
              if (person == null) return (false, $"Person {id} not found.");

              // The Report-To FK intentionally uses NO ACTION to avoid SQL Server
              // multiple-cascade-path errors. Clear dependent assignments explicitly
              // so deleting a manager preserves the existing person-delete workflow.
              var directReports = await _db.Persons
                  .Where(p => p.ReportsToPersonId == id)
                  .ToListAsync();
              foreach (var directReport in directReports)
                  directReport.ReportsToPersonId = null;
              var alternativeDirectReports = await _db.Persons
                  .Where(p => p.AlternativeReportsToPersonId == id)
                  .ToListAsync();
              foreach (var directReport in alternativeDirectReports)
                  directReport.AlternativeReportsToPersonId = null;
  
              await using var transaction = await _db.Database.BeginTransactionAsync();

            if (person.Staff != null)
            {
                if (person.Staff.VacancyId.HasValue)
                {
                    var vacancy = await _db.Vacancies.FindAsync(person.Staff.VacancyId.Value);
                    if (vacancy != null) vacancy.IsFilled = false;
                }
                _db.StaffVacancies.Remove(person.Staff);
            }

            var iu = await _userManager.FindByIdAsync(person.IdentityUserId);
            if (iu != null)
            {
                var identityResult = await _userManager.DeleteAsync(iu);
                if (!identityResult.Succeeded)
                    return (false, string.Join("; ", identityResult.Errors.Select(e => e.Description)));
            }

            if (!string.IsNullOrWhiteSpace(person.ProfilePhotoUrl))
            {
                var fp = Path.Combine(_env.WebRootPath,
                    person.ProfilePhotoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fp)) File.Delete(fp);
            }

            _db.Persons.Remove(person);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            return (true, $"Person '{person.FullName}' deleted.");
        }

        public async Task<(bool Success, string Message, bool IsActive)> SetActiveAsync(Guid id, bool isActive)
        {
            var person = await _db.Persons.FindAsync(id);
            if (person == null) return (false, $"Person {id} not found.", false);

            person.IsActive = isActive;
            var identityUser = await _userManager.FindByIdAsync(person.IdentityUserId);
            if (identityUser != null)
            {
                identityUser.LockoutEnabled = true;
                identityUser.LockoutEnd = isActive ? null : DateTimeOffset.MaxValue;
                var updateResult = await _userManager.UpdateAsync(identityUser);
                if (!updateResult.Succeeded)
                    return (false, string.Join("; ", updateResult.Errors.Select(e => e.Description)), person.IsActive);
            }

            await _db.SaveChangesAsync();
            return (true, $"Person '{person.FullName}' is now {(isActive ? "active" : "inactive")}.", isActive);
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
                ? $"{user.UserName}@"
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

            var user = await _userManager.FindByIdAsync(person.IdentityUserId);
            if (user == null || string.IsNullOrWhiteSpace(user.UserName))
                return (false, "Identity account not found.", null);

            var defaultPassword = $"{user.UserName}@";
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

            // Walk up the full ancestor chain to find the Company node by label
            OrganizationTree companyNode;
            try   { companyNode = FindCompanyNode(all, branch); }
            catch { return null; }

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
        /// Walks the full ancestor chain from startNode up to the root and returns
        /// the FIRST node whose Label == "Company" (case-insensitive).
        ///
        /// This is the ONLY rule — no positional fallbacks.
        /// The tree can be any depth:
        ///
        ///   Pakistan (Country)
        ///     └── Lal Group (Group)
        ///           └── Lal Technology (Company)   ← always found by label
        ///                 └── Software (Department)
        ///                       └── Dev Team (Branch)
        ///                             └── Sub Team (Unit)  ← selected node
        ///
        /// Throws InvalidOperationException if no ancestor (including the node itself)
        /// has Label == "Company", so the caller can return a clear error to the client.
        /// </summary>
        private static OrganizationTree FindCompanyNode(List<OrganizationTree> all, OrganizationTree startNode)
        {
            var current = startNode;
            while (current != null)
            {
                if (string.Equals(current.Label, "Company", StringComparison.OrdinalIgnoreCase))
                    return current;

                current = current.ParentId.HasValue
                    ? all.FirstOrDefault(n => n.Id == current.ParentId)
                    : null;
            }

            throw new InvalidOperationException(
                $"No ancestor of node '{startNode.Name}' (Id={startNode.Id}) has Label='Company'. " +
                $"Please ensure the organization tree has a node with Label='Company' above this node.");
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

        private void UpsertContact(Person person, string type, string? value, bool isPrimary)
        {
            var existing = person.Contacts.FirstOrDefault(c =>
                c.ContactType == type && (!isPrimary || c.IsPrimary));
            if (string.IsNullOrWhiteSpace(value))
            {
                if (existing != null) _db.PersonContacts.Remove(existing);
                return;
            }
            if (existing != null)
            {
                existing.ContactValue = value.Trim();
                existing.IsPrimary = isPrimary;
            }
            else
            {
                person.Contacts.Add(new PersonContact
                {
                    PersonId = person.PersonId,
                    ContactType = type,
                    ContactValue = value.Trim(),
                    IsPrimary = isPrimary,
                    CreatedDate = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// Splits a full name string into (FirstName, MiddleName, LastName).
        /// "Ali"              → ("Ali",  null,  null)
        /// "Ali Khan"         → ("Ali",  null,  "Khan")
        /// "Ali Hassan Khan"  → ("Ali",  "Hassan", "Khan")
        /// "Ali Raza Hassan Khan" → ("Ali", "Raza Hassan", "Khan")
        /// </summary>
        private static (string First, string? Middle, string? Last) SplitFullName(string fullName)
        {
            var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return (fullName.Trim(), null, null);
            if (parts.Length == 1) return (parts[0], null, null);
            if (parts.Length == 2) return (parts[0], null, parts[1]);
            // 3+ parts: first, last, everything in between is middle
            return (parts[0], string.Join(" ", parts[1..^1]), parts[^1]);
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

        private static (OrganizationTree? Country, OrganizationTree? Company, OrganizationTree? Branch, OrganizationTree? Department)
            ResolveOrganization(int? startId, List<OrganizationTree> nodes)
        {
            if (!startId.HasValue) return (null, null, null, null);
            var byId = nodes.ToDictionary(n => n.Id);
            var chain = new List<OrganizationTree>();
            var currentId = startId;
            while (currentId.HasValue && byId.TryGetValue(currentId.Value, out var node) && chain.Count < 20)
            {
                chain.Add(node);
                currentId = node.ParentId;
            }

            OrganizationTree? Find(params string[] labels) => chain.FirstOrDefault(n =>
                labels.Any(label => string.Equals(n.Label, label, StringComparison.OrdinalIgnoreCase)));
            return (Find("Country"), Find("Company"), Find("Branch", "Office"), Find("Department"));
        }

        private static PersonDto MapToDto(Person p, List<OrganizationTree> org)
        {
            var organizationId = p.Staff?.Vacancy?.OrganizationId;
            var (country, company, branch, department) = ResolveOrganization(organizationId, org);

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
                PersonId = p.PersonId, LoginId = p.Staff?.LoginId ?? "-", FullName = p.FullName,
                Gender = p.Gender, DateOfBirth = p.DateOfBirth, MaritalStatus = p.MaritalStatus,
                Phone = p.Phone, Email = p.Email,
                PersonalEmail = p.PersonalEmail ?? p.Contacts.FirstOrDefault(c => c.ContactType == "PersonalEmail")?.ContactValue,
                ShiftStartTime = p.ShiftStartTime, ShiftEndTime = p.ShiftEndTime, TimeZoneId = p.TimeZoneId,
                PhotoUrl = p.ProfilePhotoUrl,
                IsHired = p.Staff != null, IsActive = p.IsActive, EmploymentStatus = p.EmploymentStatus,
                TerminationDateUtc = p.TerminationDateUtc, TerminationReason = p.TerminationReason,
                RegisteredAt = p.CreatedDate.ToString("o"), JoiningDate = p.LastJoiningDate,
                BranchId = branch?.Id, BranchName = branch?.Name ?? p.LastBranchName, CompanyName = company?.Name ?? p.LastCompanyName, CountryName = country?.Name ?? p.LastCountryName,
                VacancyCode = p.Staff?.Vacancy?.VacancyCode ?? p.LastVacancyCode,
                JobTitle = p.Staff?.Vacancy?.ResolvedJobTitle ?? p.LastJobTitle,
                Department = p.Staff?.Vacancy?.Department ?? department?.Name ?? p.LastDepartment,
                CurrentAddress = curDto, PermanentAddress = permDto, SameAddress = same
            };
        }

        private static PersonProfileDto MapToProfile(Person p, List<OrganizationTree> org)
        {
            var organizationId = p.Staff?.Vacancy?.OrganizationId;
            var (country, company, branch, department) = ResolveOrganization(organizationId, org);

            var parts    = p.FullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var initials = parts.Length >= 2
                ? $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[1][0])}"
                : p.FullName.Length >= 1 ? char.ToUpper(p.FullName[0]).ToString() : "?";

            var cur  = p.Addresses.FirstOrDefault(a => a.AddressType == "Current");
            var perm = p.Addresses.FirstOrDefault(a => a.AddressType == "Permanent");

            return new PersonProfileDto
            {
                PersonId = p.PersonId, LoginId = p.Staff?.LoginId ?? "-", FullName = p.FullName, Initials = initials,
                Gender = p.Gender, DateOfBirth = p.DateOfBirth, MaritalStatus = p.MaritalStatus,
                Phone = p.Phone, UserName = p.Email, PhotoUrl = p.ProfilePhotoUrl, RegisteredAt = p.CreatedDate,
                BranchId = branch?.Id, BranchName = branch?.Name, CompanyName = company?.Name,
                CountryName = country?.Name, CountryFlag = country?.FlagUrl,
                IsHired = p.Staff != null, IsActive = p.IsActive, StaffId = p.Staff?.StaffId, JoiningDate = null,
                VacancyId = p.Staff?.VacancyId, VacancyCode = p.Staff?.Vacancy?.VacancyCode,
                JobTitle = p.Staff?.Vacancy?.ResolvedJobTitle, Department = p.Staff?.Vacancy?.Department ?? department?.Name,
                ShiftStartTime = p.ShiftStartTime, ShiftEndTime = p.ShiftEndTime, TimeZoneId = p.TimeZoneId,
                CurrentAddress   = ToAddressResponse(cur),
                PermanentAddress = ToAddressResponse(perm ?? cur)
            };
        }

        private static PersonHrProfileDto MapHrProfile(
            PersonHrProfileReadRow profile,
            IEnumerable<PersonEducation> educations,
            IEnumerable<PersonExperience> experiences)
        {
            return new PersonHrProfileDto
            {
                PersonId = profile.PersonId,
                FullName = profile.FullName,
                Phone = profile.Phone,
                Email = profile.Email,
                PersonalEmail = profile.PersonalEmail,
                Gender = profile.Gender,
                DateOfBirth = profile.DateOfBirth,
                MaritalStatus = profile.MaritalStatus,
                ShiftStartTime = profile.ShiftStartTime,
                ShiftEndTime = profile.ShiftEndTime,
                TimeZoneId = profile.TimeZoneId,
                CnicOrLicense = profile.CnicOrLicense,
                Nationality = profile.Nationality,
                Race = profile.Race,
                Language = profile.Language,
                BloodGroup = profile.BloodGroup,
                Disability = profile.Disability,
                PoliceStation = profile.PoliceStation,
                EmergencyContactNo = profile.EmergencyContactNo,
                MedicalFrom = profile.MedicalFrom,
                MedicalTo = profile.MedicalTo,
                Treatment = profile.Treatment,
                DiagnosisDisease = profile.DiagnosisDisease,
                Doctor = profile.Doctor,
                DoctorContactNo = profile.DoctorContactNo,
                BankName = profile.BankName,
                BankBranchName = profile.BankBranchName,
                BankBranchCode = profile.BankBranchCode,
                SwiftCode = profile.SwiftCode,
                AccountTitle = profile.AccountTitle,
                AccountNo = profile.AccountNo,
                IbanNo = profile.IbanNo,
                BankBranchContactNo = profile.BankBranchContactNo,
                TaxNumber = profile.TaxNumber,
                PaymentMode = profile.PaymentMode,
                InductionType = profile.InductionType,
                JoiningDate = profile.JoiningDate,
                TrainingFrom = profile.TrainingFrom,
                TrainingTo = profile.TrainingTo,
                ProbationFrom = profile.ProbationFrom,
                ProbationTo = profile.ProbationTo,
                ContractFrom = profile.ContractFrom,
                ContractTo = profile.ContractTo,
                WorkingDays = profile.WorkingDays,
                WorkingHours = profile.WorkingHours,
                TimingFrom = profile.TimingFrom,
                TimingTo = profile.TimingTo,
                PostingPerHour = profile.PostingPerHour,
                PostingPerDay = profile.PostingPerDay,
                PromotionFrom = profile.PromotionFrom,
                PromotionTo = profile.PromotionTo,
                Scale = profile.Scale,
                ScaleDate = profile.ScaleDate,
                BasicSalary = profile.BasicSalary,
                IncrementSalary = profile.IncrementSalary,
                MaxSalary = profile.MaxSalary,
                CurrentPay = profile.CurrentPay,
                AccountsPerDay = profile.AccountsPerDay,
                AccountsPerHour = profile.AccountsPerHour,
                LeaveFrom = profile.LeaveFrom,
                LeaveTo = profile.LeaveTo,
                LeaveEntitled = profile.LeaveEntitled,
                LeaveAvailed = profile.LeaveAvailed,
                Educations = educations.Select(x => new PersonEducationDto
                {
                    EducationId = x.EducationId,
                    EducationLevel = x.EducationLevel,
                    DegreeTitle = x.DegreeTitle,
                    Institute = x.Institute,
                    PassingYear = x.PassingYear,
                    Grade = x.Grade,
                    SortOrder = x.SortOrder
                }).ToList(),
                Experiences = experiences.Select(x => new PersonExperienceDto
                {
                    ExperienceId = x.ExperienceId,
                    CompanyName = x.CompanyName,
                    Role = x.Role,
                    StartDate = x.StartDate,
                    EndDate = x.EndDate,
                    Summary = x.Summary,
                    SortOrder = x.SortOrder
                }).ToList()
            };
        }

        private static PersonHrProfileDto MapHrProfile(
            Person person,
            PersonHrProfile? profile,
            IEnumerable<PersonEducation> educations,
            IEnumerable<PersonExperience> experiences)
        {
            return new PersonHrProfileDto
            {
                PersonId = person.PersonId,
                FullName = person.FullName,
                Phone = person.Phone,
                Email = person.Email,
                PersonalEmail = person.PersonalEmail ?? person.Contacts.FirstOrDefault(c => c.ContactType == "PersonalEmail")?.ContactValue,
                Gender = person.Gender,
                DateOfBirth = person.DateOfBirth,
                MaritalStatus = person.MaritalStatus,
                ShiftStartTime = person.ShiftStartTime,
                ShiftEndTime = person.ShiftEndTime,
                TimeZoneId = person.TimeZoneId,
                CnicOrLicense = profile?.CnicOrLicense,
                Nationality = profile?.Nationality,
                Race = profile?.Race,
                Language = profile?.Language,
                BloodGroup = profile?.BloodGroup,
                Disability = profile?.Disability,
                PoliceStation = profile?.PoliceStation,
                EmergencyContactNo = profile?.EmergencyContactNo ?? person.Contacts.FirstOrDefault(c => c.ContactType == "Emergency")?.ContactValue,
                MedicalFrom = profile?.MedicalFrom,
                MedicalTo = profile?.MedicalTo,
                Treatment = profile?.Treatment,
                DiagnosisDisease = profile?.DiagnosisDisease,
                Doctor = profile?.Doctor,
                DoctorContactNo = profile?.DoctorContactNo,
                BankName = profile?.BankName,
                BankBranchName = profile?.BankBranchName,
                BankBranchCode = profile?.BankBranchCode,
                SwiftCode = profile?.SwiftCode,
                AccountTitle = profile?.AccountTitle,
                AccountNo = profile?.AccountNo,
                IbanNo = profile?.IbanNo,
                BankBranchContactNo = profile?.BankBranchContactNo,
                TaxNumber = profile?.TaxNumber,
                PaymentMode = profile?.PaymentMode,
                InductionType = profile?.InductionType,
                JoiningDate = profile?.JoiningDate,
                TrainingFrom = profile?.TrainingFrom,
                TrainingTo = profile?.TrainingTo,
                ProbationFrom = profile?.ProbationFrom,
                ProbationTo = profile?.ProbationTo,
                ContractFrom = profile?.ContractFrom,
                ContractTo = profile?.ContractTo,
                WorkingDays = profile?.WorkingDays,
                WorkingHours = profile?.WorkingHours,
                TimingFrom = profile?.TimingFrom,
                TimingTo = profile?.TimingTo,
                PostingPerHour = profile?.PostingPerHour,
                PostingPerDay = profile?.PostingPerDay,
                PromotionFrom = profile?.PromotionFrom,
                PromotionTo = profile?.PromotionTo,
                Scale = profile?.Scale,
                ScaleDate = profile?.ScaleDate,
                BasicSalary = profile?.BasicSalary,
                IncrementSalary = profile?.IncrementSalary,
                MaxSalary = profile?.MaxSalary,
                CurrentPay = profile?.CurrentPay,
                AccountsPerDay = profile?.AccountsPerDay,
                AccountsPerHour = profile?.AccountsPerHour,
                LeaveFrom = profile?.LeaveFrom,
                LeaveTo = profile?.LeaveTo,
                LeaveEntitled = profile?.LeaveEntitled,
                LeaveAvailed = profile?.LeaveAvailed,
                Educations = educations.Select(x => new PersonEducationDto
                {
                    EducationId = x.EducationId,
                    EducationLevel = x.EducationLevel,
                    DegreeTitle = x.DegreeTitle,
                    Institute = x.Institute,
                    PassingYear = x.PassingYear,
                    Grade = x.Grade,
                    SortOrder = x.SortOrder
                }).ToList(),
                Experiences = experiences.Select(x => new PersonExperienceDto
                {
                    ExperienceId = x.ExperienceId,
                    CompanyName = x.CompanyName,
                    Role = x.Role,
                    StartDate = x.StartDate,
                    EndDate = x.EndDate,
                    Summary = x.Summary,
                    SortOrder = x.SortOrder
                }).ToList()
            };
        }
        private static decimal ResolveWorkingHours(string? from, string? to)
        {
            if (TimeOnly.TryParse(from, out var start) && TimeOnly.TryParse(to, out var end))
            {
                var minutes = ((end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes + 1440) % 1440;
                if (minutes > 0) return (decimal)(minutes / 60d);
            }
            return 8m;
        }
    }
}
