using Accounts.DTOs;
using Accounts.Idempotency;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
[Route("api/roznamcha")]
public sealed class RoznamchaApiController(IRoznamchaService service) : ControllerBase
{
    [HttpGet("payments")]
    public async Task<IActionResult> Payments([FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<RoznamchaEntryDto>>.Ok(await service.ListAsync(1, dateFrom, dateTo, ct)));

    [HttpPost("payments")]
    [Idempotent]
    public Task<IActionResult> CreatePayment([FromBody] CreateRoznamchaPaymentRequest request, CancellationToken ct) => Create(1, request, ct);

    [HttpPut("payments/{id:long}")]
    public Task<IActionResult> UpdatePayment(long id, [FromBody] CreateRoznamchaPaymentRequest request, CancellationToken ct) => Update(id, 1, request, ct);

    [HttpDelete("payments/{id:long}")]
    public Task<IActionResult> DeletePayment(long id, CancellationToken ct) => Delete(id, ct);

    [HttpGet("receipts")]
    public async Task<IActionResult> Receipts([FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<RoznamchaEntryDto>>.Ok(await service.ListAsync(2, dateFrom, dateTo, ct)));

    [HttpPost("receipts")]
    [Idempotent]
    public Task<IActionResult> CreateReceipt([FromBody] CreateRoznamchaReceiptRequest request, CancellationToken ct) => Create(2, request, ct);

    [HttpPut("receipts/{id:long}")]
    public Task<IActionResult> UpdateReceipt(long id, [FromBody] CreateRoznamchaReceiptRequest request, CancellationToken ct) => Update(id, 2, request, ct);

    [HttpDelete("receipts/{id:long}")]
    public Task<IActionResult> DeleteReceipt(long id, CancellationToken ct) => Delete(id, ct);

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var row = await service.GetAsync(id, ct);
        return row == null ? NotFound(ApiResponse<object?>.Fail("Transaction was not found.")) : Ok(ApiResponse<RoznamchaEntryDto>.Ok(row));
    }

    [HttpPut("{id:long}/status")]
    public Task<IActionResult> Status(long id, [FromBody] UpdateRoznamchaStatusRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<RoznamchaEntryDto>.Ok(await service.UpdateStatusAsync(id, request, ct), "Transaction status updated successfully."));

    [HttpPost("{id:long}/approve")]
    [Idempotent]
    public Task<IActionResult> Approve(long id, CancellationToken ct) =>
        Run(async () => ApiResponse<RoznamchaEntryDto>.Ok(await service.ApproveAsync(id, ct), "Transaction approved successfully."));

    [HttpPost("{id:long}/attachments")]
    [Consumes("multipart/form-data")]
    public Task<IActionResult> AddAttachment(long id, [FromForm] UploadAccountsFileRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<EntryAttachmentDto>.Ok(await service.AddAttachmentAsync(id, request.File, request.Remarks, ct), "Attachment uploaded successfully."));

    [HttpGet("{id:long}/attachments")]
    public Task<IActionResult> Attachments(long id, CancellationToken ct) =>
        Run(async () => ApiResponse<IReadOnlyList<EntryAttachmentDto>>.Ok(await service.ListAttachmentsAsync(id, ct)));

    [HttpGet("attachments/{attachmentId:long}/download")]
    public async Task<IActionResult> Download(long attachmentId, CancellationToken ct)
    {
        var file = await service.OpenAttachmentAsync(attachmentId, ct);
        return file == null ? NotFound(ApiResponse<object?>.Fail("Attachment was not found.")) : File(file.Value.Stream, file.Value.ContentType, file.Value.FileName);
    }

    [HttpDelete("attachments/{attachmentId:long}")]
    public Task<IActionResult> DeleteAttachment(long attachmentId, CancellationToken ct) =>
        Run(async () => { await service.DeleteAttachmentAsync(attachmentId, ct); return ApiResponse<object?>.Ok(null, "Attachment deleted successfully."); });

    private Task<IActionResult> Create(int type, SaveRoznamchaEntryRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<RoznamchaEntryDto>.Ok(await service.CreateAsync(type, request, ct), type == 1 ? "Payment added successfully." : "Receipt added successfully."));

    private Task<IActionResult> Update(long id, int type, SaveRoznamchaEntryRequest request, CancellationToken ct) =>
        Run(async () => ApiResponse<RoznamchaEntryDto>.Ok(await service.UpdateAsync(id, type, request, ct), "Transaction updated successfully."));

    private Task<IActionResult> Delete(long id, CancellationToken ct) =>
        Run(async () => { await service.SoftDeleteAsync(id, ct); return ApiResponse<object?>.Ok(null, "Transaction deleted successfully."); });

    private async Task<IActionResult> Run<T>(Func<Task<ApiResponse<T>>> action)
    {
        try { return Ok(await action()); }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse<object?>.Fail(ex.Message)); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<object?>.Fail(ex.Message)); }
    }
}
