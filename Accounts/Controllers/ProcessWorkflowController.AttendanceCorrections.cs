using Accounts.DTOs;
using Accounts.Idempotency;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers;

public sealed partial class ProcessWorkflowController
{
    [HttpGet("attendance-corrections/context")]
    public async Task<IActionResult> AttendanceCorrectionContext(
        [FromQuery] DateOnly attendanceDate, [FromServices] IAttendanceService attendance, CancellationToken ct)
    {
        var (context, error) = await LoadAttendanceCorrectionContextAsync(attendanceDate, attendance, ct);
        return error ?? Ok(context);
    }

    [HttpPost("attendance-corrections")]
    [Idempotent]
    public async Task<IActionResult> SubmitAttendanceCorrection(
        [FromBody] SubmitAttendanceCorrectionRequestDto dto,
        [FromServices] IAttendanceService attendance, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Description) || dto.Description.Trim().Length > 2000)
            return BadRequest(new { message = "Describe the attendance issue using 1–2000 characters." });

        // Reload the date/status from the caller's authorized report, never trust a supplied person or status label.
        var (context, error) = await LoadAttendanceCorrectionContextAsync(dto.AttendanceDate, attendance, ct);
        if (error is not null) return error;
        if (!context!.CanSubmit) return BadRequest(new { message = context.Reason });
        var requested = context.Statuses.FirstOrDefault(status => status.Id == dto.RequestedStatusId);
        if (requested is null)
            return BadRequest(new { message = "Choose an active status configured for Attendance." });
        if (string.Equals(requested.Name.Trim(), context.CurrentStatus.Trim(), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Requested status must differ from the current attendance status." });

        // Reuse the existing SQL report/route/history workflow. Submission never edits punches, status or payroll.
        return await Submit(new SubmitProcessReportDto
        {
            CategoryCode = "ATTENDANCE",
            PriorityCode = "NORMAL",
            Title = $"Attendance status correction · {context.AttendanceDate:dd MMM yyyy}",
            Description = $"Attendance date: {context.AttendanceDate:yyyy-MM-dd}\n" +
                $"Attendance ID: {context.AttendanceId?.ToString() ?? "Not yet recorded"}\n" +
                $"Current status: {context.CurrentStatus}\n" +
                $"Requested status: {requested.Name} (Status ID: {requested.Id})\n\n" + dto.Description.Trim(),
            SourceModule = "AttendanceStatusCorrection",
            SourceRecordId = $"{context.PersonId:D}:{context.AttendanceDate:yyyy-MM-dd}",
            // NULL means self in usp_ProcessReport_Submit; arbitrary subject/recipient selection is not accepted.
            SubjectStaffId = null
        }, ct);
    }

    private async Task<(AttendanceCorrectionRequestContextDto? Context, IActionResult? Error)>
        LoadAttendanceCorrectionContextAsync(DateOnly date, IAttendanceService attendance, CancellationToken ct)
    {
        if (date == default || date > BusinessClock.Today(BusinessClock.DefaultTimeZoneId))
            return (null, BadRequest(new { message = "Choose an existing attendance date, up to today." }));
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (_tenant.IsSuperAdmin || !_tenant.TenantId.HasValue || string.IsNullOrWhiteSpace(userId))
            return (null, Forbid());
        var personId = await _db.Persons.AsNoTracking()
            .Where(person => person.TenantId == _tenant.RequiredTenantId && person.IdentityUserId == userId &&
                person.IsActive && person.Staff != null)
            .Select(person => (Guid?)person.PersonId).SingleOrDefaultAsync(ct);
        if (!personId.HasValue) return (null, Forbid());

        var report = await attendance.GetDailyReportAsync(userId, false, date, date, true, ct);
        var row = report.Rows.FirstOrDefault(item => item.PersonId == personId.Value && item.Date == date);
        if (row is null) return (null, NotFound(new { message = "No attendance day is available for this request." }));
        var statuses = await _db.PlatformSettingProcessStatuses.AsNoTracking()
            .Where(item => item.Process.Name == "Attendance" && item.Process.IsActive && item.Status.IsActive)
            .Select(item => new AttendanceCorrectionStatusDto { Id = item.StatusId, Name = item.Status.Name })
            .Distinct().OrderBy(item => item.Id).ToListAsync(ct);
        var capabilityResult = await GetSubmissionCapability(ct);
        var capability = (capabilityResult as OkObjectResult)?.Value as ProcessReportSubmissionCapabilityDto;
        return (new AttendanceCorrectionRequestContextDto
        {
            PersonId = personId.Value, AttendanceId = row.Id, AttendanceDate = date,
            CurrentStatus = string.IsNullOrWhiteSpace(row.AttendanceStatus) ? row.StatusCode ?? "-" : row.AttendanceStatus,
            Statuses = statuses, CanSubmit = capability?.CanSubmit == true && statuses.Count > 0,
            ReportingManagerName = capability?.ReportingManagerName,
            Reason = statuses.Count == 0 ? "No active attendance statuses are configured." : capability?.Reason
        }, null);
    }
}
