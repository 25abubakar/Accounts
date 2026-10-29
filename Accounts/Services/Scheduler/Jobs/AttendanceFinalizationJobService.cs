using Accounts.Data;
using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Scheduler.Jobs;

// The Windows scheduler invokes one tenant at a time. No startup/hourly loop remains here.
public sealed class AttendanceFinalizationJobService(ApplicationDbContext db,
    AttendanceFinalizationService service, IRealtimePublisher realtime, ILogger<AttendanceFinalizationJobService> logger)
{
    public async Task<int> RunTenantAsync(int tenantId, CancellationToken ct)
    {
        if (!await db.Tenants.AsNoTracking().AnyAsync(x => x.Id == tenantId && x.IsActive, ct))
            throw new InvalidOperationException("The scheduled tenant is not active.");
        var zone = await db.AttendancePolicies.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.IsActive && (x.TenantId == tenantId || x.TenantId == null))
            .OrderByDescending(x => x.TenantId == tenantId).Select(x => x.TimeZoneId).FirstOrDefaultAsync(ct);
        var today = BusinessClock.Today(zone);
        var changes = await service.RefreshPeriodAsync(tenantId, today.Year, today.Month, ct);
        if (changes > 0)
            await realtime.PublishEventToTenantAsync(tenantId, RealtimeEventDto.Create(
                RealtimeEventTypes.DeductionChanged, "deduction", "attendance-finalized", tenantId,
                data: new Dictionary<string, string> { ["year"] = today.Year.ToString(), ["month"] = today.Month.ToString() }));
        logger.LogInformation("Attendance finalizer updated {Count} rows for tenant {TenantId}.", changes, tenantId);
        return changes;
    }
}
