using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers;

[ApiController]
[Route("api/attendance-status")]
[Authorize]
[Produces("application/json")]
public sealed class AttendanceStatusController : ControllerBase
{
    private readonly IAttendanceStatusService _service;
    private readonly TenantPermissionService _tenantPermissions;

    public AttendanceStatusController(
        IAttendanceStatusService service,
        TenantPermissionService tenantPermissions)
    {
        _service = service;
        _tenantPermissions = tenantPermissions;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AttendanceStatusDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AttendanceStatusDto>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await _service.GetAllAsync(cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AttendanceStatusDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(id, cancellationToken);
        return result is null ? NotFound(new { message = "Attendance status was not found." }) : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<AttendanceStatusDto>> Create(CreateAttendanceStatusDto dto, CancellationToken cancellationToken)
    {
        if (!await CanManageAsync("ADD", cancellationToken)) return Forbid();
        try
        {
            var result = await _service.CreateAsync(dto, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (DuplicateAttendanceStatusException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AttendanceStatusDto>> Update(int id, UpdateAttendanceStatusDto dto, CancellationToken cancellationToken)
    {
        if (!await CanManageAsync("EDIT", cancellationToken)) return Forbid();
        try
        {
            var result = await _service.UpdateAsync(id, dto, cancellationToken);
            return result is null ? NotFound(new { message = "Attendance status was not found." }) : Ok(result);
        }
        catch (DuplicateAttendanceStatusException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        if (!await CanManageAsync("DELETE", cancellationToken)) return Forbid();
        return await _service.DeactivateAsync(id, cancellationToken)
            ? Ok(new { message = "Attendance status deactivated successfully." })
            : NotFound(new { message = "Attendance status was not found." });
    }

    private Task<bool> CanManageAsync(string action, CancellationToken cancellationToken) =>
        _tenantPermissions.HasMenuRouteAsync(
            User,
            ["/attendance/rules/list", "/attendance/rules/rule", "/attendance/rules"],
            action,
            cancellationToken);
}
