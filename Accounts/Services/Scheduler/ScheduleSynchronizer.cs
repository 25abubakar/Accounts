using static Accounts.Services.Scheduler.SchedulerSqlStore;
namespace Accounts.Services.Scheduler;

public sealed class ScheduleSynchronizer(SchedulerSqlStore store)
{
    public Task ApplyAsync(Guid id,CancellationToken ct=default)=>store.ExecuteAsync("dbo.usp_Scheduler_Sync",ct,P("@JobId",id));
}
