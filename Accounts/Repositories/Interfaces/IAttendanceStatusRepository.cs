using Accounts.Models;

namespace Accounts.Repositories.Interfaces;

public interface IAttendanceStatusRepository
{
    Task<IReadOnlyList<ProcessStatusStyle>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ProcessStatusStyle?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> CodeExistsAsync(string code, int? excludingId = null, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string statusName, int? excludingId = null, CancellationToken cancellationToken = default);
    Task AddAsync(ProcessStatusStyle status, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
