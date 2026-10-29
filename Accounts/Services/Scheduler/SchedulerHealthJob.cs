using Accounts.Models.Scheduler;
using Accounts.Services.Interfaces;
namespace Accounts.Services.Scheduler;

public sealed class SchedulerHealthJob(SchedulerSqlStore store) : IModuleJob
{
    public ModuleJobInfo Info=>new("System.Health","System","Scheduler health check",true,"Internal database connectivity diagnostic.",SystemOnly:true);
    public async Task<string> ExecuteAsync(int tenantId,CancellationToken ct)
    {await store.QueryAsync("Session",0,null,ct,true);return "Scheduler database is reachable.";}
}
