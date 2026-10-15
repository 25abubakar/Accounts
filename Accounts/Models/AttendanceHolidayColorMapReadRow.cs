namespace Accounts.Models;

/// <summary>Read model returned by dbo.vw_AttendanceHolidayColorMaps.</summary>
public sealed class AttendanceHolidayColorMapReadRow
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string HolidayTypeCode { get; set; } = string.Empty;
    public string HolidayTypeName { get; set; } = string.Empty;
    public string ColorCode { get; set; } = string.Empty;
}
