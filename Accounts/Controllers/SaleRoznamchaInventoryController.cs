using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/sale-roznamcha/inventory")]
public sealed class SaleRoznamchaInventoryController(
    ISaleRoznamchaInventoryService service,
    ISaleRoznamchaMasterService masters,
    ISaleRoznamchaAccessService access) : ControllerBase
{
    private const string RoutePath = "/sale-roznamcha/inventory";

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? companyId,
        [FromQuery] int? platformId,
        [FromQuery] int? productCategoryId,
        [FromQuery] bool activeOnly = false,
        CancellationToken ct = default)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        return Ok(ApiResponse<IReadOnlyList<SaleRoznamchaInventoryRow>>.Ok(
            await service.ListAsync(companyId, platformId, productCategoryId, activeOnly, ct)));
    }

    [HttpGet("hierarchy")]
    public async Task<IActionResult> Hierarchy(CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        return Ok(ApiResponse<IReadOnlyList<SaleRoznamchaMasterRow>>.Ok(
            await masters.ListAsync(null, null, true, ct)));
    }

    private async Task<IActionResult?> Guard(string action, CancellationToken ct)
        => await access.CanAsync(User, RoutePath, action, ct) ? null : Forbid();
}
