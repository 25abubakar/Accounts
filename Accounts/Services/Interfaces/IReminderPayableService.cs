using Accounts.Models.SpListRows;

namespace Accounts.Services.Interfaces;

public interface IReminderPayableService
{
    Task<IReadOnlyList<ReminderPayableListRow>> ListAsync(int tenantId, CancellationToken cancellationToken = default);
}
