using Accounts.Models;

namespace Accounts.Repositories.Interfaces;

public interface IAttendanceStatusRepository
{
    Task<IReadOnlyList<AttendanceStatusMaster>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<AttendanceStatusMaster?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> CodeExistsAsync(string code, int? excludingId = null, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string statusName, int? excludingId = null, CancellationToken cancellationToken = default);
    Task AddAsync(AttendanceStatusMaster status, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
