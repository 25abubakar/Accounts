using System.Globalization;
using Accounts.Data;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class AttendanceFinalizationService(
    ApplicationDbContext db,
    ILogger<AttendanceFinalizationService> logger)
{
    public async Task<IReadOnlyDictionary<int, int>> RefreshCurrentPeriodsAsync(CancellationToken cancellationToken = default)
    {
        var today = PakistanClock.Today();
        var tenantIds = await db.Tenants.AsNoTracking()
            .Where(tenant => tenant.IsActive)
            .Select(tenant => tenant.Id)
            .ToListAsync(cancellationToken);
        var changesByTenant = new Dictionary<int, int>();

        foreach (var tenantId in tenantIds)
        {
            try
            {
                var changed = await RefreshPeriodAsync(
                    tenantId,
                    today.Year,
                    today.Month,
                    cancellationToken);
                if (changed > 0)
                    changesByTenant[tenantId] = changed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Attendance finalization failed for tenant {TenantId}.",
                    tenantId);
            }
        }

        return changesByTenant;
    }

    public async Task<int> RefreshPeriodAsync(
        int tenantId,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month));

        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var today = PakistanClock.Today();
        var dateTo = monthEnd < today ? monthEnd : today;
        if (monthStart > dateTo)
            return 0;

        var employees = await (
            from staff in db.StaffVacancies.IgnoreQueryFilters().AsNoTracking()
            join person in db.Persons.IgnoreQueryFilters().AsNoTracking()
                on staff.PersonId equals person.PersonId
            join profile in db.PersonHrProfiles.IgnoreQueryFilters().AsNoTracking()
                on new { staff.TenantId, PersonId = person.PersonId }
                equals new { profile.TenantId, profile.PersonId } into profiles
            from profile in profiles.DefaultIfEmpty()
            where staff.TenantId == tenantId && person.TenantId == tenantId && person.IsActive
            select new EmployeeRow(
                person.PersonId,
                staff.StaffId,
                person.ShiftStartTime,
                person.ShiftEndTime,
                profile == null ? null : profile.JoiningDate,
                person.TerminationDateUtc))
            .ToListAsync(cancellationToken);

        if (employees.Count == 0)
            return 0;

        var staffIds = employees.Select(employee => employee.StaffId).ToArray();
        var personIds = employees.Select(employee => employee.PersonId).ToArray();

        var mapRules = (await db.AttendanceMapRules.IgnoreQueryFilters().AsNoTracking()
                .Where(rule => rule.TenantId == tenantId && staffIds.Contains(rule.StaffId))
                .ToListAsync(cancellationToken))
            .GroupBy(rule => rule.StaffId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(rule => rule.Id).First());

        var entryTypeIds = mapRules.Values.Select(rule => rule.AttendanceEntryTypeId).Distinct().ToArray();
        var ruleSettings = (await db.AttendanceRuleSettings.IgnoreQueryFilters().AsNoTracking()
                .Where(rule =>
                    rule.TenantId == tenantId &&
                    rule.IsActive &&
                    rule.IsApproved &&
                    entryTypeIds.Contains(rule.AttendanceEntryTypeId))
                .ToListAsync(cancellationToken))
            .GroupBy(rule => rule.AttendanceEntryTypeId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(rule => rule.Id).First());

        var entryTypeCodes = await db.AttendanceTypes.IgnoreQueryFilters().AsNoTracking()
            .Where(type => type.TenantId == tenantId && entryTypeIds.Contains(type.Id))
            .ToDictionaryAsync(type => type.Id, type => type.Code, cancellationToken);

        var schedules = await db.EmployeeTimingSchedules.IgnoreQueryFilters().AsNoTracking()
            .Where(schedule =>
                schedule.TenantId == tenantId &&
                staffIds.Contains(schedule.StaffId) &&
                schedule.ScheduleDate >= monthStart &&
                schedule.ScheduleDate <= dateTo)
            .ToListAsync(cancellationToken);
        var schedulesByDay = schedules.ToDictionary(
            schedule => (schedule.StaffId, schedule.ScheduleDate));

        var records = await db.AttendanceRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(record =>
                record.TenantId == tenantId &&
                personIds.Contains(record.PersonId) &&
                record.AttendanceDate >= monthStart &&
                record.AttendanceDate <= dateTo)
            .ToListAsync(cancellationToken);
        var recordsByDay = records.ToDictionary(record => (record.PersonId, record.AttendanceDate));

        var statusIds = records
            .Where(record => record.AttendanceStatusId.HasValue)
            .Select(record => record.AttendanceStatusId!.Value)
            .Distinct()
            .ToArray();
        var statuses = statusIds.Length == 0
            ? new Dictionary<int, StatusRow>()
            : await db.ProcessStatusStyles.IgnoreQueryFilters().AsNoTracking()
                .Where(status => statusIds.Contains(status.Id))
                .Select(status => new StatusRow(
                    status.Id,
                    status.Code,
                    status.Status.StatusName,
                    status.IsPaid))
                .ToDictionaryAsync(status => status.Id, cancellationToken);

        var existingRows = await db.Set<AttendanceDailyFinalization>()
            .IgnoreQueryFilters()
            .Where(row =>
                row.TenantId == tenantId &&
                personIds.Contains(row.PersonId) &&
                row.AttendanceDate >= monthStart &&
                row.AttendanceDate <= dateTo)
            .ToListAsync(cancellationToken);
        var existingByDay = existingRows.ToDictionary(row => (row.PersonId, row.AttendanceDate));

        var localNow = PakistanClock.Now();
        var utcNow = DateTime.UtcNow;
        var changed = 0;

        foreach (var employee in employees)
        {
            mapRules.TryGetValue(employee.StaffId, out var mapRule);
            AttendanceRuleSetting? rule = null;
            if (mapRule is not null)
                ruleSettings.TryGetValue(mapRule.AttendanceEntryTypeId, out rule);

            var isNotRequired = mapRule is not null &&
                entryTypeCodes.TryGetValue(mapRule.AttendanceEntryTypeId, out var entryTypeCode) &&
                IsNotRequiredEntryType(entryTypeCode);

            for (var date = monthStart; date <= dateTo; date = date.AddDays(1))
            {
                if (!IsInsideEmploymentWindow(employee, date))
                    continue;

                schedulesByDay.TryGetValue((employee.StaffId, date), out var schedule);
                recordsByDay.TryGetValue((employee.PersonId, date), out var attendance);
                var isWorkingDay = schedule?.IsOn ?? !IsWeekend(date);
                var timeFrom = schedule?.TimeFrom ?? mapRule?.TimeFrom ?? employee.ShiftStartTime;
                var timeTo = schedule?.TimeTo ?? mapRule?.TimeTo ?? employee.ShiftEndTime;
                var requiredMinutes = isWorkingDay
                    ? ResolveRequiredMinutes(schedule, rule, timeFrom, timeTo)
                    : 0;
                var shiftWindow = ResolveShiftWindow(date, timeFrom, timeTo);
                var deadline = shiftWindow.End.AddMinutes(
                    Math.Max(0, rule?.MissingCheckoutAfterShiftEndMinutes ?? 120));
                var effectiveCheckIn = attendance?.EffectiveCheckInUtc ?? attendance?.CheckInUtc;
                var effectiveCheckOut = attendance?.EffectiveCheckOutUtc ?? attendance?.CheckOutUtc;
                var isExcused = isNotRequired || IsExcusedAttendance(attendance, statuses);
                var calculation = AttendanceDailyFinalizationCalculator.Calculate(
                    new AttendanceDayCalculationInput(
                        isWorkingDay,
                        isExcused,
                        requiredMinutes,
                        localNow,
                        deadline,
                        effectiveCheckIn,
                        effectiveCheckOut,
                        attendance?.TotalBreakMinutes ?? 0));

                if (!existingByDay.TryGetValue((employee.PersonId, date), out var row))
                {
                    row = new AttendanceDailyFinalization
                    {
                        TenantId = tenantId,
                        PersonId = employee.PersonId,
                        StaffId = employee.StaffId,
                        AttendanceDate = date
                    };
                    db.Set<AttendanceDailyFinalization>().Add(row);
                    existingByDay[(employee.PersonId, date)] = row;
                    Apply(row, attendance?.Id, calculation, utcNow);
                    changed++;
                    continue;
                }

                if (!HasChanged(row, attendance?.Id, calculation))
                    continue;

                Apply(row, attendance?.Id, calculation, utcNow);
                changed++;
            }
        }

        if (changed > 0)
            await db.SaveChangesAsync(cancellationToken);

        return changed;
    }

    private static bool HasChanged(
        AttendanceDailyFinalization row,
        long? attendanceRecordId,
        AttendanceDayCalculation value) =>
        row.AttendanceRecordId != attendanceRecordId ||
        row.State != value.State ||
        row.IsWorkingDay != value.IsWorkingDay ||
        row.IsFinalized != value.IsFinalized ||
        row.IsFullDayAbsent != value.IsFullDayAbsent ||
        row.RequiredMinutes != value.RequiredMinutes ||
        row.WorkedMinutes != value.WorkedMinutes ||
        row.ShortMinutes != value.ShortMinutes ||
        row.OvertimeMinutes != value.OvertimeMinutes;

    private static void Apply(
        AttendanceDailyFinalization row,
        long? attendanceRecordId,
        AttendanceDayCalculation value,
        DateTime utcNow)
    {
        row.AttendanceRecordId = attendanceRecordId;
        row.State = value.State;
        row.IsWorkingDay = value.IsWorkingDay;
        row.IsFinalized = value.IsFinalized;
        row.IsFullDayAbsent = value.IsFullDayAbsent;
        row.RequiredMinutes = value.RequiredMinutes;
        row.WorkedMinutes = value.WorkedMinutes;
        row.ShortMinutes = value.ShortMinutes;
        row.OvertimeMinutes = value.OvertimeMinutes;
        row.FinalizedDateUtc = value.IsFinalized ? row.FinalizedDateUtc ?? utcNow : null;
        row.LastEvaluatedDateUtc = utcNow;
    }

    private static int ResolveRequiredMinutes(
        EmployeeTimingSchedule? schedule,
        AttendanceRuleSetting? rule,
        string? timeFrom,
        string? timeTo)
    {
        if (schedule?.WorkingMinutes > 0)
            return schedule.WorkingMinutes;
        if (rule?.WorkingMinutes > 0)
            return rule.WorkingMinutes;
        return ShiftMinutes(timeFrom, timeTo);
    }

    private static int ShiftMinutes(string? timeFrom, string? timeTo)
    {
        if (!TryTime(timeFrom, out var start) || !TryTime(timeTo, out var end))
            return 540;
        var minutes = (int)(end - start).TotalMinutes;
        return minutes > 0 ? minutes : minutes + 1440;
    }

    private static (DateTime Start, DateTime End) ResolveShiftWindow(
        DateOnly date,
        string? timeFrom,
        string? timeTo)
    {
        if (!TryTime(timeFrom, out var from))
            from = new TimeOnly(9, 0);
        if (!TryTime(timeTo, out var to))
            to = new TimeOnly(18, 0);
        var start = date.ToDateTime(from);
        var end = date.ToDateTime(to);
        if (end <= start)
            end = end.AddDays(1);
        return (start, end);
    }

    private static bool TryTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(
            value,
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time) || TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out time);

    private static bool IsInsideEmploymentWindow(EmployeeRow employee, DateOnly date)
    {
        if (employee.JoiningDate.HasValue && date < DateOnly.FromDateTime(employee.JoiningDate.Value))
            return false;
        if (employee.TerminationDateUtc.HasValue && date > DateOnly.FromDateTime(employee.TerminationDateUtc.Value))
            return false;
        return true;
    }

    private static bool IsWeekend(DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static bool IsNotRequiredEntryType(string? code)
    {
        var normalized = Normalize(code);
        return normalized is "NONE" or "NOTREQUIRED" or "NOREQUIREDATTENDANCE";
    }

    private static bool IsExcusedAttendance(
        AttendanceRecord? attendance,
        IReadOnlyDictionary<int, StatusRow> statuses)
    {
        if (attendance?.AttendanceStatusId is not int statusId ||
            !statuses.TryGetValue(statusId, out var status))
            return false;

        var code = Normalize(status.Code);
        var name = Normalize(status.Name);
        return code is "L" or "LEAVE" or "ONLEAVE" or "H" or "HOLIDAY" or "DO" or "DAYOFF" ||
               name.Contains("LEAVE", StringComparison.Ordinal) ||
               name.Contains("HOLIDAY", StringComparison.Ordinal) ||
               name.Contains("DAYOFF", StringComparison.Ordinal) ||
               status.IsPaid && (attendance.CheckInUtc is null && attendance.CheckOutUtc is null);
    }

    private static string Normalize(string? value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private sealed record EmployeeRow(
        Guid PersonId,
        Guid StaffId,
        string ShiftStartTime,
        string ShiftEndTime,
        DateTime? JoiningDate,
        DateTime? TerminationDateUtc);

    private sealed record StatusRow(int Id, string Code, string Name, bool IsPaid);
}
