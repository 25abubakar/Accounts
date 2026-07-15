using Accounts.Data;
using Accounts.Models;
using Accounts.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Repositories;

public sealed class AttendanceStatusRepository : IAttendanceStatusRepository
{
    private readonly ApplicationDbContext _db;
    public AttendanceStatusRepository(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AttendanceStatusMaster>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _db.AttendanceStatuses.AsNoTracking()
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.StatusName)
            .ToListAsync(cancellationToken);

    public Task<AttendanceStatusMaster?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _db.AttendanceStatuses.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(string code, int? excludingId = null, CancellationToken cancellationToken = default) =>
        _db.AttendanceStatuses.AnyAsync(x => x.Code == code && (!excludingId.HasValue || x.Id != excludingId), cancellationToken);

    public Task<bool> NameExistsAsync(string statusName, int? excludingId = null, CancellationToken cancellationToken = default) =>
        _db.AttendanceStatuses.AnyAsync(x => x.StatusName == statusName && (!excludingId.HasValue || x.Id != excludingId), cancellationToken);

    public Task AddAsync(AttendanceStatusMaster status, CancellationToken cancellationToken = default) =>
        _db.AttendanceStatuses.AddAsync(status, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _db.SaveChangesAsync(cancellationToken);
}
