namespace Accounts.Models;

/// <summary>
/// Read model returned by dbo.vw_StaffDirectory.
/// Centralizes the repeated StaffVacancy + Person + Vacancy + JobTitle + Organization join.
/// </summary>
public sealed class StaffDirectoryRow
{
    public int TenantId { get; set; }
    public Guid StaffId { get; set; }
    public Guid PersonId { get; set; }
    public string EmployeeId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public Guid? ReportsToPersonId { get; set; }
    public string ShiftStartTime { get; set; } = "09:00";
    public string ShiftEndTime { get; set; } = "18:00";
    public string TimeZoneId { get; set; } = "Asia/Karachi";
    public int? OrganizationId { get; set; }
    public bool IsPersonActive { get; set; }
}
