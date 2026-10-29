using System.ComponentModel.DataAnnotations;

namespace Accounts.Models.Scheduler;

// The only scheduler-owned physical table. Row kinds have SQL checks and filtered indexes.
public sealed class SchedulerTaskRecord
{
    public long RecordId { get; set; }
    [MaxLength(12)] public string RecordKind { get; set; }="";
    public Guid EntityId { get; set; }
    public int TenantId { get; set; }
    public long? ParentRecordId { get; set; }
    public long? RunNumber { get; set; }
    [MaxLength(120)] public string? Name { get; set; }
    [MaxLength(100)] public string? Handler { get; set; }
    [MaxLength(100)] public string? Module { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    [MaxLength(100)] public string? ConcurrencyKey { get; set; }
    public bool? SystemOnly { get; set; }
    public bool? ReadOnly { get; set; }
    [MaxLength(30)] public string? Mode { get; set; }
    public int? IntervalDays { get; set; }
    public int? IntervalMinutes { get; set; }
    public int? DayOfMonth { get; set; }
    public int? DayOfWeek { get; set; }
    [MaxLength(5)] public string? Time { get; set; }
    public DateTime? StartLocal { get; set; }
    [MaxLength(80)] public string? TimeZone { get; set; }
    [MaxLength(20)] public string? MisfirePolicy { get; set; }
    public bool? Enabled { get; set; }
    public int? TimeoutSeconds { get; set; }
    public int? RetryCount { get; set; }
    public int? Revision { get; set; }
    public int? RuntimeRevision { get; set; }
    [MaxLength(100)] public string? ManagedKey { get; set; }
    public Guid? CreationKey { get; set; }
    public DateTime? NextRunUtc { get; set; }
    [MaxLength(140)] public string? Occurrence { get; set; }
    [MaxLength(30)] public string? Status { get; set; }
    public int? Attempt { get; set; }
    public bool? IsManual { get; set; }
    public int? ScheduleRevision { get; set; }
    public DateTime? ScheduledUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public Guid? LeaseOwner { get; set; }
    [MaxLength(2500)] public string? Result { get; set; }
    [MaxLength(1000)] public string? Error { get; set; }
    public string? PayloadJson { get; set; }
    [MaxLength(256)] public string? SettingValue { get; set; }
    public DateTime? DispatchedUtc { get; set; }
    public DateTime? DeliveredUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    [MaxLength(450)] public string? UpdatedBy { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; }=[];
}
