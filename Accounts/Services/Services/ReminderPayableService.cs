using Accounts.Data;
using Accounts.Models.SpListRows;
using Accounts.Services.Interfaces;

namespace Accounts.Services.Services;

public sealed class ReminderPayableService(ApplicationDbContext db) : IReminderPayableService
{
    public async Task<IReadOnlyList<ReminderPayableListRow>> ListAsync(
        int tenantId,
        CancellationToken cancellationToken = default)
    {
        return await SpListQuery.ExecAsync<ReminderPayableListRow>(
            db,
            "EXEC dbo.usp_Reminders_PayableList @TenantId",
            cancellationToken,
            SpListQuery.TenantId(tenantId));
    }
}
