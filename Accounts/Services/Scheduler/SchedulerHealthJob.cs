using Accounts.Data;
using Accounts.Models.Scheduler;
using Accounts.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Scheduler;

public sealed class SchedulerHealthJob(SchedulerDbContext db) : IModuleJob
{
    public ModuleJobInfo Info => new("System.Health","System","Scheduler health check",true,"Checks scheduler database connectivity.");
    public async Task<string> ExecuteAsync(int tenantId,CancellationToken ct)
    {
        await db.Database.SqlQueryRaw<int>("SELECT 1 AS Value").SingleAsync(ct);
        return "Scheduler database is reachable; execution completed.";
    }
}
