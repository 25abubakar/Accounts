using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/sale-roznamcha/history")]
public sealed class SaleRoznamchaHistoryController(
    ISaleRoznamchaHistoryService history,
    ISaleRoznamchaMasterService masters,
    ISaleRoznamchaAccessService access) : ControllerBase
{
    private const string RoutePath = "/sale-roznamcha/history";

    [HttpGet]
    public async Task<IActionResult> Report(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] int? categoryId,
        [FromQuery] int? companyId,
        [FromQuery] int? platformId,
        [FromQuery] int? productCategoryId,
        CancellationToken ct = default)
    {
        var denied = await Guard(ct); if (denied != null) return denied;
        try
        {
            return Ok(ApiResponse<IReadOnlyList<SaleRoznamchaHistoryAggregateRow>>.Ok(
                await history.ReportAsync(fromDate, toDate, categoryId, companyId, platformId, productCategoryId, ct)));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object?>.Fail(ex.Message));
        }
    }

    [HttpGet("hierarchy")]
    public async Task<IActionResult> Hierarchy(CancellationToken ct)
    {
        var denied = await Guard(ct); if (denied != null) return denied;
        return Ok(ApiResponse<IReadOnlyList<SaleRoznamchaMasterRow>>.Ok(
            await masters.ListAsync(null, null, true, ct)));
    }

    private async Task<IActionResult?> Guard(CancellationToken ct)
        => await access.CanAsync(User, RoutePath, "VIEW", ct) ? null : Forbid();
}
