using System.ComponentModel.DataAnnotations;

namespace Accounts.Models.Scheduler;

public sealed class SchedulerCommand
{
    public Guid Id { get; set; }
    public int TenantId { get; set; }
    public Guid ScheduleId { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DispatchedUtc { get; set; }
}
