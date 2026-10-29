using Accounts.DTOs;
using Accounts.Hubs;
using Accounts.Models.Scheduler;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;
using static Accounts.Services.Scheduler.SchedulerSqlStore;

namespace Accounts.Services.Scheduler;

// Transport only. All durable event state is read/acknowledged through stored procedures.
public sealed class SchedulerRealtimeRelay(SchedulerSqlStore store,SchedulerStorage storage,
    IHubContext<ApplicationRealtimeHub,IApplicationRealtimeClient> hub,ILogger<SchedulerRealtimeRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            try
            {
                await using var lease=await SchedulerSqlLease.TryAcquireAsync(storage.SchedulerConnection,"AccountsScheduler:RealtimeRelay",ct);
                if(lease!=null)
                {
                    var rows=(await store.JsonAsync("dbo.usp_Scheduler_Events",ct,null,P("@Action","Pending"))).Deserialize<List<SchedulerRealtimeEvent>>(JsonOptions)??[];
                    foreach(var row in rows)
                    {
                        var message=JsonSerializer.Deserialize<RealtimeEventDto>(row.PayloadJson,JsonOptions)??throw new InvalidOperationException("Invalid notification.");
                        if(row.TenantId<=0||message.TenantId!=row.TenantId)throw new InvalidOperationException("Notification tenant mismatch.");
                        await hub.Clients.Group(RealtimeGroups.Tenant(row.TenantId)).ReceiveEvent(message);
                        await store.ExecuteAsync("dbo.usp_Scheduler_Events",ct,P("@Action","Ack"),P("@TenantId",row.TenantId),P("@EventId",row.Id));
                    }
                }
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
            catch(Exception error){logger.LogWarning(error,"Scheduler notification delivery will retry.");}
            try{await Task.Delay(TimeSpan.FromSeconds(2),ct);}catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
        }
    }
}
