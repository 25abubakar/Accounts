using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Text.Json;

namespace Accounts.Services.Services;

public sealed class AttendanceService : IAttendanceService
{
    private const string TimingHolidayTypeLookupCode = "TIMING_HOLIDAY_TYPE";
    private const string HolidayCode = "HOLIDAY";
    private const string WorkingDayCode = "WORKING_DAY";
    private const string DayOffCode = "DAY_OFF";
    private readonly ApplicationDbContext _db;
    public AttendanceService(ApplicationDbContext db) => _db = db;

    public async Task<MyAttendanceTodayDto> GetTodayAsync(string identityUserId, CancellationToken cancellationToken = default)
    {
        var (person, localDate) = await ResolvePersonAsync(identityUserId, cancellationToken);
        var attendanceRule = await ResolveAttendanceRuleAsync(person, cancellationToken);
        var timing = await ResolveEffectiveTimingAsync(person, localDate, attendanceRule, cancellationToken);
        await EvaluateStatusesAsync(person.TenantId, localDate, localDate, cancellationToken);
        var record = await _db.AttendanceRecords.AsNoTracking()
            .Include(x => x.AttendanceEntryType).Include(x => x.AttendanceWorkMode)
            .FirstOrDefaultAsync(x => x.PersonId == person.PersonId && x.AttendanceDate == localDate, cancellationToken);
        return Map(person, record, DateTime.UtcNow, timing, attendanceRule);
    }

    public async Task<MyAttendanceTodayDto> CheckInAsync(string identityUserId, int? workModeId = null, CancellationToken cancellationToken = default)
    {
        var (person, localDate) = await ResolvePersonAsync(identityUserId, cancellationToken);
        var attendanceRule = await ResolveAttendanceRuleAsync(person, cancellationToken)
            ?? throw new InvalidOperationException("Your attendance rule has not been configured. Ask an attendance administrator to map your attendance type and shift.");
        EnsurePortalCheckInAllowed(attendanceRule);

        var timing = await ResolveEffectiveTimingAsync(person, localDate, attendanceRule, cancellationToken);
        if (!timing.IsOn && !attendanceRule.IsOpenAttendance)
            throw new InvalidOperationException($"Timing Chart marks {localDate:dd MMM yyyy} as {timing.HolidayType}; check-in is disabled.");
        var policy = await LoadPolicyAsync(person.TenantId, cancellationToken);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ResolveTimeZone(person.TimeZoneId));
        var shiftStart = ParseShift(timing.TimeFrom ?? attendanceRule.TimeFrom, new TimeOnly(9, 0));
        var earliest = shiftStart.AddMinutes(-policy.EarliestCheckInMinutesBefore);
        var absentAt = shiftStart.AddMinutes(policy.AbsentAfterShiftStartMinutes);
        var nowTime = TimeOnly.FromDateTime(localNow);
        if (!attendanceRule.IsOpenAttendance && nowTime < earliest)
            throw new InvalidOperationException($"You cannot check in before {earliest:HH:mm}. Your shift starts at {shiftStart:HH:mm}.");
        if (!attendanceRule.IsOpenAttendance && nowTime >= absentAt)
            throw new InvalidOperationException($"The {policy.AbsentAfterShiftStartMinutes}-minute check-in window has expired and attendance is marked absent.");
        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(x => x.PersonId == person.PersonId && x.AttendanceDate == localDate, cancellationToken);
        if (record?.CheckInUtc is not null) throw new InvalidOperationException("You have already checked in today.");
        var workMode = attendanceRule.EntryTypeCode == "REMOTE"
            ? await _db.AttendanceWorkModes.FirstOrDefaultAsync(x => x.Code == "REMOTE" && x.IsActive, cancellationToken)
            : workModeId.HasValue
                ? await _db.AttendanceWorkModes.FirstOrDefaultAsync(x => x.Id == workModeId.Value && x.IsActive, cancellationToken)
                : await _db.AttendanceWorkModes.FirstOrDefaultAsync(x => x.Code == "ONSITE" && x.IsActive, cancellationToken);
        if (workMode == null) throw new InvalidOperationException("Select a valid active work mode.");
        record ??= new AttendanceRecord { TenantId = person.TenantId, PersonId = person.PersonId, AttendanceDate = localDate, CreatedDate = DateTime.UtcNow };
        record.AttendanceEntryTypeId = attendanceRule.AttendanceEntryTypeId;
        record.AttendanceWorkMode = workMode;
        record.AttendanceStatusId = nowTime <= shiftStart.AddMinutes(policy.OnTimeGraceMinutesAfter)
            ? policy.PresentStatusId : policy.LateStatusId;
        record.CheckInUtc = DateTime.UtcNow;
        record.ModifiedDate = DateTime.UtcNow;
        if (record.Id == 0) _db.AttendanceRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
        await EvaluateStatusesAsync(person.TenantId, localDate, localDate, cancellationToken);
        return Map(person, record, DateTime.UtcNow, timing, attendanceRule);
    }

    public async Task<MyAttendanceTodayDto> ToggleBreakAsync(string identityUserId, CancellationToken cancellationToken = default)
    {
        var (person, localDate) = await ResolvePersonAsync(identityUserId, cancellationToken);
        var attendanceRule = await ResolveAttendanceRuleAsync(person, cancellationToken);
        var timing = await ResolveEffectiveTimingAsync(person, localDate, attendanceRule, cancellationToken);
        var record = await _db.AttendanceRecords.Include(x => x.AttendanceEntryType).Include(x => x.AttendanceWorkMode)
            .FirstOrDefaultAsync(x => x.PersonId == person.PersonId && x.AttendanceDate == localDate, cancellationToken)
            ?? throw new InvalidOperationException("Check in before starting a break.");
        if (record.CheckOutUtc.HasValue) throw new InvalidOperationException("Attendance is already closed for today.");
        var now = DateTime.UtcNow;
        if (record.BreakStartedUtc.HasValue)
        {
            record.TotalBreakMinutes += Math.Max(0, (int)Math.Floor((now - record.BreakStartedUtc.Value).TotalMinutes));
            record.BreakStartedUtc = null;
        }
        else record.BreakStartedUtc = now;
        record.ModifiedDate = now;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(person, record, now, timing, attendanceRule);
    }

    public async Task<MyAttendanceTodayDto> CheckOutAsync(string identityUserId, CancellationToken cancellationToken = default)
    {
        var (person, localDate) = await ResolvePersonAsync(identityUserId, cancellationToken);
        var attendanceRule = await ResolveAttendanceRuleAsync(person, cancellationToken);
        var timing = await ResolveEffectiveTimingAsync(person, localDate, attendanceRule, cancellationToken);
        var record = await _db.AttendanceRecords.Include(x => x.AttendanceEntryType).Include(x => x.AttendanceWorkMode)
            .FirstOrDefaultAsync(x => x.PersonId == person.PersonId && x.AttendanceDate == localDate, cancellationToken)
            ?? throw new InvalidOperationException("Check in before checking out.");
        if (record.CheckOutUtc.HasValue) throw new InvalidOperationException("You have already checked out today.");
        var now = DateTime.UtcNow;
        if (record.BreakStartedUtc.HasValue)
        {
            record.TotalBreakMinutes += Math.Max(0, (int)Math.Floor((now - record.BreakStartedUtc.Value).TotalMinutes));
            record.BreakStartedUtc = null;
        }
        record.CheckOutUtc = now;
        record.ModifiedDate = now;
        await _db.SaveChangesAsync(cancellationToken);
        await EvaluateStatusesAsync(person.TenantId, localDate, localDate, cancellationToken);
        await _db.Entry(record).Reference(x => x.AttendanceStatus).LoadAsync(cancellationToken);
        return Map(person, record, now, timing, attendanceRule);
    }

    public async Task<IReadOnlyList<AttendanceReportStaffDto>> GetReportStaffAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var dateFrom = new DateOnly(year, month, 1);
        var dateTo = dateFrom.AddMonths(1);

        var staffRows = await _db.StaffDirectoryRows.AsNoTracking()
            .Where(s => s.IsPersonActive)
            .OrderBy(s => s.FullName)
            .Select(s => new
            {
                Dto = new AttendanceReportStaffDto
                {
                    PersonId = s.PersonId, StaffId = s.StaffId, EmployeeId = s.EmployeeId,
                    FullName = s.FullName, Department = s.Department, Designation = s.Designation,
                    PhotoUrl = s.PhotoUrl
                },
                s.ShiftStartTime, s.ShiftEndTime, s.TimeZoneId
            })
            .ToListAsync(cancellationToken);

        var personIds = staffRows.Select(x => x.Dto.PersonId).ToList();
        var records = await _db.AttendanceRecords.AsNoTracking()
            .Where(r => personIds.Contains(r.PersonId) && r.AttendanceDate >= dateFrom && r.AttendanceDate < dateTo)
            .Select(r => new { r.PersonId, r.CheckInUtc, r.CheckOutUtc, r.TotalBreakMinutes })
            .ToListAsync(cancellationToken);
        var recordsByPerson = records.ToLookup(r => r.PersonId);
        var workdays = CountWorkingDays(year, month);
        foreach (var row in staffRows)
        {
            var employeeRecords = recordsByPerson[row.Dto.PersonId].ToList();
            var required = ShiftMinutes(row.ShiftStartTime, row.ShiftEndTime);
            var worked = employeeRecords.Sum(r => r.CheckInUtc.HasValue && r.CheckOutUtc.HasValue
                ? Math.Max(0, (int)Math.Floor((r.CheckOutUtc.Value - r.CheckInUtc.Value).TotalMinutes) - r.TotalBreakMinutes) : 0);
            var checkIns = employeeRecords.Where(r => r.CheckInUtc.HasValue).ToList();
            var zone = ResolveTimeZone(row.TimeZoneId);
            var shiftStart = TimeOnly.TryParseExact(row.ShiftStartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ? start : new TimeOnly(9, 0);
            var onTime = checkIns.Count(r => TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(r.CheckInUtc!.Value, zone)) <= shiftStart);
            row.Dto.AttendanceDays = employeeRecords.Count;
            row.Dto.CompletedDays = employeeRecords.Count(r => r.CheckOutUtc.HasValue);
            row.Dto.AttendancePercentage = workdays == 0 ? 0 : Math.Round(Math.Min(100, employeeRecords.Count * 100d / workdays), 1);
            row.Dto.ShiftCompletionPercentage = employeeRecords.Count == 0 || required == 0 ? 0 : Math.Round(Math.Min(100, worked * 100d / (required * employeeRecords.Count)), 1);
            row.Dto.PunctualityPercentage = checkIns.Count == 0 ? 0 : Math.Round(onTime * 100d / checkIns.Count, 1);
        }
        return staffRows.Select(x => x.Dto).ToList();
    }

    public async Task<IReadOnlyList<AttendanceReportStaffDto>> GetTimingChartStaffAsync(
        string identityUserId,
        bool organizationWide,
        CancellationToken cancellationToken = default)
    {
        var visibility = await ResolveAttendanceVisibilityAsync(
            identityUserId, organizationWide, selfOnly: false, cancellationToken);
        var visiblePersonIds = visibility.VisiblePersonIds;
        var callerPersonId = visibility.CallerPersonId;

        var staffRows = await _db.StaffDirectoryRows.AsNoTracking()
            .Where(staff =>
                visiblePersonIds.Contains(staff.PersonId) &&
                staff.IsPersonActive)
            .OrderBy(staff => staff.FullName)
            .Select(staff => new
            {
                Dto = new AttendanceReportStaffDto
                {
                    PersonId = staff.PersonId,
                    StaffId = staff.StaffId,
                    EmployeeId = staff.EmployeeId,
                    FullName = staff.FullName,
                    BranchName = string.Empty,
                    Department = staff.Department,
                    Designation = staff.Designation,
                    PhotoUrl = staff.PhotoUrl,
                    IsCurrentUser = staff.PersonId == callerPersonId,
                    CanEditTiming = organizationWide || staff.PersonId != callerPersonId
                },
                staff.OrganizationId
            })
            .ToListAsync(cancellationToken);

        var organizationNodes = await _db.OrganizationTree.AsNoTracking()
            .Select(node => new { node.Id, node.ParentId, node.Name, node.Label })
            .ToListAsync(cancellationToken);
        var nodesById = organizationNodes.ToDictionary(node => node.Id);

        foreach (var staffRow in staffRows)
        {
            var organizationId = (int?)staffRow.OrganizationId;
            for (var depth = 0; organizationId.HasValue && depth < 20; depth++)
            {
                if (!nodesById.TryGetValue(organizationId.Value, out var node)) break;
                if (string.Equals(node.Label, "Branch", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(node.Label, "Office", StringComparison.OrdinalIgnoreCase))
                {
                    staffRow.Dto.BranchName = node.Name;
                    break;
                }
                organizationId = node.ParentId;
            }

            if (string.IsNullOrWhiteSpace(staffRow.Dto.BranchName))
                staffRow.Dto.BranchName = staffRow.OrganizationId.HasValue
                    ? (nodesById.GetValueOrDefault(staffRow.OrganizationId.Value)?.Name
                        ?? staffRow.Dto.Department)
                    : staffRow.Dto.Department;
        }

        return staffRows.Select(staffRow => staffRow.Dto).ToList();
    }

    public async Task<TimingChartScheduleMonthDto> GetTimingChartSchedulesAsync(
        string identityUserId,
        bool organizationWide,
        Guid staffId,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "A valid Timing Chart month is required.");

        var visibility = await ResolveAttendanceVisibilityAsync(
            identityUserId, organizationWide, selfOnly: false, cancellationToken);
        var employee = await GetTimingChartEmployeeAsync(staffId, cancellationToken);
        if (!visibility.VisiblePersonIds.Contains(employee.PersonId))
            throw new InvalidOperationException("This employee is outside your attendance hierarchy.");

        var dateFrom = new DateOnly(year, month, 1);
        var dateTo = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var savedSchedules = await _db.EmployeeTimingSchedules.AsNoTracking()
            .Include(schedule => schedule.HolidayType)
            .Where(schedule =>
                schedule.StaffId == staffId &&
                schedule.ScheduleYear == year &&
                schedule.ScheduleMonth == month)
            .ToDictionaryAsync(schedule => schedule.ScheduleDate, cancellationToken);

        var holidayTypes = await GetTimingHolidayTypesAsync(cancellationToken);
        var holidayTypesByCode = holidayTypes.ToDictionary(type => type.Code, StringComparer.OrdinalIgnoreCase);
        var rows = new List<TimingChartScheduleRowDto>(dateTo.Day);
        for (var date = dateFrom; date <= dateTo; date = date.AddDays(1))
        {
            savedSchedules.TryGetValue(date, out var schedule);
            rows.Add(MapTimingChartSchedule(employee, schedule, date, holidayTypesByCode));
        }

        return new TimingChartScheduleMonthDto
        {
            PersonId = employee.PersonId,
            StaffId = employee.StaffId,
            Year = year,
            Month = month,
            CanEdit = organizationWide || employee.PersonId != visibility.CallerPersonId,
            HolidayTypes = holidayTypes,
            Rows = rows
        };
    }

    public async Task<TimingChartStaffScheduleMonthDto> GetTimingChartStaffScheduleAsync(
        string identityUserId,
        bool organizationWide,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "A valid Staff Schedule month is required.");

        var visibility = await ResolveAttendanceVisibilityAsync(
            identityUserId, organizationWide, selfOnly: false, cancellationToken);
        var visiblePersonIds = visibility.VisiblePersonIds;
        var dateFrom = new DateOnly(year, month, 1);
        var dateTo = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        var employees = await _db.StaffDirectoryRows.AsNoTracking()
            .Where(staff =>
                visiblePersonIds.Contains(staff.PersonId) &&
                staff.IsPersonActive)
            .OrderBy(staff => staff.FullName)
            .Select(staff => new
            {
                staff.TenantId,
                staff.StaffId,
                staff.PersonId,
                staff.EmployeeId,
                staff.FullName,
                staff.Department,
                staff.Designation,
                staff.PhotoUrl,
                staff.ShiftStartTime,
                staff.ShiftEndTime
            })
            .ToListAsync(cancellationToken);

        var employeeStaffIds = employees.Select(employee => employee.StaffId).ToList();
        var savedSchedules = await _db.EmployeeTimingSchedules.AsNoTracking()
            .Include(schedule => schedule.HolidayType)
            .Where(schedule =>
                employeeStaffIds.Contains(schedule.StaffId) &&
                schedule.ScheduleYear == year &&
                schedule.ScheduleMonth == month)
            .ToListAsync(cancellationToken);
        var schedulesByEmployee = savedSchedules.ToLookup(schedule => schedule.StaffId);
        var holidayTypes = await GetTimingHolidayTypesAsync(cancellationToken);
        var holidayTypesByCode = holidayTypes.ToDictionary(type => type.Code, StringComparer.OrdinalIgnoreCase);

        var rows = employees.Select(employee =>
        {
            var employeeContext = new TimingChartEmployeeContext
            {
                TenantId = employee.TenantId,
                StaffId = employee.StaffId,
                PersonId = employee.PersonId,
                FullName = employee.FullName,
                EmployeeId = employee.EmployeeId,
                Department = employee.Department,
                DefaultTimeFrom = employee.ShiftStartTime,
                DefaultTimeTo = employee.ShiftEndTime
            };
            var employeeSchedules = schedulesByEmployee[employee.StaffId]
                .ToDictionary(schedule => schedule.ScheduleDate);
            var days = new List<TimingChartStaffScheduleDayDto>(dateTo.Day);
            for (var date = dateFrom; date <= dateTo; date = date.AddDays(1))
            {
                employeeSchedules.TryGetValue(date, out var schedule);
                var mapped = MapTimingChartSchedule(employeeContext, schedule, date, holidayTypesByCode);
                days.Add(new TimingChartStaffScheduleDayDto
                {
                    Id = mapped.Id,
                    Date = mapped.HolidayDate,
                    Day = mapped.Day,
                    HolidayTypeId = mapped.HolidayTypeId,
                    HolidayType = mapped.HolidayType,
                    HolidayTypeName = mapped.HolidayTypeName,
                    TimeFrom = mapped.TimeFrom,
                    TimeTo = mapped.TimeTo,
                    WorkingMinutes = mapped.WorkingMinutes,
                    IsOn = mapped.IsOn,
                    IsOverride = mapped.IsOverride
                });
            }

            return new TimingChartStaffScheduleEmployeeDto
            {
                PersonId = employee.PersonId,
                StaffId = employee.StaffId,
                EmployeeId = employee.EmployeeId,
                FullName = employee.FullName,
                Department = employee.Department,
                Designation = employee.Designation,
                PhotoUrl = employee.PhotoUrl,
                IsCurrentUser = employee.PersonId == visibility.CallerPersonId,
                CanEditTiming = organizationWide || employee.PersonId != visibility.CallerPersonId,
                Days = days
            };
        }).ToList();

        return new TimingChartStaffScheduleMonthDto
        {
            Year = year,
            Month = month,
            DateFrom = dateFrom,
            DateTo = dateTo,
            DaysInMonth = dateTo.Day,
            HolidayTypes = holidayTypes,
            Employees = rows
        };
    }

    public async Task<TimingChartScheduleRowDto> SaveTimingChartScheduleAsync(
        string identityUserId,
        bool organizationWide,
        Guid staffId,
        DateOnly holidayDate,
        SaveTimingChartScheduleDto dto,
        CancellationToken cancellationToken = default)
    {
        if (holidayDate.Year is < 2000 or > 2100)
            throw new ArgumentOutOfRangeException(nameof(holidayDate), "A valid holiday date is required.");

        var employee = await GetEditableTimingChartEmployeeAsync(
            identityUserId, organizationWide, staffId, cancellationToken);
        var requestedTiming = await ValidateTimingScheduleAsync(
            employee, dto.HolidayTypeId, dto.TimeFrom, dto.TimeTo, dto.IsOn, cancellationToken);
        var timing = await ApplyRequiredWeekendRuleAsync(holidayDate, requestedTiming, cancellationToken);
        EmployeeTimingSchedule? savedSchedule = null;

        // SQL Server retry-on-failure requires every user transaction to run inside
        // the configured execution strategy. Keeping the schedule and status refresh
        // in the same retriable transaction also prevents partially saved changes.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            var schedule = await _db.EmployeeTimingSchedules
                .Include(item => item.HolidayType)
                .FirstOrDefaultAsync(item =>
                    item.StaffId == staffId && item.ScheduleDate == holidayDate,
                    cancellationToken);
            if (schedule == null)
            {
                schedule = new EmployeeTimingSchedule
                {
                    TenantId = employee.TenantId,
                    StaffId = staffId,
                    ScheduleDate = holidayDate,
                    ScheduleYear = holidayDate.Year,
                    ScheduleMonth = holidayDate.Month,
                    CreatedByUserId = identityUserId,
                    CreatedDate = DateTime.UtcNow
                };
                _db.EmployeeTimingSchedules.Add(schedule);
            }

            schedule.HolidayTypeId = timing.HolidayTypeId;
            schedule.TimeFrom = timing.TimeFrom;
            schedule.TimeTo = timing.TimeTo;
            schedule.IsOn = timing.IsOn;
            schedule.WorkingMinutes = timing.WorkingMinutes;
            schedule.ModifiedByUserId = identityUserId;
            schedule.ModifiedDate = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await _db.Entry(schedule).Reference(item => item.HolidayType).LoadAsync(cancellationToken);
            await EvaluateStatusesAsync(employee.TenantId, holidayDate, holidayDate, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            savedSchedule = schedule;
        });

        var holidayTypes = await GetTimingHolidayTypesAsync(cancellationToken);
        return MapTimingChartSchedule(
            employee,
            savedSchedule,
            holidayDate,
            holidayTypes.ToDictionary(type => type.Code, StringComparer.OrdinalIgnoreCase));
    }

    public async Task<TimingChartScheduleRangeResultDto> SaveTimingChartScheduleRangeAsync(
        string identityUserId,
        bool organizationWide,
        Guid staffId,
        SaveTimingChartScheduleRangeDto dto,
        CancellationToken cancellationToken = default)
    {
        if (dto.DateFrom.Year is < 2000 or > 2100 || dto.DateTo.Year is < 2000 or > 2100)
            throw new ArgumentOutOfRangeException(nameof(dto), "A valid Timing Chart date range is required.");
        if (dto.DateTo < dto.DateFrom)
            throw new InvalidOperationException("Date To must be the same as or later than Date From.");
        if (dto.DateTo.DayNumber - dto.DateFrom.DayNumber + 1 > 366)
            throw new InvalidOperationException("A Timing Chart range cannot exceed 366 days.");
        if (dto.DayOfWeek.HasValue && dto.DayOfWeek.Value is < 0 or > 6)
            throw new InvalidOperationException("Select a valid day of the week.");

        var employee = await GetEditableTimingChartEmployeeAsync(
            identityUserId, organizationWide, staffId, cancellationToken);
        var requestedTiming = await ValidateTimingScheduleAsync(
            employee, dto.HolidayTypeId, dto.TimeFrom, dto.TimeTo, dto.IsOn, cancellationToken);

        var dates = new List<DateOnly>();
        for (var date = dto.DateFrom; date <= dto.DateTo; date = date.AddDays(1))
        {
            if (!dto.DayOfWeek.HasValue || (int)date.DayOfWeek == dto.DayOfWeek.Value)
                dates.Add(date);
        }
        if (dates.Count == 0)
            throw new InvalidOperationException("The selected day does not occur inside this date range.");

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // A retry starts with a clean tracker so failed-attempt entities cannot
            // leak into the next attempt or create duplicate schedule inserts.
            _db.ChangeTracker.Clear();
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            var existing = await _db.EmployeeTimingSchedules
                .Where(schedule =>
                    schedule.StaffId == staffId &&
                    schedule.ScheduleDate >= dto.DateFrom &&
                    schedule.ScheduleDate <= dto.DateTo)
                .ToDictionaryAsync(schedule => schedule.ScheduleDate, cancellationToken);
            var now = DateTime.UtcNow;

            foreach (var date in dates)
            {
                var timing = await ApplyRequiredWeekendRuleAsync(date, requestedTiming, cancellationToken);
                if (!existing.TryGetValue(date, out var schedule))
                {
                    schedule = new EmployeeTimingSchedule
                    {
                        TenantId = employee.TenantId,
                        StaffId = staffId,
                        ScheduleDate = date,
                        ScheduleYear = date.Year,
                        ScheduleMonth = date.Month,
                        CreatedByUserId = identityUserId,
                        CreatedDate = now
                    };
                    _db.EmployeeTimingSchedules.Add(schedule);
                }

                schedule.HolidayTypeId = timing.HolidayTypeId;
                schedule.TimeFrom = timing.TimeFrom;
                schedule.TimeTo = timing.TimeTo;
                schedule.IsOn = timing.IsOn;
                schedule.WorkingMinutes = timing.WorkingMinutes;
                schedule.ModifiedByUserId = identityUserId;
                schedule.ModifiedDate = now;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await EvaluateStatusesAsync(employee.TenantId, dto.DateFrom, dto.DateTo, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        return new TimingChartScheduleRangeResultDto
        {
            PersonId = employee.PersonId,
            StaffId = staffId,
            DateFrom = dto.DateFrom,
            DateTo = dto.DateTo,
            SavedDays = dates.Count
        };
    }

    public Task<DailyAttendanceReportDto> GetDailyReportAsync(
        string identityUserId, bool organizationWide, DateOnly dateFrom, DateOnly dateTo,
        CancellationToken cancellationToken = default) =>
        GetAttendanceReportAsync(identityUserId, organizationWide, selfOnly: false, dateFrom, dateTo, cancellationToken);

    public async Task<DailyAttendanceReportDto> GetRemoteAttendanceReportAsync(
        string identityUserId, bool organizationWide, DateOnly dateFrom, DateOnly dateTo,
        CancellationToken cancellationToken = default)
    {
        // Remote Attendance intentionally uses the same hierarchy boundary and
        // status evaluation as Daily Attendance, then filters by the database
        // attendance-type master used by Map Attendance.
        var report = await GetAttendanceReportAsync(
            identityUserId, organizationWide, selfOnly: false, dateFrom, dateTo, cancellationToken);
        var remoteAttendanceTypeId = await _db.AttendanceEntryTypes.AsNoTracking()
            .Where(type => type.IsActive && type.Code == "REMOTE")
            .Select(type => (int?)type.Id)
            .SingleOrDefaultAsync(cancellationToken);
        var rows = remoteAttendanceTypeId.HasValue
            ? report.Rows.Where(row => row.AttendanceEntryTypeId == remoteAttendanceTypeId.Value).ToList()
            : new List<DailyAttendanceRowDto>();

        return new DailyAttendanceReportDto
        {
            DateFrom = report.DateFrom,
            DateTo = report.DateTo,
            Rows = rows,
            Summary = new DailyAttendanceSummaryDto
            {
                TotalEmployees = rows.Select(row => row.PersonId).Distinct().Count(),
                Present = rows.Count(row => row.Present),
                Absent = rows.Count(row => row.Absent),
                Late = rows.Count(row => row.LateMinutes > 0),
                OnLeave = rows.Count(row => row.OnLeave),
                Remote = rows.Count,
                MissingCheckIn = rows.Count(row => row.MissingCheckIn),
                MissingCheckOut = rows.Count(row => row.MissingCheckOut),
                TotalWorkingMinutes = rows.Sum(row => row.WorkingMinutes),
                TotalOvertimeMinutes = rows.Sum(row => row.OvertimeMinutes)
            }
        };
    }

    public Task<DailyAttendanceReportDto> GetStaffAttendanceReportAsync(
        string identityUserId, DateOnly dateFrom, DateOnly dateTo,
        CancellationToken cancellationToken = default) =>
        GetAttendanceReportAsync(identityUserId, organizationWide: false, selfOnly: true, dateFrom, dateTo, cancellationToken);

    public async Task<MonthlyAttendanceChartDto> GetMonthlyChartAsync(
        string identityUserId, bool organizationWide, int year, int month,
        CancellationToken cancellationToken = default)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "A valid monthly chart period is required.");

        var dateFrom = new DateOnly(year, month, 1);
        var dateTo = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        // Monthly Chart intentionally delegates to the same hierarchy boundary
        // as Daily Attendance. Admin organization-wide access and supervisor/job
        // rank visibility are therefore resolved in exactly one place.
        var report = await GetAttendanceReportAsync(
            identityUserId, organizationWide, selfOnly: false, dateFrom, dateTo, cancellationToken);

        var visiblePersonIds = report.Rows.Select(row => row.PersonId).Distinct().ToList();
        var profilePhotos = await _db.Persons.AsNoTracking()
            .Where(person => visiblePersonIds.Contains(person.PersonId))
            .Select(person => new { person.PersonId, person.ProfilePhotoUrl })
            .ToDictionaryAsync(person => person.PersonId, person => person.ProfilePhotoUrl, cancellationToken);

        var employees = report.Rows
            .GroupBy(row => row.PersonId)
            .Select(group =>
            {
                var first = group.First();
                return new MonthlyAttendanceChartEmployeeDto
                {
                    PersonId = first.PersonId,
                    EmployeeNumber = first.EmployeeNumber,
                    FullName = first.EmployeeName,
                    PhotoUrl = profilePhotos.GetValueOrDefault(first.PersonId),
                    Department = first.Department,
                    Designation = first.Designation,
                    ReportingManager = first.ReportingManager,
                    IsCurrentUser = group.Any(row => row.IsCurrentUser),
                    Days = group
                        .OrderBy(row => row.Date)
                        .Select(row => new MonthlyAttendanceChartCellDto
                        {
                            AttendanceId = row.Id,
                            Date = row.Date,
                            AttendanceStatusId = row.AttendanceStatusId,
                            StatusCode = row.StatusCode,
                            AttendanceStatus = row.AttendanceStatus,
                            StatusColorCode = row.StatusColorCode,
                            StatusFontColor = row.StatusFontColor,
                            StatusFontSize = row.StatusFontSize,
                            AttendanceType = row.AttendanceType,
                            WorkMode = row.WorkMode,
                            CheckInTime = row.CheckInTime,
                            CheckOutTime = row.CheckOutTime,
                            WorkingMinutes = row.WorkingMinutes,
                            LateMinutes = row.LateMinutes,
                            EarlyDepartureMinutes = row.EarlyDepartureMinutes,
                            OvertimeMinutes = row.OvertimeMinutes,
                            Present = row.Present,
                            Absent = row.Absent,
                            OnLeave = row.OnLeave,
                            Remote = row.Remote,
                            MissingCheckIn = row.MissingCheckIn,
                            MissingCheckOut = row.MissingCheckOut
                        })
                        .ToList()
                };
            })
            .OrderBy(employee => employee.FullName)
            .ToList();

        return new MonthlyAttendanceChartDto
        {
            Year = year,
            Month = month,
            DateFrom = dateFrom,
            DateTo = dateTo,
            DaysInMonth = DateTime.DaysInMonth(year, month),
            Summary = report.Summary,
            Employees = employees
        };
    }

    private async Task<DailyAttendanceReportDto> GetAttendanceReportAsync(
        string identityUserId, bool organizationWide, bool selfOnly, DateOnly dateFrom, DateOnly dateTo,
        CancellationToken cancellationToken)
    {
        if (dateFrom == default || dateTo == default || dateTo < dateFrom)
            throw new ArgumentOutOfRangeException(nameof(dateFrom), "A valid attendance date range is required.");
        if (dateTo.DayNumber - dateFrom.DayNumber > 366)
            throw new ArgumentOutOfRangeException(nameof(dateTo), "Attendance reports are limited to 367 days at a time.");

        var visibility = await ResolveAttendanceVisibilityAsync(
            identityUserId, organizationWide, selfOnly, cancellationToken);

        await EvaluateStatusesAsync(visibility.TenantId, dateFrom, dateTo, cancellationToken);

        // The hierarchy is authorized above; row generation, date expansion and
        // attendance joins are performed set-wise by SQL Server.
        try
        {
            return await BuildDailyReportFromProcedureAsync(
                visibility.TenantId,
                visibility.CallerPersonId,
                visibility.VisiblePersonIds,
                visibility.People.ToDictionary(person => person.PersonId, person => person.FullName),
                dateFrom, dateTo, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == 2812)
        {
            throw new InvalidOperationException(
                "The set-based attendance report procedure is not installed. Apply pending database migrations before requesting reports.", ex);
        }

#pragma warning disable CS0162 // Retained only as a temporary rollback reference; never executed.
        var staff = await _db.StaffVacancies.AsNoTracking()
            .Where(s => s.PersonId.HasValue && visibility.VisiblePersonIds.Contains(s.PersonId.Value) && s.Person != null && s.Person.IsActive)
            .Select(s => new
            {
                PersonId = s.PersonId!.Value,
                EmployeeNumber = s.LoginId ?? s.Vacancy!.VacancyCode,
                EmployeeName = s.Person!.FullName,
                Department = s.Vacancy!.Department ?? s.Vacancy.Organization!.Name,
                Designation = s.Vacancy.JobTitleNav != null ? s.Vacancy.JobTitleNav.TitleName : (s.Vacancy.JobTitle ?? string.Empty),
                s.Person.ReportsToPersonId,
                s.Person.ShiftStartTime,
                s.Person.ShiftEndTime,
                s.Person.TimeZoneId
            })
            .OrderBy(s => s.EmployeeName)
            .ToListAsync(cancellationToken);

        var staffIds = staff.Select(s => s.PersonId).ToList();
        var records = await _db.AttendanceRecords.AsNoTracking()
            .Include(r => r.AttendanceStatus)
            .Where(r => staffIds.Contains(r.PersonId) && r.AttendanceDate >= dateFrom && r.AttendanceDate <= dateTo)
            .ToListAsync(cancellationToken);
        var recordsByPersonAndDate = records.ToDictionary(r => (r.PersonId, r.AttendanceDate));
        var names = visibility.People.ToDictionary(p => p.PersonId, p => p.FullName);
        var todayByZone = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<DailyAttendanceRowDto>();

        foreach (var employee in staff)
        {
            var zone = ResolveTimeZone(employee.TimeZoneId);
            if (!todayByZone.TryGetValue(employee.TimeZoneId, out var localToday))
            {
                localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
                todayByZone[employee.TimeZoneId] = localToday;
            }
            var shiftStart = ParseShift(employee.ShiftStartTime, new TimeOnly(9, 0));
            var shiftEnd = ParseShift(employee.ShiftEndTime, new TimeOnly(18, 0));
            var required = ShiftMinutes(employee.ShiftStartTime, employee.ShiftEndTime);

            for (var date = dateFrom; date <= dateTo && date <= localToday; date = date.AddDays(1))
            {
                if ((date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) &&
                    !recordsByPersonAndDate.ContainsKey((employee.PersonId, date))) continue;
                recordsByPersonAndDate.TryGetValue((employee.PersonId, date), out var record);
                var checkInLocal = record?.CheckInUtc is DateTime checkIn ? TimeZoneInfo.ConvertTimeFromUtc(checkIn, zone) : (DateTime?)null;
                var checkOutLocal = record?.CheckOutUtc is DateTime checkOut ? TimeZoneInfo.ConvertTimeFromUtc(checkOut, zone) : (DateTime?)null;
                var working = record?.CheckInUtc is DateTime startUtc && record.CheckOutUtc is DateTime endUtc
                    ? Math.Max(0, (int)Math.Floor((endUtc - startUtc).TotalMinutes) - record.TotalBreakMinutes) : 0;
                var late = checkInLocal.HasValue ? Math.Max(0, (int)(TimeOnly.FromDateTime(checkInLocal.Value).ToTimeSpan() - shiftStart.ToTimeSpan()).TotalMinutes) : 0;
                var early = checkOutLocal.HasValue ? Math.Max(0, (int)(shiftEnd.ToTimeSpan() - TimeOnly.FromDateTime(checkOutLocal.Value).ToTimeSpan()).TotalMinutes) : 0;
                var statusName = record?.AttendanceStatus?.Status.StatusName;
                var hasCheckIn = record?.CheckInUtc.HasValue == true;
                var hasCheckOut = record?.CheckOutUtc.HasValue == true;
                var isPast = date < localToday;
                var normalizedStatus = statusName?.Trim().ToLowerInvariant() ?? string.Empty;
                var isLeave = !hasCheckIn && normalizedStatus.Contains("leave", StringComparison.Ordinal);
                var isRemote = normalizedStatus.Contains("remote", StringComparison.Ordinal) || normalizedStatus.Contains("work from home", StringComparison.Ordinal);
                var fallbackStatus = record is null ? (isPast ? "Absent" : "Not Marked") : late > 0 ? "Late" : "Present";

                rows.Add(new DailyAttendanceRowDto
                {
                    Id = record?.Id, PersonId = employee.PersonId, EmployeeNumber = employee.EmployeeNumber,
                    EmployeeName = employee.EmployeeName, Department = employee.Department, Designation = employee.Designation,
                    ReportingManager = employee.ReportsToPersonId.HasValue && names.TryGetValue(employee.ReportsToPersonId.Value, out var manager) ? manager : null,
                    Date = date, AttendanceType = statusName ?? (record is null ? "No attendance" : "Check In / Out"),
                    CheckInTime = checkInLocal?.ToString("HH:mm"), CheckOutTime = checkOutLocal?.ToString("HH:mm"),
                    WorkingMinutes = working, LateMinutes = late, EarlyDepartureMinutes = early,
                    OvertimeMinutes = hasCheckOut ? Math.Max(0, working - required) : 0,
                    AttendanceStatusId = record?.AttendanceStatusId, AttendanceStatus = statusName ?? fallbackStatus,
                    StatusCode = record?.AttendanceStatus?.Code, StatusColorCode = record?.AttendanceStatus?.ColorStyle.ColorCode,
                    Present = hasCheckIn, Absent = record is null && isPast, OnLeave = isLeave,
                    Remote = isRemote, MissingCheckIn = record is not null && !hasCheckIn && !isLeave,
                    MissingCheckOut = hasCheckIn && !hasCheckOut && isPast,
                    Comments = null
                });
            }
        }

        var summary = new DailyAttendanceSummaryDto
        {
            TotalEmployees = staff.Count,
            Present = rows.Count(r => r.Present), Absent = rows.Count(r => r.Absent),
            Late = rows.Count(r => r.LateMinutes > 0), OnLeave = rows.Count(r => r.OnLeave), Remote = rows.Count(r => r.Remote),
            MissingCheckIn = rows.Count(r => r.MissingCheckIn), MissingCheckOut = rows.Count(r => r.MissingCheckOut),
            TotalWorkingMinutes = rows.Sum(r => r.WorkingMinutes), TotalOvertimeMinutes = rows.Sum(r => r.OvertimeMinutes)
        };
        return new DailyAttendanceReportDto { DateFrom = dateFrom, DateTo = dateTo, Rows = rows, Summary = summary };
#pragma warning restore CS0162
    }

    private async Task<DailyAttendanceReportDto> BuildDailyReportFromProcedureAsync(
        int tenantId,
        Guid callerPersonId,
        IReadOnlyCollection<Guid> visiblePersonIds,
        IReadOnlyDictionary<Guid, string> personNames,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken)
    {
        var sqlRows = await _db.AttendanceDailyReportRows
            .FromSqlRaw(
                "EXEC dbo.usp_Attendance_DailyReport @TenantId, @DateFrom, @DateTo, @VisiblePersonIds",
                new SqlParameter("@TenantId", tenantId),
                new SqlParameter("@DateFrom", dateFrom.ToDateTime(TimeOnly.MinValue)),
                new SqlParameter("@DateTo", dateTo.ToDateTime(TimeOnly.MinValue)),
                new SqlParameter("@VisiblePersonIds", JsonSerializer.Serialize(visiblePersonIds)))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var policy = await LoadPolicyAsync(tenantId, cancellationToken);

        var rows = new List<DailyAttendanceRowDto>(sqlRows.Count);
        foreach (var source in sqlRows)
        {
            var zone = ResolveTimeZone(source.TimeZoneId);
            var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
            var checkInLocal = source.CheckInUtc.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(source.CheckInUtc.Value, zone) : (DateTime?)null;
            var checkOutLocal = source.CheckOutUtc.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(source.CheckOutUtc.Value, zone) : (DateTime?)null;
            var shiftStart = ParseShift(source.ShiftStartTime, new TimeOnly(9, 0));
            var shiftEnd = ParseShift(source.ShiftEndTime, new TimeOnly(18, 0));
            var working = source.CheckInUtc.HasValue && source.CheckOutUtc.HasValue
                ? Math.Max(0, (int)Math.Floor((source.CheckOutUtc.Value - source.CheckInUtc.Value).TotalMinutes) - (source.TotalBreakMinutes ?? 0)) : 0;
            var statusName = source.StatusName ?? string.Empty;
            var statusCode = source.StatusCode;
            var isScheduledOff = statusCode is not null &&
                (statusCode.Equals("DO", StringComparison.OrdinalIgnoreCase) ||
                 statusCode.Equals("H", StringComparison.OrdinalIgnoreCase));
            var required = isScheduledOff ? 0 : ShiftMinutes(source.ShiftStartTime, source.ShiftEndTime);
            var late = !isScheduledOff && checkInLocal.HasValue ? Math.Max(0, (int)(TimeOnly.FromDateTime(checkInLocal.Value).ToTimeSpan() - shiftStart.ToTimeSpan()).TotalMinutes) : 0;
            var early = !isScheduledOff && checkOutLocal.HasValue ? Math.Max(0, (int)(shiftEnd.ToTimeSpan() - TimeOnly.FromDateTime(checkOutLocal.Value).ToTimeSpan()).TotalMinutes) : 0;

            rows.Add(new DailyAttendanceRowDto
            {
                Id = source.Id,
                PersonId = source.PersonId,
                EmployeeNumber = source.EmployeeNumber,
                EmployeeName = source.EmployeeName,
                Department = source.Department,
                Designation = source.Designation,
                ReportingManager = source.ReportsToPersonId.HasValue && personNames.TryGetValue(source.ReportsToPersonId.Value, out var manager) ? manager : null,
                Date = source.AttendanceDate,
                AttendanceType = source.AttendanceEntryType ?? statusName,
                AttendanceEntryTypeId = source.AttendanceEntryTypeId,
                AttendanceWorkModeId = source.AttendanceWorkModeId,
                WorkMode = source.AttendanceWorkMode,
                CheckInTime = checkInLocal?.ToString("HH:mm"),
                CheckOutTime = checkOutLocal?.ToString("HH:mm"),
                WorkingMinutes = working,
                LateMinutes = late,
                EarlyDepartureMinutes = early,
                OvertimeMinutes = source.CheckOutUtc.HasValue ? Math.Max(0, working - required) : 0,
                AttendanceStatusId = source.AttendanceStatusId,
                AttendanceStatus = statusName,
                StatusCode = statusCode,
                StatusColorCode = source.StatusColorCode,
                StatusFontColor = source.StatusFontColor,
                StatusFontSize = source.StatusFontSize,
                IsCurrentUser = source.PersonId == callerPersonId,
                BreakMinutes = source.TotalBreakMinutes ?? 0,
                RequiredMinutes = required,
                Present = source.AttendanceStatusId == policy.PresentStatusId || source.AttendanceStatusId == policy.CompletedLateStatusId,
                Absent = source.AttendanceStatusId == policy.AbsentStatusId,
                OnLeave = statusCode?.Equals("L", StringComparison.OrdinalIgnoreCase) == true,
                Remote = source.AttendanceWorkMode?.Equals("Remote", StringComparison.OrdinalIgnoreCase) == true,
                MissingCheckIn = source.Id.HasValue && !source.CheckInUtc.HasValue &&
                    statusCode?.Equals("L", StringComparison.OrdinalIgnoreCase) != true && !isScheduledOff,
                MissingCheckOut = source.CheckInUtc.HasValue && !source.CheckOutUtc.HasValue && source.AttendanceDate < localToday
            });
        }

        var summary = new DailyAttendanceSummaryDto
        {
            TotalEmployees = rows.Select(r => r.PersonId).Distinct().Count(),
            Present = rows.Count(r => r.Present),
            Absent = rows.Count(r => r.Absent),
            Late = rows.Count(r => r.LateMinutes > 0),
            OnLeave = rows.Count(r => r.OnLeave),
            Remote = rows.Count(r => r.Remote),
            MissingCheckIn = rows.Count(r => r.MissingCheckIn),
            MissingCheckOut = rows.Count(r => r.MissingCheckOut),
            TotalWorkingMinutes = rows.Sum(r => r.WorkingMinutes),
            TotalOvertimeMinutes = rows.Sum(r => r.OvertimeMinutes)
        };
        return new DailyAttendanceReportDto { DateFrom = dateFrom, DateTo = dateTo, Rows = rows, Summary = summary };
    }

    private async Task<EffectiveTiming> ResolveEffectiveTimingAsync(
        Person person,
        DateOnly date,
        EffectiveAttendanceRule? attendanceRule,
        CancellationToken cancellationToken)
    {
        // Weekend policy is organization-wide and cannot be overridden by a
        // stale or manually inserted schedule row.
        if (date.DayOfWeek == DayOfWeek.Saturday)
            return new EffectiveTiming(false, DayOffCode, null, null);
        if (date.DayOfWeek == DayOfWeek.Sunday)
            return new EffectiveTiming(false, HolidayCode, null, null);

        var schedule = await _db.EmployeeTimingSchedules.AsNoTracking()
            .Include(item => item.HolidayType)
            .FirstOrDefaultAsync(item =>
                item.Staff.PersonId == person.PersonId && item.ScheduleDate == date,
                cancellationToken);
        if (schedule != null)
            return new EffectiveTiming(
                schedule.IsOn,
                schedule.HolidayType.ValueCode,
                schedule.TimeFrom ?? attendanceRule?.TimeFrom ?? person.ShiftStartTime,
                schedule.TimeTo ?? attendanceRule?.TimeTo ?? person.ShiftEndTime);

        return new EffectiveTiming(
            true,
            WorkingDayCode,
            attendanceRule?.TimeFrom ?? person.ShiftStartTime,
            attendanceRule?.TimeTo ?? person.ShiftEndTime);
    }

    private async Task<EffectiveAttendanceRule?> ResolveAttendanceRuleAsync(
        Person person,
        CancellationToken cancellationToken) =>
        await _db.AttendanceMapRules.AsNoTracking()
            .Where(rule =>
                rule.TenantId == person.TenantId &&
                rule.Staff.PersonId == person.PersonId)
            .Select(rule => new EffectiveAttendanceRule(
                rule.AttendanceEntryTypeId,
                rule.AttendanceEntryType.Code,
                rule.AttendanceEntryType.Name,
                rule.AttendanceEntryType.IsActive,
                rule.ShiftCode,
                rule.TimeFrom,
                rule.TimeTo,
                rule.IsOpenAttendance))
            .SingleOrDefaultAsync(cancellationToken);

    private static void EnsurePortalCheckInAllowed(EffectiveAttendanceRule attendanceRule)
    {
        if (!attendanceRule.EntryTypeIsActive)
            throw new InvalidOperationException("Your mapped attendance type is inactive. Ask an attendance administrator to update your attendance rule.");

        var reason = attendanceRule.EntryTypeCode switch
        {
            "CHECK" or "REMOTE" => null,
            "LOGIN" => "Your attendance is recorded through system login; manual check-in is not available.",
            "MACHINE" => "Your attendance is recorded through the attendance machine; manual check-in is not available.",
            "CAMERA" => "Your attendance is recorded through the camera system; manual check-in is not available.",
            "STAFF_GUARD" => "Your attendance is recorded by attendance staff; manual check-in is not available.",
            "SYSTEM_IP" => "Your attendance is recorded through the approved system/IP; manual check-in is not available.",
            "NONE" => "Attendance is not required for your mapped attendance rule.",
            "BY_SUPERVISOR" => "Your attendance must be recorded by your supervisor; manual check-in is not available.",
            _ => "Your mapped attendance type does not support portal check-in."
        };

        if (reason != null) throw new InvalidOperationException(reason);
    }

    private static string? PortalCheckInRestriction(EffectiveAttendanceRule? attendanceRule)
    {
        if (attendanceRule == null)
            return "Attendance rule is not configured. Ask an attendance administrator to map your attendance type and shift.";
        if (!attendanceRule.EntryTypeIsActive)
            return "The mapped attendance type is inactive. Ask an attendance administrator to update it.";

        return attendanceRule.EntryTypeCode switch
        {
            "CHECK" or "REMOTE" => null,
            "LOGIN" => "Attendance is recorded through system login.",
            "MACHINE" => "Attendance is recorded through the attendance machine.",
            "CAMERA" => "Attendance is recorded through the camera system.",
            "STAFF_GUARD" => "Attendance is recorded by attendance staff.",
            "SYSTEM_IP" => "Attendance is recorded through the approved system/IP.",
            "NONE" => "Attendance is not required for this rule.",
            "BY_SUPERVISOR" => "Attendance is recorded by your supervisor.",
            _ => "This attendance type does not support portal check-in."
        };
    }

    private sealed record EffectiveTiming(
        bool IsOn,
        string HolidayType,
        string? TimeFrom,
        string? TimeTo);

    private sealed record EffectiveAttendanceRule(
        int AttendanceEntryTypeId,
        string EntryTypeCode,
        string EntryTypeName,
        bool EntryTypeIsActive,
        string ShiftCode,
        string TimeFrom,
        string TimeTo,
        bool IsOpenAttendance);

    private sealed record ValidatedTimingSchedule(
        int HolidayTypeId,
        string HolidayTypeCode,
        string? TimeFrom,
        string? TimeTo,
        bool IsOn,
        int WorkingMinutes);

    private async Task<TimingChartEmployeeContext> GetEditableTimingChartEmployeeAsync(
        string identityUserId,
        bool organizationWide,
        Guid staffId,
        CancellationToken cancellationToken)
    {
        var visibility = await ResolveAttendanceVisibilityAsync(
            identityUserId, organizationWide, selfOnly: false, cancellationToken);
        var employee = await GetTimingChartEmployeeAsync(staffId, cancellationToken);
        var canEdit = visibility.VisiblePersonIds.Contains(employee.PersonId) &&
            (organizationWide || employee.PersonId != visibility.CallerPersonId);
        if (!canEdit)
            throw new InvalidOperationException("Only an authorized head can update this employee's Timing Chart.");

        return employee;
    }

    private async Task<ValidatedTimingSchedule> ValidateTimingScheduleAsync(
        TimingChartEmployeeContext employee,
        int holidayTypeId,
        string? timeFromInput,
        string? timeToInput,
        bool isOn,
        CancellationToken cancellationToken)
    {
        var holidayType = await _db.AppLookupValues.AsNoTracking()
            .Where(value =>
                value.IsActive &&
                value.LookupType != null &&
                value.LookupType.IsActive &&
                value.LookupType.LookupTypeCode == TimingHolidayTypeLookupCode &&
                value.LookupValueId == holidayTypeId)
            .Select(value => new { value.LookupValueId, value.ValueCode })
            .SingleOrDefaultAsync(cancellationToken);
        if (holidayType == null)
            throw new InvalidOperationException("Select a valid active Work Type.");

        string? timeFrom = null;
        string? timeTo = null;
        var holidayTypeCode = holidayType.ValueCode;
        var effectiveHolidayTypeId = holidayType.LookupValueId;
        var workingMinutes = 0;
        if (isOn)
        {
            if (!TryNormalizeTime(timeFromInput ?? employee.DefaultTimeFrom, out timeFrom) ||
                !TryNormalizeTime(timeToInput ?? employee.DefaultTimeTo, out timeTo))
                throw new InvalidOperationException("Time From and Time To are required in HH:mm format for an On day.");
            if (timeFrom == timeTo)
                throw new InvalidOperationException("Time From and Time To cannot be the same.");
            if (holidayTypeCode == DayOffCode)
            {
                var workingDay = await GetTimingHolidayTypeByCodeAsync(WorkingDayCode, cancellationToken);
                effectiveHolidayTypeId = workingDay.LookupValueId;
                holidayTypeCode = workingDay.ValueCode;
            }
            workingMinutes = ShiftMinutes(timeFrom, timeTo);
        }
        else if (holidayTypeCode == WorkingDayCode)
        {
            var dayOff = await GetTimingHolidayTypeByCodeAsync(DayOffCode, cancellationToken);
            effectiveHolidayTypeId = dayOff.LookupValueId;
            holidayTypeCode = dayOff.ValueCode;
        }

        return new ValidatedTimingSchedule(effectiveHolidayTypeId, holidayTypeCode, timeFrom, timeTo, isOn, workingMinutes);
    }

    private async Task<ValidatedTimingSchedule> ApplyRequiredWeekendRuleAsync(
        DateOnly date,
        ValidatedTimingSchedule requested,
        CancellationToken cancellationToken) => date.DayOfWeek switch
    {
        DayOfWeek.Saturday => await RequiredOffScheduleAsync(DayOffCode, cancellationToken),
        DayOfWeek.Sunday => await RequiredOffScheduleAsync(HolidayCode, cancellationToken),
        _ => requested
    };

    private async Task<ValidatedTimingSchedule> RequiredOffScheduleAsync(
        string holidayTypeCode,
        CancellationToken cancellationToken)
    {
        var holidayType = await GetTimingHolidayTypeByCodeAsync(holidayTypeCode, cancellationToken);
        return new ValidatedTimingSchedule(holidayType.LookupValueId, holidayType.ValueCode, null, null, false, 0);
    }

    private async Task<(int LookupValueId, string ValueCode, string DisplayText)> GetTimingHolidayTypeByCodeAsync(
        string holidayTypeCode,
        CancellationToken cancellationToken)
    {
        var holidayType = await _db.AppLookupValues.AsNoTracking()
            .Where(value =>
                value.IsActive &&
                value.LookupType != null &&
                value.LookupType.IsActive &&
                value.LookupType.LookupTypeCode == TimingHolidayTypeLookupCode &&
                value.ValueCode == holidayTypeCode)
            .Select(value => new { value.LookupValueId, value.ValueCode, value.DisplayText })
            .SingleOrDefaultAsync(cancellationToken);
        return holidayType == null
            ? throw new InvalidOperationException($"Timing holiday type '{holidayTypeCode}' is not configured.")
            : (holidayType.LookupValueId, holidayType.ValueCode, holidayType.DisplayText);
    }

    private async Task<TimingChartEmployeeContext> GetTimingChartEmployeeAsync(
        Guid staffId,
        CancellationToken cancellationToken) =>
        await _db.StaffDirectoryRows.AsNoTracking()
            .Where(staff =>
                staff.StaffId == staffId &&
                staff.IsPersonActive)
            .Select(staff => new TimingChartEmployeeContext
            {
                TenantId = staff.TenantId,
                StaffId = staff.StaffId,
                PersonId = staff.PersonId,
                FullName = staff.FullName,
                EmployeeId = staff.EmployeeId,
                Department = staff.Department,
                DefaultTimeFrom = staff.ShiftStartTime,
                DefaultTimeTo = staff.ShiftEndTime
            })
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new KeyNotFoundException("The selected employee was not found in your organization.");

    private async Task<IReadOnlyList<TimingChartHolidayTypeDto>> GetTimingHolidayTypesAsync(
        CancellationToken cancellationToken)
    {
        var values = await _db.AppLookupValues.AsNoTracking()
            .Where(value =>
                value.IsActive &&
                value.LookupType != null &&
                value.LookupType.IsActive &&
                value.LookupType.LookupTypeCode == TimingHolidayTypeLookupCode)
            .OrderBy(value => value.SortOrder)
            .Select(value => new { value.LookupValueId, value.ValueCode, value.DisplayText, value.MetadataJson })
            .ToListAsync(cancellationToken);

        return values.Select(value => new TimingChartHolidayTypeDto
        {
            Id = value.LookupValueId,
            Code = value.ValueCode,
            Name = value.DisplayText,
            DefaultIsOn = ReadDefaultIsOn(value.MetadataJson)
        }).ToList();
    }

    private static bool ReadDefaultIsOn(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return true;
        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            return !document.RootElement.TryGetProperty("defaultIsOn", out var value) ||
                value.ValueKind != JsonValueKind.False;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static TimingChartScheduleRowDto MapTimingChartSchedule(
        TimingChartEmployeeContext employee,
        EmployeeTimingSchedule? schedule,
        DateOnly date,
        IReadOnlyDictionary<string, TimingChartHolidayTypeDto> holidayTypesByCode)
    {
        var requiredWeekendType = date.DayOfWeek switch
        {
            DayOfWeek.Saturday => DayOffCode,
            DayOfWeek.Sunday => HolidayCode,
            _ => null
        };
        var isOn = requiredWeekendType == null && (schedule?.IsOn ?? true);
        var holidayType = requiredWeekendType ?? schedule?.HolidayType?.ValueCode ?? WorkingDayCode;
        holidayTypesByCode.TryGetValue(holidayType, out var holidayTypeInfo);
        var holidayTypeId = requiredWeekendType == null
            ? schedule?.HolidayTypeId ?? holidayTypeInfo?.Id ?? 0
            : holidayTypeInfo?.Id ?? 0;
        var timeFrom = requiredWeekendType == null
            ? schedule != null ? schedule.TimeFrom : employee.DefaultTimeFrom
            : null;
        var timeTo = requiredWeekendType == null
            ? schedule != null ? schedule.TimeTo : employee.DefaultTimeTo
            : null;
        var workingMinutes = isOn && timeFrom != null && timeTo != null
            ? schedule?.WorkingMinutes > 0 ? schedule.WorkingMinutes : ShiftMinutes(timeFrom, timeTo)
            : 0;

        return new TimingChartScheduleRowDto
        {
            Id = schedule?.Id,
            PersonId = employee.PersonId,
            StaffId = employee.StaffId,
            FullName = employee.FullName,
            EmployeeId = employee.EmployeeId,
            Department = employee.Department,
            HolidayDate = date,
            Day = date.ToString("ddd", CultureInfo.InvariantCulture),
            HolidayTypeId = holidayTypeId,
            HolidayType = holidayType,
            HolidayTypeName = holidayTypeInfo?.Name ?? schedule?.HolidayType?.DisplayText ?? holidayType,
            TimeFrom = timeFrom,
            TimeTo = timeTo,
            WorkingMinutes = workingMinutes,
            IsOn = isOn,
            IsOverride = schedule != null
        };
    }

    private static bool TryNormalizeTime(string? value, out string? normalized)
    {
        normalized = null;
        if (!TimeOnly.TryParseExact(
                value?.Trim(),
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
            return false;

        normalized = parsed.ToString("HH:mm", CultureInfo.InvariantCulture);
        return true;
    }

    private sealed class TimingChartEmployeeContext
    {
        public int TenantId { get; init; }
        public Guid StaffId { get; init; }
        public Guid PersonId { get; init; }
        public string FullName { get; init; } = string.Empty;
        public string EmployeeId { get; init; } = string.Empty;
        public string Department { get; init; } = string.Empty;
        public string DefaultTimeFrom { get; init; } = "09:00";
        public string DefaultTimeTo { get; init; } = "18:00";
    }

    private async Task<AttendanceVisibilityContext> ResolveAttendanceVisibilityAsync(
        string identityUserId,
        bool organizationWide,
        bool selfOnly,
        CancellationToken cancellationToken)
    {
        var caller = await _db.Persons.AsNoTracking()
            .Where(person => person.IdentityUserId == identityUserId && person.IsActive)
            .Select(person => new
            {
                person.PersonId,
                person.TenantId,
                OrganizationId = person.Staff != null && person.Staff.Vacancy != null
                    ? (int?)person.Staff.Vacancy.OrganizationId
                    : null,
                JobTitle = person.Staff != null && person.Staff.Vacancy != null
                    ? (person.Staff.Vacancy.JobTitleNav != null
                        ? person.Staff.Vacancy.JobTitleNav.TitleName
                        : person.Staff.Vacancy.JobTitle)
                    : null,
                AttendanceScope = person.Staff != null &&
                    person.Staff.Vacancy != null &&
                    person.Staff.Vacancy.JobTitleNav != null
                        ? person.Staff.Vacancy.JobTitleNav.AttendanceVisibilityScope
                        : AttendanceVisibilityScope.Self
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("No active employee profile is linked to this account.");

        var people = await _db.Persons.AsNoTracking()
            .Where(person => person.IsActive)
            .Select(person => new AttendanceVisibilityPerson
            {
                PersonId = person.PersonId,
                FullName = person.FullName,
                OrganizationId = person.Staff != null && person.Staff.Vacancy != null
                    ? (int?)person.Staff.Vacancy.OrganizationId
                    : null,
                JobTitle = person.Staff != null && person.Staff.Vacancy != null
                    ? (person.Staff.Vacancy.JobTitleNav != null
                        ? person.Staff.Vacancy.JobTitleNav.TitleName
                        : person.Staff.Vacancy.JobTitle)
                    : null
            })
            .ToListAsync(cancellationToken);

        var visiblePersonIds = new HashSet<Guid> { caller.PersonId };
        var callerRank = AttendanceRoleRank(caller.JobTitle);

        if (!selfOnly && organizationWide)
        {
            foreach (var person in people) visiblePersonIds.Add(person.PersonId);
        }
        else if (!selfOnly && caller.OrganizationId.HasValue)
        {
            // The same title rank and configured visibility scope drive Daily Attendance,
            // Monthly Chart, and Timing Chart so an attendance screen cannot widen access.
            var derivedScope = callerRank switch
            {
                >= 300 => AttendanceVisibilityScope.OrganizationNodeAndDescendants,
                >= 200 => AttendanceVisibilityScope.OrganizationNode,
                _ => AttendanceVisibilityScope.Self
            };
            var effectiveScope = (AttendanceVisibilityScope)Math.Max(
                (int)caller.AttendanceScope,
                (int)derivedScope);

            if (effectiveScope != AttendanceVisibilityScope.Self)
            {
                var visibleNodeIds = new HashSet<int> { caller.OrganizationId.Value };
                if (effectiveScope == AttendanceVisibilityScope.OrganizationNodeAndDescendants)
                {
                    var nodes = await _db.OrganizationTree.AsNoTracking()
                        .Where(node => node.IsActive)
                        .Select(node => new { node.Id, node.ParentId })
                        .ToListAsync(cancellationToken);
                    var nodeChildren = nodes
                        .Where(node => node.ParentId.HasValue)
                        .ToLookup(node => node.ParentId!.Value, node => node.Id);
                    var pendingNodes = new Queue<int>();
                    pendingNodes.Enqueue(caller.OrganizationId.Value);
                    while (pendingNodes.TryDequeue(out var parentNodeId))
                        foreach (var childNodeId in nodeChildren[parentNodeId])
                            if (visibleNodeIds.Add(childNodeId)) pendingNodes.Enqueue(childNodeId);
                }

                foreach (var person in people)
                    if (person.OrganizationId.HasValue &&
                        visibleNodeIds.Contains(person.OrganizationId.Value) &&
                        AttendanceRoleRank(person.JobTitle) < callerRank)
                        visiblePersonIds.Add(person.PersonId);
            }
        }

        return new AttendanceVisibilityContext
        {
            CallerPersonId = caller.PersonId,
            TenantId = caller.TenantId,
            People = people,
            VisiblePersonIds = visiblePersonIds
        };
    }

    private sealed class AttendanceVisibilityPerson
    {
        public Guid PersonId { get; init; }
        public string FullName { get; init; } = string.Empty;
        public int? OrganizationId { get; init; }
        public string? JobTitle { get; init; }
    }

    private sealed class AttendanceVisibilityContext
    {
        public Guid CallerPersonId { get; init; }
        public int TenantId { get; init; }
        public IReadOnlyList<AttendanceVisibilityPerson> People { get; init; } = [];
        public HashSet<Guid> VisiblePersonIds { get; init; } = [];
    }

    private static int AttendanceRoleRank(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return 0;
        var value = new string(title.Trim().ToLowerInvariant()
            .Where(char.IsLetterOrDigit).ToArray());

        // Detect compound titles first, then apply the hierarchy in its actual order.
        // This prevents "Duty CEO" from being classified as CEO and deputy/assistant
        // managers from being classified as Manager merely because their titles contain it.
        var isDutyCeo = value.Contains("dutyceo");
        var isDeputyManager = value.Contains("deputymanager") || value.Contains("deptymanager");
        var isAssistantManager = value.Contains("assistantmanager") ||
                                 value.Contains("asstmanager") ||
                                 value.Contains("assistmanager");

        if (!isDutyCeo && (value.Contains("ceo") || value.Contains("chiefexecutive"))) return 700;
        if (isDutyCeo) return 600;
        if (!isDeputyManager && !isAssistantManager && value.Contains("manager")) return 500;
        if (isDeputyManager) return 400;
        if (isAssistantManager) return 300;
        if (value.Contains("supervisor") || value.Contains("teamlead")) return 200;
        if (value.Contains("agent") || value.Contains("bellboy")) return 100;
        return 0;
    }

    public async Task<MonthlyAttendanceReportDto> GetMonthlyReportAsync(string identityUserId, bool canViewOthers, Guid? requestedPersonId, int year, int month, CancellationToken cancellationToken = default)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month), "A valid report month is required.");
        var callerPersonId = await _db.Persons.AsNoTracking().Where(p => p.IdentityUserId == identityUserId).Select(p => (Guid?)p.PersonId).FirstOrDefaultAsync(cancellationToken);
        var personId = canViewOthers && requestedPersonId.HasValue ? requestedPersonId.Value : callerPersonId
            ?? throw new KeyNotFoundException("No employee profile is linked to this account.");

        var employee = await _db.StaffDirectoryRows.AsNoTracking()
            .Where(s => s.PersonId == personId && s.IsPersonActive)
            .Select(s => new AttendanceReportStaffDto
            {
                PersonId = personId, StaffId = s.StaffId, EmployeeId = s.EmployeeId,
                FullName = s.FullName, Department = s.Department, Designation = s.Designation
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("The selected employee was not found in your organization.");

        var person = await _db.Persons.AsNoTracking().Where(p => p.PersonId == personId)
            .Select(p => new { p.TimeZoneId, p.ShiftStartTime, p.ShiftEndTime }).FirstAsync(cancellationToken);
        var schedules = await _db.EmployeeTimingSchedules.AsNoTracking()
            .Where(schedule =>
                schedule.StaffId == employee.StaffId &&
                schedule.ScheduleYear == year &&
                schedule.ScheduleMonth == month)
            .ToDictionaryAsync(schedule => schedule.ScheduleDate, cancellationToken);
        var dateFrom = new DateOnly(year, month, 1);
        var dateTo = dateFrom.AddMonths(1);
        var records = await _db.AttendanceRecords.AsNoTracking().Include(r => r.AttendanceStatus)
            .Where(r => r.PersonId == personId && r.AttendanceDate >= dateFrom && r.AttendanceDate < dateTo)
            .OrderByDescending(r => r.AttendanceDate).ToListAsync(cancellationToken);
        var zone = ResolveTimeZone(person.TimeZoneId);
        var rows = records.Select(r =>
        {
            var end = r.CheckOutUtc;
            var gross = r.CheckInUtc.HasValue && end.HasValue ? Math.Max(0, (int)Math.Floor((end.Value - r.CheckInUtc.Value).TotalMinutes)) : 0;
            var worked = Math.Max(0, gross - r.TotalBreakMinutes);
            schedules.TryGetValue(r.AttendanceDate, out var schedule);
            var defaultOn = r.AttendanceDate.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
            var isOn = schedule?.IsOn ?? defaultOn;
            var timeFrom = schedule?.TimeFrom ?? person.ShiftStartTime;
            var timeTo = schedule?.TimeTo ?? person.ShiftEndTime;
            var required = isOn ? ShiftMinutes(timeFrom, timeTo) : 0;
            return new MonthlyAttendanceRowDto
            {
                Id = r.Id, PersonId = personId, StaffId = employee.StaffId, EmployeeId = employee.EmployeeId,
                FullName = employee.FullName, Department = employee.Department, Designation = employee.Designation,
                AttendanceDate = r.AttendanceDate,
                CheckIn = r.CheckInUtc.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(r.CheckInUtc.Value, zone).ToString("HH:mm") : null,
                CheckOut = r.CheckOutUtc.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(r.CheckOutUtc.Value, zone).ToString("HH:mm") : null,
                WorkingMinutes = worked, BreakMinutes = r.TotalBreakMinutes, RequiredMinutes = required,
                ShortMinutes = r.CheckOutUtc.HasValue ? Math.Max(0, required - worked) : 0,
                AttendanceStatusId = r.AttendanceStatusId, StatusCode = r.AttendanceStatus?.Code,
                StatusName = r.AttendanceStatus?.Status.StatusName, StatusColorCode = r.AttendanceStatus?.ColorStyle.ColorCode
            };
        }).ToList();
        return new MonthlyAttendanceReportDto { Employee = employee, Year = year, Month = month, Rows = rows };
    }

    private async Task<(Person Person, DateOnly LocalDate)> ResolvePersonAsync(string identityUserId, CancellationToken cancellationToken)
    {
        var person = await _db.Persons.AsNoTracking().FirstOrDefaultAsync(x => x.IdentityUserId == identityUserId, cancellationToken)
            ?? throw new KeyNotFoundException("No employee profile is linked to this account.");
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ResolveTimeZone(person.TimeZoneId));
        return (person, DateOnly.FromDateTime(localNow));
    }

    private async Task<AttendancePolicy> LoadPolicyAsync(int tenantId, CancellationToken cancellationToken) =>
        await _db.AttendancePolicies.AsNoTracking()
            .Where(x => x.IsActive && (x.TenantId == tenantId || x.TenantId == null))
            .OrderByDescending(x => x.TenantId == tenantId)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException("No active attendance policy is configured for this company.");

    private Task EvaluateStatusesAsync(int tenantId, DateOnly dateFrom, DateOnly dateTo, CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlRawAsync(
            "EXEC dbo.usp_Attendance_EvaluateStatuses @TenantId, @DateFrom, @DateTo, @AsOfUtc",
            new object[] {
                new SqlParameter("@TenantId", tenantId),
                new SqlParameter("@DateFrom", dateFrom.ToDateTime(TimeOnly.MinValue)),
                new SqlParameter("@DateTo", dateTo.ToDateTime(TimeOnly.MinValue)),
                new SqlParameter("@AsOfUtc", DateTime.UtcNow)
            }, cancellationToken);

    private static MyAttendanceTodayDto Map(
        Person person,
        AttendanceRecord? record,
        DateTime utcNow,
        EffectiveTiming timing,
        EffectiveAttendanceRule? attendanceRule)
    {
        var shiftStart = timing.TimeFrom ?? person.ShiftStartTime;
        var shiftEnd = timing.TimeTo ?? person.ShiftEndTime;
        var required = timing.IsOn ? ShiftMinutes(shiftStart, shiftEnd) : 0;
        var end = record?.CheckOutUtc ?? (record?.CheckInUtc.HasValue == true ? utcNow : null);
        var activeBreak = record?.BreakStartedUtc.HasValue == true ? Math.Max(0, (int)Math.Floor((utcNow - record.BreakStartedUtc.Value).TotalMinutes)) : 0;
        var gross = record?.CheckInUtc.HasValue == true && end.HasValue ? Math.Max(0, (int)Math.Floor((end.Value - record.CheckInUtc.Value).TotalMinutes)) : 0;
        var worked = Math.Max(0, gross - (record?.TotalBreakMinutes ?? 0) - activeBreak);
        var checkInRestriction = PortalCheckInRestriction(attendanceRule);
        return new MyAttendanceTodayDto
        {
            Id = record?.Id, AttendanceDate = record?.AttendanceDate ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, ResolveTimeZone(person.TimeZoneId))),
            EmployeeName = person.FullName, ShiftStartTime = shiftStart, ShiftEndTime = shiftEnd, TimeZoneId = person.TimeZoneId,
            // SQL Server datetime2 does not preserve DateTime.Kind. Mark persisted UTC
            // values explicitly so JSON includes the UTC designator and clients do not
            // interpret them as local wall-clock values.
            CheckInUtc = AsUtc(record?.CheckInUtc),
            CheckOutUtc = AsUtc(record?.CheckOutUtc),
            BreakStartedUtc = AsUtc(record?.BreakStartedUtc),
            TotalBreakMinutes = record?.TotalBreakMinutes ?? 0, WorkedMinutes = worked, RequiredMinutes = required,
            ShortMinutes = record?.CheckOutUtc.HasValue == true ? Math.Max(0, required - worked) : 0,
            RemainingMinutes = record?.CheckInUtc.HasValue == true && !record.CheckOutUtc.HasValue ? Math.Max(0, required - worked) : 0,
            ProgressPercent = required == 0 ? 0 : Math.Round(Math.Min(100, worked * 100d / required), 1),
            IsWorkingDay = timing.IsOn,
            HolidayType = timing.HolidayType
            ,AttendanceEntryTypeId = record?.AttendanceEntryTypeId
            ,AttendanceEntryType = record?.AttendanceEntryType?.Name ??
                (record?.AttendanceEntryTypeId == attendanceRule?.AttendanceEntryTypeId ? attendanceRule?.EntryTypeName : null)
            ,AttendanceWorkModeId = record?.AttendanceWorkModeId
            ,AttendanceWorkMode = record?.AttendanceWorkMode?.Name
            ,AttendanceRuleConfigured = attendanceRule != null
            ,AttendanceTypeCode = attendanceRule?.EntryTypeCode
            ,AttendanceTypeName = attendanceRule?.EntryTypeName
            ,AttendanceShiftCode = attendanceRule?.ShiftCode
            ,IsOpenAttendance = attendanceRule?.IsOpenAttendance ?? false
            ,CanSelfCheckIn = checkInRestriction == null
            ,CheckInRestrictionReason = checkInRestriction
        };
    }

    private static int ShiftMinutes(string start, string end)
    {
        if (!TimeOnly.TryParseExact(start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) || !TimeOnly.TryParseExact(end, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to)) return 540;
        var minutes = (int)(to.ToTimeSpan() - from.ToTimeSpan()).TotalMinutes;
        return minutes > 0 ? minutes : minutes + 1440;
    }

    private static TimeOnly ParseShift(string value, TimeOnly fallback) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : fallback;

    private static int CountWorkingDays(int year, int month)
    {
        var end = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (year == today.Year && month == today.Month && today < end) end = today;
        var count = 0;
        for (var day = new DateOnly(year, month, 1); day <= end; day = day.AddDays(1))
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday) count++;
        return count;
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time"); }
    }

    private static DateTime? AsUtc(DateTime? value) => value.HasValue
        ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        : null;
}
