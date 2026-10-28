using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models.Scheduler;
using Accounts.Services.Interfaces;
using System.Text.Json;

namespace Accounts.Services.Scheduler;

// Worker commits notification metadata to SQL; only the API owns connected SignalR clients.
public sealed class SchedulerOutboxPublisher(SchedulerDbContext db, ScheduledTenantContext tenant) : IRealtimePublisher
{
    public async Task PublishEventToTenantAsync(int tenantId, RealtimeEventDto message)
    {
        if(tenantId<=0||tenant.Id!=tenantId||message.TenantId!=tenantId)throw new InvalidOperationException("Invalid scheduler notification tenant.");
        var payload=JsonSerializer.Serialize(message);
        if(payload.Length>8000)throw new InvalidOperationException("Scheduler notification is too large.");
        db.RealtimeEvents.Add(new SchedulerRealtimeEvent{Id=message.EventId,TenantId=tenantId,PayloadJson=payload});
        await db.SaveChangesAsync();
    }
    public Task PublishEventToIdentityUserAsync(string id,RealtimeEventDto message)=>throw Unsupported();
    public Task PublishEventToPersonAsync(Guid id,RealtimeEventDto message)=>throw Unsupported();
    public Task PublishEventToStaffAsync(Guid id,RealtimeEventDto message)=>throw Unsupported();
    public Task PublishNotificationToIdentityUserAsync(string id,RealtimeNotificationDto message)=>throw Unsupported();
    public Task PublishNotificationToPersonAsync(Guid id,RealtimeNotificationDto message)=>throw Unsupported();
    private static NotSupportedException Unsupported()=>new("Worker jobs publish only tenant-scoped domain events.");
}
