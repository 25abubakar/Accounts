using Accounts.Data;
using Accounts.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class StaffController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment  _env;

        public StaffController(ApplicationDbContext db, IWebHostEnvironment env)
        {
            _db  = db;
            _env = env;
        }

        // GET /api/staff
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var list = await GetStaffWithIncludes().ToListAsync();
            return Ok(list.Select(MapToDto));
        }

        // GET /api/staff/{id}
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var s = await GetStaffWithIncludes().FirstOrDefaultAsync(x => x.StaffId == id);
            if (s == null) return NotFound(new { message = $"Staff {id} not found." });
            return Ok(MapToDto(s));
        }

        // GET /api/staff/search?q=ali
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(new { message = "Query 'q' is required." });

            var list = await GetStaffWithIncludes()
                .Where(s => s.FullName.Contains(q) || (s.Email != null && s.Email.Contains(q)))
                .ToListAsync();

            return Ok(list.Select(MapToDto));
        }

        // POST /api/staff/hire/{vacancyId}
        [HttpPost("hire/{vacancyId:guid}")]
        public async Task<IActionResult> Hire(Guid vacancyId, [FromBody] HireStaffDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var vacancy = await _db.Vacancies.FindAsync(vacancyId);
            if (vacancy == null)
                return NotFound(new { message = $"Vacancy {vacancyId} not found." });

            if (vacancy.IsFilled)
                return BadRequest(new { message = $"Vacancy '{vacancy.VacancyCode}' is already filled." });

            var staff = new Staff
            {
                StaffId     = Guid.NewGuid(),
                FullName    = dto.FullName,
                Email       = dto.Email,
                Phone       = dto.Phone,
                VacancyId   = vacancyId,
                JoiningDate = DateTime.UtcNow
            };

            _db.Staff.Add(staff);
            vacancy.IsFilled = true;
            await _db.SaveChangesAsync();

            var created = await GetStaffWithIncludes().FirstOrDefaultAsync(s => s.StaffId == staff.StaffId);
            return CreatedAtAction(nameof(GetById), new { id = staff.StaffId }, MapToDto(created!));
        }

        // PUT /api/staff/{id}
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStaffDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var staff = await _db.Staff.FindAsync(id);
            if (staff == null) return NotFound(new { message = $"Staff {id} not found." });

            staff.FullName = dto.FullName;
            staff.Email    = dto.Email;
            staff.Phone    = dto.Phone;

            await _db.SaveChangesAsync();

            var updated = await GetStaffWithIncludes().FirstOrDefaultAsync(s => s.StaffId == id);
            return Ok(MapToDto(updated!));
        }

        // POST /api/staff/{id}/upload-photo
        [HttpPost("{id:guid}/upload-photo")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadPhoto(Guid id, IFormFile photo)
        {
            var staff = await _db.Staff.FindAsync(id);
            if (staff == null) return NotFound(new { message = $"Staff {id} not found." });

            if (photo == null || photo.Length == 0)
                return BadRequest(new { message = "No file uploaded." });

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext))
                return BadRequest(new { message = "Only jpg, jpeg, png, webp files are allowed." });

            if (photo.Length > 5 * 1024 * 1024)
                return BadRequest(new { message = "File size must be under 5MB." });

            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "staff");
            Directory.CreateDirectory(uploadsDir);

            if (!string.IsNullOrWhiteSpace(staff.PhotoUrl))
            {
                var oldFile = Path.Combine(_env.WebRootPath,
                    staff.PhotoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(oldFile))
                    System.IO.File.Delete(oldFile);
            }

            var fileName = $"staff_{id:N}_{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
                await photo.CopyToAsync(stream);

            staff.PhotoUrl = $"/uploads/staff/{fileName}";
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message  = "Photo uploaded successfully.",
                photoUrl = staff.PhotoUrl,
                fullUrl  = $"{Request.Scheme}://{Request.Host}{staff.PhotoUrl}"
            });
        }

        // DELETE /api/staff/{id}/photo
        [HttpDelete("{id:guid}/photo")]
        public async Task<IActionResult> DeletePhoto(Guid id)
        {
            var staff = await _db.Staff.FindAsync(id);
            if (staff == null) return NotFound(new { message = $"Staff {id} not found." });

            if (string.IsNullOrWhiteSpace(staff.PhotoUrl))
                return BadRequest(new { message = "No photo to delete." });

            var filePath = Path.Combine(_env.WebRootPath,
                staff.PhotoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);

            staff.PhotoUrl = null;
            await _db.SaveChangesAsync();

            return Ok(new { message = "Photo removed." });
        }

        // PUT /api/staff/{id}/transfer
        [HttpPut("{id:guid}/transfer")]
        public async Task<IActionResult> Transfer(Guid id, [FromBody] TransferStaffDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var staff = await _db.Staff.FindAsync(id);
            if (staff == null) return NotFound(new { message = $"Staff {id} not found." });

            var newVacancy = await _db.Vacancies.FindAsync(dto.NewVacancyId);
            if (newVacancy == null)
                return NotFound(new { message = $"Vacancy {dto.NewVacancyId} not found." });

            if (newVacancy.IsFilled)
                return BadRequest(new { message = $"Vacancy '{newVacancy.VacancyCode}' is already filled." });

            // Free old vacancy
            if (staff.VacancyId.HasValue)
            {
                var oldVacancy = await _db.Vacancies.FindAsync(staff.VacancyId.Value);
                if (oldVacancy != null) oldVacancy.IsFilled = false;
            }

            staff.VacancyId     = dto.NewVacancyId;
            newVacancy.IsFilled = true;
            await _db.SaveChangesAsync();

            var updated = await GetStaffWithIncludes().FirstOrDefaultAsync(s => s.StaffId == id);
            return Ok(MapToDto(updated!));
        }

        // DELETE /api/staff/{id}
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var staff = await _db.Staff.FindAsync(id);
            if (staff == null) return NotFound(new { message = $"Staff {id} not found." });

            if (staff.VacancyId.HasValue)
            {
                var vacancy = await _db.Vacancies.FindAsync(staff.VacancyId.Value);
                if (vacancy != null) vacancy.IsFilled = false;
            }

            if (!string.IsNullOrWhiteSpace(staff.PhotoUrl))
            {
                var filePath = Path.Combine(_env.WebRootPath,
                    staff.PhotoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);
            }

            _db.Staff.Remove(staff);
            await _db.SaveChangesAsync();

            return Ok(new { message = $"Employee '{staff.FullName}' removed. Vacancy is now vacant." });
        }

        // ── HELPERS ───────────────────────────────────────────────────────────

        private IQueryable<Staff> GetStaffWithIncludes() =>
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
