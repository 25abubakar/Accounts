namespace Accounts.Models;

/// <summary>Read model returned by dbo.vw_AttendanceRuleSettings.</summary>
public sealed class AttendanceRuleSettingReadRow
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int AttendanceEntryTypeId { get; set; }
    public string AttendanceTypeCode { get; set; } = string.Empty;
    public string AttendanceTypeName { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public int WorkingMinutes { get; set; }
    public int BeforeCheckInMinutes { get; set; }
    public int AfterCheckOutMinutes { get; set; }
    public int CheckInAdjustMinutes { get; set; }
    public int CheckOutAdjustMinutes { get; set; }
    public int AbsentAfterShiftStartMinutes { get; set; }
    public int MissingCheckoutAfterShiftEndMinutes { get; set; }
    public int AccountLockAbsentDays { get; set; }
    public decimal WeekendChargeValue { get; set; }
    public int AdjustAbsentDays { get; set; }
    public bool IsApproved { get; set; }
    public bool IsActive { get; set; }
    public string? Remarks { get; set; }
}
