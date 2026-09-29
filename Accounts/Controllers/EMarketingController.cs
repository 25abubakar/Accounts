using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/e-marketing")]
public sealed class EMarketingController(IEMarketingService service) : ControllerBase
{
    [HttpGet("stock-info")]
    public async Task<IActionResult> StockInfo(
        [FromQuery] int accountId,
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        CancellationToken ct)
    {
        if (accountId <= 0)
            return BadRequest(ApiResponse<object?>.Fail("AccountId is required."));
        return Ok(ApiResponse<IReadOnlyList<EMarketingStockInfoDto>>.Ok(
            await service.ListStockInfoAsync(accountId, dateFrom, dateTo, ct)));
    }

    [HttpGet("sales-roz")]
    public async Task<IActionResult> SalesRozList(
        [FromQuery] int? accountId,
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<EMarketingSalesRozDto>>.Ok(
            await service.ListSalesRozAsync(accountId, dateFrom, dateTo, ct)));

    [HttpGet("sales-roz/{id:long}")]
    public async Task<IActionResult> SalesRozGet(long id, CancellationToken ct)
    {
        var row = await service.GetSalesRozAsync(id, ct);
        return row == null
            ? NotFound(ApiResponse<object?>.Fail("Sales (Roz) entry was not found."))
            : Ok(ApiResponse<EMarketingSalesRozDto>.Ok(row));
    }

    [HttpPost("sales-roz")]
    public Task<IActionResult> SalesRozCreate([FromBody] SaveEMarketingSalesRozRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<EMarketingSalesRozDto>.Ok(
            await service.SaveSalesRozAsync(null, request, ct), "Sales (Roz) entry saved."));

    [HttpPut("sales-roz/{id:long}")]
    public Task<IActionResult> SalesRozUpdate(long id, [FromBody] SaveEMarketingSalesRozRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<EMarketingSalesRozDto>.Ok(
            await service.SaveSalesRozAsync(id, request, ct), "Sales (Roz) entry updated."));

    [HttpDelete("sales-roz/{id:long}")]
    public Task<IActionResult> SalesRozDelete(long id, CancellationToken ct) =>
        Run(async () =>
        {
            await service.DeleteSalesRozAsync(id, ct);
            return ApiResponse<object?>.Ok(null, "Sales (Roz) entry deleted.");
        });

    private async Task<IActionResult> Run(Func<Task<ApiResponse<object?>>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }

    private async Task<IActionResult> Run<T>(Func<Task<ApiResponse<T>>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }
}
