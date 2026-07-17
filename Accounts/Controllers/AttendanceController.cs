using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController, Route("api/attendance"), Authorize]
public sealed class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _service;
    public AttendanceController(IAttendanceService service) => _service = service;

    [HttpGet("me/today")]
    public Task<IActionResult> Today(CancellationToken ct) => Execute(() => _service.GetTodayAsync(UserId(), ct));

    [HttpPost("me/check-in")]
    public Task<IActionResult> CheckIn([FromQuery] int? workModeId, CancellationToken ct) => Execute(() => _service.CheckInAsync(UserId(), workModeId, ct));

    [HttpGet("work-modes")]
    public async Task<IActionResult> WorkModes([FromServices] Accounts.Data.ApplicationDbContext db, CancellationToken ct) =>
        Ok(await db.AttendanceWorkModes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct));

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

    [HttpGet("report/monthly")]
    public Task<IActionResult> MonthlyReport([FromQuery] int year, [FromQuery] int month, [FromQuery] Guid? personId, CancellationToken ct) =>
        Execute(() => _service.GetMonthlyReportAsync(UserId(), CanViewOthers(), personId, year, month, ct));

    [HttpGet("report/daily")]
    public Task<IActionResult> DailyReport([FromQuery] DateOnly dateFrom, [FromQuery] DateOnly dateTo, CancellationToken ct) =>
        Execute(() => _service.GetDailyReportAsync(UserId(), CanViewOrganization(), dateFrom, dateTo, ct));

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    private bool CanViewOthers() => User.IsInRole("SuperAdmin") || User.IsInRole("Admin") || User.HasClaim(ITenantService.ClaimIsTenantAdmin, "true");
    private bool CanViewOrganization() => User.IsInRole("SuperAdmin") || User.IsInRole("Admin") || User.HasClaim(ITenantService.ClaimIsTenantAdmin, "true");
    private async Task<IActionResult> Execute<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (ArgumentOutOfRangeException ex) { return BadRequest(new { message = ex.Message }); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }
}
