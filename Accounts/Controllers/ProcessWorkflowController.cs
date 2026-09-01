using Accounts.Data;
using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Accounts.Idempotency;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Claims;

namespace Accounts.Controllers;

[ApiController]
[Route("api/process-workflow")]
[Authorize]
[ProcessWorkflowExceptionFilter]
public sealed class ProcessWorkflowController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;
    private readonly IRealtimePublisher _realtime;

    public ProcessWorkflowController(
        ApplicationDbContext db,
        ITenantService tenant,
        IRealtimePublisher realtime)
    {
        _db = db;
        _tenant = tenant;
        _realtime = realtime;
    }

    [HttpGet("lookups")]
    public async Task<IActionResult> GetLookups(CancellationToken ct)
    {
        var rows = new List<ProcessLookupDto>();
        await WithCommandAsync("dbo.usp_ProcessReport_Lookups", async command =>
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new ProcessLookupDto
                {
                    LookupType = reader.GetString(reader.GetOrdinal("LookupType")),
                    Code = reader.GetString(reader.GetOrdinal("Code")),
                    Name = reader.GetString(reader.GetOrdinal("Name")),
                    ColorCode = DbString(reader, "ColorCode"),
                    DisplayOrder = reader.GetInt32(reader.GetOrdinal("DisplayOrder")),
                    RequiresComments = DbBoolean(reader, "RequiresComments")
                });
            }
        }, ct);
        return Ok(rows);
    }

    [HttpGet("submission-capability")]
    public async Task<IActionResult> GetSubmissionCapability(CancellationToken ct)
    {
        ProcessReportSubmissionCapabilityDto? result = null;
        await WithCommandAsync("dbo.usp_ProcessReport_SubmissionCapability", async command =>
        {
            AddScope(command);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return;

            var managerStaffOrdinal = reader.GetOrdinal("ReportingManagerStaffId");
            result = new ProcessReportSubmissionCapabilityDto
            {
                CanSubmit = reader.GetBoolean(reader.GetOrdinal("CanSubmit")),
                Reason = DbString(reader, "Reason"),
                ReportingManagerStaffId = reader.IsDBNull(managerStaffOrdinal)
                    ? null
                    : reader.GetGuid(managerStaffOrdinal),
                ReportingManagerName = DbString(reader, "ReportingManagerName")
            };
        }, ct);

        return Ok(result ?? new ProcessReportSubmissionCapabilityDto
        {
            CanSubmit = false,
            Reason = "No active reporting manager is configured for this staff member."
        });
    }

    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasks([FromQuery] string mode = "INBOX", CancellationToken ct = default)
    {
        var normalizedMode = mode.Trim().ToUpperInvariant();
        if (normalizedMode is not ("INBOX" or "MINE" or "COMPLETED"))
            return BadRequest(new { message = "Unsupported task-list mode." });

        if (!await HasActiveStaffProfileAsync(ct))
            return Ok(Array.Empty<ProcessReportListDto>());

        var rows = new List<ProcessReportListDto>();
        await WithCommandAsync("dbo.usp_ProcessReport_List", async command =>
        {
            AddScope(command);
            command.Parameters.Add("@Mode", SqlDbType.NVarChar, 20).Value = normalizedMode;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add(MapReport(reader));
        }, ct);
        return Ok(rows);
    }

    private async Task<bool> HasActiveStaffProfileAsync(CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || _tenant.IsSuperAdmin) return false;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return false;

        return await _db.Persons.AsNoTracking().AnyAsync(person =>
            person.TenantId == _tenant.TenantId.Value &&
            person.IdentityUserId == userId &&
            person.IsActive &&
            person.Staff != null, ct);
    }

    [HttpGet("tasks/{reportId:long}/timeline")]
    public async Task<IActionResult> GetTimeline(long reportId, CancellationToken ct)
    {
        var steps = new List<ProcessRouteStepDto>();
        var actions = new List<ProcessActionHistoryDto>();
        await WithCommandAsync("dbo.usp_ProcessReport_Timeline", async command =>
        {
            AddScope(command);
            command.Parameters.Add("@ReportId", SqlDbType.BigInt).Value = reportId;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                steps.Add(new ProcessRouteStepDto
                {
                    Id = reader.GetInt64(reader.GetOrdinal("Id")),
                    StepOrder = reader.GetInt32(reader.GetOrdinal("StepOrder")),
                    ApproverName = reader.GetString(reader.GetOrdinal("ApproverName")),
                    ApproverNumber = DbString(reader, "ApproverNumber"),
                    StatusCode = reader.GetString(reader.GetOrdinal("StatusCode")),
                    StatusName = reader.GetString(reader.GetOrdinal("StatusName")),
                    StatusColor = reader.GetString(reader.GetOrdinal("StatusColor")),
                    AssignedDateUtc = DbDate(reader, "AssignedDateUtc"),
                    ActedDateUtc = DbDate(reader, "ActedDateUtc"),
                    IsCurrent = reader.GetBoolean(reader.GetOrdinal("IsCurrent"))
                });
            }
            if (await reader.NextResultAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    actions.Add(new ProcessActionHistoryDto
                    {
                        Id = reader.GetInt64(reader.GetOrdinal("Id")),
                        ActionCode = reader.GetString(reader.GetOrdinal("ActionCode")),
                        ActionName = reader.GetString(reader.GetOrdinal("ActionName")),
                        ActorName = reader.GetString(reader.GetOrdinal("ActorName")),
                        Comments = DbString(reader, "Comments"),
                        ActionDateUtc = reader.GetDateTime(reader.GetOrdinal("ActionDateUtc")),
                        ToStatusCode = reader.GetString(reader.GetOrdinal("ToStatusCode")),
                        ToStatusName = reader.GetString(reader.GetOrdinal("ToStatusName")),
                        ToStatusColor = reader.GetString(reader.GetOrdinal("ToStatusColor"))
                    });
                }
            }
        }, ct);
        return Ok(new { steps, actions });
    }

    [HttpPost("reports")]
    [Idempotent]
    public async Task<IActionResult> Submit([FromBody] SubmitProcessReportDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Title) || string.IsNullOrWhiteSpace(dto.Description) ||
            string.IsNullOrWhiteSpace(dto.CategoryCode) || string.IsNullOrWhiteSpace(dto.PriorityCode))
            return BadRequest(new { message = "Category, priority, title and description are required." });
        if (dto.Title.Trim().Length > 200)
            return BadRequest(new { message = "Title cannot exceed 200 characters." });

        object? result = null;
        await WithCommandAsync("dbo.usp_ProcessReport_Submit", async command =>
        {
            AddScope(command);
            command.Parameters.Add("@SubjectStaffId", SqlDbType.UniqueIdentifier).Value =
                dto.SubjectStaffId.HasValue ? dto.SubjectStaffId.Value : DBNull.Value;
            command.Parameters.Add("@CategoryCode", SqlDbType.NVarChar, 50).Value = dto.CategoryCode.Trim();
            command.Parameters.Add("@PriorityCode", SqlDbType.NVarChar, 30).Value = dto.PriorityCode.Trim();
            command.Parameters.Add("@Title", SqlDbType.NVarChar, 200).Value = dto.Title.Trim();
            command.Parameters.Add("@Description", SqlDbType.NVarChar, -1).Value = dto.Description.Trim();
            command.Parameters.Add("@SourceModule", SqlDbType.NVarChar, 80).Value =
                string.IsNullOrWhiteSpace(dto.SourceModule) ? DBNull.Value : dto.SourceModule.Trim();
            command.Parameters.Add("@SourceRecordId", SqlDbType.NVarChar, 100).Value =
                string.IsNullOrWhiteSpace(dto.SourceRecordId) ? DBNull.Value : dto.SourceRecordId.Trim();
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                result = new
                {
                    id = reader.GetInt64(reader.GetOrdinal("Id")),
                    requestNumber = DbString(reader, "RequestNumber"),
                    rowVersion = reader.GetString(reader.GetOrdinal("RowVersion"))
                };
        }, ct);
        await PublishProcessChangedAsync("submitted");
        return Ok(result);
    }

    [HttpPost("tasks/{reportId:long}/actions")]
    [Idempotent]
    public async Task<IActionResult> Act(long reportId, [FromBody] ProcessReportActionDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ActionCode) || string.IsNullOrWhiteSpace(dto.RowVersion))
            return BadRequest(new { message = "Action and row version are required." });
        if (!TryNormalizeRowVersion(dto.RowVersion, out var rowVersionHex))
            return Conflict(new
            {
                message = "This task contains an outdated version token. Refresh the task list and try again."
            });

        object? result = null;
        var failure = await TryWithCommandAsync("dbo.usp_ProcessReport_Action", async command =>
        {
            AddScope(command);
            command.Parameters.Add("@ReportId", SqlDbType.BigInt).Value = reportId;
            command.Parameters.Add("@ActionCode", SqlDbType.NVarChar, 40).Value = dto.ActionCode.Trim();
            command.Parameters.Add("@Comments", SqlDbType.NVarChar, 2000).Value =
                string.IsNullOrWhiteSpace(dto.Comments) ? DBNull.Value : dto.Comments.Trim();
            command.Parameters.Add("@ExpectedRowVersionHex", SqlDbType.VarChar, 24).Value = rowVersionHex;
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                result = new
                {
                    id = reader.GetInt64(reader.GetOrdinal("Id")),
                    requestNumber = DbString(reader, "RequestNumber"),
                    statusCode = reader.GetString(reader.GetOrdinal("StatusCode")),
                    statusName = reader.GetString(reader.GetOrdinal("StatusName")),
                    rowVersion = reader.GetString(reader.GetOrdinal("RowVersion"))
                };
        }, ct);

        if (failure is not null)
        {
            return failure.SqlErrorNumber == 51232
                ? Conflict(new { message = failure.Message })
                : BadRequest(new { message = failure.Message });
        }

        await PublishProcessChangedAsync(dto.ActionCode.Trim().ToLowerInvariant(), reportId);
        return Ok(result);
    }

    private Task PublishProcessChangedAsync(string action, long? reportId = null) =>
        _realtime.PublishEventToTenantAsync(
            _tenant.RequiredTenantId,
            RealtimeEventDto.Create(
                RealtimeEventTypes.ProcessChanged,
                "process",
                action,
                _tenant.RequiredTenantId,
                reportId?.ToString()));

    private static bool TryNormalizeRowVersion(string value, out string normalized)
    {
        normalized = value.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[2..];

        if (normalized.Length != 16 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = normalized.ToUpperInvariant();
        return true;
    }

    private void AddScope(SqlCommand command)
    {
        if (!_tenant.TenantId.HasValue || _tenant.IsSuperAdmin)
            throw new InvalidOperationException("Process reports require an active tenant staff account.");
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            throw new UnauthorizedAccessException("Not authenticated.");
        command.Parameters.Add("@TenantId", SqlDbType.Int).Value = _tenant.TenantId.Value;
        command.Parameters.Add("@ActorUserId", SqlDbType.NVarChar, 450).Value = userId;
    }

    private async Task WithCommandAsync(
        string procedure,
        Func<SqlCommand, Task> execute,
        CancellationToken ct)
    {
        if (!_db.Database.IsSqlServer())
            throw new InvalidOperationException("Process workflow procedures require SQL Server.");

        var connection = (SqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = procedure;
            command.CommandType = CommandType.StoredProcedure;
            command.CommandTimeout = 30;
            await execute(command);
        }
        catch (SqlException ex) when (ex.Number is >= 51200 and <= 51299)
        {
            throw new ProcessWorkflowException(ex.Message, ex.Number, ex);
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private async Task<ProcessWorkflowFailure?> TryWithCommandAsync(
        string procedure,
        Func<SqlCommand, Task> execute,
        CancellationToken ct)
    {
        if (!_db.Database.IsSqlServer())
            throw new InvalidOperationException("Process workflow procedures require SQL Server.");

        var connection = (SqlConnection)_db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = procedure;
            command.CommandType = CommandType.StoredProcedure;
            command.CommandTimeout = 30;
            await execute(command);
            return null;
        }
        catch (SqlException ex) when (ex.Number is >= 51200 and <= 51299)
        {

            return new ProcessWorkflowFailure(ex.Message, ex.Number);
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static ProcessReportListDto MapReport(SqlDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("Id")),
        RequestNumber = DbString(reader, "RequestNumber"),
        Title = reader.GetString(reader.GetOrdinal("Title")),
        Description = reader.GetString(reader.GetOrdinal("Description")),
        SourceModule = DbString(reader, "SourceModule"),
        SourceRecordId = DbString(reader, "SourceRecordId"),
        CategoryCode = reader.GetString(reader.GetOrdinal("CategoryCode")),
        CategoryName = reader.GetString(reader.GetOrdinal("CategoryName")),
        PriorityCode = reader.GetString(reader.GetOrdinal("PriorityCode")),
        PriorityName = reader.GetString(reader.GetOrdinal("PriorityName")),
        PriorityColor = reader.GetString(reader.GetOrdinal("PriorityColor")),
        StatusCode = reader.GetString(reader.GetOrdinal("StatusCode")),
        StatusName = reader.GetString(reader.GetOrdinal("StatusName")),
        StatusColor = reader.GetString(reader.GetOrdinal("StatusColor")),
        IsTerminal = reader.GetBoolean(reader.GetOrdinal("IsTerminal")),
        RequesterStaffId = reader.GetGuid(reader.GetOrdinal("RequesterStaffId")),
        RequesterName = reader.GetString(reader.GetOrdinal("RequesterName")),
        RequesterNumber = DbString(reader, "RequesterNumber"),
        SubjectStaffId = reader.GetGuid(reader.GetOrdinal("SubjectStaffId")),
        SubjectName = reader.GetString(reader.GetOrdinal("SubjectName")),
        SubjectNumber = DbString(reader, "SubjectNumber"),
        CurrentApproverName = DbString(reader, "CurrentApproverName"),
        IsRequester = DbBoolean(reader, "IsRequester"),
        IsFinalApprover = DbBoolean(reader, "IsFinalApprover"),
        CreatedDateUtc = reader.GetDateTime(reader.GetOrdinal("CreatedDateUtc")),
        ModifiedDateUtc = DbDate(reader, "ModifiedDateUtc"),
        CompletedDateUtc = DbDate(reader, "CompletedDateUtc"),
        RowVersion = reader.GetString(reader.GetOrdinal("RowVersion"))
    };

    private static string? DbString(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTime? DbDate(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }

    private static bool DbBoolean(SqlDataReader reader, string name)
    {
        for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
        {
            if (!reader.GetName(ordinal).Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            return !reader.IsDBNull(ordinal) && reader.GetBoolean(ordinal);
        }

        return false;
    }
}

public sealed record ProcessWorkflowFailure(string Message, int SqlErrorNumber);

public sealed class ProcessWorkflowException : Exception
{
    public int SqlErrorNumber { get; }
    public ProcessWorkflowException(string message, int sqlErrorNumber, Exception inner)
        : base(message, inner) => SqlErrorNumber = sqlErrorNumber;
}

public sealed class ProcessWorkflowExceptionFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not ProcessWorkflowException error) return;
        context.Result = new ObjectResult(new { message = error.Message })
        {
            StatusCode = error.SqlErrorNumber == 51232 ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest
        };
        context.ExceptionHandled = true;
    }
}
