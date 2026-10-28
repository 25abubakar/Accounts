

namespace Accounts.DTOs.Scheduler;

public sealed record ScheduleEdit(string Name,string Handler,string Mode,int IntervalDays,int DayOfMonth,int DayOfWeek,
    string Time,DateTime StartLocal,string MisfirePolicy,bool Enabled,int TimeoutSeconds,int RetryCount,string? RowVersion,Guid CreationKey);
