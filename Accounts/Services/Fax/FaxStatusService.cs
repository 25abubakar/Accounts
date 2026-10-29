using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;

namespace Accounts.Services.Fax;

public sealed class FaxStatusService(ApplicationDbContext db,IRealtimePublisher realtime) : IFaxStatusService
{
    private static readonly Dictionary<string,IReadOnlySet<string>> Allowed=new(StringComparer.OrdinalIgnoreCase)
    {
        [FaxStatuses.Preparing]=new HashSet<string>(StringComparer.OrdinalIgnoreCase){FaxStatuses.Submitting,FaxStatuses.Failed},
        [FaxStatuses.Submitting]=new HashSet<string>(StringComparer.OrdinalIgnoreCase){FaxStatuses.Accepted,FaxStatuses.Failed,FaxStatuses.AcceptanceUnknown},
        [FaxStatuses.Accepted]=new HashSet<string>(StringComparer.OrdinalIgnoreCase){FaxStatuses.Transmitting,FaxStatuses.Delivered,FaxStatuses.Failed,FaxStatuses.ReconciliationRequired},
        [FaxStatuses.Transmitting]=new HashSet<string>(StringComparer.OrdinalIgnoreCase){FaxStatuses.Delivered,FaxStatuses.Failed,FaxStatuses.ReconciliationRequired},
        [FaxStatuses.AcceptanceUnknown]=new HashSet<string>(StringComparer.OrdinalIgnoreCase){FaxStatuses.Accepted,FaxStatuses.Delivered,FaxStatuses.Failed,FaxStatuses.ReconciliationRequired},
        [FaxStatuses.ReconciliationRequired]=new HashSet<string>(StringComparer.OrdinalIgnoreCase){FaxStatuses.Accepted,FaxStatuses.Transmitting,FaxStatuses.Delivered,FaxStatuses.Failed},
    };
    public async Task TransitionAsync(FaxTransmission row,string status,string source,string? detail,string? providerEventReference,string? actorUserId,CancellationToken ct)
    {
        if(string.Equals(row.Status,status,StringComparison.OrdinalIgnoreCase)) return;
        if(FaxStatuses.Terminal.Contains(row.Status) || !Allowed.TryGetValue(row.Status,out var next) || !next.Contains(status)) throw new InvalidOperationException($"Invalid fax status transition from {row.Status} to {status}.");
        var previous=row.Status; row.Status=status; row.LastCheckedAtUtc=DateTime.UtcNow;
        if(status==FaxStatuses.Accepted && row.AcceptedAtUtc==null) row.AcceptedAtUtc=DateTime.UtcNow;
        if(status==FaxStatuses.Delivered) row.DeliveredAtUtc=DateTime.UtcNow;
        db.FaxStatusEvents.Add(new FaxStatusEvent{TenantId=row.TenantId,TransmissionId=row.Id,PreviousStatus=previous,Status=status,Source=source,Detail=detail,ProviderEventReference=providerEventReference,ActorUserId=actorUserId});
        await db.SaveChangesAsync(ct);
        await realtime.PublishEventToTenantAsync(row.TenantId,RealtimeEventDto.Create("fax.status.changed","Fax",status,row.TenantId,row.Id.ToString(),new Dictionary<string,string>{{"reference",row.Reference},{"status",status}}));
    }
}
