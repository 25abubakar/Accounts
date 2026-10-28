using System.ComponentModel.DataAnnotations;

namespace Accounts.Models.Scheduler;

public sealed class ScheduleDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    [MaxLength(120)] public string Name { get; set; } = "";
    [MaxLength(100)] public string Handler { get; set; } = "";
    [MaxLength(30)] public string Mode { get; set; } = "Daily";
    public int IntervalDays { get; set; } = 15;
    public int DayOfMonth { get; set; } = 1;
    public int DayOfWeek { get; set; } = 1;
    [MaxLength(5)] public string Time { get; set; } = "09:00";
    public DateTime StartLocal { get; set; }
    [MaxLength(80)] public string TimeZone { get; set; } = "Asia/Karachi";
    [MaxLength(20)] public string MisfirePolicy { get; set; } = "RunOnce";
    public bool Enabled { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
    public int RetryCount { get; set; } = 2;
    public int Revision { get; set; } = 1;
    public int RuntimeRevision { get; set; }
    public DateTime? NextRunUtc { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string UpdatedBy { get; set; } = "";
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public Guid CreationKey { get; set; }
}
