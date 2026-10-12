using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/sale-roznamcha/settings")]
public sealed class SaleRoznamchaSettingsController(
    ISaleRoznamchaSettingsService service,
    ISaleRoznamchaAccessService access) : ControllerBase
{
    private const string RoutePath = "/sale-roznamcha/settings";

    [HttpGet("{settingType}")]
    public async Task<IActionResult> List(string settingType, CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        return await Run(async () => ApiResponse<IReadOnlyList<SaleRoznamchaSettingRow>>.Ok(
            await service.ListAsync(settingType, ct)));
    }

    [HttpPost("{settingType}")]
    public async Task<IActionResult> Create(
        string settingType,
        SaveSaleRoznamchaSettingRequest request,
        CancellationToken ct)
    {
        var denied = await Guard("ADD", ct); if (denied != null) return denied;
        return await Run(async () => ApiResponse<SaleRoznamchaSettingRow>.Ok(
            await service.SaveAsync(settingType, null, request, ct),
            "Setting added successfully."));
    }

    [HttpPut("{settingType}/{id:int}")]
    public async Task<IActionResult> Update(
        string settingType,
        int id,
        SaveSaleRoznamchaSettingRequest request,
        CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        return await Run(async () => ApiResponse<SaleRoznamchaSettingRow>.Ok(
            await service.SaveAsync(settingType, id, request, ct),
            "Setting updated successfully."));
    }

    [HttpDelete("{settingType}/{id:int}")]
    public async Task<IActionResult> Deactivate(string settingType, int id, CancellationToken ct)
    {
        var denied = await Guard("DELETE", ct); if (denied != null) return denied;
        return await Run(async () =>
        {
            await service.DeactivateAsync(settingType, id, ct);
            return ApiResponse<object?>.Ok(null, "Setting deactivated successfully.");
        });
    }

    private async Task<IActionResult> Run<T>(Func<Task<ApiResponse<T>>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }

    private async Task<IActionResult?> Guard(string action, CancellationToken ct)
        => await access.CanAsync(User, RoutePath, action, ct) ? null : Forbid();
}
