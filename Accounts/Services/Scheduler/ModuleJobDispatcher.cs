using Accounts.Models.Scheduler;
using Accounts.Services.Interfaces;
using System.Text.Json;
using static Accounts.Services.Scheduler.SchedulerSqlStore;

namespace Accounts.Services.Scheduler;

public sealed record SchedulerExecution(long RunNumber,Guid JobId,int TenantId,string Handler,string ConcurrencyKey,int TimeoutSeconds);

public sealed class ModuleJobDispatcher(SchedulerSqlStore store,IEnumerable<IModuleJob> handlers,SchedulerStorage storage,
    ScheduledTenantContext executionTenant,ILogger<ModuleJobDispatcher> logger)
{
    public async Task ExecuteAsync(long runNumber,CancellationToken ct)
    {
        var candidate=(await store.JsonAsync("dbo.usp_Scheduler_Execution",ct,null,P("@RunNumber",runNumber))).Deserialize<SchedulerExecution>(JsonOptions);
        if(candidate==null)return;
        await using var lease=await SchedulerSqlLease.TryAcquireAsync(storage.SchedulerConnection,
            $"AccountsScheduler:Handler:{candidate.TenantId}:{candidate.ConcurrencyKey}",ct);
        if(lease==null)return;
        var owner=Guid.NewGuid();
        var execution=(await store.JsonAsync("dbo.usp_Scheduler_Claim",ct,lease.Connection,P("@RunNumber",runNumber),P("@Owner",owner))).Deserialize<SchedulerExecution>(JsonOptions);
        if(execution==null)return;
        var outcome="Failure";string? result=null;string? error=null;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(execution.TimeoutSeconds));
        try
        {
            var handler=handlers.SingleOrDefault(x=>x.Info.Key==execution.Handler)??throw new InvalidOperationException("This worker does not implement the registered job.");
            if(handler.Info.SystemOnly!=(execution.TenantId==0)||
               (handler.Info.ConcurrencyKey??handler.Info.Key)!=execution.ConcurrencyKey)
                throw new InvalidOperationException("Worker registration does not match the database job scope.");
            executionTenant.Id=execution.TenantId;
            result=await handler.ExecuteAsync(execution.TenantId,timeout.Token);outcome="Success";
        }
        catch(Exception exception)
        {
            outcome=ct.IsCancellationRequested?"Cancelled":"Failure";
            error=exception is OperationCanceledException?"Execution was cancelled or timed out.":exception.Message;
            logger.LogWarning(exception,"Scheduled execution {RunNumber} did not complete.",runNumber);
        }
        await store.JsonAsync("dbo.usp_Scheduler_Complete",CancellationToken.None,lease.Connection,
            P("@RunNumber",runNumber),P("@Owner",owner),P("@Outcome",outcome),P("@Result",Trim(result,2500)),P("@Error",Trim(error,1000)));
    }
    private static string? Trim(string? value,int length)=>value is null?null:value[..Math.Min(value.Length,length)];
}
