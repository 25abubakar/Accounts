using static Accounts.Services.Scheduler.SchedulerSqlStore;
namespace Accounts.Services.Scheduler;

public sealed class SchedulerDefinitionPlan(SchedulerSqlStore store,SchedulerTenant tenant)
{
    public async Task<int> InstallAsync(CancellationToken ct,bool enableNew=true)=>(await Call("Install",0,enableNew,ct)).GetProperty("added").GetInt32();
    public async Task<int> ActivateStagedAsync(CancellationToken ct)=>(await Call("Activate",0,true,ct)).GetProperty("activated").GetInt32();
    public async Task<int> RemoveLegacyTestJobsAsync(CancellationToken ct)=>(await Call("RemoveTests",0,false,ct)).GetProperty("removed").GetInt32();
    public async Task<bool> QueueAssessmentAsync(int tenantId,CancellationToken ct)=>(await Call("QueueAssessment",tenantId,true,ct)).GetProperty("queued").GetBoolean();
    public async Task RuleChangedAsync(int tenantId,CancellationToken ct)=>_ = await Call("RuleChanged",tenantId,true,ct);
    private Task<System.Text.Json.JsonElement> Call(string action,int tenantId,bool enabled,CancellationToken ct)
        =>store.JsonAsync("dbo.usp_Scheduler_Plan",ct,null,P("@Action",action),P("@TenantId",tenantId),P("@Actor",tenant.Actor),P("@EnableNew",enabled));
}
