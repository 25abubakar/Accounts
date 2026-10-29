using static Accounts.Services.Scheduler.SchedulerSqlStore;
namespace Accounts.Services.Scheduler;

public sealed class SchedulerQueue(SchedulerSqlStore store)
{
    public async Task<IReadOnlyList<long>> CollectAsync(CancellationToken ct)
        =>(await store.JsonAsync("dbo.usp_Scheduler_Collect",ct)).EnumerateArray().Select(x=>x.GetProperty("id").GetInt64()).ToArray();
}
