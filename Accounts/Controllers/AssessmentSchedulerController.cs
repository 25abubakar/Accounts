using Accounts.Services;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Accounts.Controllers;

[ApiController]
[Route("api/internal/scheduler/assessment")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[EnableRateLimiting("scheduler")]
public sealed class AssessmentSchedulerController : ControllerBase
{
    private readonly AssessmentSchedulerService _scheduler;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AssessmentSchedulerController> _logger;

    public AssessmentSchedulerController(
        AssessmentSchedulerService scheduler,
        IConfiguration configuration,
        ILogger<AssessmentSchedulerController> logger)
    {
        _scheduler = scheduler;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("evaluate")]
    public async Task<IActionResult> Evaluate(CancellationToken cancellationToken)
    {
        var authenticationFailure = AuthenticateScheduler();
        if (authenticationFailure != null) return authenticationFailure;

        var executionId = Guid.NewGuid();
        var startedAtUtc = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var result = await _scheduler.RunNowAsync(cancellationToken);
        stopwatch.Stop();

        if (result.SkippedBecauseAlreadyRunning)
            return Conflict(new
            {
                success = false,
                code = "ASSESSMENT_EVALUATION_ALREADY_RUNNING",
                message = "Another assessment evaluation is already running."
            });

        return Ok(new
        {
            success = true,
            executionId,
            startedAtUtc,
            completedAtUtc = DateTime.UtcNow,
            durationMilliseconds = stopwatch.ElapsedMilliseconds,
            asOfPakistanLocal = PakistanClock.Now(),
            result.ActiveTenants,
            result.OpenTenants,
            result.GeneratedRows,
            result.RemindersCreated
        });
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

        var suppliedKey = Request.Headers[AttendanceSchedulerController.ApiKeyHeaderName].ToString();
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
