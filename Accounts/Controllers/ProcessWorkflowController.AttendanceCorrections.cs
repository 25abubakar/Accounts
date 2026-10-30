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
    private async Task<long?> ApplyResolvedAttendanceStatusCorrectionAsync(long reportId, CancellationToken ct)
    {
        var tenantId = _tenant.RequiredTenantId;
        string? json = null;
        var connection = (SqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqlTransaction?)_db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                SELECT report.AttendanceRequestJson
                FROM dbo.ProcessReports report
                JOIN dbo.ProcessWorkflowStatuses status ON status.Id=report.StatusId
                WHERE report.Id=@reportId AND report.TenantId=@tenantId
                  AND report.SourceModule=N'AttendanceStatusCorrection'
                  AND status.Code IN(N'APPROVED',N'RESOLVED')
                """;
            command.Parameters.AddWithValue("@reportId", reportId);
            command.Parameters.AddWithValue("@tenantId", tenantId);
            json = (await command.ExecuteScalarAsync(ct)) as string;
        }
        finally { if (shouldClose) await connection.CloseAsync(); }

        if (string.IsNullOrWhiteSpace(json)) return null;
        var details = JsonSerializer.Deserialize<AttendanceRequestDetailsDto>(json);
        if (details is null || details.Type is not ("Status" or "Check In" or "Check Out")) return null;

        var record = await _db.AttendanceRecords.SingleOrDefaultAsync(item =>
            item.TenantId == tenantId && item.PersonId == details.PersonId && item.AttendanceDate == details.Date &&
            (!details.AttendanceId.HasValue || item.Id == details.AttendanceId.Value), ct);
        if (record == null)
        {
            record = new Models.AttendanceRecord
            {
                TenantId = tenantId,
                PersonId = details.PersonId,
                AttendanceDate = details.Date,
                CreatedDate = BusinessClock.UtcNow()
            };
            _db.AttendanceRecords.Add(record);
        }

        if (details.Type == "Status")
        {
            if (details.RequestedStatusId.HasValue)
            {
                var requestedStatusId = details.RequestedStatusId.Value;
                var validStatus = await _db.PlatformSettingProcessStatuses.AsNoTracking()
                    .AnyAsync(mapping => mapping.Process.IsActive && mapping.Process.Name == "Attendance" &&
                        mapping.Status.IsActive && mapping.StatusId == requestedStatusId, ct);
                if (!validStatus)
                    throw new InvalidOperationException("The approved attendance status is no longer active in Attendance configuration.");

                record.ApprovedStatusOverrideId = requestedStatusId;
                record.CanonicalStatusId = requestedStatusId;
            }

            var checkInTime = details.ApprovedCheckInTime ?? details.RequestedCheckInTime;
            var checkOutTime = details.ApprovedCheckOutTime ?? details.RequestedCheckOutTime;

            // Backward compatibility for single legacy time:
            if (!checkInTime.HasValue && !checkOutTime.HasValue && (details.ApprovedTime ?? details.RequestedTime).HasValue)
            {
                var legacyTime = (details.ApprovedTime ?? details.RequestedTime)!.Value;
                if (!record.CheckInUtc.HasValue) checkInTime = legacyTime;
                else if (!record.CheckOutUtc.HasValue) checkOutTime = legacyTime;
            }

            if (checkInTime.HasValue)
            {
                record.CheckInUtc = details.Date.ToDateTime(checkInTime.Value);
            }

            if (checkOutTime.HasValue)
            {
                var correctedCheckOut = details.Date.ToDateTime(checkOutTime.Value);
                if (record.CheckInUtc.HasValue && correctedCheckOut <= record.CheckInUtc.Value)
                {
                    correctedCheckOut = correctedCheckOut.AddDays(1);
                }
                else if (await IsOvernightAttendanceShiftAsync(tenantId, details.PersonId, details.Date, ct) &&
                         correctedCheckOut.TimeOfDay < (record.CheckInUtc?.TimeOfDay ?? TimeSpan.FromHours(12)))
                {
                    correctedCheckOut = correctedCheckOut.AddDays(1);
                }
                record.CheckOutUtc = correctedCheckOut;
            }
        }
        else
        {
            var approvedTime = details.ApprovedTime ?? details.RequestedTime
                ?? throw new InvalidOperationException("The approved attendance punch request has no time.");
            var correctedAt = details.Date.ToDateTime(approvedTime);
            if (details.Type == "Check In")
            {
                if (record.CheckOutUtc.HasValue && correctedAt >= record.CheckOutUtc.Value && record.CheckOutUtc.Value.Date == correctedAt.Date)
                {
                    record.CheckOutUtc = record.CheckOutUtc.Value.AddDays(1);
                }
                record.CheckInUtc = correctedAt;
            }
            else
            {
                if (record.CheckInUtc.HasValue && correctedAt <= record.CheckInUtc.Value)
                {
                    correctedAt = correctedAt.AddDays(1);
                }
                record.CheckOutUtc = correctedAt;
            }

            // A punch correction must be recalculated from the approved times; it is
            // deliberately not converted into a permanent manual status override.
            record.ApprovedStatusOverrideId = null;
            record.CanonicalStatusId = null;
        }

        record.ApprovedStatusOverrideReportId = reportId;
        record.ApprovedStatusOverrideByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        record.ApprovedStatusOverrideDateUtc = BusinessClock.UtcNow();
        record.PlatformActionStatusId = null;
        record.AttendanceStatusId = null;
        record.ModifiedDate = BusinessClock.UtcNow();
        await _db.SaveChangesAsync(ct);

        await _attendanceFinalization.RefreshPeriodAsync(
            tenantId, details.Date.Year, details.Date.Month, [details.PersonId], ct);
        return record.Id;
    }

    private async Task<bool> IsOvernightAttendanceShiftAsync(
        int tenantId, Guid personId, DateOnly date, CancellationToken ct)
    {
        var staffId = await _db.StaffVacancies.AsNoTracking()
            .Where(staff => staff.TenantId == tenantId && staff.PersonId == personId)
            .Select(staff => (Guid?)staff.StaffId).FirstOrDefaultAsync(ct);
        if (!staffId.HasValue) return false;

        var daily = await _db.EmployeeTimingSchedules.AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.StaffId == staffId.Value &&
                item.ScheduleDate == date && item.IsOn)
            .Select(item => new { item.TimeFrom, item.TimeTo }).SingleOrDefaultAsync(ct);
        var from = daily?.TimeFrom;
        var to = daily?.TimeTo;
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            var mapped = await _db.AttendanceMapRules.AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.StaffId == staffId.Value)
                .Select(item => new { item.TimeFrom, item.TimeTo }).SingleOrDefaultAsync(ct);
            from = mapped?.TimeFrom;
            to = mapped?.TimeTo;
        }
        return TimeOnly.TryParse(from, out var shiftFrom) &&
               TimeOnly.TryParse(to, out var shiftTo) && shiftTo <= shiftFrom;
    }

    [HttpGet("tasks/{reportId:long}/attendance-request")]
    public async Task<IActionResult> GetAttendanceTaskDetails(long reportId, CancellationToken ct)
    {
        var result = await LoadAttendanceTaskDetailsAsync(reportId, requireCurrentFinalApprover: false, ct);
        return result == null
            ? NotFound(new { message = "Attendance request details are unavailable or you cannot view this task." })
            : Ok(new { details = result.Value.Details, canEditTime = result.Value.IsCurrentFinalApprover });
    }

    private async Task<(AttendanceRequestDetailsDto Details, bool IsCurrentFinalApprover)?> LoadAttendanceTaskDetailsAsync(
        long reportId, bool requireCurrentFinalApprover, CancellationToken ct)
    {
        var tenantId = _tenant.RequiredTenantId;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return null;
        string? json = null;
        var isFinal = false;
        var connection = (SqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqlTransaction?)_db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = """
                DECLARE @ActorStaffId uniqueidentifier;
                SELECT TOP(1) @ActorStaffId=staff.StaffId
                FROM dbo.Persons person
                JOIN dbo.StaffVacancy staff ON staff.PersonId=person.PersonId AND staff.TenantId=person.TenantId
                WHERE person.TenantId=@tenantId AND person.IdentityUserId=@userId AND person.IsActive=1;

                SELECT report.AttendanceRequestJson,
                       CONVERT(bit,CASE WHEN report.CurrentApproverStaffId=@ActorStaffId AND EXISTS
                       (SELECT 1 FROM dbo.ProcessCategoryApprovers authority
                        WHERE authority.TenantId=report.TenantId AND authority.CategoryId=report.CategoryId
                          AND authority.StaffId=@ActorStaffId) THEN 1 ELSE 0 END) AS IsCurrentFinalApprover
                FROM dbo.ProcessReports report
                WHERE report.Id=@reportId AND report.TenantId=@tenantId
                  AND report.SourceModule=N'AttendanceStatusCorrection'
                  AND (@requireFinal=0 AND
                       (report.RequesterStaffId=@ActorStaffId OR report.SubjectStaffId=@ActorStaffId OR
                        report.CurrentApproverStaffId=@ActorStaffId OR EXISTS
                        (SELECT 1 FROM dbo.ProcessReportRouteSteps route
                         WHERE route.ReportId=report.Id AND route.ApproverStaffId=@ActorStaffId))
                       OR @requireFinal=1 AND report.CurrentApproverStaffId=@ActorStaffId AND EXISTS
                        (SELECT 1 FROM dbo.ProcessCategoryApprovers authority
                         WHERE authority.TenantId=report.TenantId AND authority.CategoryId=report.CategoryId
                           AND authority.StaffId=@ActorStaffId));
                """;
            command.Parameters.AddWithValue("@reportId", reportId);
            command.Parameters.AddWithValue("@tenantId", tenantId);
            command.Parameters.AddWithValue("@userId", userId);
            command.Parameters.AddWithValue("@requireFinal", requireCurrentFinalApprover);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            json = DbString(reader, "AttendanceRequestJson");
            isFinal = reader.GetBoolean(reader.GetOrdinal("IsCurrentFinalApprover"));
        }
        finally { if (shouldClose) await connection.CloseAsync(); }

        if (string.IsNullOrWhiteSpace(json)) return null;
        var details = JsonSerializer.Deserialize<AttendanceRequestDetailsDto>(json);
        return details == null ? null : (details, isFinal);
    }

    private async Task SaveApprovedAttendanceTimeAsync(
        long reportId, AttendanceRequestDetailsDto details,
        TimeOnly? approvedTime, TimeOnly? approvedCheckInTime, TimeOnly? approvedCheckOutTime,
        CancellationToken ct)
    {
        if (approvedTime.HasValue) details.ApprovedTime = approvedTime;
        if (approvedCheckInTime.HasValue) details.ApprovedCheckInTime = approvedCheckInTime;
        if (approvedCheckOutTime.HasValue) details.ApprovedCheckOutTime = approvedCheckOutTime;
        var json = JsonSerializer.Serialize(details);
        var tenantId = _tenant.RequiredTenantId;
        var affected = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.ProcessReports SET AttendanceRequestJson={json} WHERE Id={reportId} AND TenantId={tenantId}", ct);
        if (affected != 1)
            throw new InvalidOperationException("The approved attendance request could not be updated.");
    }

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
        if (dto.Type is not ("Status" or "Check In" or "Check Out" or "Leave" or "Short Leave" or "Over Time" or "Meal Status"))
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
            AttendanceId = context.AttendanceId, CurrentStatus = context.CurrentStatus,
            CurrentCheckIn = context.CurrentCheckIn, CurrentCheckOut = context.CurrentCheckOut,
            Remarks = dto.Remarks.Trim()
        };
        if (dto.Type == "Status")
        {
            if (dto.RequestedStatusId.HasValue)
            {
                var requested = context.Statuses.FirstOrDefault(status => status.Id == dto.RequestedStatusId);
                if (requested is null) return BadRequest(new { message = "Choose an active status configured for Attendance." });
                if (string.Equals(requested.Name.Trim(), context.CurrentStatus.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    !dto.RequestedCheckInTime.HasValue && !dto.RequestedCheckOutTime.HasValue && !dto.RequestedTime.HasValue)
                    return BadRequest(new { message = "Requested status or times must differ from the current attendance record." });
                details.RequestedStatusId = requested.Id;
                details.RequestedStatus = requested.Name;
            }
            else if (!dto.RequestedCheckInTime.HasValue && !dto.RequestedCheckOutTime.HasValue && !dto.RequestedTime.HasValue)
            {
                return BadRequest(new { message = "Select a requested status or enter attendance times to correct." });
            }

            details.RequestedTime = dto.RequestedTime;
            details.RequestedCheckInTime = dto.RequestedCheckInTime;
            details.RequestedCheckOutTime = dto.RequestedCheckOutTime;
        }
        else if (dto.Type is "Check In" or "Check Out")
        {
            if (!dto.RequestedTime.HasValue)
                return BadRequest(new { message = $"Requested {dto.Type.ToLowerInvariant()} time is required." });
            details.RequestedTime = dto.RequestedTime;
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
        var timeDesc = new List<string>();
        if (details.RequestedCheckInTime.HasValue)
            timeDesc.Add($"Requested check-in: {details.RequestedCheckInTime:HH:mm}");
        if (details.RequestedCheckOutTime.HasValue)
            timeDesc.Add($"Requested check-out: {details.RequestedCheckOutTime:HH:mm}");
        if (!details.RequestedCheckInTime.HasValue && !details.RequestedCheckOutTime.HasValue && details.RequestedTime.HasValue)
            timeDesc.Add($"Related attendance time: {details.RequestedTime:HH:mm}");

        var report = new SubmitProcessReportDto
        {
            CategoryCode = dto.Type == "Leave" ? "LEAVE" : "ATTENDANCE",
            PriorityCode = "NORMAL",
            Title = $"Attendance {dto.Type} request · {context.AttendanceDate:dd MMM yyyy}",
            Description = $"Attendance date: {context.AttendanceDate:yyyy-MM-dd}\n" +
                $"Attendance ID: {context.AttendanceId?.ToString() ?? "Not yet recorded"}\n" +
                $"Current status: {context.CurrentStatus}\n" +
                $"Current check-in: {(context.CurrentCheckIn.HasValue ? context.CurrentCheckIn.Value.ToString("HH:mm") : "Not recorded")}\n" +
                $"Current check-out: {(context.CurrentCheckOut.HasValue ? context.CurrentCheckOut.Value.ToString("HH:mm") : "Not recorded")}\n" +
                $"Request type: {details.Type}\n" +
                (dto.Type == "Leave" ? $"Date From: {details.Date:yyyy-MM-dd}\nDate To: {details.DateTo:yyyy-MM-dd}\nLeave Type: {details.LeaveType}\n" :
                    dto.Type is "Check In" or "Check Out" ? $"Requested time: {details.RequestedTime:HH:mm}\n" :
                    (details.RequestedStatus != null ? $"Requested status: {details.RequestedStatus}\n" : string.Empty) +
                    (timeDesc.Count > 0 ? string.Join("\n", timeDesc) + "\n" : string.Empty)) +
                (dto.Type is "Short Leave" or "Over Time" ? $"Time From: {details.TimeFrom:HH:mm}\nTime To: {details.TimeTo:HH:mm}\nHours: {details.Minutes / 60m:0.##}\n" : string.Empty) +
                "\n" + details.Remarks,
            SourceModule = dto.Type is "Status" or "Check In" or "Check Out" ? "AttendanceStatusCorrection" : "AttendanceRequest",
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
            CurrentCheckIn = TimeOnly.TryParse(row.CheckInTime, out var checkIn) ? checkIn : null,
            CurrentCheckOut = TimeOnly.TryParse(row.CheckOutTime, out var checkOut) ? checkOut : null,
            Statuses = statuses, CanSubmit = capability?.CanSubmit == true && statuses.Count > 0,
            ReportingManagerName = capability?.ReportingManagerName,
            Reason = statuses.Count == 0 ? "No active attendance statuses are configured." : capability?.Reason
        }, null);
    }
}
