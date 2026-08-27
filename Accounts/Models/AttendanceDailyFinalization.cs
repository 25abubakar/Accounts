using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("AttendanceDailyFinalizations")]
public sealed class AttendanceDailyFinalization : ITenantEntity
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public Guid PersonId { get; set; }
    public Guid StaffId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public long? AttendanceRecordId { get; set; }

    [Required, MaxLength(30)]
    public string State { get; set; } = AttendanceFinalizationStates.Open;

    public bool IsWorkingDay { get; set; }
    public bool IsFinalized { get; set; }
    public bool IsFullDayAbsent { get; set; }
    public int RequiredMinutes { get; set; }
    public int WorkedMinutes { get; set; }
    public int ShortMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int LateBandMinutes { get; set; }
    public int LatePenaltyMinutes { get; set; }
    public DateTime? FinalizedDateUtc { get; set; }
    public DateTime LastEvaluatedDateUtc { get; set; }
}

public static class AttendanceFinalizationStates
{
    public const string Open = "OPEN";
    public const string InProgress = "IN_PROGRESS";
    public const string Completed = "COMPLETED";
    public const string Absent = "ABSENT";
    public const string PendingReview = "PENDING_REVIEW";
    public const string DayOff = "DAY_OFF";
    public const string Excused = "EXCUSED";
}
