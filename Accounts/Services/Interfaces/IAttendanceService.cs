using Accounts.DTOs;

namespace Accounts.Services.Interfaces;

public interface IAttendanceService
{
    Task<MyAttendanceTodayDto> GetTodayAsync(string identityUserId, CancellationToken cancellationToken = default);
    Task<MyAttendanceTodayDto> CheckInAsync(string identityUserId, int? workModeId = null, CancellationToken cancellationToken = default);
    Task<MyAttendanceTodayDto> ToggleBreakAsync(string identityUserId, CancellationToken cancellationToken = default);
    Task<MyAttendanceTodayDto> CheckOutAsync(string identityUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AttendanceReportStaffDto>> GetReportStaffAsync(int year, int month, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AttendanceReportStaffDto>> GetTimingChartStaffAsync(string identityUserId, bool organizationWide, CancellationToken cancellationToken = default);
    Task<TimingChartScheduleMonthDto> GetTimingChartSchedulesAsync(string identityUserId, bool organizationWide, Guid staffId, int year, int month, CancellationToken cancellationToken = default);
    Task<TimingChartStaffScheduleMonthDto> GetTimingChartStaffScheduleAsync(string identityUserId, bool organizationWide, int year, int month, CancellationToken cancellationToken = default);
    Task<TimingChartScheduleRowDto> SaveTimingChartScheduleAsync(string identityUserId, bool organizationWide, Guid staffId, DateOnly holidayDate, SaveTimingChartScheduleDto dto, CancellationToken cancellationToken = default);
    Task<TimingChartScheduleRangeResultDto> SaveTimingChartScheduleRangeAsync(string identityUserId, bool organizationWide, Guid staffId, SaveTimingChartScheduleRangeDto dto, CancellationToken cancellationToken = default);
    Task<MonthlyAttendanceReportDto> GetMonthlyReportAsync(string identityUserId, bool canViewOthers, Guid? requestedPersonId, int year, int month, CancellationToken cancellationToken = default);
    Task<DailyAttendanceReportDto> GetDailyReportAsync(string identityUserId, bool organizationWide, DateOnly dateFrom, DateOnly dateTo, bool includeAllAttendanceTypes = false, CancellationToken cancellationToken = default);
    Task<DailyAttendanceReportDto> GetRemoteAttendanceReportAsync(string identityUserId, bool organizationWide, DateOnly dateFrom, DateOnly dateTo, CancellationToken cancellationToken = default);
    Task<LoginAttendanceReportDto> GetLoginAttendanceReportAsync(string identityUserId, bool organizationWide, DateOnly dateFrom, DateOnly dateTo, CancellationToken cancellationToken = default);
    Task<DailyAttendanceReportDto> GetStaffAttendanceReportAsync(string identityUserId, bool organizationWide, DateOnly dateFrom, DateOnly dateTo, CancellationToken cancellationToken = default);
    Task<MonthlyAttendanceChartDto> GetMonthlyChartAsync(string identityUserId, bool organizationWide, int year, int month, CancellationToken cancellationToken = default);
    Task<AttendanceDeductionReportDto> GetDeductionReportAsync(string identityUserId, bool organizationWide, int year, int month, CancellationToken cancellationToken = default);
}
