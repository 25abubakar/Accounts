using Accounts.Models.Scheduler;
using System.Text.Json;
namespace Accounts.Services.Scheduler;

public sealed class SchedulerCalendar(SchedulerSqlStore store)
{
    public Task<DateTime?> NextAfterAsync(ScheduleDefinition value,DateTime after,CancellationToken ct)
        =>store.NextAsync(value.Id,value.TenantId,JsonSerializer.Serialize(value,SchedulerSqlStore.JsonOptions),after,ct);
}
