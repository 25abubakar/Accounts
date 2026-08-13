using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    public class DesignationService
    {
        private readonly ApplicationDbContext _db;
        private readonly ITenantService _tenantService;

        public DesignationService(ApplicationDbContext db, ITenantService tenantService)
        {
            _db = db;
            _tenantService = tenantService;
        }

        public async Task<IReadOnlyList<Designation>> GetAllAsync() =>
            await _db.Designations.AsNoTracking()
                .OrderBy(d => d.Name)
                .ToListAsync();

        public async Task<IReadOnlyList<DesignationResponseDto>> GetAllWithCountAsync() =>
            await _db.Designations.AsNoTracking()
                .Select(d => new DesignationResponseDto
                {
                    Id = d.Id,
                    Name = d.Name,
                    AttendanceVisibilityScope = d.AttendanceVisibilityScope,
                    Count = _db.Vacancies.Count(v => v.DesignationId == d.Id)
                })
                .OrderBy(d => d.Name)
                .ToListAsync();

        public async Task<Designation?> GetByIdAsync(int id) =>
            await _db.Designations.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id);

        public async Task<int> UpsertByNameAsync(string name)
        {
            var normalized = name.Trim();

            var existing = await _db.Designations
                .FirstOrDefaultAsync(d => d.Name == normalized);

            if (existing != null)
                return existing.Id;

            var designation = new Designation
            {
                Name = normalized,
                TenantId = _tenantService.RequiredTenantId
            };
            _db.Designations.Add(designation);

            try
            {
                await _db.SaveChangesAsync();
                return designation.Id;
            }
            catch (DbUpdateException)
            {
                var race = await _db.Designations.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Name == normalized);
                return race?.Id
                    ?? throw new InvalidOperationException($"Failed to upsert designation '{normalized}'.");
            }
        }

        public async Task<bool> UpdateAsync(int id, string newName)
        {
            var designation = await _db.Designations.FindAsync(id);
            if (designation == null) return false;

            designation.Name = newName.Trim();
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateAttendanceScopeAsync(int id, AttendanceVisibilityScope scope)
        {
            if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
            var designation = await _db.Designations.FindAsync(id);
            if (designation == null) return false;
            designation.AttendanceVisibilityScope = scope;
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var designation = await _db.Designations.FindAsync(id);
            if (designation == null) return false;

            var isUsed = await _db.Vacancies.AnyAsync(v => v.DesignationId == id);
            if (isUsed)
                throw new InvalidOperationException("Cannot delete! This designation is attached to existing vacancies.");

            _db.Designations.Remove(designation);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<(int Id, string? Error)> ResolveAsync(int? designationId, string? designationName)
        {
            if (designationId.HasValue && designationId.Value > 0)
            {
                var exists = await _db.Designations.AnyAsync(d => d.Id == designationId.Value);
                if (!exists)
                    return (0, $"Designation Id {designationId.Value} not found.");
                return (designationId.Value, null);
            }

            if (!string.IsNullOrWhiteSpace(designationName))
            {
                var id = await UpsertByNameAsync(designationName);
                return (id, null);
            }

            return (0, "Either DesignationId or DesignationName must be provided.");
        }
    }

    public class DesignationResponseDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TitleName
        {
            get => Name;
            set => Name = value;
        }
        public int Count { get; set; }
        public AttendanceVisibilityScope AttendanceVisibilityScope { get; set; }
    }
}
