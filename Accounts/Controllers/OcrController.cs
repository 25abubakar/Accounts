using System.Security.Claims;
using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Accounts.Services.Ocr;
using Accounts.Services.Ocr.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers;

[ApiController]
[Route("api/ocr")]
[Authorize]
public sealed class OcrController : ControllerBase
{
    private readonly OcrPipelineCoordinator _coordinator;
    private readonly IOcrDocumentStore _store;
    private readonly ITenantService _tenantService;
    private readonly ILogger<OcrController> _logger;

    public OcrController(
        OcrPipelineCoordinator coordinator,
        IOcrDocumentStore store,
        ITenantService tenantService,
        ILogger<OcrController> logger)
    {
        _coordinator = coordinator;
        _store = store;
        _tenantService = tenantService;
        _logger = logger;
    }

    private int CurrentOrgId => _tenantService.TenantId ?? 1;
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpPost("availity")]
    public async Task<ActionResult<List<OcrUploadResult>>> UploadAvaility([FromForm] IFormFileCollection files, CancellationToken ct)
    {
        if (files == null || files.Count == 0)
            return BadRequest("No files provided.");

        var results = new List<OcrUploadResult>();
        foreach (var file in files)
        {
            if (file.Length == 0) continue;
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var res = await _coordinator.ProcessAvailityAsync(ms.ToArray(), file.FileName, CurrentOrgId, CurrentUserId, ct);
            results.Add(res);
        }

        return Ok(results);
    }

    [HttpPost("cca")]
    public async Task<ActionResult<List<OcrUploadResult>>> UploadCca([FromForm] IFormFileCollection files, CancellationToken ct)
    {
        if (files == null || files.Count == 0)
            return BadRequest("No files provided.");

        var results = new List<OcrUploadResult>();
        foreach (var file in files)
        {
            if (file.Length == 0) continue;
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var res = await _coordinator.ProcessCcaAsync(ms.ToArray(), file.FileName, CurrentOrgId, CurrentUserId, ct);
            results.Add(res);
        }

        return Ok(results);
    }

    [HttpPost("lasalle")]
    public async Task<ActionResult<List<OcrUploadResult>>> UploadLaSalle([FromForm] IFormFileCollection files, CancellationToken ct)
    {
        if (files == null || files.Count == 0)
            return BadRequest("No files provided.");

        var results = new List<OcrUploadResult>();
        foreach (var file in files)
        {
            if (file.Length == 0) continue;
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var res = await _coordinator.ProcessLaSalleAsync(ms.ToArray(), file.FileName, CurrentOrgId, CurrentUserId, ct);
            results.Add(res);
        }

        return Ok(results);
    }

    [HttpPost("allcare")]
    public async Task<ActionResult<List<AllCareParseResult>>> UploadAllCare([FromForm] IFormFileCollection files, CancellationToken ct)
    {
        if (files == null || files.Count == 0)
            return BadRequest("No files provided.");

        var results = new List<AllCareParseResult>();
        foreach (var file in files)
        {
            if (file.Length == 0) continue;
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var res = await _coordinator.ProcessAllCareAsync(ms.ToArray(), file.FileName, ct);
            results.Add(res);
        }

        return Ok(results);
    }

    [HttpPost("zelis")]
    public async Task<ActionResult<List<OcrUploadResult>>> UploadZelis([FromForm] IFormFileCollection files, CancellationToken ct)
    {
        if (files == null || files.Count == 0)
            return BadRequest("No files provided.");

        var results = new List<OcrUploadResult>();
        foreach (var file in files)
        {
            if (file.Length == 0) continue;
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var res = await _coordinator.ProcessZelisAsync(ms.ToArray(), file.FileName, CurrentOrgId, CurrentUserId, ct);
            results.Add(res);
        }

        return Ok(results);
    }

    [HttpGet("documents")]
    public async Task<ActionResult<List<OcrDocumentListItem>>> GetDocuments(
        [FromQuery] string? source,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        CancellationToken ct)
    {
        // Rule: If either date is missing, return an empty list
        if (!dateFrom.HasValue || !dateTo.HasValue)
        {
            return Ok(new List<OcrDocumentListItem>());
        }

        var list = await _store.GetDocumentListAsync(source ?? "", dateFrom, dateTo, CurrentOrgId, ct);
        return Ok(list);
    }

    [HttpGet("documents/{id:int}")]
    public async Task<ActionResult<OcrDocumentDetail>> GetDocumentDetail([FromRoute] int id, CancellationToken ct)
    {
        var detail = await _store.GetDocumentDetailAsync(id, ct);
        if (detail == null) return NotFound();
        return Ok(detail);
    }

    [HttpPut("procedures")]
    public async Task<IActionResult> UpdateProcedureCodes([FromBody] ProcedureCodeUpdateRequest request, CancellationToken ct)
    {
        if (request?.Items == null || request.Items.Count == 0)
            return BadRequest("No procedure codes to update.");

        await _store.UpdateProcedureCodesAsync(request.Items, ct);
        return Ok(new { success = true });
    }

    [HttpDelete("cca/{checkNo}")]
    public async Task<IActionResult> DeleteCcaDocument([FromRoute] string checkNo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(checkNo))
            return BadRequest("Check number required.");

        await _store.DeleteCcaByCheckNoAsync(checkNo, CurrentOrgId, ct);
        return NoContent();
    }
}
