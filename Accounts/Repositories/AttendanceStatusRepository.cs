using Accounts.Data;
using Accounts.Models;
using Accounts.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Repositories;

public sealed class AttendanceStatusRepository : IAttendanceStatusRepository
{
    private readonly ApplicationDbContext _db;
    public AttendanceStatusRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<ProcessStatusStyle>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _db.AttendanceStatuses.AsNoTracking()
            .Include(x => x.Process).Include(x => x.Status).Include(x => x.ColorStyle)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Status.StatusName)
            .ToListAsync(cancellationToken);

    public Task<ProcessStatusStyle?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _db.AttendanceStatuses.Include(x => x.Process).Include(x => x.Status).Include(x => x.ColorStyle)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(string code, int? excludingId = null, CancellationToken cancellationToken = default) =>
        _db.AttendanceStatuses.AnyAsync(x => x.Code == code && (!excludingId.HasValue || x.Id != excludingId), cancellationToken);

    public Task<bool> NameExistsAsync(string statusName, int? excludingId = null, CancellationToken cancellationToken = default) =>
        _db.AttendanceStatuses.AnyAsync(x => x.Status.StatusName == statusName && (!excludingId.HasValue || x.Id != excludingId), cancellationToken);

    public Task AddAsync(ProcessStatusStyle status, CancellationToken cancellationToken = default) =>
        _db.ProcessStatusStyles.AddAsync(status, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _db.SaveChangesAsync(cancellationToken);
}
