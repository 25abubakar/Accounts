using Accounts.DTOs;
using Accounts.Idempotency;
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
        [FromQuery] int? categoryId,
        [FromQuery] int? companyId,
        [FromQuery] int? platformId,
        [FromQuery] int? productCategoryId,
        [FromQuery] bool activeOnly = false,
        CancellationToken ct = default)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        return Ok(ApiResponse<IReadOnlyList<SaleRoznamchaInventoryRow>>.Ok(
            await service.ListAsync(categoryId, companyId, platformId, productCategoryId, activeOnly, ct)));
    }

    [HttpGet("hierarchy")]
    public async Task<IActionResult> Hierarchy(CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        return Ok(ApiResponse<IReadOnlyList<SaleRoznamchaMasterRow>>.Ok(
            await masters.ListAsync(null, null, true, ct)));
    }

    [HttpGet("currencies")]
    public async Task<IActionResult> Currencies(CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        return Ok(ApiResponse<IReadOnlyList<SaleRoznamchaCurrencyRow>>.Ok(
            await service.ListCurrenciesAsync(ct)));
    }

    [HttpPost]
    [Idempotent]
    public async Task<IActionResult> Create(SaveSaleRoznamchaInventoryRequest request, CancellationToken ct)
    {
        var denied = await Guard("ADD", ct); if (denied != null) return denied;
        return await Run(async () => ApiResponse<SaleRoznamchaInventoryRow>.Ok(
            await service.SaveAsync(null, request, ct), "Inventory product added successfully."));
    }

    [HttpPut("{id:long}")]
    [Idempotent]
    public async Task<IActionResult> Update(long id, SaveSaleRoznamchaInventoryRequest request, CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        return await Run(async () => ApiResponse<SaleRoznamchaInventoryRow>.Ok(
            await service.SaveAsync(id, request, ct), "Inventory product updated successfully."));
    }

    [HttpPost("{id:long}/stock")]
    [Idempotent]
    public async Task<IActionResult> AddStock(long id, AddSaleRoznamchaStockRequest request, CancellationToken ct)
    {
        var denied = await Guard("ADD", ct); if (denied != null) return denied;
        return await Run(async () => ApiResponse<SaleRoznamchaInventoryRow>.Ok(
            await service.AddStockAsync(id, request, ct), "Stock purchase added successfully."));
    }

    [HttpDelete("{id:long}")]
    [Idempotent]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var denied = await Guard("DELETE", ct); if (denied != null) return denied;
        return await Run(async () =>
        {
            await service.DeleteAsync(id, ct);
            return ApiResponse<object?>.Ok(null, "Inventory product removed successfully.");
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
