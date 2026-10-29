using Accounts.DTOs;
using Accounts.Services.Interfaces;
using System.Text.Json;
using static Accounts.Services.Scheduler.SchedulerSqlStore;

namespace Accounts.Services.Scheduler;

public sealed class SchedulerOutboxPublisher(SchedulerSqlStore store,ScheduledTenantContext tenant) : IRealtimePublisher
{
    public Task PublishEventToTenantAsync(int tenantId,RealtimeEventDto message)
    {
        if(tenantId<=0||tenant.Id!=tenantId||message.TenantId!=tenantId)throw new InvalidOperationException("Invalid scheduler notification tenant.");
        return store.ExecuteAsync("dbo.usp_Scheduler_Events",CancellationToken.None,P("@Action","Enqueue"),P("@TenantId",tenantId),P("@EventId",message.EventId),P("@PayloadJson",JsonSerializer.Serialize(message,JsonOptions)));
    }
    public Task PublishEventToIdentityUserAsync(string id,RealtimeEventDto value)=>throw Unsupported();
    public Task PublishEventToPersonAsync(Guid id,RealtimeEventDto value)=>throw Unsupported();
    public Task PublishEventToStaffAsync(Guid id,RealtimeEventDto value)=>throw Unsupported();
    public Task PublishNotificationToIdentityUserAsync(string id,RealtimeNotificationDto value)=>throw Unsupported();
    public Task PublishNotificationToPersonAsync(Guid id,RealtimeNotificationDto value)=>throw Unsupported();
    private static Exception Unsupported()=>new NotSupportedException("Worker jobs publish only tenant-scoped domain events.");
}
