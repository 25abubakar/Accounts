using System.ComponentModel.DataAnnotations;

namespace Accounts.Models.Scheduler;

public sealed class SchedulerRealtimeEvent
{
    public Guid Id { get; set; }
    public int TenantId { get; set; }
    [MaxLength(8000)] public string PayloadJson { get; set; }="";
    public DateTime CreatedUtc { get; set; }=DateTime.UtcNow;
    public DateTime? DeliveredUtc { get; set; }
}
