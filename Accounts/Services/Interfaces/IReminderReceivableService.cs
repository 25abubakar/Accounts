using Accounts.Models.SpListRows;

namespace Accounts.Services.Interfaces;

public interface IReminderReceivableService
{
    Task<IReadOnlyList<ReminderReceivableListRow>> ListAsync(int tenantId, CancellationToken cancellationToken = default);
}
