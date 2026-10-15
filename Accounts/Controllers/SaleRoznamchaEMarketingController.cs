using Accounts.DTOs;
using Accounts.Idempotency;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/sale-roznamcha/e-marketing")]
public sealed class SaleRoznamchaEMarketingController(
    ISaleRoznamchaMasterService service,
    ISaleRoznamchaInventoryService inventory,
    ISaleRoznamchaAccessService access) : ControllerBase
{
    private const string RoutePath = "/sale-roznamcha/e-marketing";

    [HttpGet("hierarchy")]
    public async Task<IActionResult> Hierarchy(
        [FromQuery] bool activeOnly = false,
        CancellationToken ct = default)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        return Ok(ApiResponse<IReadOnlyList<SaleRoznamchaMasterRow>>.Ok(
            await service.ListAsync(null, null, activeOnly, ct)));
    }

    [HttpGet("products")]
    public async Task<IActionResult> Products(
        [FromQuery] int? companyId,
        [FromQuery] int? platformId,
        [FromQuery] int? productCategoryId,
        [FromQuery] bool activeOnly = false,
        CancellationToken ct = default)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        return Ok(ApiResponse<IReadOnlyList<SaleRoznamchaInventoryRow>>.Ok(
            await inventory.ListAsync(companyId, platformId, productCategoryId, activeOnly, ct)));
    }

    [HttpPost("products")]
    [Idempotent]
    public async Task<IActionResult> CreateProduct(
        [FromBody] SaveSaleRoznamchaProductRequest request,
        CancellationToken ct)
    {
        var denied = await Guard("ADD", ct); if (denied != null) return denied;
        return await Run(async () => ApiResponse<SaleRoznamchaInventoryRow>.Ok(
            await inventory.SaveProductAsync(null, request, ct),
            "Product added successfully."));
    }

    [HttpPut("products/{id:long}")]
    [Idempotent]
    public async Task<IActionResult> UpdateProduct(
        long id,
        [FromBody] SaveSaleRoznamchaProductRequest request,
        CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        return await Run(async () => ApiResponse<SaleRoznamchaInventoryRow>.Ok(
            await inventory.SaveProductAsync(id, request, ct),
            "Product updated successfully."));
    }

    [HttpDelete("products/{id:long}")]
    [Idempotent]
    public async Task<IActionResult> DeleteProduct(long id, CancellationToken ct)
    {
        var denied = await Guard("DELETE", ct); if (denied != null) return denied;
        return await Run(async () =>
        {
            await inventory.DeleteProductAsync(id, ct);
            return ApiResponse<object?>.Ok(null, "Product removed successfully.");
        });
    }

    [HttpGet("{entityType}")]
    public async Task<IActionResult> List(
        string entityType,
        [FromQuery] int? parentId,
        [FromQuery] bool activeOnly = false,
        CancellationToken ct = default)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        return await Run(async () => ApiResponse<IReadOnlyList<SaleRoznamchaMasterRow>>.Ok(
            await service.ListAsync(entityType, parentId, activeOnly, ct)));
    }

    [HttpPost("{entityType}")]
    public async Task<IActionResult> Create(
        string entityType,
        [FromBody] SaveSaleRoznamchaMasterRequest request,
        CancellationToken ct)
    {
        var denied = await Guard("ADD", ct); if (denied != null) return denied;
        return await Run(async () => ApiResponse<SaleRoznamchaMasterRow>.Ok(
            await service.SaveAsync(entityType, null, request, ct),
            "Master record added successfully."));
    }

    [HttpPut("{entityType}/{id:int}")]
    public async Task<IActionResult> Update(
        string entityType,
        int id,
        [FromBody] SaveSaleRoznamchaMasterRequest request,
        CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        return await Run(async () => ApiResponse<SaleRoznamchaMasterRow>.Ok(
            await service.SaveAsync(entityType, id, request, ct),
            "Master record updated successfully."));
    }

    [HttpDelete("{entityType}/{id:int}")]
    public async Task<IActionResult> Delete(string entityType, int id, CancellationToken ct)
    {
        var denied = await Guard("DELETE", ct); if (denied != null) return denied;
        return await Run(async () =>
        {
            await service.DeleteAsync(entityType, id, ct);
            return ApiResponse<object?>.Ok(null, "Master record deleted successfully.");
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
