using Accounts.DTOs;

namespace Accounts.Services.Interfaces;

public interface IAttendanceService
{
    Task<MyAttendanceTodayDto> GetTodayAsync(string identityUserId, CancellationToken cancellationToken = default);
    Task<MyAttendanceTodayDto> CheckInAsync(string identityUserId, int? workModeId = null, CancellationToken cancellationToken = default);
    Task<MyAttendanceTodayDto> ToggleBreakAsync(string identityUserId, CancellationToken cancellationToken = default);
    Task<MyAttendanceTodayDto> CheckOutAsync(string identityUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AttendanceReportStaffDto>> GetReportStaffAsync(int year, int month, CancellationToken cancellationToken = default);
    Task<MonthlyAttendanceReportDto> GetMonthlyReportAsync(string identityUserId, bool canViewOthers, Guid? requestedPersonId, int year, int month, CancellationToken cancellationToken = default);
    Task<DailyAttendanceReportDto> GetDailyReportAsync(string identityUserId, bool organizationWide, DateOnly dateFrom, DateOnly dateTo, CancellationToken cancellationToken = default);
}
