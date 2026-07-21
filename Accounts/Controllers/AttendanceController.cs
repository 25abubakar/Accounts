using Accounts.DTOs;
using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
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

    public AttendanceController(IAttendanceService service, ApplicationDbContext db, ITenantService tenant)
    {
        _service = service;
        _db = db;
        _tenant = tenant;
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
        if (!CanViewOrganization() || !_tenant.TenantId.HasValue) return Forbid();
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
            .SingleOrDefaultAsync(item => item.StaffId == dto.StaffId, ct);
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
    public Task<IActionResult> SaveTimingChartSchedule(
        Guid staffId,
        DateOnly holidayDate,
        [FromBody] SaveTimingChartScheduleDto dto,
        CancellationToken ct) =>
        Execute(() => _service.SaveTimingChartScheduleAsync(
            UserId(), CanViewOrganization(), staffId, holidayDate, dto, ct));

    [HttpPost("report/timing-chart/staff/{staffId:guid}/schedules/range")]
    public Task<IActionResult> SaveTimingChartScheduleRange(
        Guid staffId,
        [FromBody] SaveTimingChartScheduleRangeDto dto,
        CancellationToken ct) =>
        Execute(() => _service.SaveTimingChartScheduleRangeAsync(
            UserId(), CanViewOrganization(), staffId, dto, ct));

    [HttpGet("report/monthly")]
    public Task<IActionResult> MonthlyReport([FromQuery] int year, [FromQuery] int month, [FromQuery] Guid? personId, CancellationToken ct) =>
        Execute(() => _service.GetMonthlyReportAsync(UserId(), CanViewOthers(), personId, year, month, ct));

    [HttpGet("report/daily")]
    public Task<IActionResult> DailyReport([FromQuery] DateOnly dateFrom, [FromQuery] DateOnly dateTo, CancellationToken ct) =>
        Execute(() => _service.GetDailyReportAsync(UserId(), CanViewOrganization(), dateFrom, dateTo, ct));

    [HttpGet("report/remote")]
    public Task<IActionResult> RemoteAttendanceReport([FromQuery] DateOnly dateFrom, [FromQuery] DateOnly dateTo, CancellationToken ct) =>
        Execute(() => _service.GetRemoteAttendanceReportAsync(UserId(), CanViewOrganization(), dateFrom, dateTo, ct));

    [HttpGet("report/staff-attendance")]
    public Task<IActionResult> StaffAttendanceReport([FromQuery] DateOnly dateFrom, [FromQuery] DateOnly dateTo, CancellationToken ct) =>
        Execute(() => _service.GetStaffAttendanceReportAsync(UserId(), dateFrom, dateTo, ct));

    [HttpGet("report/monthly-chart")]
    public Task<IActionResult> MonthlyChart([FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        Execute(() => _service.GetMonthlyChartAsync(UserId(), CanViewOrganization(), year, month, ct));

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    private bool CanViewOthers() => User.IsInRole("SuperAdmin") || User.IsInRole("Admin") || User.IsInRole("CEO") || User.HasClaim(ITenantService.ClaimIsTenantAdmin, "true");
    private bool CanViewOrganization() => User.IsInRole("SuperAdmin") || User.IsInRole("Admin") || User.IsInRole("CEO") || User.HasClaim(ITenantService.ClaimIsTenantAdmin, "true");
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
        if (!CanViewOrganization() || !_tenant.TenantId.HasValue) return Forbid();

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

    private static AttendanceHolidayColorMapDto ToHolidayColorMapDto(
        AttendanceHolidayColorMap map,
        string holidayTypeName) => new()
    {
        Id = map.Id,
        HolidayTypeCode = map.HolidayTypeCode,
        HolidayTypeName = holidayTypeName,
        ColorCode = map.ColorCode
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
