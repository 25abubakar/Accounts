using Accounts.DTOs;
using Accounts.Idempotency;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/payable-receivable")]
public sealed class PayableReceivableApiController(IPayableReceivableService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(ApiResponse<IReadOnlyList<RecurringTransactionDto>>.Ok(await service.ListAsync(null, ct)));

    [HttpGet("payable")]
    public async Task<IActionResult> Payable(CancellationToken ct) => Ok(ApiResponse<IReadOnlyList<RecurringTransactionDto>>.Ok(await service.ListAsync("payable", ct)));

    [HttpGet("receivable")]
    public async Task<IActionResult> Receivable(CancellationToken ct) => Ok(ApiResponse<IReadOnlyList<RecurringTransactionDto>>.Ok(await service.ListAsync("receivable", ct)));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, [FromQuery] string kind, CancellationToken ct)
    {
        try
        {
            var row = await service.GetAsync(id, kind, ct);
            return row == null ? NotFound(ApiResponse<object?>.Fail("Recurring entry was not found.")) : Ok(ApiResponse<RecurringTransactionDto>.Ok(row));
        }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }

    [HttpPost]
    public Task<IActionResult> Create([FromBody] CreateRecurringTransactionRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<RecurringTransactionDto>.Ok(await service.CreateAsync(request, ct), "Recurring entry added successfully."));

    [HttpPut("{id:int}")]
    public Task<IActionResult> Update(int id, [FromBody] CreateRecurringTransactionRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<RecurringTransactionDto>.Ok(await service.UpdateAsync(id, request, ct), "Recurring entry updated successfully."));

    [HttpPut("{id:int}/status")]
    public Task<IActionResult> Status(int id, [FromQuery] string kind, [FromBody] UpdateRecurringStatusRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<RecurringTransactionDto>.Ok(
            await service.UpdateStatusAsync(id, kind, request.IsInactive, ct), "Recurring status updated successfully."));

    [HttpPost("{id:int}/settle")]
    [Idempotent]
    public Task<IActionResult> Settle(int id, [FromQuery] string kind, [FromBody] SettleRecurringTransactionRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<RecurringTransactionDto>.Ok(await service.SettleAsync(id, kind, request, ct), "Recurring entry settled successfully."));

    [HttpPost("generate-next")]
    public Task<IActionResult> GenerateNext([FromQuery] int id, [FromQuery] string kind, CancellationToken ct) =>
        Run(async () => ApiResponse<RecurringTransactionDto>.Ok(await service.GenerateNextAsync(id, kind, ct), "Next recurring entry generated successfully."));

    [HttpPost("invoice-temp")]
    public IActionResult InvoiceTemp([FromBody] object payload) => Ok(ApiResponse<object>.Ok(payload, "Invoice template accepted."));

    private async Task<IActionResult> Run<T>(Func<Task<ApiResponse<T>>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }
}
