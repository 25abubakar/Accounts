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
