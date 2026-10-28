using System.ComponentModel.DataAnnotations;

namespace Accounts.Models.Scheduler;

public sealed class ScheduleRun
{
    public long Id { get; set; }
    public Guid ScheduleId { get; set; }
    public int TenantId { get; set; }
    [MaxLength(140)] public string Occurrence { get; set; } = "";
    public DateTime ScheduledUtc { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    [MaxLength(30)] public string Status { get; set; } = "Running";
    public int Attempt { get; set; }
    public bool IsManual { get; set; }
    public int ScheduleRevision { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    [MaxLength(2500)] public string? Result { get; set; }
    [MaxLength(1000)] public string? Error { get; set; }
}
