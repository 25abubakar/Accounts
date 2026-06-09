using Accounts.Data;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    /// <summary>
    /// Manages the normalized JobTitles lookup table.
    ///
    /// Upsert contract:
    ///   - Frontend sends an Id   → validate it exists, use directly.
    ///   - Frontend sends a Name  → insert if not exists, return the Id.
    /// Prevents duplicate title strings and enforces the ID-based handshake.
    /// </summary>
    public class JobTitleService
    {
        private readonly ApplicationDbContext _db;
        public JobTitleService(ApplicationDbContext db) => _db = db;

        /// <summary>Get all job titles (for dropdown population).</summary>
        public async Task<IReadOnlyList<JobTitle>> GetAllAsync() =>
            await _db.JobTitles.AsNoTracking()
                .OrderBy(jt => jt.TitleName)
                .ToListAsync();

        /// <summary>Get a single title by Id.</summary>
        public async Task<JobTitle?> GetByIdAsync(int id) =>
            await _db.JobTitles.AsNoTracking()
                .FirstOrDefaultAsync(jt => jt.Id == id);

        /// <summary>
        /// Upsert by name: find existing (case-insensitive) or insert new.
        /// Returns the stable integer Id — never inserts duplicates.
        /// </summary>
        public async Task<int> UpsertByNameAsync(string titleName)
        {
            var normalized = titleName.Trim();

            var existing = await _db.JobTitles
                .FirstOrDefaultAsync(jt => jt.TitleName == normalized);

            if (existing != null)
                return existing.Id;

            var newTitle = new JobTitle { TitleName = normalized };
            _db.JobTitles.Add(newTitle);

            try
            {
                await _db.SaveChangesAsync();
                return newTitle.Id;
            }
            catch (DbUpdateException)
            {
                // Race condition: another request inserted the same name concurrently.
                var race = await _db.JobTitles.AsNoTracking()
                    .FirstOrDefaultAsync(jt => jt.TitleName == normalized);
                return race?.Id
                    ?? throw new InvalidOperationException($"Failed to upsert JobTitle '{normalized}'.");
            }
        }

        /// <summary>
        /// Resolves a vacancy write request to a JobTitleId.
        /// Accepts either a numeric Id OR a new string name — never both.
        /// Returns (id, error). If error is non-null the id is 0.
        /// </summary>
        public async Task<(int Id, string? Error)> ResolveAsync(
            int? jobTitleId, string? jobTitleName)
        {
            if (jobTitleId.HasValue && jobTitleId.Value > 0)
            {
                var exists = await _db.JobTitles.AnyAsync(jt => jt.Id == jobTitleId.Value);
                if (!exists)
                    return (0, $"JobTitle Id {jobTitleId.Value} not found.");
                return (jobTitleId.Value, null);
            }

            if (!string.IsNullOrWhiteSpace(jobTitleName))
            {
                var id = await UpsertByNameAsync(jobTitleName);
                return (id, null);
            }

            return (0, "Either JobTitleId or JobTitleName must be provided.");
        }
    }
}
