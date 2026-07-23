using Accounts.DTOs;
using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Accounts.Controllers;

[ApiController, Route("api/attendance"), Authorize]
public sealed class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _service;
    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;
    private readonly RbacService _rbac;

    public AttendanceController(IAttendanceService service, ApplicationDbContext db, ITenantService tenant, RbacService rbac)
    {
        _service = service;
        _db = db;
        _tenant = tenant;
        _rbac = rbac;
    }

    [HttpGet("me/today")]
    public Task<IActionResult> Today(CancellationToken ct) => Execute(() => _service.GetTodayAsync(UserId(), ct));

    [HttpPost("me/check-in")]
    public Task<IActionResult> CheckIn([FromQuery] int? workModeId, CancellationToken ct) => Execute(() => _service.CheckInAsync(UserId(), workModeId, ct));

    [HttpGet("work-modes")]
    public async Task<IActionResult> WorkModes([FromServices] Accounts.Data.ApplicationDbContext db, CancellationToken ct) =>
        Ok(await db.AttendanceWorkModes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct));

    [HttpGet("entry-types")]
    public async Task<IActionResult> EntryTypes([FromServices] Accounts.Data.ApplicationDbContext db, CancellationToken ct) =>
        Ok(await db.AttendanceEntryTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct));

    [HttpGet("map-attendance")]
    [HttpGet("rules/map-attendance")]
    public async Task<IActionResult> MapAttendanceRules(CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue) return Ok(Array.Empty<AttendanceMapRuleDto>());

        var rules = await _db.AttendanceMapRuleReadRows.AsNoTracking()
            .Where(rule => rule.TenantId == _tenant.RequiredTenantId)
            .OrderBy(rule => rule.Id)
            .Select(rule => new AttendanceMapRuleDto
            {
                Id = rule.Id,
                StaffId = rule.StaffId,
                AttendanceEntryTypeId = rule.AttendanceEntryTypeId,
                AttendanceTypeCode = rule.AttendanceTypeCode,
                AttendanceTypeName = rule.AttendanceTypeName,
                ShiftCode = rule.ShiftCode,
                ShiftName = rule.ShiftName,
                TimeFrom = rule.TimeFrom,
                TimeTo = rule.TimeTo,
                IsOpenAttendance = rule.IsOpenAttendance
            })
            .ToListAsync(ct);

        return Ok(rules);
    }

    [HttpPost("map-attendance")]
    [HttpPost("rules/map-attendance")]
    public async Task<IActionResult> SaveMapAttendanceRule([FromBody] SaveAttendanceMapRuleDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue) return Forbid();
        if (dto.StaffId == Guid.Empty) return BadRequest(new { message = "A staff member is required." });

        var tenantId = _tenant.RequiredTenantId;
        var staffExists = await _db.StaffVacancies.AsNoTracking()
            .AnyAsync(staff => staff.StaffId == dto.StaffId && staff.TenantId == tenantId, ct);
        if (!staffExists) return NotFound(new { message = "Staff member was not found in the current organization." });

        var attendanceType = await _db.AttendanceEntryTypes
            .SingleOrDefaultAsync(type => type.Id == dto.AttendanceEntryTypeId && type.IsActive, ct);
        if (attendanceType == null) return BadRequest(new { message = "Select an active attendance type." });

        var shift = await _db.AppLookupValues.AsNoTracking()
            .Where(value => value.IsActive && value.LookupType != null && value.LookupType.IsActive
                && value.LookupType.LookupTypeCode == "ATTENDANCE_SHIFT"
                && value.ValueCode == dto.ShiftCode)
            .Select(value => new { value.ValueCode, value.DisplayText })
            .SingleOrDefaultAsync(ct);
        if (shift == null) return BadRequest(new { message = "Select an active attendance shift." });

        if (!TimeOnly.TryParseExact(dto.TimeFrom, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeFrom)
            || !TimeOnly.TryParseExact(dto.TimeTo, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeTo))
            return BadRequest(new { message = "Valid Time From and Time To values are required." });

        var rule = await _db.AttendanceMapRules
            .SingleOrDefaultAsync(item => item.TenantId == tenantId && item.StaffId == dto.StaffId, ct);

        var requiredAction = rule == null ? "ADD" : "EDIT";
        if (!await HasAttendanceMenuActionAsync(requiredAction, ct, "/attendance/rules/map-attendance", "/attendance/map-attendance"))
            return Forbid();

        var now = DateTime.UtcNow;
        if (rule == null)
        {
            rule = new AttendanceMapRule
            {
                TenantId = tenantId,
                StaffId = dto.StaffId,
                CreatedByUserId = UserId(),
                CreatedDate = now,
            };
            _db.AttendanceMapRules.Add(rule);
        }
        else
        {
            rule.ModifiedByUserId = UserId();
            rule.ModifiedDate = now;
        }

        rule.AttendanceEntryTypeId = attendanceType.Id;
        rule.AttendanceEntryType = attendanceType;
        rule.ShiftCode = shift.ValueCode;
        rule.TimeFrom = timeFrom.ToString("HH:mm", CultureInfo.InvariantCulture);
        rule.TimeTo = timeTo.ToString("HH:mm", CultureInfo.InvariantCulture);
        rule.IsOpenAttendance = dto.IsOpenAttendance;
        await _db.SaveChangesAsync(ct);

        return Ok(ToMapRuleDto(rule, shift.DisplayText));
    }

    [HttpGet("rules/map-color")]
    public async Task<IActionResult> MapColors(CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue) return Ok(Array.Empty<AttendanceHolidayColorMapDto>());

        var maps = await _db.AttendanceHolidayColorMapReadRows.AsNoTracking()
            .Where(map => map.TenantId == _tenant.RequiredTenantId)
            .OrderBy(map => map.Id)
            .Select(map => new AttendanceHolidayColorMapDto
            {
                Id = map.Id,
                HolidayTypeCode = map.HolidayTypeCode,
                HolidayTypeName = map.HolidayTypeName,
                ColorCode = map.ColorCode
            })
            .ToListAsync(ct);

        return Ok(maps);
    }

    [HttpPost("rules/map-color")]
    public Task<IActionResult> CreateMapColor([FromBody] SaveAttendanceHolidayColorMapDto dto, CancellationToken ct) =>
        SaveMapColor(null, dto, ct);

    [HttpPut("rules/map-color/{id:int}")]
    public Task<IActionResult> UpdateMapColor(int id, [FromBody] SaveAttendanceHolidayColorMapDto dto, CancellationToken ct) =>
        SaveMapColor(id, dto, ct);

    [HttpPost("me/toggle-break")]
    public Task<IActionResult> ToggleBreak(CancellationToken ct) => Execute(() => _service.ToggleBreakAsync(UserId(), ct));

    [HttpPost("me/check-out")]
    public Task<IActionResult> CheckOut(CancellationToken ct) => Execute(() => _service.CheckOutAsync(UserId(), ct));

    [HttpGet("rules/settings")]
    public async Task<IActionResult> AttendanceRuleSettings(CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue) return Ok(Array.Empty<AttendanceRuleSettingDto>());
        if (!await HasAttendanceMenuActionAsync("VIEW", ct, "/attendance/rules/list", "/attendance/rules/rule", "/attendance/rules"))
            return Forbid();

        var rules = await _db.AttendanceRuleSettingReadRows.AsNoTracking()
            .Where(rule => rule.TenantId == _tenant.RequiredTenantId)
            .OrderBy(rule => rule.Id)
            .Select(rule => new AttendanceRuleSettingDto
            {
                Id = rule.Id,
                AttendanceEntryTypeId = rule.AttendanceEntryTypeId,
                AttendanceTypeCode = rule.AttendanceTypeCode,
                AttendanceTypeName = rule.AttendanceTypeName,
                Reference = rule.Reference,
                RuleName = rule.RuleName,
                WorkingMinutes = rule.WorkingMinutes,
                BeforeCheckInMinutes = rule.BeforeCheckInMinutes,
                AfterCheckOutMinutes = rule.AfterCheckOutMinutes,
                CheckInAdjustMinutes = rule.CheckInAdjustMinutes,
                CheckOutAdjustMinutes = rule.CheckOutAdjustMinutes,
                AbsentAfterShiftStartMinutes = rule.AbsentAfterShiftStartMinutes,
                MissingCheckoutAfterShiftEndMinutes = rule.MissingCheckoutAfterShiftEndMinutes,
                AccountLockAbsentDays = rule.AccountLockAbsentDays,
                WeekendChargeValue = rule.WeekendChargeValue,
                AdjustAbsentDays = rule.AdjustAbsentDays,
                IsApproved = rule.IsApproved,
                IsActive = rule.IsActive,
                Remarks = rule.Remarks
            })
            .ToListAsync(ct);

        return Ok(rules);
    }

    [HttpPost("rules/settings")]
    public Task<IActionResult> CreateAttendanceRuleSetting([FromBody] SaveAttendanceRuleSettingDto dto, CancellationToken ct) =>
        SaveAttendanceRuleSetting(null, dto, ct);

    [HttpPut("rules/settings/{id:int}")]
    public Task<IActionResult> UpdateAttendanceRuleSetting(int id, [FromBody] SaveAttendanceRuleSettingDto dto, CancellationToken ct) =>
        SaveAttendanceRuleSetting(id, dto, ct);

    [HttpDelete("rules/settings/{id:int}")]
    public async Task<IActionResult> DeleteAttendanceRuleSetting(int id, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue) return Forbid();
        if (!await HasAttendanceMenuActionAsync("DELETE", ct, "/attendance/rules/list", "/attendance/rules/rule", "/attendance/rules"))
            return Forbid();

        var rule = await _db.AttendanceRuleSettings.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (rule == null) return NotFound(new { message = "The attendance rule was not found." });
        _db.AttendanceRuleSettings.Remove(rule);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Attendance rule deleted successfully." });
    }

    [HttpGet("report/staff")]
    public async Task<IActionResult> ReportStaff([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        if (!CanViewOthers()) return Forbid();
        if (year is < 2000 or > 2100 || month is < 1 or > 12) return BadRequest(new { message = "A valid report month is required." });
        return Ok(await _service.GetReportStaffAsync(year, month, ct));
    }

    [HttpGet("report/timing-chart/staff")]
    public Task<IActionResult> TimingChartStaff(CancellationToken ct) =>
        Execute(() => _service.GetTimingChartStaffAsync(UserId(), CanViewOrganization(), ct));

    [HttpGet("report/timing-chart/staff/{staffId:guid}/schedules")]
    public Task<IActionResult> TimingChartSchedules(
        Guid staffId,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct) =>
        Execute(() => _service.GetTimingChartSchedulesAsync(
            UserId(), CanViewOrganization(), staffId, year, month, ct));

    [HttpGet("report/timing-chart/staff-schedule")]
    public Task<IActionResult> TimingChartStaffSchedule(
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct) =>
        Execute(() => _service.GetTimingChartStaffScheduleAsync(
            UserId(), CanViewOrganization(), year, month, ct));

    [HttpPut("report/timing-chart/staff/{staffId:guid}/schedules/{holidayDate}")]
    public async Task<IActionResult> SaveTimingChartSchedule(
        Guid staffId,
        DateOnly holidayDate,
        [FromBody] SaveTimingChartScheduleDto dto,
        CancellationToken ct)
    {
        if (!await HasAttendanceMenuActionAsync("EDIT", ct, "/attendance/timing-chart"))
            return Forbid();

        return await Execute(() => _service.SaveTimingChartScheduleAsync(
            UserId(), CanViewOrganization(), staffId, holidayDate, dto, ct));
    }

    [HttpPost("report/timing-chart/staff/{staffId:guid}/schedules/range")]
    public async Task<IActionResult> SaveTimingChartScheduleRange(
        Guid staffId,
        [FromBody] SaveTimingChartScheduleRangeDto dto,
        CancellationToken ct)
    {
        if (!await HasAttendanceMenuActionAsync("EDIT", ct, "/attendance/timing-chart"))
            return Forbid();

        return await Execute(() => _service.SaveTimingChartScheduleRangeAsync(
            UserId(), CanViewOrganization(), staffId, dto, ct));
    }

    [HttpGet("report/monthly")]
    public Task<IActionResult> MonthlyReport([FromQuery] int year, [FromQuery] int month, [FromQuery] Guid? personId, CancellationToken ct) =>
        Execute(() => _service.GetMonthlyReportAsync(UserId(), CanViewOthers(), personId, year, month, ct));

    [HttpGet("report/daily")]
    public Task<IActionResult> DailyReport([FromQuery] DateOnly dateFrom, [FromQuery] DateOnly dateTo, CancellationToken ct) =>
        Execute(() => _service.GetDailyReportAsync(UserId(), CanViewOrganization(), dateFrom, dateTo, ct));

    [HttpGet("report/remote")]
    public Task<IActionResult> RemoteAttendanceReport([FromQuery] DateOnly dateFrom, [FromQuery] DateOnly dateTo, CancellationToken ct) =>
        Execute(() => _service.GetRemoteAttendanceReportAsync(UserId(), CanViewOrganization(), dateFrom, dateTo, ct));

    [HttpGet("report/login")]
    public async Task<IActionResult> LoginAttendanceReport([FromQuery] DateOnly dateFrom, [FromQuery] DateOnly dateTo, CancellationToken ct)
    {
        if (dateFrom > dateTo) return BadRequest(new { message = "Date From cannot be later than Date To." });

        var userId = UserId();
        var canViewOrganization = CanViewOrganization();
        await ApplicationLoginSessionSchema.EnsureCreatedAsync(_db, ct);
        var query = _db.ApplicationLoginSessions
            .AsNoTracking()
            .Include(session => session.Person)
            .ThenInclude(person => person!.Staff)
            .ThenInclude(staff => staff!.Vacancy)
            .ThenInclude(vacancy => vacancy!.JobTitleNav)
            .Where(session =>
                session.SessionDate >= dateFrom &&
                session.SessionDate <= dateTo &&
                !_db.Users.Any(user =>
                    user.Id == session.IdentityUserId &&
                    (user.IsTenantAdmin || user.IsSuperAdmin)));

        if (!canViewOrganization)
            query = query.Where(session => session.IdentityUserId == userId);

        var rows = await query
            .OrderByDescending(session => session.LoginUtc)
            .ThenBy(session => session.Person != null ? session.Person.FullName : session.IdentityUserId)
            .Select(session => new
            {
                session.Id,
                session.StaffId,
                session.PersonId,
                session.SessionDate,
                session.LoginUtc,
                session.LogoutUtc,
                session.WorkingMinutes,
                session.IdentityUserId,
                session.Source,
                session.IpAddress,
                session.Remarks,
                PersonName = session.Person != null ? session.Person.FullName : string.Empty,
                TimeZoneId = session.Person != null ? session.Person.TimeZoneId : null,
                StaffNumber = session.Person != null && session.Person.Staff != null ? session.Person.Staff.LoginId : null,
                Department = session.Person != null && session.Person.Staff != null && session.Person.Staff.Vacancy != null
                    ? session.Person.Staff.Vacancy.Department
                    : string.Empty,
                Designation = session.Person != null && session.Person.Staff != null && session.Person.Staff.Vacancy != null
                    ? (session.Person.Staff.Vacancy.JobTitleNav != null
                        ? session.Person.Staff.Vacancy.JobTitleNav.TitleName
                        : session.Person.Staff.Vacancy.JobTitle)
                    : string.Empty,
            })
            .ToListAsync(ct);

        var result = rows.Select(row =>
        {
            var zone = ResolveTimeZone(row.TimeZoneId);
            return new LoginAttendanceSessionDto
            {
                Id = row.Id,
                StaffId = row.StaffId,
                PersonId = row.PersonId,
                EmployeeNumber = row.StaffNumber ?? string.Empty,
                EmployeeName = string.IsNullOrWhiteSpace(row.PersonName) ? row.IdentityUserId : row.PersonName,
                Department = row.Department ?? string.Empty,
                Designation = row.Designation ?? string.Empty,
                Date = row.SessionDate,
                LoginTime = TimeZoneInfo.ConvertTimeFromUtc(row.LoginUtc, zone).ToString("HH:mm", CultureInfo.InvariantCulture),
                LogoutTime = row.LogoutUtc.HasValue
                    ? TimeZoneInfo.ConvertTimeFromUtc(row.LogoutUtc.Value, zone).ToString("HH:mm", CultureInfo.InvariantCulture)
                    : null,
                WorkingMinutes = row.LogoutUtc.HasValue
                    ? Math.Max(0, (int)Math.Floor((row.LogoutUtc.Value - row.LoginUtc).TotalMinutes))
                    : Math.Max(0, (int)Math.Floor((DateTime.UtcNow - row.LoginUtc).TotalMinutes)),
                Source = row.Source,
                IpAddress = row.IpAddress,
                Remarks = row.Remarks,
            };
        }).ToList();

        return Ok(new LoginAttendanceReportDto
        {
            DateFrom = dateFrom,
            DateTo = dateTo,
            Rows = result,
        });
    }

    [HttpPost("types/camera/entries")]
    public async Task<IActionResult> SaveCameraAttendance([FromBody] SaveCameraAttendanceDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue) return Forbid();
        if (!await HasAttendanceMenuActionAsync("ADD", ct, "/attendance/types/camera"))
            return Forbid();
        if (dto.PersonId == Guid.Empty) return BadRequest(new { message = "Select a staff member." });
        if (string.IsNullOrWhiteSpace(dto.CheckInTime) && string.IsNullOrWhiteSpace(dto.CheckOutTime))
            return BadRequest(new { message = "Enter at least check-in or check-out time." });

        var person = await _db.Persons
            .Include(p => p.Staff)
            .SingleOrDefaultAsync(p => p.PersonId == dto.PersonId && p.TenantId == _tenant.RequiredTenantId, ct);
        if (person?.Staff == null) return NotFound(new { message = "Staff member was not found in the current organization." });

        var zone = ResolveTimeZone(person.TimeZoneId);
        DateTime? checkInUtc = null;
        DateTime? checkOutUtc = null;
        if (!string.IsNullOrWhiteSpace(dto.CheckInTime))
        {
            if (!TimeOnly.TryParseExact(dto.CheckInTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                return BadRequest(new { message = "Check-in time must be in HH:mm format." });
            checkInUtc = ToUtc(dto.AttendanceDate, time, zone);
        }
        if (!string.IsNullOrWhiteSpace(dto.CheckOutTime))
        {
            if (!TimeOnly.TryParseExact(dto.CheckOutTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                return BadRequest(new { message = "Check-out time must be in HH:mm format." });
            checkOutUtc = ToUtc(dto.AttendanceDate, time, zone);
        }
        if (checkInUtc.HasValue && checkOutUtc.HasValue && checkOutUtc.Value < checkInUtc.Value)
            return BadRequest(new { message = "Check-out time cannot be earlier than check-in time." });

        var entryType = await _db.AttendanceEntryTypes.SingleOrDefaultAsync(x => x.Code == "CAMERA" && x.IsActive, ct)
            ?? await _db.AttendanceEntryTypes.SingleOrDefaultAsync(x => x.Code == "MANUAL" && x.IsActive, ct);
        if (entryType == null) return BadRequest(new { message = "Camera attendance type is not configured." });
        var workMode = await _db.AttendanceWorkModes.SingleOrDefaultAsync(x => x.Code == "ONSITE" && x.IsActive, ct);

        var record = await _db.AttendanceRecords
            .SingleOrDefaultAsync(x => x.PersonId == person.PersonId && x.AttendanceDate == dto.AttendanceDate, ct);
        var now = DateTime.UtcNow;
        if (record == null)
        {
            record = new AttendanceRecord
            {
                TenantId = person.TenantId,
                PersonId = person.PersonId,
                AttendanceDate = dto.AttendanceDate,
                CreatedDate = now,
            };
            _db.AttendanceRecords.Add(record);
        }

        record.AttendanceEntryTypeId = entryType.Id;
        record.AttendanceWorkModeId = workMode?.Id;
        if (checkInUtc.HasValue) record.CheckInUtc = checkInUtc.Value;
        if (checkOutUtc.HasValue) record.CheckOutUtc = checkOutUtc.Value;
        record.ModifiedDate = now;
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "Camera attendance saved successfully." });
    }

    [HttpGet("report/staff-attendance")]
    public Task<IActionResult> StaffAttendanceReport([FromQuery] DateOnly dateFrom, [FromQuery] DateOnly dateTo, CancellationToken ct) =>
        Execute(() => _service.GetStaffAttendanceReportAsync(UserId(), CanViewOrganization(), dateFrom, dateTo, ct));

    [HttpGet("report/monthly-chart")]
    public Task<IActionResult> MonthlyChart([FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        Execute(() => _service.GetMonthlyChartAsync(UserId(), CanViewOrganization(), year, month, ct));

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    private bool CanViewOthers() => User.IsInRole("SuperAdmin") || User.IsInRole("Admin") || User.IsInRole("TenantAdmin") || User.HasClaim(ITenantService.ClaimIsTenantAdmin, "true");
    private bool CanViewOrganization() => User.IsInRole("SuperAdmin") || User.IsInRole("Admin") || User.IsInRole("TenantAdmin") || User.HasClaim(ITenantService.ClaimIsTenantAdmin, "true");

    private async Task<Guid?> CurrentStaffIdAsync(CancellationToken ct)
    {
        var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(identityUserId)) return null;

        return await _db.Persons.AsNoTracking()
            .Where(person => person.IdentityUserId == identityUserId && person.Staff != null)
            .Select(person => (Guid?)person.Staff!.StaffId)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<bool> HasAttendanceMenuActionAsync(string action, CancellationToken ct, params string[] routes)
    {
        if (CanViewOrganization()) return true;

        var staffId = await CurrentStaffIdAsync(ct);
        if (!staffId.HasValue) return false;

        var normalizedAction = action.Trim().ToUpperInvariant();
        var normalizedRoutes = routes
            .Where(route => !string.IsNullOrWhiteSpace(route))
            .Select(route => route.Trim().ToLowerInvariant())
            .ToArray();

        var menuIds = await _db.Menus.AsNoTracking()
            .Where(menu => menu.IsActive && menu.Route != null && normalizedRoutes.Contains(menu.Route.ToLower()))
            .Select(menu => menu.Id)
            .ToListAsync(ct);

        foreach (var menuId in menuIds)
        {
            if (normalizedAction == "VIEW" && await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId}"))
                return true;

            if (await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId}_{normalizedAction}"))
                return true;
        }

        return false;
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Local;
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch { return TimeZoneInfo.Local; }
    }

    private static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private static AttendanceMapRuleDto ToMapRuleDto(AttendanceMapRule rule, string shiftName) => new()
    {
        Id = rule.Id,
        StaffId = rule.StaffId,
        AttendanceEntryTypeId = rule.AttendanceEntryTypeId,
        AttendanceTypeCode = rule.AttendanceEntryType.Code,
        AttendanceTypeName = rule.AttendanceEntryType.Name,
        ShiftCode = rule.ShiftCode,
        ShiftName = shiftName,
        TimeFrom = rule.TimeFrom,
        TimeTo = rule.TimeTo,
        IsOpenAttendance = rule.IsOpenAttendance,
    };

    private async Task<IActionResult> SaveMapColor(
        int? id,
        SaveAttendanceHolidayColorMapDto dto,
        CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue) return Forbid();
        if (!await HasAttendanceMenuActionAsync(id.HasValue ? "EDIT" : "ADD", ct, "/attendance/rules/map-color"))
            return Forbid();

        var holidayTypeCode = dto.HolidayTypeCode?.Trim() ?? string.Empty;
        var holidayType = await _db.AppLookupValues.AsNoTracking()
            .Where(value => value.IsActive && value.LookupType != null && value.LookupType.IsActive
                && value.LookupType.LookupTypeCode == "TIMING_HOLIDAY_TYPE"
                && value.ValueCode == holidayTypeCode)
            .Select(value => new { value.ValueCode, value.DisplayText })
            .SingleOrDefaultAsync(ct);
        if (holidayType == null) return BadRequest(new { message = "Select an active holiday type." });

        var colorCode = dto.ColorCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!Regex.IsMatch(colorCode, "^#[0-9A-F]{6}$", RegexOptions.CultureInvariant))
            return BadRequest(new { message = "Select a valid six-digit color." });
        var colorIsAllowed = await _db.AppLookupValues.AsNoTracking()
            .AnyAsync(value => value.IsActive && value.LookupType != null && value.LookupType.IsActive
                && value.LookupType.LookupTypeCode == "ATTENDANCE_MAP_COLOR"
                && value.ValueCode == colorCode, ct);
        if (!colorIsAllowed) return BadRequest(new { message = "Select an active color from the map color list." });

        var duplicate = await _db.AttendanceHolidayColorMaps.AsNoTracking()
            .AnyAsync(map => map.HolidayTypeCode == holidayType.ValueCode && (!id.HasValue || map.Id != id.Value), ct);
        if (duplicate) return BadRequest(new { message = "This holiday type already has a color mapping." });

        AttendanceHolidayColorMap map;
        var now = DateTime.UtcNow;
        if (id.HasValue)
        {
            var existingMap = await _db.AttendanceHolidayColorMaps.SingleOrDefaultAsync(item => item.Id == id.Value, ct);
            if (existingMap == null) return NotFound(new { message = "The color mapping was not found." });
            map = existingMap;
            map.ModifiedByUserId = UserId();
            map.ModifiedDate = now;
        }
        else
        {
            map = new AttendanceHolidayColorMap
            {
                TenantId = _tenant.RequiredTenantId,
                CreatedByUserId = UserId(),
                CreatedDate = now
            };
            _db.AttendanceHolidayColorMaps.Add(map);
        }

        map.HolidayTypeCode = holidayType.ValueCode;
        map.ColorCode = colorCode;
        await _db.SaveChangesAsync(ct);

        return Ok(ToHolidayColorMapDto(map, holidayType.DisplayText));
    }

    private async Task<IActionResult> SaveAttendanceRuleSetting(
        int? id,
        SaveAttendanceRuleSettingDto dto,
        CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue) return Forbid();
        if (!await HasAttendanceMenuActionAsync(id.HasValue ? "EDIT" : "ADD", ct, "/attendance/rules/list", "/attendance/rules/rule", "/attendance/rules"))
            return Forbid();

        var tenantId = _tenant.RequiredTenantId;
        var reference = dto.Reference.Trim();
        var ruleName = dto.RuleName.Trim();
        if (string.IsNullOrWhiteSpace(reference)) return BadRequest(new { message = "Reference is required." });
        if (string.IsNullOrWhiteSpace(ruleName)) return BadRequest(new { message = "Rule name is required." });

        if (dto.WorkingMinutes is < 0 or > 1440) return BadRequest(new { message = "Working hours must be between 0 and 24 hours." });
        if (dto.BeforeCheckInMinutes is < 0 or > 720) return BadRequest(new { message = "Before check-in allowance must be between 0 and 720 minutes." });
        if (dto.AfterCheckOutMinutes is < 0 or > 720) return BadRequest(new { message = "After check-out allowance must be between 0 and 720 minutes." });
        if (dto.CheckInAdjustMinutes is < 0 or > 720) return BadRequest(new { message = "Check-in adjust time must be between 0 and 720 minutes." });
        if (dto.CheckOutAdjustMinutes is < 0 or > 720) return BadRequest(new { message = "Check-out adjust time must be between 0 and 720 minutes." });
        if (dto.AbsentAfterShiftStartMinutes is < 1 or > 1440) return BadRequest(new { message = "Absent-after time must be between 1 and 1440 minutes." });
        if (dto.MissingCheckoutAfterShiftEndMinutes is < 1 or > 1440) return BadRequest(new { message = "Missing checkout time must be between 1 and 1440 minutes." });
        if (dto.AccountLockAbsentDays is < 0 or > 31) return BadRequest(new { message = "Account lock absent days must be between 0 and 31." });
        if (dto.WeekendChargeValue is < 0 or > 31) return BadRequest(new { message = "Weekend charged value must be between 0 and 31." });
        if (dto.AdjustAbsentDays is < 0 or > 31) return BadRequest(new { message = "Adjust absent days must be between 0 and 31." });

        var attendanceType = await _db.AttendanceEntryTypes
            .SingleOrDefaultAsync(type => type.Id == dto.AttendanceEntryTypeId && type.IsActive, ct);
        if (attendanceType == null) return BadRequest(new { message = "Select an active attendance type." });

        var duplicate = await _db.AttendanceRuleSettings.AsNoTracking()
            .AnyAsync(rule =>
                rule.TenantId == tenantId &&
                rule.AttendanceEntryTypeId == attendanceType.Id &&
                (!id.HasValue || rule.Id != id.Value), ct);
        if (duplicate) return BadRequest(new { message = "This attendance type already has a rule. Edit the existing rule instead." });

        var now = DateTime.UtcNow;
        AttendanceRuleSetting rule;
        if (id.HasValue)
        {
            rule = await _db.AttendanceRuleSettings.SingleOrDefaultAsync(item => item.Id == id.Value, ct)
                ?? throw new KeyNotFoundException("The attendance rule was not found.");
            rule.ModifiedByUserId = UserId();
            rule.ModifiedDate = now;
        }
        else
        {
            rule = new AttendanceRuleSetting
            {
                TenantId = tenantId,
                CreatedByUserId = UserId(),
                CreatedDate = now
            };
            _db.AttendanceRuleSettings.Add(rule);
        }

        rule.AttendanceEntryTypeId = attendanceType.Id;
        rule.AttendanceEntryType = attendanceType;
        rule.Reference = reference.Length > 50 ? reference[..50] : reference;
        rule.RuleName = ruleName.Length > 150 ? ruleName[..150] : ruleName;
        rule.WorkingMinutes = dto.WorkingMinutes;
        rule.BeforeCheckInMinutes = dto.BeforeCheckInMinutes;
        rule.AfterCheckOutMinutes = dto.AfterCheckOutMinutes;
        rule.CheckInAdjustMinutes = dto.CheckInAdjustMinutes;
        rule.CheckOutAdjustMinutes = dto.CheckOutAdjustMinutes;
        rule.AbsentAfterShiftStartMinutes = dto.AbsentAfterShiftStartMinutes;
        rule.MissingCheckoutAfterShiftEndMinutes = dto.MissingCheckoutAfterShiftEndMinutes;
        rule.AccountLockAbsentDays = dto.AccountLockAbsentDays;
        rule.WeekendChargeValue = dto.WeekendChargeValue;
        rule.AdjustAbsentDays = dto.AdjustAbsentDays;
        rule.IsApproved = dto.IsApproved;
        rule.IsActive = dto.IsActive;
        rule.Remarks = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim();

        await _db.SaveChangesAsync(ct);
        return Ok(ToAttendanceRuleSettingDto(rule, attendanceType));
    }

    private static AttendanceHolidayColorMapDto ToHolidayColorMapDto(
        AttendanceHolidayColorMap map,
        string holidayTypeName) => new()
    {
        Id = map.Id,
        HolidayTypeCode = map.HolidayTypeCode,
        HolidayTypeName = holidayTypeName,
        ColorCode = map.ColorCode
    };

    private static AttendanceRuleSettingDto ToAttendanceRuleSettingDto(
        AttendanceRuleSetting rule,
        AttendanceEntryType attendanceType) => new()
    {
        Id = rule.Id,
        AttendanceEntryTypeId = rule.AttendanceEntryTypeId,
        AttendanceTypeCode = attendanceType.Code,
        AttendanceTypeName = attendanceType.Name,
        Reference = rule.Reference,
        RuleName = rule.RuleName,
        WorkingMinutes = rule.WorkingMinutes,
        BeforeCheckInMinutes = rule.BeforeCheckInMinutes,
        AfterCheckOutMinutes = rule.AfterCheckOutMinutes,
        CheckInAdjustMinutes = rule.CheckInAdjustMinutes,
        CheckOutAdjustMinutes = rule.CheckOutAdjustMinutes,
        AbsentAfterShiftStartMinutes = rule.AbsentAfterShiftStartMinutes,
        MissingCheckoutAfterShiftEndMinutes = rule.MissingCheckoutAfterShiftEndMinutes,
        AccountLockAbsentDays = rule.AccountLockAbsentDays,
        WeekendChargeValue = rule.WeekendChargeValue,
        AdjustAbsentDays = rule.AdjustAbsentDays,
        IsApproved = rule.IsApproved,
        IsActive = rule.IsActive,
        Remarks = rule.Remarks
    };

    private async Task<IActionResult> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (ArgumentOutOfRangeException ex) { return BadRequest(new { message = ex.Message }); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }
}
