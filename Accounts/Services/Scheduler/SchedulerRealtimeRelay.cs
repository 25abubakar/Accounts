using Accounts.Data;
using Accounts.DTOs;
using Accounts.Hubs;
using Accounts.Models.Scheduler;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Accounts.Services.Scheduler;

// Notification transport only, not a business timer. Worker business execution remains independent of the API.
public sealed class SchedulerRealtimeRelay(IServiceScopeFactory scopes, IHubContext<ApplicationRealtimeHub,IApplicationRealtimeClient> hub,
    SchedulerStorage storage, ILogger<SchedulerRealtimeRelay> logger) : BackgroundService
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
                    await using var scope=scopes.CreateAsyncScope();
                    var db=scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
                    var rows=await db.RealtimeEvents.IgnoreQueryFilters().Where(x=>x.DeliveredUtc==null).OrderBy(x=>x.CreatedUtc).Take(100).ToListAsync(ct);
                    foreach(var row in rows)
                    {
                        var message=JsonSerializer.Deserialize<RealtimeEventDto>(row.PayloadJson)??throw new InvalidOperationException("Invalid notification.");
                        if(row.TenantId<=0||message.TenantId!=row.TenantId)throw new InvalidOperationException("Notification tenant mismatch.");
                        await hub.Clients.Group(RealtimeGroups.Tenant(row.TenantId)).ReceiveEvent(message);
                        row.DeliveredUtc=DateTime.UtcNow;
                    }
                    if(rows.Count>0)await db.SaveChangesAsync(ct);
                }
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogWarning(ex,"Scheduler notification delivery will retry.");}
            try{await Task.Delay(TimeSpan.FromSeconds(2),ct);}catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
        }
    }
}
