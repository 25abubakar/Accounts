using Accounts.Data;
using Accounts.Models.SpListRows;
using Accounts.Services.Interfaces;

namespace Accounts.Services.Services;

public sealed class ReminderReceivableService(ApplicationDbContext db) : IReminderReceivableService
{
    public async Task<IReadOnlyList<ReminderReceivableListRow>> ListAsync(
        int tenantId,
        CancellationToken cancellationToken = default)
    {
        return await SpListQuery.ExecAsync<ReminderReceivableListRow>(
            db,
            "EXEC dbo.usp_Reminders_ReceivableList @TenantId",
            cancellationToken,
            SpListQuery.TenantId(tenantId));
    }
}
