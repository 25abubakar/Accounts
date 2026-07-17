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
    private readonly ApplicationDbContext _db;
    public AttendanceService(ApplicationDbContext db) => _db = db;

    public async Task<MyAttendanceTodayDto> GetTodayAsync(string identityUserId, CancellationToken cancellationToken = default)
    {
        var (person, localDate) = await ResolvePersonAsync(identityUserId, cancellationToken);
        var record = await _db.AttendanceRecords.AsNoTracking()
            .Include(x => x.AttendanceEntryType).Include(x => x.AttendanceWorkMode)
            .FirstOrDefaultAsync(x => x.PersonId == person.PersonId && x.AttendanceDate == localDate, cancellationToken);
        return Map(person, record, DateTime.UtcNow);
    }

    public async Task<MyAttendanceTodayDto> CheckInAsync(string identityUserId, int? workModeId = null, CancellationToken cancellationToken = default)
    {
        var (person, localDate) = await ResolvePersonAsync(identityUserId, cancellationToken);
        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(x => x.PersonId == person.PersonId && x.AttendanceDate == localDate, cancellationToken);
        if (record?.CheckInUtc is not null) throw new InvalidOperationException("You have already checked in today.");
        var entryType = await _db.AttendanceEntryTypes.SingleAsync(x => x.Code == "CHECK" && x.IsActive, cancellationToken);
        var workMode = workModeId.HasValue
            ? await _db.AttendanceWorkModes.FirstOrDefaultAsync(x => x.Id == workModeId.Value && x.IsActive, cancellationToken)
            : await _db.AttendanceWorkModes.FirstOrDefaultAsync(x => x.Code == "ONSITE" && x.IsActive, cancellationToken);
        if (workMode == null) throw new InvalidOperationException("Select a valid active work mode.");
        record ??= new AttendanceRecord { TenantId = person.TenantId, PersonId = person.PersonId, AttendanceDate = localDate, CreatedDate = DateTime.UtcNow };
        record.AttendanceEntryType = entryType;
        record.AttendanceWorkMode = workMode;
        record.CheckInUtc = DateTime.UtcNow;
        record.ModifiedDate = DateTime.UtcNow;
        if (record.Id == 0) _db.AttendanceRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(person, record, DateTime.UtcNow);
    }

    public async Task<MyAttendanceTodayDto> ToggleBreakAsync(string identityUserId, CancellationToken cancellationToken = default)
    {
        var (person, localDate) = await ResolvePersonAsync(identityUserId, cancellationToken);
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
        return Map(person, record, now);
    }

    public async Task<MyAttendanceTodayDto> CheckOutAsync(string identityUserId, CancellationToken cancellationToken = default)
    {
        var (person, localDate) = await ResolvePersonAsync(identityUserId, cancellationToken);
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
            .Select(p => new
            {
                p.PersonId, p.TenantId, p.TimeZoneId,
                OrganizationId = p.Staff != null && p.Staff.Vacancy != null
                    ? (int?)p.Staff.Vacancy.OrganizationId : null,
                JobTitle = p.Staff != null && p.Staff.Vacancy != null
                    ? (p.Staff.Vacancy.JobTitleNav != null
                        ? p.Staff.Vacancy.JobTitleNav.TitleName
                        : p.Staff.Vacancy.JobTitle)
                    : null,
                AttendanceScope = p.Staff != null && p.Staff.Vacancy != null && p.Staff.Vacancy.JobTitleNav != null
                    ? p.Staff.Vacancy.JobTitleNav.AttendanceVisibilityScope
                    : AttendanceVisibilityScope.Self
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("No active employee profile is linked to this account.");

        var people = await _db.Persons.AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new
            {
                p.PersonId, p.FullName, p.ReportsToPersonId, p.ShiftStartTime, p.ShiftEndTime, p.TimeZoneId,
                OrganizationId = p.Staff != null && p.Staff.Vacancy != null
                    ? (int?)p.Staff.Vacancy.OrganizationId : null,
                JobTitle = p.Staff != null && p.Staff.Vacancy != null
                    ? (p.Staff.Vacancy.JobTitleNav != null
                        ? p.Staff.Vacancy.JobTitleNav.TitleName
                        : p.Staff.Vacancy.JobTitle)
                    : null
            })
            .ToListAsync(cancellationToken);

        var visibleIds = new HashSet<Guid> { caller.PersonId };
        var callerRank = AttendanceRoleRank(caller.JobTitle);
        if (organizationWide)
        {
            foreach (var person in people) visibleIds.Add(person.PersonId);
        }
        else if (caller.OrganizationId.HasValue)
        {
            // Role order inside every organization node:
            // CEO > Duty CEO > Manager > Deputy Manager > Assistant Manager > Supervisor > Agent/Bell Boy.
            // The stored scope can widen a custom title, while known leadership titles receive
            // their natural hierarchy scope automatically.
            var derivedScope = callerRank switch
            {
                >= 300 => AttendanceVisibilityScope.OrganizationNodeAndDescendants,
                >= 200 => AttendanceVisibilityScope.OrganizationNode,
                _ => AttendanceVisibilityScope.Self
            };
            var effectiveScope = (AttendanceVisibilityScope)Math.Max(
                (int)caller.AttendanceScope, (int)derivedScope);
            if (effectiveScope == AttendanceVisibilityScope.Self)
                goto VisibilityResolved;

            var visibleNodeIds = new HashSet<int> { caller.OrganizationId.Value };
            if (effectiveScope == AttendanceVisibilityScope.OrganizationNodeAndDescendants)
            {
                var nodes = await _db.OrganizationTree.AsNoTracking()
                    .Where(n => n.IsActive)
                    .Select(n => new { n.Id, n.ParentId })
                    .ToListAsync(cancellationToken);
                var nodeChildren = nodes.Where(n => n.ParentId.HasValue)
                    .ToLookup(n => n.ParentId!.Value, n => n.Id);
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
                    visibleIds.Add(person.PersonId);
        }

        VisibilityResolved:

        // The hierarchy is authorized above; row generation, date expansion and
        // attendance joins are performed set-wise by SQL Server.
        try
        {
            return await BuildDailyReportFromProcedureAsync(
                caller.TenantId, visibleIds, people.ToDictionary(p => p.PersonId, p => p.FullName),
                dateFrom, dateTo, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == 2812)
        {
            throw new InvalidOperationException(
                "The set-based attendance report procedure is not installed. Apply pending database migrations before requesting reports.", ex);
        }

#pragma warning disable CS0162 // Retained only as a temporary rollback reference; never executed.
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

        var statusByCode = await _db.ProcessStatusStyles.AsNoTracking()
            .Include(s => s.Process).Include(s => s.Status).Include(s => s.ColorStyle)
            .Where(s => s.Process.ProcessName == "Attendance" && s.IsActive)
            .ToDictionaryAsync(s => s.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        statusByCode.TryGetValue("P", out var presentStatus);
        statusByCode.TryGetValue("A", out var absentStatus);
        statusByCode.TryGetValue("LT", out var lateStatus);

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
            var late = checkInLocal.HasValue ? Math.Max(0, (int)(TimeOnly.FromDateTime(checkInLocal.Value).ToTimeSpan() - shiftStart.ToTimeSpan()).TotalMinutes) : 0;
            var early = checkOutLocal.HasValue ? Math.Max(0, (int)(shiftEnd.ToTimeSpan() - TimeOnly.FromDateTime(checkOutLocal.Value).ToTimeSpan()).TotalMinutes) : 0;
            var effectiveStatus = source.StatusName != null ? null
                : source.AttendanceDate < localToday && !source.CheckInUtc.HasValue ? absentStatus
                : late > 0 ? lateStatus
                : source.CheckInUtc.HasValue ? presentStatus : null;
            var statusName = source.StatusName ?? effectiveStatus?.Status.StatusName ?? string.Empty;
            var statusCode = source.StatusCode ?? effectiveStatus?.Code;

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
                OvertimeMinutes = source.CheckOutUtc.HasValue ? Math.Max(0, working - ShiftMinutes(source.ShiftStartTime, source.ShiftEndTime)) : 0,
                AttendanceStatusId = source.AttendanceStatusId ?? effectiveStatus?.Id,
                AttendanceStatus = statusName,
                StatusCode = statusCode,
                StatusColorCode = source.StatusColorCode ?? effectiveStatus?.ColorStyle.ColorCode,
                Present = source.CheckInUtc.HasValue,
                Absent = statusCode?.Equals("A", StringComparison.OrdinalIgnoreCase) == true,
                OnLeave = statusCode?.Equals("L", StringComparison.OrdinalIgnoreCase) == true,
                Remote = source.AttendanceWorkMode?.Equals("Remote", StringComparison.OrdinalIgnoreCase) == true,
                MissingCheckIn = source.Id.HasValue && !source.CheckInUtc.HasValue && statusCode?.Equals("L", StringComparison.OrdinalIgnoreCase) != true,
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

    private static int AttendanceRoleRank(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return 0;
        var value = new string(title.Trim().ToLowerInvariant()
            .Where(char.IsLetterOrDigit).ToArray());

        // Check Duty CEO first because it also contains "CEO".
        if (value.Contains("dutyceo")) return 600;
        if (value.Contains("ceo") || value.Contains("chiefexecutive")) return 700;
        if (value.Contains("deputymanager") || value.Contains("deptymanager")) return 400;
        if (value.Contains("assistantmanager") || value.Contains("asstmanager") || value.Contains("assistmanager")) return 300;
        if (value.Contains("manager")) return 500;
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
            ,AttendanceEntryTypeId = record?.AttendanceEntryTypeId
            ,AttendanceEntryType = record?.AttendanceEntryType?.Name
            ,AttendanceWorkModeId = record?.AttendanceWorkModeId
            ,AttendanceWorkMode = record?.AttendanceWorkMode?.Name
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
