namespace Accounts.Models;

/// <summary>Read model returned by dbo.vw_AttendanceMapRules.</summary>
public sealed class AttendanceMapRuleReadRow
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public Guid StaffId { get; set; }
    public int AttendanceEntryTypeId { get; set; }
    public string AttendanceTypeCode { get; set; } = string.Empty;
    public string AttendanceTypeName { get; set; } = string.Empty;
    public string ShiftCode { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    public string TimeFrom { get; set; } = string.Empty;
    public string TimeTo { get; set; } = string.Empty;
    public bool IsOpenAttendance { get; set; }
}
