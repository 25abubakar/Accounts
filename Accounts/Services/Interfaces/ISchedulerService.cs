using Accounts.DTOs.Scheduler;
using System.Text.Json;

namespace Accounts.Services.Interfaces;

public interface ISchedulerService
{
    Task<JsonElement> SessionAsync(CancellationToken ct);
    Task<JsonElement> CatalogAsync(CancellationToken ct);
    Task<JsonElement> JobsAsync(CancellationToken ct);
    Task<JsonElement> UpcomingAsync(CancellationToken ct);
    Task<JsonElement> RunsAsync(Guid? id,CancellationToken ct);
    Task<IReadOnlyList<DateTime>> PreviewAsync(ScheduleEdit input,Guid? id,CancellationToken ct);
    Task<object> SaveAsync(Guid? id,ScheduleEdit input,CancellationToken ct);
    Task<object> DeleteAsync(Guid id,string? rowVersion,CancellationToken ct);
    Task<object> EnableAsync(Guid id,EnableSchedule input,CancellationToken ct);
    Task<JsonElement> RunAsync(Guid id,string? requestKey,CancellationToken ct);
}
