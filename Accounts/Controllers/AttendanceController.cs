using Accounts.DTOs;
using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Accounts.Idempotency;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
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
    private readonly TenantPermissionService _tenantPermissions;
    private readonly IOrganizationDataScopeService _dataScope;
    private readonly IRealtimePublisher _realtime;
    private readonly AttendanceFinalizationService _attendanceFinalization;

    public AttendanceController(
        IAttendanceService service,
        ApplicationDbContext db,
        ITenantService tenant,
        RbacService rbac,
        TenantPermissionService tenantPermissions,
        IOrganizationDataScopeService dataScope,
        IRealtimePublisher realtime,
        AttendanceFinalizationService attendanceFinalization)
    {
        _service = service;
        _db = db;
        _tenant = tenant;
        _rbac = rbac;
        _tenantPermissions = tenantPermissions;
        _dataScope = dataScope;
        _realtime = realtime;
        _attendanceFinalization = attendanceFinalization;
    }

    [HttpGet("me/today")]
    public Task<IActionResult> Today(CancellationToken ct) => Execute(() => _service.GetTodayAsync(UserId(), ct));

    [HttpPost("me/check-in")]
    public Task<IActionResult> CheckIn([FromQuery] int? workModeId, CancellationToken ct) =>
        ExecuteRealtime(() => _service.CheckInAsync(UserId(), workModeId, ct), "check-in");

    [HttpGet("work-modes")]
    public async Task<IActionResult> WorkModes([FromServices] Accounts.Data.ApplicationDbContext db, CancellationToken ct) =>
        Ok(await db.AttendanceWorkModes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct));

    [HttpGet("entry-types")]
    public async Task<IActionResult> EntryTypes([FromServices] Accounts.Data.ApplicationDbContext db, CancellationToken ct) =>
        Ok(await db.AttendanceTypes.AsNoTracking()
            .Where(x => x.IsActive && x.TenantId == _tenant.RequiredTenantId)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct));

    [HttpGet("map-attendance")]
    [HttpGet("rules/map-attendance")]
    public async Task<IActionResult> MapAttendanceRules(CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue) return Ok(Array.Empty<AttendanceMapRuleDto>());
        if (!await HasAttendanceMenuActionAsync(
                "VIEW", ct, "/attendance/rules/map-attendance", "/attendance/map-attendance"))
            return Forbid();

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

        var attendanceType = await _db.AttendanceTypes
            .SingleOrDefaultAsync(type => type.Id == dto.AttendanceEntryTypeId && type.IsActive && type.TenantId == tenantId, ct);
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

        var now = PakistanClock.Now();
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

        // Map AppLookupValues -> PlatformSettings.StatusCrDbValues
        // We know that Holiday types (AppLookupValues TIMING_HOLIDAY_TYPE) are:
        // DAY_OFF, HOLIDAY, ANNUAL_HOLIDAY, WORKING_DAY
        // And StatusCrDbValues for ActionId=1 (Attendance) are: DO, HO, H, WD (or whatever user set)
        // Since the user might have named their statuses exactly as the Lookup Display Texts,
        // we can join on the Status Name = Lookup Display Text!
        
        var maps = await (from lookup in _db.AppLookupValues
                          join type in _db.AppLookupTypes on lookup.LookupTypeId equals type.LookupTypeId
                          where type.LookupTypeCode == "TIMING_HOLIDAY_TYPE" && lookup.IsActive
                          join status in _db.PlatformSettingStatuses on lookup.DisplayText equals status.Name
                          join actionStatus in _db.PlatformSettingActionStatuses on status.Id equals actionStatus.StatusId
                          join action in _db.PlatformSettingActions on actionStatus.ActionId equals action.Id
                          join color in _db.PlatformSettingColors on actionStatus.ColorId equals color.Id
                          where action.Name == "Attendance" && (actionStatus.TenantId == _tenant.RequiredTenantId || actionStatus.TenantId == null)
                          select new AttendanceHolidayColorMapDto
                          {
                              Id = lookup.LookupValueId,
                              HolidayTypeCode = lookup.ValueCode,
                              HolidayTypeName = lookup.DisplayText,
                              ColorCode = color.ColorCode
                          }).Distinct().ToListAsync(ct);

        return Ok(maps);
    }

    [HttpPost("me/toggle-break")]
    public Task<IActionResult> ToggleBreak(CancellationToken ct) =>
        ExecuteRealtime(() => _service.ToggleBreakAsync(UserId(), ct), "break-toggled");

    [HttpPost("me/check-out")]
    public Task<IActionResult> CheckOut(CancellationToken ct) =>
        ExecuteRealtime(() => _service.CheckOutAsync(UserId(), ct), "check-out");

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
                EarlyCheckoutAbsentAfterMinutes = rule.EarlyCheckoutAbsentAfterMinutes,
                MissingCheckoutAfterShiftEndMinutes = rule.MissingCheckoutAfterShiftEndMinutes,
                CameraVerificationToleranceMinutes = rule.CameraVerificationToleranceMinutes,
                AccountLockAbsentDays = rule.AccountLockAbsentDays,
                WeekendChargeValue = rule.WeekendChargeValue,
                AdjustAbsentDays = rule.AdjustAbsentDays,
                ExtremeLateAfterMinutes = rule.ExtremeLateAfterMinutes,
                PlatformLateStatusId = rule.PlatformLateStatusId,
                PlatformExtremeLateStatusId = rule.PlatformExtremeLateStatusId,
                ExtremeEarlyDepartureAfterMinutes = rule.ExtremeEarlyDepartureAfterMinutes,
                PlatformEarlyDepartureStatusId = rule.PlatformEarlyDepartureStatusId,
                PlatformExtremeEarlyDepartureStatusId = rule.PlatformExtremeEarlyDepartureStatusId,
                IsApproved = rule.IsApproved,
                IsActive = rule.IsActive,
                IsOvertimeBonusActive = rule.IsOvertimeBonusActive,
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
        if (!await HasAttendanceMenuActionAsync("VIEW", ct, "/attendance/timing-chart"))
            return Forbid();
        if (!await CanViewOthersAsync(ct)) return Forbid();
        if (year is < 2000 or > 2100 || month is < 1 or > 12) return BadRequest(new { message = "A valid report month is required." });
        return Ok(await _service.GetReportStaffAsync(year, month, ct));
    }

    [HttpGet("report/timing-chart/staff")]
    public async Task<IActionResult> TimingChartStaff(CancellationToken ct)
    {
        if (!await HasAttendanceMenuActionAsync("VIEW", ct, "/attendance/timing-chart"))
            return Forbid();
        var orgWide = await CanViewOrganizationAsync(ct);
        return await Execute(() => _service.GetTimingChartStaffAsync(UserId(), orgWide, ct));
    }

    [HttpGet("report/timing-chart/staff/{staffId:guid}/schedules")]
    public async Task<IActionResult> TimingChartSchedules(
        Guid staffId,
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct)
    {
        if (!await HasAttendanceMenuActionAsync("VIEW", ct, "/attendance/timing-chart"))
            return Forbid();
        var orgWide = await CanViewOrganizationAsync(ct);
        return await Execute(() => _service.GetTimingChartSchedulesAsync(
            UserId(), orgWide, staffId, year, month, ct));
    }

    [HttpGet("report/timing-chart/staff-schedule")]
    public async Task<IActionResult> TimingChartStaffSchedule(
        [FromQuery] int year,
        [FromQuery] int month,
        CancellationToken ct)
    {
        if (!await HasAttendanceMenuActionAsync("VIEW", ct, "/attendance/timing-chart"))
            return Forbid();
        var orgWide = await CanViewOrganizationAsync(ct);
        return await Execute(() => _service.GetTimingChartStaffScheduleAsync(
            UserId(), orgWide, year, month, ct));
    }

    [HttpPut("report/timing-chart/staff/{staffId:guid}/schedules/{holidayDate}")]
    public async Task<IActionResult> SaveTimingChartSchedule(
        Guid staffId,
        DateOnly holidayDate,
        [FromBody] SaveTimingChartScheduleDto dto,
        CancellationToken ct)
    {
        if (!await HasAttendanceMenuActionAsync("EDIT", ct, "/attendance/timing-chart"))
            return Forbid();

        var orgWide = await CanViewOrganizationAsync(ct);
        return await Execute(() => _service.SaveTimingChartScheduleAsync(
            UserId(), orgWide, staffId, holidayDate, dto, ct));
    }

    [HttpPost("report/timing-chart/staff/{staffId:guid}/schedules/range")]
    public async Task<IActionResult> SaveTimingChartScheduleRange(
        Guid staffId,
        [FromBody] SaveTimingChartScheduleRangeDto dto,
        CancellationToken ct)
    {
        if (!await HasAttendanceMenuActionAsync("EDIT", ct, "/attendance/timing-chart"))
            return Forbid();

        var orgWide = await CanViewOrganizationAsync(ct);
        return await Execute(() => _service.SaveTimingChartScheduleRangeAsync(
            UserId(), orgWide, staffId, dto, ct));
    }

    [HttpGet("report/monthly")]
    public async Task<IActionResult> MonthlyReport(
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] Guid? personId,
        CancellationToken ct)
    {
        if (!await HasAttendanceMenuActionAsync(
                "VIEW", ct, "/attendance/monthly-chart", "/attendance/staff", "/attendance/daily-report"))
            return Forbid();
        var canViewOthers = await CanViewOthersAsync(ct);
        if (personId.HasValue && canViewOthers)
        {
            var scope = await _dataScope.ResolveAsync(UserId(), ct);
            if (!scope.PersonIds.Contains(personId.Value)) return Forbid();
        }
        return await Execute(() => _service.GetMonthlyReportAsync(UserId(), canViewOthers, personId, year, month, ct));
    }

    [HttpGet("report/daily")]
    public async Task<IActionResult> DailyReport(
        [FromQuery] DateOnly dateFrom,
        [FromQuery] DateOnly dateTo,
        [FromQuery] bool includeAllTypes,
        CancellationToken ct)
    {
        if (!await HasAttendanceMenuActionAsync(
                "VIEW", ct, "/attendance/daily-report", "/attendance/types/check-in", "/attendance/types/camera"))
            return Forbid();
        var orgWide = await CanViewOrganizationAsync(ct);
        return await Execute(() => _service.GetDailyReportAsync(UserId(), orgWide, dateFrom, dateTo, includeAllTypes, ct));
    }

    [HttpGet("report/comparative")]
    public async Task<IActionResult> ComparativeAttendanceReport(
        [FromQuery] DateOnly dateFrom,
        [FromQuery] DateOnly dateTo,
        CancellationToken ct)
    {
        if (!await HasAttendanceMenuActionAsync("VIEW", ct, "/attendance/types/comparative"))
            return Forbid();

        var orgWide = await CanViewOrganizationAsync(ct);
        return await Execute(() =>
            _service.GetDailyReportAsync(UserId(), orgWide, dateFrom, dateTo, true, ct));
    }

    [HttpGet("report/remote")]
    public async Task<IActionResult> RemoteAttendanceReport([FromQuery] DateOnly dateFrom, [FromQuery] DateOnly dateTo, CancellationToken ct)
    {
        if (!await HasAttendanceMenuActionAsync("VIEW", ct, "/attendance/types/remote"))
            return Forbid();
        var orgWide = await CanViewOrganizationAsync(ct);
        return await Execute(() => _service.GetRemoteAttendanceReportAsync(UserId(), orgWide, dateFrom, dateTo, ct));
    }

    [HttpGet("report/login")]
    public async Task<IActionResult> LoginAttendanceReport([FromQuery] DateOnly dateFrom, [FromQuery] DateOnly dateTo, CancellationToken ct)
    {
        if (!await HasAttendanceMenuActionAsync("VIEW", ct, "/attendance/types/login"))
            return Forbid();
        var orgWide = await CanViewOrganizationAsync(ct);
        return await Execute(() => _service.GetLoginAttendanceReportAsync(UserId(), orgWide, dateFrom, dateTo, ct));
    }

    [HttpGet("report/deduction")]
    public async Task<IActionResult> DeductionReport([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        if (!await HasAttendanceMenuActionAsync("VIEW", ct, "/attendance/deduction"))
            return Forbid();
        // As per requirements: Deduction page always shows overall staff (not hierarchy wise) for anyone with access.
        return await Execute(() => _service.GetDeductionReportAsync(UserId(), organizationWide: true, year, month, ct));
    }

    [HttpPost("deductions/approve-overtime")]
    [Idempotent]
    public async Task<IActionResult> ApproveOvertime([FromBody] ApproveOvertimeRequestDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue) return Forbid();
        if (!await HasAttendanceMenuActionAsync("EDIT", ct, "/attendance/deduction"))
            return Forbid();
        if (await HasPendingAttendanceReviewAsync(dto.PersonId, dto.Year, dto.Month, ct))
            return Conflict(new { message = "Missing or invalid checkout attendance must be resolved before overtime approval." });

        var overtimeEnabled = await (from staff in _db.StaffVacancies.AsNoTracking()
                                     join map in _db.AttendanceMapRules.AsNoTracking() on staff.StaffId equals map.StaffId
                                     join rule in _db.AttendanceRuleSettings.AsNoTracking() on map.AttendanceEntryTypeId equals rule.AttendanceEntryTypeId
                                     where staff.TenantId == _tenant.RequiredTenantId && staff.PersonId == dto.PersonId
                                           && map.TenantId == _tenant.RequiredTenantId && rule.TenantId == _tenant.RequiredTenantId
                                           && rule.IsActive && rule.IsApproved && rule.IsOvertimeBonusActive
                                     select rule.Id).AnyAsync(ct);
        if (!overtimeEnabled)
            return BadRequest(new { message = "Overtime bonus is not active in this employee's attendance rule." });

        var record = await _db.AttendanceMonthlySettlements
            .FirstOrDefaultAsync(s => s.PersonId == dto.PersonId 
                                      && s.SettlementYear == dto.Year 
                                      && s.SettlementMonth == dto.Month
                                      && s.TenantId == _tenant.RequiredTenantId, ct);

        if (record == null)
        {
            record = new AttendanceMonthlySettlement
            {
                TenantId = _tenant.RequiredTenantId,
                PersonId = dto.PersonId,
                SettlementYear = dto.Year,
                SettlementMonth = dto.Month
            };
            _db.AttendanceMonthlySettlements.Add(record);
        }

        record.IsOvertimeApproved = dto.IsApproved;
        record.ApprovedByUserId = UserId();
        record.ApprovedDateUtc = PakistanClock.Now();

        await _db.SaveChangesAsync(ct);
        await PublishDeductionChangedAsync(
            dto.PersonId,
            dto.Year,
            dto.Month,
            dto.IsApproved ? "overtime-approved" : "overtime-unapproved",
            "Overtime decision updated",
            dto.IsApproved
                ? "Your overtime bonus was approved."
                : "Your overtime bonus approval was withdrawn.");
        return Ok(new { message = "Overtime approval status updated." });
    }

    [HttpPost("deductions/adjustment")]
    [Idempotent]
    public async Task<IActionResult> SaveAdjustment([FromBody] SaveAdjustmentRequestDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue) return Forbid();
        if (!await HasAttendanceMenuActionAsync("EDIT", ct, "/attendance/deduction"))
            return Forbid();

        var record = await _db.AttendanceMonthlySettlements
            .FirstOrDefaultAsync(s => s.PersonId == dto.PersonId 
                                      && s.SettlementYear == dto.Year 
                                      && s.SettlementMonth == dto.Month
                                      && s.TenantId == _tenant.RequiredTenantId, ct);

        if (record == null)
        {
            record = new AttendanceMonthlySettlement
            {
                TenantId = _tenant.RequiredTenantId,
                PersonId = dto.PersonId,
                SettlementYear = dto.Year,
                SettlementMonth = dto.Month
            };
            _db.AttendanceMonthlySettlements.Add(record);
        }

        record.AdjustmentAmount = dto.AdjustmentAmount;
        record.AdjustmentRemarks = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim();

        await _db.SaveChangesAsync(ct);
        await PublishDeductionChangedAsync(
            dto.PersonId,
            dto.Year,
            dto.Month,
            "adjustment-saved");
        return Ok(new { message = "Adjustment saved successfully." });
    }

    [HttpPost("deductions/adjustment/approve")]
    [Idempotent]
    public async Task<IActionResult> ApproveAdjustment([FromBody] ApproveAdjustmentRequestDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue) return Forbid();
        if (!await HasAttendanceMenuActionAsync("EDIT", ct, "/attendance/deduction"))
            return Forbid();
        if (await HasPendingAttendanceReviewAsync(dto.PersonId, dto.Year, dto.Month, ct))
            return Conflict(new { message = "Missing or invalid checkout attendance must be resolved before deduction approval." });

        var validCode = await _db.ProcessApprovalCodes.FirstOrDefaultAsync(x => x.TenantId == _tenant.RequiredTenantId && x.ProcessName == "DeductionAdjustment", ct);
        if (validCode == null || validCode.PinCode != dto.PinCode)
        {
            return BadRequest(new { message = "Invalid approval code." });
        }

        var record = await _db.AttendanceMonthlySettlements
            .FirstOrDefaultAsync(s => s.PersonId == dto.PersonId 
                                      && s.SettlementYear == dto.Year 
                                      && s.SettlementMonth == dto.Month
                                      && s.TenantId == _tenant.RequiredTenantId, ct);

        if (record == null)
        {
            record = new AttendanceMonthlySettlement
            {
                TenantId = _tenant.RequiredTenantId,
                PersonId = dto.PersonId,
                SettlementYear = dto.Year,
                SettlementMonth = dto.Month
            };
            _db.AttendanceMonthlySettlements.Add(record);
        }

        if (record.IsAdjustmentApproved)
            return BadRequest(new { message = "Already approved." });

        record.IsAdjustmentApproved = true;
        record.ApprovedByUserId = UserId();
        record.ApprovedDateUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await PublishDeductionChangedAsync(
            dto.PersonId,
            dto.Year,
            dto.Month,
            "adjustment-approved",
            "Deduction approved",
            "Your monthly deduction adjustment was approved.");
        return Ok(new { message = "Deduction approved successfully." });
    }

    [HttpPost("deductions/requests")]
    [Idempotent]
    public async Task<IActionResult> CreateDeductionRequest([FromBody] SaveAttendanceDeductionRequestDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue) return Forbid();
        if (!await HasAttendanceMenuActionAsync("ADD", ct, "/attendance/deduction"))
            return Forbid();

        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "Name is required." });
        if (string.IsNullOrWhiteSpace(dto.UserId)) return BadRequest(new { message = "User ID is required." });
        if (dto.DeductionMonth is < 1 or > 12) return BadRequest(new { message = "Select a valid deduction month." });
        if (dto.DeductionYear is < 2000 or > 2100) return BadRequest(new { message = "Select a valid deduction year." });
        if (string.IsNullOrWhiteSpace(dto.ActionName)) return BadRequest(new { message = "Select an action." });

        await AttendanceRecordSchema.EnsureDeductionRequestTableAsync(_db, ct);

        var request = new AttendanceDeductionRequest
        {
            TenantId = _tenant.RequiredTenantId,
            RegNo = Trim(dto.RegNo, 50),
            Name = TrimRequired(dto.Name, 200),
            UserId = TrimRequired(dto.UserId, 100),
            DateOfBirth = dto.DateOfBirth,
            Phone = Trim(dto.Phone, 50),
            Email = Trim(dto.Email, 256),
            Office = Trim(dto.Office, 150),
            Department = Trim(dto.Department, 150),
            Designation = Trim(dto.Designation, 150),
            Classification = Trim(dto.Classification, 100),
            Routing = Trim(dto.Routing, 150),
            Authority = Trim(dto.Authority, 150),
            Subject = Trim(dto.Subject, 250),
            DocumentName = Trim(dto.DocumentName, 260),
            DeductionMonth = dto.DeductionMonth,
            DeductionYear = dto.DeductionYear,
            ActionRouting = Trim(dto.ActionRouting, 150),
            ActionName = Trim(dto.ActionName, 100),
            Comments = Trim(dto.Comments, 1000),
            CreatedByUserId = UserId(),
            CreatedDate = PakistanClock.Now()
        };

        _db.AttendanceDeductionRequests.Add(request);
        await _db.SaveChangesAsync(ct);
        await _realtime.PublishEventToTenantAsync(
            _tenant.RequiredTenantId,
            RealtimeEventDto.Create(
                RealtimeEventTypes.DeductionChanged,
                "deduction",
                "request-submitted",
                _tenant.RequiredTenantId,
                request.Id.ToString(CultureInfo.InvariantCulture),
                new Dictionary<string, string>
                {
                    ["year"] = dto.DeductionYear.ToString(CultureInfo.InvariantCulture),
                    ["month"] = dto.DeductionMonth.ToString(CultureInfo.InvariantCulture),
                }));

        return Ok(new { id = request.Id, message = "Deduction request submitted successfully." });
    }

    [HttpPost("types/camera/entries")]
    public async Task<IActionResult> SaveCameraAttendance([FromBody] SaveCameraAttendanceDto dto, CancellationToken ct)
    {
        await AttendanceRecordSchema.EnsureCameraColumnsAsync(_db, ct);
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
        var scope = await _dataScope.ResolveAsync(UserId(), ct);
        if (!scope.PersonIds.Contains(person.PersonId)) return Forbid();

        DateTime? checkInUtc = null;
        DateTime? checkOutUtc = null;
        if (!string.IsNullOrWhiteSpace(dto.CheckInTime))
        {
            if (!TimeOnly.TryParseExact(dto.CheckInTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                return BadRequest(new { message = "Check-in time must be in HH:mm format." });
            checkInUtc = PakistanClock.AsDatabaseLocal(dto.AttendanceDate.ToDateTime(time));
        }
        if (!string.IsNullOrWhiteSpace(dto.CheckOutTime))
        {
            if (!TimeOnly.TryParseExact(dto.CheckOutTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                return BadRequest(new { message = "Check-out time must be in HH:mm format." });
            checkOutUtc = PakistanClock.AsDatabaseLocal(dto.AttendanceDate.ToDateTime(time));
        }
        if (checkInUtc.HasValue && checkOutUtc.HasValue && checkOutUtc.Value < checkInUtc.Value)
            checkOutUtc = checkOutUtc.Value.AddDays(1);

        var checkEntryType = await _db.AttendanceTypes.SingleOrDefaultAsync(x =>
            x.IsActive &&
            x.TenantId == person.TenantId &&
            (x.Code == "CHECK_IN_OUT" || x.Code == "CHECK"), ct);
        if (checkEntryType == null) return BadRequest(new { message = "Check in/Out attendance type is not configured." });
        var cameraEntryTypeId = await _db.AttendanceTypes
            .AsNoTracking()
            .Where(x => x.Code == "CAMERA" && x.TenantId == person.TenantId)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);
        var isMappedForCheckInOut = await _db.AttendanceMapRules
            .AsNoTracking()
            .AnyAsync(rule =>
                rule.TenantId == person.TenantId &&
                rule.StaffId == person.Staff.StaffId &&
                rule.AttendanceEntryTypeId == checkEntryType.Id,
                ct);
        if (!isMappedForCheckInOut)
            return BadRequest(new { message = "Camera verification is only available for staff mapped to Check in/Out attendance." });
        var workMode = await _db.AttendanceWorkModes.SingleOrDefaultAsync(x => x.Code == "ONSITE" && x.IsActive, ct);

        var record = await _db.AttendanceRecords
            .SingleOrDefaultAsync(x => x.PersonId == person.PersonId && x.AttendanceDate == dto.AttendanceDate, ct);
        var now = PakistanClock.Now();
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

        if (!record.AttendanceEntryTypeId.HasValue ||
            (cameraEntryTypeId.HasValue && record.AttendanceEntryTypeId.Value == cameraEntryTypeId.Value))
        {
            record.AttendanceEntryTypeId = checkEntryType.Id;
        }
        record.AttendanceWorkModeId ??= workMode?.Id;
        // The form is the only source of camera evidence. A blank camera time must
        // remain NULL; never infer it from the portal punch or the scheduled shift.
        record.CameraCheckInUtc = checkInUtc;
        record.CameraCheckOutUtc = checkOutUtc;
        record.CameraRemarks = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim();
        record.ModifiedDate = now;
        await _db.SaveChangesAsync(ct);

        await _db.Database.ExecuteSqlRawAsync(
            "EXEC dbo.usp_Attendance_ApplyCameraVerification @TenantId, @AttendanceRecordId, @ActorUserId",
            new SqlParameter("@TenantId", person.TenantId),
            new SqlParameter("@AttendanceRecordId", record.Id),
            new SqlParameter("@ActorUserId", UserId()));

        await _db.Entry(record).ReloadAsync(ct);
        return Ok(new
        {
            message = record.HasVerificationAnomaly
                ? "Camera evidence saved. A mismatch was detected and sent for independent approval."
                : "Camera attendance saved and verified successfully.",
            record.ApprovalRequestId,
            record.HasVerificationAnomaly,
            record.VerificationDifferenceMinutes
        });
    }

    [HttpPost("types/by-supervisor/entries")]
    [Idempotent]
    public async Task<IActionResult> SaveSupervisorAttendance(
        [FromBody] SaveSupervisorAttendanceDto dto,
        CancellationToken ct)
    {
        try
        {
            var result = await _service.SaveSupervisorAttendanceAsync(UserId(), dto, ct);
            if (_tenant.TenantId.HasValue)
            {
                await _realtime.PublishEventToTenantAsync(
                    _tenant.TenantId.Value,
                    RealtimeEventDto.Create(
                        RealtimeEventTypes.AttendanceChanged,
                        "attendance",
                        "supervisor-entry-saved",
                        _tenant.TenantId.Value));
            }

            return Ok(new
            {
                result.AttendanceDate,
                result.SavedEntries,
                message = $"{result.SavedEntries} supervisor attendance row(s) saved and recalculated."
            });
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("types/camera/verifications/{requestId:long}/decision")]
    public async Task<IActionResult> ReviewCameraAttendance(
        long requestId,
        [FromBody] ReviewCameraAttendanceDto dto,
        CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue) return Forbid();
        if (!await HasAttendanceMenuActionAsync("EDIT", ct, "/attendance/types/camera"))
            return Forbid();

        var decision = dto.DecisionCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var request = await _db.WorkflowApprovalRequests.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == requestId, ct);
        if (request == null) return NotFound(new { message = "The approval request was not found." });

        var reviewerId = UserId();
        var subjectUserId = request.SubjectPersonId.HasValue
            ? await _db.Persons.AsNoTracking()
                .Where(person => person.PersonId == request.SubjectPersonId.Value)
                .Select(person => person.IdentityUserId)
                .SingleOrDefaultAsync(ct)
            : null;
        if (reviewerId == request.RequestedByUserId || reviewerId == subjectUserId)
            return Conflict(new { message = "Self-approval is not allowed. A different authorized approver must review this entry." });

        DateTime? manualIn = null;
        DateTime? manualOut = null;
        if (decision == "MANUAL_CORRECTION")
        {
            var attendanceDate = await _db.AttendanceRecords.AsNoTracking()
                .Where(record => record.ApprovalRequestId == requestId)
                .Select(record => (DateOnly?)record.AttendanceDate)
                .SingleOrDefaultAsync(ct);
            if (!attendanceDate.HasValue) return NotFound(new { message = "The linked attendance record was not found." });
            try
            {
                manualIn = ParseCameraDecisionTime(dto.ManualCheckInTime, attendanceDate.Value);
                manualOut = ParseCameraDecisionTime(dto.ManualCheckOutTime, attendanceDate.Value);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            if (!manualIn.HasValue && !manualOut.HasValue)
                return BadRequest(new { message = "Enter at least one corrected check-in or check-out time." });
            if (manualIn.HasValue && manualOut.HasValue && manualOut < manualIn) manualOut = manualOut.Value.AddDays(1);
        }

        try
        {
            await _db.Database.ExecuteSqlRawAsync(
                "EXEC dbo.usp_WorkflowApproval_DecideCameraAttendance @TenantId, @RequestId, @ReviewerUserId, @DecisionCode, @ManualCheckIn, @ManualCheckOut, @Comments",
                new SqlParameter("@TenantId", _tenant.RequiredTenantId),
                new SqlParameter("@RequestId", requestId),
                new SqlParameter("@ReviewerUserId", reviewerId),
                new SqlParameter("@DecisionCode", decision),
                new SqlParameter("@ManualCheckIn", (object?)manualIn ?? DBNull.Value),
                new SqlParameter("@ManualCheckOut", (object?)manualOut ?? DBNull.Value),
                new SqlParameter("@Comments", (object?)dto.Comments?.Trim() ?? DBNull.Value));
        }
        catch (SqlException ex) when (ex.Number is 51021 or 51022 or 51023 or 51024)
        {
            return Conflict(new { message = ex.Message });
        }

        return Ok(new { message = "Camera attendance verification decision saved successfully." });
    }

    private static DateTime? ParseCameraDecisionTime(string? value, DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new ArgumentException("Manual attendance time must be in HH:mm format.");
        return PakistanClock.AsDatabaseLocal(date.ToDateTime(time));
    }

    [HttpGet("report/staff-attendance")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> StaffAttendanceReport([FromQuery] DateOnly dateFrom, [FromQuery] DateOnly dateTo, CancellationToken ct)
    {
        if (!await HasAttendanceMenuActionAsync("VIEW", ct, "/attendance/staff"))
            return Forbid();
        var orgWide = await CanViewOrganizationAsync(ct);
        return await Execute(() => _service.GetStaffAttendanceReportAsync(UserId(), orgWide, dateFrom, dateTo, ct));
    }

    [HttpGet("report/by-supervisor")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public Task<IActionResult> SupervisorAttendanceReport(
        [FromQuery] DateOnly attendanceDate,
        CancellationToken ct) =>
        Execute(() => _service.GetSupervisorAttendanceReportAsync(
            UserId(),
            attendanceDate,
            ct));

    [HttpGet("report/staff-attendance/access")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> StaffAttendanceAccess(CancellationToken ct)
    {
        if (!await HasAttendanceMenuActionAsync("VIEW", ct, "/attendance/staff"))
            return Forbid();
        var orgWide = await CanViewOrganizationAsync(ct);
        return Ok(new { canViewHistorical = await _service.CanViewHistoricalAttendanceAsync(UserId(), orgWide, ct) });
    }

    [HttpGet("report/daily/access")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> DailyAttendanceAccess(CancellationToken ct)
    {
        if (!await HasAttendanceMenuActionAsync("VIEW", ct, "/attendance/daily-report"))
            return Forbid();
        var orgWide = await CanViewOrganizationAsync(ct);
        return Ok(new { canViewHistorical = await _service.CanViewTeamHistoricalAttendanceAsync(UserId(), orgWide, ct) });
    }

    [HttpGet("report/monthly-chart")]
    public async Task<IActionResult> MonthlyChart([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        if (!await HasAttendanceMenuActionAsync("VIEW", ct, "/attendance/monthly-chart"))
            return Forbid();
        var orgWide = await CanViewOrganizationAsync(ct);
        return await Execute(() => _service.GetMonthlyChartAsync(UserId(), orgWide, year, month, ct));
    }

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();

    private async Task<bool> CanViewOthersAsync(CancellationToken ct)
    {
        if (TenantPermissionService.IsSuperAdmin(User)) return true;
        if (TenantPermissionService.IsTenantAdmin(User))
            return await _tenantPermissions.HasMenuRouteAsync(User,
                ["/attendance/report", "/attendance/daily-report"], "VIEW", ct);
        return false;
    }

    private async Task<bool> CanViewOrganizationAsync(CancellationToken ct)
    {
        if (TenantPermissionService.IsSuperAdmin(User)) return true;
        if (TenantPermissionService.IsTenantAdmin(User))
            return await _tenantPermissions.HasMenuRouteAsync(User,
                ["/attendance/daily-report", "/attendance/staff", "/attendance/report", "/attendance/timing-chart", "/attendance/deduction"], "VIEW", ct);
        return false;
    }

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
        if (TenantPermissionService.IsSuperAdmin(User)) return true;
        if (TenantPermissionService.IsTenantAdmin(User))
            return await _tenantPermissions.HasMenuRouteAsync(User, routes, action, ct);

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
        if (dto.EarlyCheckoutAbsentAfterMinutes is < 1 or > 1440) return BadRequest(new { message = "Early checkout absent-after time must be between 1 and 1440 minutes." });
        if (dto.MissingCheckoutAfterShiftEndMinutes is < 1 or > 1440) return BadRequest(new { message = "Missing checkout time must be between 1 and 1440 minutes." });
        if (dto.CameraVerificationToleranceMinutes is < 0 or > 240) return BadRequest(new { message = "Camera verification tolerance must be between 0 and 240 minutes." });
        if (dto.AccountLockAbsentDays is < 0 or > 31) return BadRequest(new { message = "Account lock absent days must be between 0 and 31." });
        if (dto.WeekendChargeValue is < 0 or > 31) return BadRequest(new { message = "Weekend charged value must be between 0 and 31." });
        if (dto.AdjustAbsentDays is < 0 or > 31) return BadRequest(new { message = "Adjust absent days must be between 0 and 31." });

        var attendanceType = await _db.AttendanceTypes
            .SingleOrDefaultAsync(type => type.Id == dto.AttendanceEntryTypeId && type.IsActive && type.TenantId == tenantId, ct);
        if (attendanceType == null) return BadRequest(new { message = "Select an active attendance type." });

        var duplicate = await _db.AttendanceRuleSettings.AsNoTracking()
            .AnyAsync(rule =>
                rule.TenantId == tenantId &&
                rule.AttendanceEntryTypeId == attendanceType.Id &&
                (!id.HasValue || rule.Id != id.Value), ct);
        if (duplicate) return BadRequest(new { message = "This attendance type already has a rule. Edit the existing rule instead." });

        var now = PakistanClock.Now();
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
        rule.EarlyCheckoutAbsentAfterMinutes = dto.EarlyCheckoutAbsentAfterMinutes;
        rule.MissingCheckoutAfterShiftEndMinutes = dto.MissingCheckoutAfterShiftEndMinutes;
        rule.CameraVerificationToleranceMinutes = dto.CameraVerificationToleranceMinutes;
        rule.AccountLockAbsentDays = dto.AccountLockAbsentDays;
        rule.WeekendChargeValue = dto.WeekendChargeValue;
        rule.AdjustAbsentDays = dto.AdjustAbsentDays;
        rule.ExtremeLateAfterMinutes = dto.ExtremeLateAfterMinutes;
        rule.PlatformLateStatusId = dto.PlatformLateStatusId;
        rule.PlatformExtremeLateStatusId = dto.PlatformExtremeLateStatusId;
        rule.ExtremeEarlyDepartureAfterMinutes = dto.ExtremeEarlyDepartureAfterMinutes;
        rule.PlatformEarlyDepartureStatusId = dto.PlatformEarlyDepartureStatusId;
        rule.PlatformExtremeEarlyDepartureStatusId = dto.PlatformExtremeEarlyDepartureStatusId;
        rule.IsApproved = dto.IsApproved;
        rule.IsActive = dto.IsActive;
        rule.IsOvertimeBonusActive = dto.IsOvertimeBonusActive;
        rule.Remarks = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim();

        await _db.SaveChangesAsync(ct);
        return Ok(ToAttendanceRuleSettingDto(rule, attendanceType));
    }



    private static AttendanceRuleSettingDto ToAttendanceRuleSettingDto(
        AttendanceRuleSetting rule,
        AttendanceType attendanceType) => new()
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
        EarlyCheckoutAbsentAfterMinutes = rule.EarlyCheckoutAbsentAfterMinutes,
        MissingCheckoutAfterShiftEndMinutes = rule.MissingCheckoutAfterShiftEndMinutes,
        CameraVerificationToleranceMinutes = rule.CameraVerificationToleranceMinutes,
        AccountLockAbsentDays = rule.AccountLockAbsentDays,
        WeekendChargeValue = rule.WeekendChargeValue,
        AdjustAbsentDays = rule.AdjustAbsentDays,
        ExtremeLateAfterMinutes = rule.ExtremeLateAfterMinutes,
        PlatformLateStatusId = rule.PlatformLateStatusId,
        PlatformExtremeLateStatusId = rule.PlatformExtremeLateStatusId,
        ExtremeEarlyDepartureAfterMinutes = rule.ExtremeEarlyDepartureAfterMinutes,
        PlatformEarlyDepartureStatusId = rule.PlatformEarlyDepartureStatusId,
        PlatformExtremeEarlyDepartureStatusId = rule.PlatformExtremeEarlyDepartureStatusId,
        IsApproved = rule.IsApproved,
        IsActive = rule.IsActive,
        IsOvertimeBonusActive = rule.IsOvertimeBonusActive,
        Remarks = rule.Remarks
    };

    private async Task<bool> HasPendingAttendanceReviewAsync(
        Guid personId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        await _attendanceFinalization.RefreshPeriodAsync(
            _tenant.RequiredTenantId,
            year,
            month,
            cancellationToken);

        return await _db.AttendanceDailyFinalizations.AsNoTracking().AnyAsync(
            row =>
                row.TenantId == _tenant.RequiredTenantId &&
                row.PersonId == personId &&
                row.AttendanceDate.Year == year &&
                row.AttendanceDate.Month == month &&
                row.State == AttendanceFinalizationStates.PendingReview,
            cancellationToken);
    }

    private async Task PublishDeductionChangedAsync(
        Guid personId,
        int year,
        int month,
        string action,
        string? notificationTitle = null,
        string? notificationMessage = null)
    {
        var tenantId = _tenant.RequiredTenantId;
        var message = RealtimeEventDto.Create(
            RealtimeEventTypes.DeductionChanged,
            "deduction",
            action,
            tenantId,
            personId.ToString(),
            new Dictionary<string, string>
            {
                ["year"] = year.ToString(CultureInfo.InvariantCulture),
                ["month"] = month.ToString(CultureInfo.InvariantCulture),
            });
        await _realtime.PublishEventToTenantAsync(tenantId, message);

        if (!string.IsNullOrWhiteSpace(notificationTitle)
            && !string.IsNullOrWhiteSpace(notificationMessage))
        {
            await _realtime.PublishNotificationToPersonAsync(
                personId,
                RealtimeNotificationDto.Create(
                    "deduction",
                    "success",
                    notificationTitle,
                    notificationMessage,
                    "/attendance/deduction"));
        }
    }

    private async Task<IActionResult> ExecuteRealtime<T>(
        Func<Task<T>> action,
        string realtimeAction)
    {
        try
        {
            var result = await action();
            if (_tenant.TenantId.HasValue)
            {
                await _realtime.PublishEventToTenantAsync(
                    _tenant.TenantId.Value,
                    RealtimeEventDto.Create(
                        RealtimeEventTypes.AttendanceChanged,
                        "attendance",
                        realtimeAction,
                        _tenant.TenantId.Value));
            }
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (ArgumentOutOfRangeException ex) { return BadRequest(new { message = ex.Message }); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }

    private async Task<IActionResult> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (ArgumentOutOfRangeException ex) { return BadRequest(new { message = ex.Message }); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static string TrimRequired(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}

