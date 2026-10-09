using Accounts.DTOs;
using Accounts.Idempotency;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/sale-roznamcha/daily-sale")]
public sealed class SaleRoznamchaDailySaleController(
    ISaleRoznamchaInventoryService inventory,
    ISaleRoznamchaDailySaleService sales,
    ISaleRoznamchaMasterService masters,
    ISaleRoznamchaAccessService access) : ControllerBase
{
    private const string RoutePath = "/sale-roznamcha/daily-sale";

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? categoryId,
        [FromQuery] int? companyId,
        [FromQuery] int? platformId,
        [FromQuery] int? productCategoryId,
        CancellationToken ct = default)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        return Ok(ApiResponse<IReadOnlyList<SaleRoznamchaDailySaleRow>>.Ok(
            await sales.ListAsync(categoryId, companyId, platformId, productCategoryId, ct)));
    }

    // Active inventory picker used by the Daily Sale registration form.
    [HttpGet("products")]
    public async Task<IActionResult> Products(
        [FromQuery] int? categoryId,
        [FromQuery] int? companyId,
        [FromQuery] int? platformId,
        [FromQuery] int? productCategoryId,
        CancellationToken ct = default)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        return Ok(ApiResponse<IReadOnlyList<SaleRoznamchaInventoryRow>>.Ok(
            await inventory.ListAsync(categoryId, companyId, platformId, productCategoryId, true, ct)));
    }

    [HttpGet("hierarchy")]
    public async Task<IActionResult> Hierarchy(CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        return Ok(ApiResponse<IReadOnlyList<SaleRoznamchaMasterRow>>.Ok(
            await masters.ListAsync(null, null, true, ct)));
    }

    [HttpGet("statuses")]
    public async Task<IActionResult> Statuses(CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        return Ok(ApiResponse<IReadOnlyList<SaleRoznamchaSaleStatusRow>>.Ok(
            await sales.StatusesAsync(ct)));
    }

    [HttpPost]
    [Idempotent]
    public async Task<IActionResult> Create(CreateSaleRoznamchaDailySaleRequest request, CancellationToken ct)
    {
        var denied = await Guard("ADD", ct); if (denied != null) return denied;
        try
        {
            return Ok(ApiResponse<SaleRoznamchaDailySaleRow>.Ok(
                await sales.CreateAsync(request, ct), "Daily sale saved and inventory updated successfully."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object?>.Fail(ex.Message));
        }
    }

    private async Task<IActionResult?> Guard(string action, CancellationToken ct)
        => await access.CanAsync(User, RoutePath, action, ct) ? null : Forbid();
}
