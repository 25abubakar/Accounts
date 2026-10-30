using System.Security.Claims;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Accounts.Services.Ocr;
using Accounts.Services.Ocr.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Route("api/ocr")]
[Authorize]
[AutoValidateAntiforgeryToken]
[RequestFormLimits(MultipartBodyLengthLimit = 262_144_000)]
[RequestSizeLimit(262_144_000)]
public sealed class OcrController : ControllerBase
{
    private const string MenuRoute = "/ocr/eob";
    private const long MaximumPdfBytes = 25L * 1024 * 1024;
    private const long MaximumBatchBytes = 250L * 1024 * 1024;
    private const int MaximumBatchFiles = 50;
    private readonly OcrPipelineCoordinator _coordinator;
    private readonly IOcrDocumentStore _store;
    private readonly ITenantService _tenantService;
    private readonly ApplicationDbContext _db;
    private readonly TenantPermissionService _tenantPermissions;
    private readonly RbacService _rbac;

    public OcrController(
        OcrPipelineCoordinator coordinator,
        IOcrDocumentStore store,
        ITenantService tenantService,
        ApplicationDbContext db,
        TenantPermissionService tenantPermissions,
        RbacService rbac)
    {
        _coordinator = coordinator;
        _store = store;
        _tenantService = tenantService;
        _db = db;
        _tenantPermissions = tenantPermissions;
        _rbac = rbac;
    }

    private int CurrentOrgId => _tenantService.RequiredTenantId;
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpPost("availity")]
    public async Task<ActionResult<List<OcrUploadResult>>> UploadAvaility([FromForm] IFormFileCollection files, CancellationToken ct)
    {
        var rejection = await ValidateUploadAsync(files, "ADD", ct);
        if (rejection != null) return rejection;

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
        var rejection = await ValidateUploadAsync(files, "ADD", ct);
        if (rejection != null) return rejection;

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
        var rejection = await ValidateUploadAsync(files, "ADD", ct);
        if (rejection != null) return rejection;

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
        var rejection = await ValidateUploadAsync(files, "ADD", ct);
        if (rejection != null) return rejection;

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
        var rejection = await ValidateUploadAsync(files, "ADD", ct);
        if (rejection != null) return rejection;

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
        if (!HasTenant || !await HasActionAsync("VIEW", ct)) return Forbid();
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
        if (!HasTenant || !await HasActionAsync("VIEW", ct)) return Forbid();
        var detail = await _store.GetDocumentDetailAsync(id, CurrentOrgId, ct);
        if (detail == null) return NotFound();
        return Ok(detail);
    }

    [HttpGet("documents/{id:int}/attachment")]
    public async Task<IActionResult> GetAttachment([FromRoute] int id, CancellationToken ct)
    {
        if (!HasTenant || !await HasActionAsync("VIEW", ct)) return Forbid();
        var attachment = await _store.GetAttachmentAsync(id, CurrentOrgId, ct);
        if (attachment == null) return NotFound();
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.ContentDisposition = $"inline; filename=\"{attachment.Value.FileName.Replace("\"", string.Empty)}\"";
        return PhysicalFile(attachment.Value.Path, "application/pdf", enableRangeProcessing: true);
    }

    [HttpPut("procedures")]
    public async Task<IActionResult> UpdateProcedureCodes([FromBody] ProcedureCodeUpdateRequest request, CancellationToken ct)
    {
        if (!HasTenant || !await HasActionAsync("EDIT", ct)) return Forbid();
        if (request?.Items == null || request.Items.Count == 0)
            return BadRequest("No procedure codes to update.");

        await _store.UpdateProcedureCodesAsync(request.Items, CurrentOrgId, ct);
        return Ok(new { success = true });
    }

    [HttpDelete("cca/{checkNo}")]
    public async Task<IActionResult> DeleteCcaDocument([FromRoute] string checkNo, CancellationToken ct)
    {
        if (!HasTenant || !await HasActionAsync("DELETE", ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(checkNo))
            return BadRequest("Check number required.");

        await _store.DeleteCcaByCheckNoAsync(checkNo, CurrentOrgId, ct);
        return NoContent();
    }

    private bool HasTenant => _tenantService.TenantId.HasValue;

    private async Task<ActionResult?> ValidateUploadAsync(
        IFormFileCollection? files,
        string action,
        CancellationToken ct)
    {
        if (!HasTenant || !await HasActionAsync(action, ct)) return Forbid();
        if (files == null || files.Count == 0) return BadRequest("No files provided.");
        if (files.Count > MaximumBatchFiles) return BadRequest($"Maximum of {MaximumBatchFiles} files allowed per upload.");
        if (files.Sum(x => x.Length) > MaximumBatchBytes) return BadRequest("The upload batch exceeds the 250 MB limit.");

        foreach (var file in files)
        {
            if (file.Length <= 0) return BadRequest($"File '{Path.GetFileName(file.FileName)}' is empty.");
            if (file.Length > MaximumPdfBytes) return BadRequest($"File '{Path.GetFileName(file.FileName)}' exceeds the 25 MB limit.");
            if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
                return BadRequest($"File '{Path.GetFileName(file.FileName)}' is not a PDF.");
        }

        return null;
    }

    private async Task<bool> HasActionAsync(string action, CancellationToken ct)
    {
        if (!HasTenant) return false;
        if (TenantPermissionService.IsSuperAdmin(User)) return false;
        if (TenantPermissionService.IsTenantAdmin(User))
            return await _tenantPermissions.HasMenuRouteAsync(User, [MenuRoute], action, ct);

        var userId = CurrentUserId;
        if (string.IsNullOrWhiteSpace(userId)) return false;
        var staffId = await _db.Persons.AsNoTracking()
            .Where(x => x.IdentityUserId == userId && x.Staff != null)
            .Select(x => (Guid?)x.Staff!.StaffId)
            .FirstOrDefaultAsync(ct);
        var menuId = await _db.Menus.AsNoTracking()
            .Where(x => x.IsActive && x.Route == MenuRoute)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct);
        if (!staffId.HasValue || !menuId.HasValue) return false;

        var normalized = action.Trim().ToUpperInvariant();
        if (normalized == "VIEW" && await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}")) return true;
        return await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_{normalized}");
    }
}

