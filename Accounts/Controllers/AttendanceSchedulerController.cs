using Accounts.Data;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Accounts.Controllers;

[ApiController]
[Route("api/internal/scheduler/attendance")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[EnableRateLimiting("scheduler")]
public sealed class AttendanceSchedulerController : ControllerBase
{
    public const string ApiKeyHeaderName = "X-Scheduler-Key";
    private const int MaximumEvaluationDays = 31;
    private static readonly SemaphoreSlim ExecutionGate = new(1, 1);

    private readonly ApplicationDbContext _db;
    private readonly IAttendanceService _attendance;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AttendanceSchedulerController> _logger;

    public AttendanceSchedulerController(
        ApplicationDbContext db,
        IAttendanceService attendance,
        IConfiguration configuration,
        ILogger<AttendanceSchedulerController> logger)
    {
        _db = db;
        _attendance = attendance;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        var authenticationFailure = AuthenticateScheduler();
        if (authenticationFailure != null) return authenticationFailure;

        return Ok(new
        {
            success = true,
            service = "attendance-evaluator",
            pakistanTime = PakistanClock.Now(),
            utcTime = DateTime.UtcNow
        });
    }

    [HttpPost("evaluate")]
    public async Task<IActionResult> Evaluate(
        [FromBody] AttendanceSchedulerEvaluationRequest? request,
        CancellationToken cancellationToken)
    {
        var authenticationFailure = AuthenticateScheduler();
        if (authenticationFailure != null) return authenticationFailure;

        request ??= new AttendanceSchedulerEvaluationRequest();
        var asOfPakistanLocal = PakistanClock.Now();
        var currentDate = DateOnly.FromDateTime(asOfPakistanLocal);
        var dateFrom = request.DateFrom ?? currentDate.AddDays(-1);
        var dateTo = request.DateTo ?? currentDate;

        if (request.TenantId is <= 0)
            return BadRequest(new { success = false, code = "INVALID_TENANT", message = "TenantId must be a positive integer." });
        if (dateFrom > dateTo)
            return BadRequest(new { success = false, code = "INVALID_DATE_RANGE", message = "DateFrom cannot be after DateTo." });
        if (dateTo > currentDate)
            return BadRequest(new { success = false, code = "FUTURE_DATE_NOT_ALLOWED", message = "Attendance cannot be evaluated for a future Pakistan date." });
        if (dateTo.DayNumber - dateFrom.DayNumber + 1 > MaximumEvaluationDays)
            return BadRequest(new
            {
                success = false,
                code = "DATE_RANGE_TOO_LARGE",
                message = $"A scheduler run can evaluate at most {MaximumEvaluationDays} days."
            });

        if (!await ExecutionGate.WaitAsync(0, cancellationToken))
            return Conflict(new
            {
                success = false,
                code = "ATTENDANCE_EVALUATION_ALREADY_RUNNING",
                message = "Another attendance evaluation is already running."
            });

        var executionId = Guid.NewGuid();
        var startedAtUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var tenantQuery = _db.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(tenant => tenant.IsActive);

            if (request.TenantId.HasValue)
                tenantQuery = tenantQuery.Where(tenant => tenant.Id == request.TenantId.Value);

            var tenantIds = await tenantQuery
                .OrderBy(tenant => tenant.Id)
                .Select(tenant => tenant.Id)
                .ToListAsync(cancellationToken);

            if (request.TenantId.HasValue && tenantIds.Count == 0)
                return NotFound(new
                {
                    success = false,
                    code = "TENANT_NOT_FOUND",
                    message = "The requested active tenant was not found."
                });

            var succeededTenantIds = new List<int>(tenantIds.Count);
            var failures = new List<object>();

            foreach (var tenantId in tenantIds)
            {
                try
                {
                    await _attendance.EvaluateStatusesAsync(
                        tenantId,
                        dateFrom,
                        dateTo,
                        cancellationToken,
                        asOfPakistanLocal);
                    succeededTenantIds.Add(tenantId);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Attendance scheduler run {ExecutionId} failed for tenant {TenantId}.",
                        executionId,
                        tenantId);
                    failures.Add(new
                    {
                        tenantId,
                        code = "TENANT_EVALUATION_FAILED"
                    });
                }
            }

            stopwatch.Stop();
            var response = new
            {
                success = failures.Count == 0,
                executionId,
                startedAtUtc,
                completedAtUtc = DateTime.UtcNow,
                durationMilliseconds = stopwatch.ElapsedMilliseconds,
                asOfPakistanLocal,
                dateFrom,
                dateTo,
                requestedTenantId = request.TenantId,
                tenantsFound = tenantIds.Count,
                tenantsEvaluated = succeededTenantIds.Count,
                succeededTenantIds,
                failures
            };

            return failures.Count == 0
                ? Ok(response)
                : StatusCode(StatusCodes.Status500InternalServerError, response);
        }
        finally
        {
            ExecutionGate.Release();
        }
    }

    private IActionResult? AuthenticateScheduler()
    {
        var expectedKey = _configuration["Scheduler:ApiKey"];
        if (string.IsNullOrWhiteSpace(expectedKey) || expectedKey.Length < 32)
        {
            _logger.LogError("Scheduler API is disabled because Scheduler:ApiKey is missing or shorter than 32 characters.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                success = false,
                code = "SCHEDULER_API_NOT_CONFIGURED",
                message = "Scheduler API authentication is not securely configured."
            });
        }

        var suppliedKey = Request.Headers[ApiKeyHeaderName].ToString();
        if (string.IsNullOrWhiteSpace(suppliedKey) || !KeysMatch(expectedKey, suppliedKey))
        {
            Response.Headers.WWWAuthenticate = "ApiKey";
            return Unauthorized(new
            {
                success = false,
                code = "INVALID_SCHEDULER_KEY",
                message = "Scheduler authentication failed."
            });
        }

        return null;
    }

    private static bool KeysMatch(string expected, string supplied)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }
}

public sealed class AttendanceSchedulerEvaluationRequest
{
    public int? TenantId { get; set; }
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
}
