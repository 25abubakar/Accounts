using Accounts.Data;
using Accounts.Models;
using Accounts.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using Accounts.Services.Interfaces;

namespace Accounts.Repositories;

public sealed class AttendanceStatusRepository : IAttendanceStatusRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;
    public AttendanceStatusRepository(ApplicationDbContext db, ITenantService tenant) { _db = db; _tenant = tenant; }

    private IQueryable<ProcessStatusStyle> Visible() => _db.AttendanceStatuses
        .Where(x => x.TenantId == null || (_tenant.TenantId.HasValue && x.TenantId == _tenant.TenantId));

    public async Task<IReadOnlyList<ProcessStatusStyle>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Visible().AsNoTracking()
            .Include(x => x.Process).Include(x => x.Status).Include(x => x.ColorStyle)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Status.StatusName)
            .ToListAsync(cancellationToken);

    public Task<ProcessStatusStyle?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Visible().Include(x => x.Process).Include(x => x.Status).Include(x => x.ColorStyle)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(string code, int? excludingId = null, CancellationToken cancellationToken = default) =>
        Visible().AnyAsync(x => x.TenantId == _tenant.TenantId && x.Code == code && (!excludingId.HasValue || x.Id != excludingId), cancellationToken);

    public Task<bool> NameExistsAsync(string statusName, int? excludingId = null, CancellationToken cancellationToken = default) =>
        Visible().AnyAsync(x => x.TenantId == _tenant.TenantId && x.Status.StatusName == statusName && (!excludingId.HasValue || x.Id != excludingId), cancellationToken);

    public Task AddAsync(ProcessStatusStyle status, CancellationToken cancellationToken = default) =>
        _db.ProcessStatusStyles.AddAsync(status, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _db.SaveChangesAsync(cancellationToken);
}
