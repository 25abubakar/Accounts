using Accounts.Data;
using Accounts.Models.Scheduler;
using Accounts.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Scheduler;

public sealed class BusinessReadConnection(SchedulerStorage storage)
{
    public async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var settings=new SqlConnectionStringBuilder(storage.BusinessConnection) { ApplicationIntent=ApplicationIntent.ReadOnly };
        var connection=new SqlConnection(settings.ConnectionString);
        try { await connection.OpenAsync(ct);return connection; }
        catch { await connection.DisposeAsync();throw; }
    }
}
