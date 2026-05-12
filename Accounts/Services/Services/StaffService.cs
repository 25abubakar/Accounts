using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    public class StaffService : IStaffService
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment  _env;

        public StaffService(ApplicationDbContext db, IWebHostEnvironment env)
        {
            _db  = db;
            _env = env;
        }

        public async Task<IEnumerable<StaffDto>> GetAllAsync()
        {
            var list = await WithIncludes().ToListAsync();
            return list.Select(MapToDto);
        }

        public async Task<StaffDto?> GetByIdAsync(Guid id)
        {
            var s = await WithIncludes().FirstOrDefaultAsync(x => x.StaffId == id);
            return s == null ? null : MapToDto(s);
        }

        public async Task<IEnumerable<StaffDto>> SearchAsync(string q)
        {
            var list = await WithIncludes()
                .Where(s => s.FullName.Contains(q) || (s.Email != null && s.Email.Contains(q)))
                .ToListAsync();
            return list.Select(MapToDto);
        }

        public async Task<(StaffDto? Staff, string? Error)> HireAsync(Guid vacancyId, HireStaffDto dto)
        {
            var vacancy = await _db.Vacancies.FindAsync(vacancyId);
            if (vacancy == null) return (null, $"Vacancy {vacancyId} not found.");
            if (vacancy.IsFilled) return (null, $"Vacancy '{vacancy.VacancyCode}' is already filled.");

            Person? linkedPerson = null;
            if (!string.IsNullOrWhiteSpace(dto.Email))
                linkedPerson = await _db.Persons.FirstOrDefaultAsync(p => p.Email == dto.Email.Trim());

            var staff = new Staff
            {
                StaffId     = Guid.NewGuid(),
                FullName    = dto.FullName,
                Email       = dto.Email,
                Phone       = dto.Phone,
                VacancyId   = vacancyId,
                PersonId    = linkedPerson?.PersonId,
                JoiningDate = DateTime.UtcNow
            };

            _db.Staff.Add(staff);
            vacancy.IsFilled = true;
            await _db.SaveChangesAsync();

            var created = await WithIncludes().FirstOrDefaultAsync(s => s.StaffId == staff.StaffId);
            return (MapToDto(created!), null);
        }

        public async Task<(StaffDto? Staff, string? Error)> HirePersonAsync(Guid vacancyId, Guid personId)
        {
            var vacancy = await _db.Vacancies.FindAsync(vacancyId);
            if (vacancy == null) return (null, $"Vacancy {vacancyId} not found.");
            if (vacancy.IsFilled) return (null, $"Vacancy '{vacancy.VacancyCode}' is already filled.");

            var person = await _db.Persons.FindAsync(personId);
            if (person == null) return (null, $"Person {personId} not found.");

            if (await _db.Staff.AnyAsync(s => s.PersonId == personId))
                return (null, $"Person '{person.FullName}' is already hired.");

            var staff = new Staff
            {
                StaffId     = Guid.NewGuid(),
                FullName    = person.FullName,
                Email       = person.Email,
                Phone       = person.Phone,
                VacancyId   = vacancyId,
                PersonId    = personId,
                JoiningDate = DateTime.UtcNow
            };

            _db.Staff.Add(staff);
            vacancy.IsFilled = true;
            await _db.SaveChangesAsync();

            var created = await WithIncludes().FirstOrDefaultAsync(s => s.StaffId == staff.StaffId);
            return (MapToDto(created!), null);
        }

        public async Task<(StaffDto? Staff, string? Error)> UpdateAsync(Guid id, UpdateStaffDto dto)
        {
            var staff = await _db.Staff.FindAsync(id);
            if (staff == null) return (null, $"Staff {id} not found.");

            staff.FullName = dto.FullName;
            staff.Email    = dto.Email;
            staff.Phone    = dto.Phone;
            await _db.SaveChangesAsync();

            var updated = await WithIncludes().FirstOrDefaultAsync(s => s.StaffId == id);
            return (MapToDto(updated!), null);
        }

        public async Task<(StaffDto? Staff, string? Error)> TransferAsync(Guid id, TransferStaffDto dto)
        {
            var staff = await _db.Staff.FindAsync(id);
            if (staff == null) return (null, $"Staff {id} not found.");
            if (!staff.VacancyId.HasValue) return (null, "Staff member is not assigned to any vacancy.");

            var currentVacancy = await _db.Vacancies
                .Include(v => v.Organization).ThenInclude(o => o!.Parent).ThenInclude(p => p!.Parent)
                .FirstOrDefaultAsync(v => v.VacancyId == staff.VacancyId.Value);
            if (currentVacancy == null) return (null, "Current vacancy not found.");

            var newVacancy = await _db.Vacancies
                .Include(v => v.Organization).ThenInclude(o => o!.Parent).ThenInclude(p => p!.Parent)
                .FirstOrDefaultAsync(v => v.VacancyId == dto.NewVacancyId);
            if (newVacancy == null) return (null, $"Vacancy {dto.NewVacancyId} not found.");
            if (newVacancy.IsFilled) return (null, $"Vacancy '{newVacancy.VacancyCode}' is already filled.");

            var currentCompany = currentVacancy.Organization?.Parent;
            var currentCountry = currentCompany?.Parent;
            var targetCompany  = newVacancy.Organization?.Parent;
            var targetCountry  = targetCompany?.Parent;

            if (currentCompany?.Id != targetCompany?.Id || currentCountry?.Id != targetCountry?.Id)
                return (null, "Transfers are strictly limited to roles within the same Company and Country.");

            var oldVacancy = await _db.Vacancies.FindAsync(staff.VacancyId.Value);
            if (oldVacancy != null) oldVacancy.IsFilled = false;

            staff.VacancyId     = dto.NewVacancyId;
            newVacancy.IsFilled = true;
            await _db.SaveChangesAsync();

            var updated = await WithIncludes().FirstOrDefaultAsync(s => s.StaffId == id);
            return (MapToDto(updated!), null);
        }

        public async Task<(bool Success, string Message)> DeleteAsync(Guid id)
        {
            var staff = await _db.Staff.FindAsync(id);
            if (staff == null) return (false, $"Staff {id} not found.");

            if (staff.VacancyId.HasValue)
            {
                var vacancy = await _db.Vacancies.FindAsync(staff.VacancyId.Value);
                if (vacancy != null) vacancy.IsFilled = false;
            }

            if (!string.IsNullOrWhiteSpace(staff.PhotoUrl))
            {
                var filePath = Path.Combine(_env.WebRootPath,
                    staff.PhotoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(filePath)) File.Delete(filePath);
            }

            _db.Staff.Remove(staff);
            await _db.SaveChangesAsync();
            return (true, $"Employee '{staff.FullName}' removed. Vacancy is now vacant.");
        }

        public async Task<(string? PhotoUrl, string? FullUrl, string? Error)> UploadPhotoAsync(
            Guid id, IFormFile photo, string baseUrl)
        {
            var staff = await _db.Staff.FindAsync(id);
            if (staff == null) return (null, null, $"Staff {id} not found.");
            if (photo == null || photo.Length == 0) return (null, null, "No file uploaded.");

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext)) return (null, null, "Only jpg, jpeg, png, webp files are allowed.");
            if (photo.Length > 5 * 1024 * 1024) return (null, null, "File size must be under 5MB.");

            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "staff");
            Directory.CreateDirectory(uploadsDir);

            if (!string.IsNullOrWhiteSpace(staff.PhotoUrl))
            {
                var oldFile = Path.Combine(_env.WebRootPath,
                    staff.PhotoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(oldFile)) File.Delete(oldFile);
            }

            var fileName = $"staff_{id:N}_{Guid.NewGuid():N}{ext}";
            using (var stream = new FileStream(Path.Combine(uploadsDir, fileName), FileMode.Create))
                await photo.CopyToAsync(stream);

            staff.PhotoUrl = $"/uploads/staff/{fileName}";
            await _db.SaveChangesAsync();

            return (staff.PhotoUrl, $"{baseUrl}{staff.PhotoUrl}", null);
        }

        public async Task<(bool Success, string Message)> DeletePhotoAsync(Guid id)
        {
            var staff = await _db.Staff.FindAsync(id);
            if (staff == null) return (false, $"Staff {id} not found.");
            if (string.IsNullOrWhiteSpace(staff.PhotoUrl)) return (false, "No photo to delete.");

            var filePath = Path.Combine(_env.WebRootPath,
                staff.PhotoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(filePath)) File.Delete(filePath);

            staff.PhotoUrl = null;
            await _db.SaveChangesAsync();
            return (true, "Photo removed.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private IQueryable<Staff> WithIncludes() =>
            _db.Staff
               .Include(s => s.Vacancy)
                   .ThenInclude(v => v!.Organization)
                       .ThenInclude(o => o!.Parent)
                           .ThenInclude(p => p!.Parent);

        private static StaffDto MapToDto(Staff s)
        {
            var branch  = s.Vacancy?.Organization;
            var company = branch?.Parent;
            var country = company?.Parent;

            return new StaffDto
            {
                StaffId     = s.StaffId,
                FullName    = s.FullName,
                Email       = s.Email,
                Phone       = s.Phone,
                PhotoUrl    = s.PhotoUrl,
                VacancyId   = s.VacancyId,
                VacancyCode = s.Vacancy?.VacancyCode,
                JobTitle    = s.Vacancy?.JobTitle,
                BranchName  = branch?.Name,
                CompanyName = company?.Name,
                CountryName = country?.Name,
                JoiningDate = s.JoiningDate
            };
        }
    }
}
