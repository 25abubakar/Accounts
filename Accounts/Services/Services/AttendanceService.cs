using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Accounts.Services.Services;

public sealed class AttendanceService : IAttendanceService
{
    private readonly ApplicationDbContext _db;
    public AttendanceService(ApplicationDbContext db) => _db = db;

    public async Task<MyAttendanceTodayDto> GetTodayAsync(string identityUserId, CancellationToken cancellationToken = default)
    {
        var (person, localDate) = await ResolvePersonAsync(identityUserId, cancellationToken);
        var record = await _db.AttendanceRecords.AsNoTracking().FirstOrDefaultAsync(x => x.PersonId == person.PersonId && x.AttendanceDate == localDate, cancellationToken);
        return Map(person, record, DateTime.UtcNow);
    }

    public async Task<MyAttendanceTodayDto> CheckInAsync(string identityUserId, CancellationToken cancellationToken = default)
    {
        var (person, localDate) = await ResolvePersonAsync(identityUserId, cancellationToken);
        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(x => x.PersonId == person.PersonId && x.AttendanceDate == localDate, cancellationToken);
        if (record?.CheckInUtc is not null) throw new InvalidOperationException("You have already checked in today.");
        record ??= new AttendanceRecord { TenantId = person.TenantId, PersonId = person.PersonId, AttendanceDate = localDate, CreatedDate = DateTime.UtcNow };
        record.CheckInUtc = DateTime.UtcNow;
        record.ModifiedDate = DateTime.UtcNow;
        if (record.Id == 0) _db.AttendanceRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(person, record, DateTime.UtcNow);
    }

    public async Task<MyAttendanceTodayDto> ToggleBreakAsync(string identityUserId, CancellationToken cancellationToken = default)
    {
        var (person, localDate) = await ResolvePersonAsync(identityUserId, cancellationToken);
        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(x => x.PersonId == person.PersonId && x.AttendanceDate == localDate, cancellationToken)
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
        return Map(person, record, now);
    }

    public async Task<MyAttendanceTodayDto> CheckOutAsync(string identityUserId, CancellationToken cancellationToken = default)
    {
        var (person, localDate) = await ResolvePersonAsync(identityUserId, cancellationToken);
        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(x => x.PersonId == person.PersonId && x.AttendanceDate == localDate, cancellationToken)
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
        return Map(person, record, now);
    }

    public async Task<IReadOnlyList<AttendanceReportStaffDto>> GetReportStaffAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var staffRows = await _db.StaffVacancies.AsNoTracking()
            .Where(s => s.PersonId.HasValue && s.Person != null && s.Person.IsActive)
            .OrderBy(s => s.Person!.FullName)
            .Select(s => new
            {
                Dto = new AttendanceReportStaffDto
                {
                    PersonId = s.PersonId!.Value, StaffId = s.StaffId, EmployeeId = s.LoginId ?? s.Vacancy!.VacancyCode,
                    FullName = s.Person!.FullName, Department = s.Vacancy!.Department ?? s.Vacancy.Organization!.Name,
                    Designation = s.Vacancy.JobTitleNav != null ? s.Vacancy.JobTitleNav.TitleName : (s.Vacancy.JobTitle ?? string.Empty),
                    PhotoUrl = s.Person.ProfilePhotoUrl
                },
                s.Person!.ShiftStartTime, s.Person.ShiftEndTime, s.Person.TimeZoneId
            })
            .ToListAsync(cancellationToken);

        var personIds = staffRows.Select(x => x.Dto.PersonId).ToList();
        var records = await _db.AttendanceRecords.AsNoTracking()
            .Where(r => personIds.Contains(r.PersonId) && r.AttendanceDate.Year == year && r.AttendanceDate.Month == month)
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

    public async Task<DailyAttendanceReportDto> GetDailyReportAsync(
        string identityUserId, bool organizationWide, DateOnly dateFrom, DateOnly dateTo,
        CancellationToken cancellationToken = default)
    {
        if (dateFrom == default || dateTo == default || dateTo < dateFrom)
            throw new ArgumentOutOfRangeException(nameof(dateFrom), "A valid attendance date range is required.");
        if (dateTo.DayNumber - dateFrom.DayNumber > 366)
            throw new ArgumentOutOfRangeException(nameof(dateTo), "Attendance reports are limited to 367 days at a time.");

        var caller = await _db.Persons.AsNoTracking()
            .Where(p => p.IdentityUserId == identityUserId && p.IsActive)
            .Select(p => new { p.PersonId, p.TimeZoneId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("No active employee profile is linked to this account.");

        var people = await _db.Persons.AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new
            {
                p.PersonId, p.FullName, p.ReportsToPersonId, p.ShiftStartTime, p.ShiftEndTime, p.TimeZoneId,
                OrganizationId = p.Staff != null && p.Staff.Vacancy != null
                    ? (int?)p.Staff.Vacancy.OrganizationId : null
            })
            .ToListAsync(cancellationToken);

        var visibleIds = new HashSet<Guid> { caller.PersonId };
        if (organizationWide)
        {
            foreach (var person in people) visibleIds.Add(person.PersonId);
        }
        else
        {
            var children = people.Where(p => p.ReportsToPersonId.HasValue)
                .ToLookup(p => p.ReportsToPersonId!.Value, p => p.PersonId);
            var pending = new Queue<Guid>();
            pending.Enqueue(caller.PersonId);
            while (pending.TryDequeue(out var managerId))
                foreach (var childId in children[managerId])
                    if (visibleIds.Add(childId)) pending.Enqueue(childId);

            // Organization-tree scope complements explicit Report-To links for an
            // employee who is demonstrably a hierarchy owner (has direct reports).
            // A regular employee with no direct reports never receives peer access.
            var callerPerson = people.First(p => p.PersonId == caller.PersonId);
            var directReportIds = children[caller.PersonId].ToHashSet();
            if (directReportIds.Count > 0 && callerPerson.OrganizationId.HasValue)
            {
                var nodes = await _db.OrganizationTree.AsNoTracking()
                    .Where(n => n.IsActive)
                    .Select(n => new { n.Id, n.ParentId })
                    .ToListAsync(cancellationToken);
                var nodeChildren = nodes.Where(n => n.ParentId.HasValue)
                    .ToLookup(n => n.ParentId!.Value, n => n.Id);
                var visibleNodeIds = new HashSet<int> { callerPerson.OrganizationId.Value };
                var pendingNodes = new Queue<int>();
                pendingNodes.Enqueue(callerPerson.OrganizationId.Value);
                while (pendingNodes.TryDequeue(out var parentNodeId))
                    foreach (var childNodeId in nodeChildren[parentNodeId])
                        if (visibleNodeIds.Add(childNodeId)) pendingNodes.Enqueue(childNodeId);

                // Node-wide scope is valid only when the employee is positioned at
                // a parent node and at least one direct report sits below that node.
                // Same-node supervisors remain governed entirely by Report-To links.
                var isTopReportingPerson = !callerPerson.ReportsToPersonId.HasValue;
                var managesAcrossDescendantNodes = people.Any(person => directReportIds.Contains(person.PersonId)
                    && person.OrganizationId.HasValue
                    && person.OrganizationId.Value != callerPerson.OrganizationId.Value
                    && visibleNodeIds.Contains(person.OrganizationId.Value));
                // The root reporting person (for example the CEO assigned to the
                // Company node) owns the complete subtree even when immediate
                // executives are also attached to that same Company node.
                if (isTopReportingPerson || managesAcrossDescendantNodes)
                    foreach (var person in people)
                        if (person.OrganizationId.HasValue && visibleNodeIds.Contains(person.OrganizationId.Value))
                            visibleIds.Add(person.PersonId);
            }
        }

        var staff = await _db.StaffVacancies.AsNoTracking()
            .Where(s => s.PersonId.HasValue && visibleIds.Contains(s.PersonId.Value) && s.Person != null && s.Person.IsActive)
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
        var names = people.ToDictionary(p => p.PersonId, p => p.FullName);
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
                var statusName = record?.AttendanceStatus?.StatusName;
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
                    StatusCode = record?.AttendanceStatus?.Code, StatusColorCode = record?.AttendanceStatus?.ColorCode,
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
    }

    public async Task<MonthlyAttendanceReportDto> GetMonthlyReportAsync(string identityUserId, bool canViewOthers, Guid? requestedPersonId, int year, int month, CancellationToken cancellationToken = default)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month), "A valid report month is required.");
        var callerPersonId = await _db.Persons.AsNoTracking().Where(p => p.IdentityUserId == identityUserId).Select(p => (Guid?)p.PersonId).FirstOrDefaultAsync(cancellationToken);
        var personId = canViewOthers && requestedPersonId.HasValue ? requestedPersonId.Value : callerPersonId
            ?? throw new KeyNotFoundException("No employee profile is linked to this account.");

        var employee = await _db.StaffVacancies.AsNoTracking()
            .Where(s => s.PersonId == personId && s.Person != null)
            .Select(s => new AttendanceReportStaffDto
            {
                PersonId = personId, StaffId = s.StaffId, EmployeeId = s.LoginId ?? s.Vacancy!.VacancyCode,
                FullName = s.Person!.FullName, Department = s.Vacancy!.Department ?? s.Vacancy.Organization!.Name,
                Designation = s.Vacancy.JobTitleNav != null ? s.Vacancy.JobTitleNav.TitleName : (s.Vacancy.JobTitle ?? string.Empty)
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("The selected employee was not found in your organization.");

        var person = await _db.Persons.AsNoTracking().Where(p => p.PersonId == personId)
            .Select(p => new { p.TimeZoneId, p.ShiftStartTime, p.ShiftEndTime }).FirstAsync(cancellationToken);
        var required = ShiftMinutes(person.ShiftStartTime, person.ShiftEndTime);
        var records = await _db.AttendanceRecords.AsNoTracking().Include(r => r.AttendanceStatus)
            .Where(r => r.PersonId == personId && r.AttendanceDate.Year == year && r.AttendanceDate.Month == month)
            .OrderByDescending(r => r.AttendanceDate).ToListAsync(cancellationToken);
        var zone = ResolveTimeZone(person.TimeZoneId);
        var rows = records.Select(r =>
        {
            var end = r.CheckOutUtc;
            var gross = r.CheckInUtc.HasValue && end.HasValue ? Math.Max(0, (int)Math.Floor((end.Value - r.CheckInUtc.Value).TotalMinutes)) : 0;
            var worked = Math.Max(0, gross - r.TotalBreakMinutes);
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
                StatusName = r.AttendanceStatus?.StatusName, StatusColorCode = r.AttendanceStatus?.ColorCode
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

    private static MyAttendanceTodayDto Map(Person person, AttendanceRecord? record, DateTime utcNow)
    {
        var required = ShiftMinutes(person.ShiftStartTime, person.ShiftEndTime);
        var end = record?.CheckOutUtc ?? (record?.CheckInUtc.HasValue == true ? utcNow : null);
        var activeBreak = record?.BreakStartedUtc.HasValue == true ? Math.Max(0, (int)Math.Floor((utcNow - record.BreakStartedUtc.Value).TotalMinutes)) : 0;
        var gross = record?.CheckInUtc.HasValue == true && end.HasValue ? Math.Max(0, (int)Math.Floor((end.Value - record.CheckInUtc.Value).TotalMinutes)) : 0;
        var worked = Math.Max(0, gross - (record?.TotalBreakMinutes ?? 0) - activeBreak);
        return new MyAttendanceTodayDto
        {
            Id = record?.Id, AttendanceDate = record?.AttendanceDate ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, ResolveTimeZone(person.TimeZoneId))),
            EmployeeName = person.FullName, ShiftStartTime = person.ShiftStartTime, ShiftEndTime = person.ShiftEndTime, TimeZoneId = person.TimeZoneId,
            CheckInUtc = record?.CheckInUtc, CheckOutUtc = record?.CheckOutUtc, BreakStartedUtc = record?.BreakStartedUtc,
            TotalBreakMinutes = record?.TotalBreakMinutes ?? 0, WorkedMinutes = worked, RequiredMinutes = required,
            ShortMinutes = record?.CheckOutUtc.HasValue == true ? Math.Max(0, required - worked) : 0,
            RemainingMinutes = record?.CheckInUtc.HasValue == true && !record.CheckOutUtc.HasValue ? Math.Max(0, required - worked) : 0,
            ProgressPercent = required == 0 ? 0 : Math.Round(Math.Min(100, worked * 100d / required), 1)
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
}
