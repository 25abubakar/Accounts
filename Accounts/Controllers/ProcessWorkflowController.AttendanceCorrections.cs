using Accounts.DTOs;
using Accounts.Idempotency;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;

namespace Accounts.Controllers;

public sealed partial class ProcessWorkflowController
{
    [HttpGet("leave-request-context")]
    public async Task<IActionResult> LeaveRequestContext([FromQuery] DateOnly attendanceDate, CancellationToken ct)
    {
        var (context,error) = await LoadLeaveRequestContextAsync(attendanceDate,ct);
        return error ?? Ok(context);
    }

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
        => await SubmitAttendanceRequest(new SubmitAttendanceRequestDto
        {
            AttendanceDate = dto.AttendanceDate, Type = "Status", RequestedStatusId = dto.RequestedStatusId,
            Remarks = dto.Description
        }, attendance, ct);

    [HttpPost("attendance-requests")]
    [Idempotent]
    public async Task<IActionResult> SubmitAttendanceRequest(
        [FromBody] SubmitAttendanceRequestDto dto, [FromServices] IAttendanceService attendance, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Remarks) || dto.Remarks.Trim().Length > 2000)
            return BadRequest(new { message = "Describe the attendance issue using 1–2000 characters." });
        if (dto.Type is not ("Status" or "Leave" or "Short Leave" or "Over Time" or "Meal Status"))
            return BadRequest(new { message = "Choose a supported attendance request type." });

        // Reload the date/status from the caller's authorized report, never trust a supplied person or status label.
        var (context, error) = dto.Type == "Leave"
            ? await LoadLeaveRequestContextAsync(dto.AttendanceDate,ct)
            : await LoadAttendanceCorrectionContextAsync(dto.AttendanceDate, attendance, ct);
        if (error is not null) return error;
        if (!context!.CanSubmit) return BadRequest(new { message = context.Reason });
        var details = new AttendanceRequestDetailsDto
        {
            Type = dto.Type, Date = context.AttendanceDate, PersonId = context.PersonId,
            AttendanceId = context.AttendanceId, CurrentStatus = context.CurrentStatus, Remarks = dto.Remarks.Trim()
        };
        if (dto.Type == "Status")
        {
            var requested = context.Statuses.FirstOrDefault(status => status.Id == dto.RequestedStatusId);
            if (requested is null) return BadRequest(new { message = "Choose an active status configured for Attendance." });
            if (string.Equals(requested.Name.Trim(), context.CurrentStatus.Trim(), StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Requested status must differ from the current attendance status." });
            details.RequestedStatusId = requested.Id;
            details.RequestedStatus = requested.Name;
        }
        else if (dto.Type == "Leave")
        {
            if (!dto.DateTo.HasValue || dto.DateTo.Value < dto.AttendanceDate)
                return BadRequest(new { message = "Date To is required and must be on or after Date From." });
            var leaveType = context.LeaveTypes.FirstOrDefault(item => item.Id == dto.LeaveTypeId);
            if (leaveType == null) return BadRequest(new { message = "Choose an active saved Leave Type." });
            details.DateTo = dto.DateTo;
            details.LeaveTypeId = leaveType.Id;
            details.LeaveType = leaveType.Name;
        }
        else if (dto.Type is "Short Leave" or "Over Time")
        {
            if (!dto.TimeFrom.HasValue || !dto.TimeTo.HasValue || dto.TimeFrom == dto.TimeTo)
                return BadRequest(new { message = "Time From and Time To must define a non-zero period." });
            details.TimeFrom = dto.TimeFrom;
            details.TimeTo = dto.TimeTo;
            // TimeOnly subtraction handles an overnight period without relying on server-local dates.
            details.Minutes = (int)(dto.TimeTo.Value - dto.TimeFrom.Value).TotalMinutes;
            if (details.Minutes < 1)
                return BadRequest(new { message = "The requested period must be at least one minute." });
        }
        else
        {
            if (dto.MealStatus is not ("Entitled" or "Not Entitled"))
                return BadRequest(new { message = "Choose Entitled or Not Entitled for Meal Status." });
            details.RequestedStatus = dto.MealStatus;
        }

        // Reuse the existing SQL report/route/history workflow. Submission never edits punches, status or payroll.
        var report = new SubmitProcessReportDto
        {
            CategoryCode = dto.Type == "Leave" ? "LEAVE" : "ATTENDANCE",
            PriorityCode = "NORMAL",
            Title = $"Attendance {dto.Type} request · {context.AttendanceDate:dd MMM yyyy}",
            Description = $"Attendance date: {context.AttendanceDate:yyyy-MM-dd}\n" +
                $"Attendance ID: {context.AttendanceId?.ToString() ?? "Not yet recorded"}\n" +
                $"Current status: {context.CurrentStatus}\n" +
                $"Request type: {details.Type}\n" +
                (dto.Type == "Leave" ? $"Date From: {details.Date:yyyy-MM-dd}\nDate To: {details.DateTo:yyyy-MM-dd}\nLeave Type: {details.LeaveType}\n" :
                    details.RequestedStatus != null ? $"Requested status: {details.RequestedStatus}\n" :
                    $"Time From: {details.TimeFrom:HH:mm}\nTime To: {details.TimeTo:HH:mm}\nHours: {details.Minutes / 60m:0.##}\n") +
                "\n" + details.Remarks,
            SourceModule = dto.Type == "Status" ? "AttendanceStatusCorrection" : "AttendanceRequest",
            SourceRecordId = $"{context.PersonId:D}:{context.AttendanceDate:yyyy-MM-dd}",
            // NULL means self in usp_ProcessReport_Submit; arbitrary subject/recipient selection is not accepted.
            SubjectStaffId = null
        };
        var saved = await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = _db.Database.CurrentTransaction == null
                ? await _db.Database.BeginTransactionAsync(ct) : null;
            var result = await SaveReportAsync(report, ct)
                ?? throw new InvalidOperationException("The report was not saved.");
            var json = JsonSerializer.Serialize(details);
            var tenantId = _tenant.RequiredTenantId;
            await _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.ProcessReports SET AttendanceRequestJson={json} WHERE Id={result.Id} AND TenantId={tenantId}", ct);
            result.RowVersion = await _db.Database.SqlQueryRaw<string>(
                "SELECT CONVERT(varchar(18),RowVersion,1) AS Value FROM dbo.ProcessReports WHERE Id={0} AND TenantId={1}",
                result.Id,tenantId).SingleAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return result;
        });
        await PublishProcessChangedAsync("submitted", saved.Id);
        return Ok(saved);
    }

    [HttpGet("attendance-requests")]
    public async Task<IActionResult> AttendanceRequestHistory(CancellationToken ct)
    {
        if (!await HasActiveStaffProfileAsync(ct)) return Forbid();
        var tenantId = _tenant.RequiredTenantId;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var rows = new List<AttendanceRequestHistoryDto>();
        var connection = (SqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqlTransaction?)_db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                SELECT report.Id,report.RequestNumber,report.AttendanceRequestJson,report.SourceRecordId,report.Description,
                       person.FullName,staff.LoginId,status.Code AS StatusCode,status.Name AS StatusName
                FROM dbo.ProcessReports report
                JOIN dbo.StaffVacancy staff ON staff.StaffId=report.RequesterStaffId AND staff.TenantId=report.TenantId
                JOIN dbo.Persons person ON person.PersonId=staff.PersonId AND person.TenantId=report.TenantId
                JOIN dbo.ProcessWorkflowStatuses status ON status.Id=report.StatusId
                WHERE report.TenantId=@tenant AND person.IdentityUserId=@user
                  AND report.SourceModule IN (N'AttendanceStatusCorrection',N'AttendanceRequest')
                ORDER BY report.Id DESC
                """;
            command.Parameters.AddWithValue("@tenant",tenantId);
            command.Parameters.AddWithValue("@user",userId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var json = DbString(reader,"AttendanceRequestJson");
                AttendanceRequestDetailsDto? details = null;
                if (json != null) details = JsonSerializer.Deserialize<AttendanceRequestDetailsDto>(json);
                if (details == null)
                {
                    // Earlier status requests already exist as reports. Preserve and display them without reimporting.
                    var source = DbString(reader,"SourceRecordId") ?? "";
                    if (source.Length < 10 || !DateOnly.TryParse(source[^10..],out var date)) continue;
                    var description = reader.GetString(reader.GetOrdinal("Description"));
                    string Line(string prefix) => description.Split('\n').FirstOrDefault(line => line.StartsWith(prefix,StringComparison.Ordinal))?[prefix.Length..].Trim() ?? "";
                    details = new AttendanceRequestDetailsDto { Date=date,Type="Status",CurrentStatus=Line("Current status:"),RequestedStatus=Line("Requested status:"),Remarks=description.Split("\n\n",2).Last() };
                }
                var code = reader.GetString(reader.GetOrdinal("StatusCode"));
                rows.Add(new AttendanceRequestHistoryDto
                {
                    Id=reader.GetInt64(reader.GetOrdinal("Id")), RequestNumber=DbString(reader,"RequestNumber"),
                    FullName=reader.GetString(reader.GetOrdinal("FullName")),EmployeeNumber=DbString(reader,"LoginId"),
                    Details=details,StatusCode=code,Status=reader.GetString(reader.GetOrdinal("StatusName")),
                    IsApproved=code is "APPROVED" or "RESOLVED"
                });
            }
        }
        finally { if (shouldClose) await connection.CloseAsync(); }
        return Ok(rows);
    }

    private async Task<(AttendanceCorrectionRequestContextDto? Context, IActionResult? Error)>
        LoadLeaveRequestContextAsync(DateOnly date,CancellationToken ct)
    {
        if (date == default) return (null,BadRequest(new { message = "Date From is required." }));
        if (!await HasActiveStaffProfileAsync(ct)) return (null,Forbid());
        var userId=User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var personId=await _db.Persons.AsNoTracking()
            .Where(person=>person.TenantId==_tenant.RequiredTenantId && person.IdentityUserId==userId && person.IsActive)
            .Select(person=>person.PersonId).SingleAsync(ct);
        var leaveTypes=await _db.LeaveTypes.AsNoTracking()
            .Where(item=>item.TenantId==_tenant.RequiredTenantId && item.IsActive)
            .OrderBy(item=>item.DisplayOrder).ThenBy(item=>item.Name)
            .Select(item=>new AttendanceCorrectionStatusDto { Id=item.Id,Name=item.Name }).ToListAsync(ct);
        var capability=((await GetSubmissionCapability(ct)) as OkObjectResult)?.Value as ProcessReportSubmissionCapabilityDto;
        return (new AttendanceCorrectionRequestContextDto
        {
            PersonId=personId,AttendanceDate=date,LeaveTypes=leaveTypes,
            CanSubmit=capability?.CanSubmit==true && leaveTypes.Count>0,
            ReportingManagerName=capability?.ReportingManagerName,
            Reason=leaveTypes.Count==0 ? "No active Leave Types are saved for this company." : capability?.Reason
        },null);
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

        DailyAttendanceReportDto report;
        try
        {
            report = await attendance.GetDailyReportAsync(userId, false, date, date, true, ct);
        }
        catch (UnauthorizedAccessException) { return (null, Forbid()); }
        catch (InvalidOperationException ex) { return (null, BadRequest(new { message = ex.Message })); }
        catch (ArgumentOutOfRangeException ex) { return (null, BadRequest(new { message = ex.Message })); }
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
