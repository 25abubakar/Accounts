using Accounts.Services.Scheduler;
using static Accounts.Services.Scheduler.SchedulerSqlStore;
namespace Accounts.Scheduler.Worker;

public static class WorkerOperations
{
    public static async Task RunAsync(IServiceProvider services,string[] args)
    {
        var store=services.GetRequiredService<SchedulerSqlStore>();
        var pause=Array.IndexOf(args,"--pause-system-job");var resume=Array.IndexOf(args,"--resume-system-job");
        var index=pause>=0?pause:resume;
        if(index>=0)
        {
            if(index+1>=args.Length||!Guid.TryParse(args[index+1],out var id))throw new ArgumentException("Supply the system job GUID.");
            await store.JsonAsync("dbo.usp_Scheduler_Enable",default,null,P("@TenantId",0),P("@Actor","SYSTEM: service operator"),P("@JobId",id),P("@Enabled",resume>=0),P("@Operator",true));
            Console.WriteLine($"System job {id}: {(resume>=0?"enabled":"paused")}.");
        }
        if(args.Contains("--system-jobs"))Console.WriteLine(await store.QueryAsync("SystemJobs",0,null,default,true));
    }
}
