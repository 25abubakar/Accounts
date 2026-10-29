using Accounts.DTOs.Scheduler;
using Accounts.Services.Interfaces;
using System.Data;
using System.Text.Json;
using static Accounts.Services.Scheduler.SchedulerSqlStore;

namespace Accounts.Services.Scheduler;

public sealed class SchedulerApplicationService(SchedulerSqlStore store,SchedulerTenant tenant) : ISchedulerService
{
    public Task<JsonElement> SessionAsync(CancellationToken ct)=>store.QueryAsync("Session",tenant.Id,null,ct);
    public Task<JsonElement> CatalogAsync(CancellationToken ct)=>store.QueryAsync("Catalog",tenant.Id,null,ct);
    public Task<JsonElement> JobsAsync(CancellationToken ct)=>store.QueryAsync("Jobs",tenant.Id,null,ct);
    public Task<JsonElement> UpcomingAsync(CancellationToken ct)=>store.QueryAsync("Upcoming",tenant.Id,null,ct);
    public Task<JsonElement> RunsAsync(Guid? id,CancellationToken ct)=>store.QueryAsync("Runs",tenant.Id,id,ct);
    public async Task<IReadOnlyList<DateTime>> PreviewAsync(ScheduleEdit input,Guid? id,CancellationToken ct)
    {
        var result=await store.JsonAsync("dbo.usp_Scheduler_Preview",ct,null,P("@TenantId",tenant.Id),P("@InputJson",Serialize(input)),P("@JobId",id,SqlDbType.UniqueIdentifier));
        return result.EnumerateArray().Select(x=>DateTime.SpecifyKind(x.GetProperty("due").GetDateTime(),DateTimeKind.Utc)).ToArray();
    }
    public async Task<object> SaveAsync(Guid? id,ScheduleEdit input,CancellationToken ct)
    {
        var definition=await store.JsonAsync("dbo.usp_Scheduler_Save",ct,null,P("@TenantId",tenant.Id),P("@Actor",tenant.Actor),P("@InputJson",Serialize(input)),
            P("@JobId",id,SqlDbType.UniqueIdentifier),P("@ExpectedVersion",id.HasValue?Version(input.RowVersion):null,SqlDbType.VarBinary,8));
        return new{definition,syncPending=false};
    }
    public async Task<object> EnableAsync(Guid id,EnableSchedule input,CancellationToken ct)
    {
        var definition=await store.JsonAsync("dbo.usp_Scheduler_Enable",ct,null,P("@TenantId",tenant.Id),P("@Actor",tenant.Actor),P("@JobId",id),
            P("@Enabled",input.Enabled),P("@ExpectedVersion",Version(input.RowVersion)));
        return new{definition,syncPending=false};
    }
    public async Task<object> DeleteAsync(Guid id,string? rowVersion,CancellationToken ct)
    {
        await store.ExecuteAsync("dbo.usp_Scheduler_Delete",ct,P("@TenantId",tenant.Id),P("@Actor",tenant.Actor),P("@JobId",id),P("@ExpectedVersion",Version(rowVersion)));
        return new{deleted=true,id};
    }
    public Task<JsonElement> RunAsync(Guid id,string? requestKey,CancellationToken ct)
    {
        if(!Guid.TryParse(requestKey,out var key)||key==Guid.Empty)throw new SchedulerFault(400,"A UUID idempotency key is required.");
        return store.JsonAsync("dbo.usp_Scheduler_RunNow",ct,null,P("@TenantId",tenant.Id),P("@Actor",tenant.Actor),P("@JobId",id),P("@RequestKey",key));
    }
    private static string Serialize(ScheduleEdit input)
    {
        if(input.StartLocal.Kind!=DateTimeKind.Unspecified)throw new SchedulerFault(400,"Start must be a Pakistan local date/time without a UTC suffix.");
        return JsonSerializer.Serialize(input,JsonOptions);
    }
    private static byte[] Version(string? text)
    {
        try{var value=Convert.FromBase64String(text??"");if(value.Length==8)return value;}catch(FormatException){}
        throw new SchedulerFault(400,"A valid row version is required.");
    }
}
