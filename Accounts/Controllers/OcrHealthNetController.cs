using System.Security.Claims;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Accounts.Services.Ocr;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Route("api/ocr/healthnet")]
[Authorize]
[AutoValidateAntiforgeryToken]
[RequestFormLimits(MultipartBodyLengthLimit = 262_144_000)]
[RequestSizeLimit(262_144_000)]
public sealed class OcrHealthNetController : ControllerBase
{
    private const string MenuRoute = "/ocr/eob";
    private const long MaximumPdfBytes = 25L * 1024 * 1024;
    private const long MaximumBatchBytes = 250L * 1024 * 1024;
    private readonly OcrPipelineCoordinator _coordinator;
    private readonly ITenantService _tenantService;
    private readonly ApplicationDbContext _db;
    private readonly TenantPermissionService _tenantPermissions;
    private readonly RbacService _rbac;

    public OcrHealthNetController(
        OcrPipelineCoordinator coordinator,
        ITenantService tenantService,
        ApplicationDbContext db,
        TenantPermissionService tenantPermissions,
        RbacService rbac)
    {
        _coordinator = coordinator;
        _tenantService = tenantService;
        _db = db;
        _tenantPermissions = tenantPermissions;
        _rbac = rbac;
    }

    private int CurrentOrgId => _tenantService.RequiredTenantId;
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpPost]
    public async Task<ActionResult<List<HealthNetFileResult>>> UploadHealthNet(
        [FromForm] IFormFileCollection? files,
        [FromForm] IFormFile? file,
        CancellationToken ct)
    {
        if (!_tenantService.TenantId.HasValue || !await HasActionAsync("ADD", ct)) return Forbid();

        var fileList = new List<IFormFile>();
        if (files != null && files.Count > 0)
        {
            fileList.AddRange(files);
        }
        else if (file != null)
        {
            fileList.Add(file);
        }

        // Rule: Reject a missing file
        if (fileList.Count == 0)
        {
            return BadRequest("A PDF file is required.");
        }

        // Rule: Health Net allows up to 50 files
        if (fileList.Count > 50)
        {
            return BadRequest("Maximum of 50 files allowed per upload.");
        }
        if (fileList.Sum(x => x.Length) > MaximumBatchBytes)
        {
            return BadRequest("The upload batch exceeds the 250 MB limit.");
        }

        // Rule: Reject any file that is not .pdf
        foreach (var f in fileList)
        {
            var ext = Path.GetExtension(f.FileName);
            if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest($"File '{f.FileName}' is not a valid PDF. Only .pdf files are accepted.");
            }
            if (f.Length <= 0) return BadRequest($"File '{Path.GetFileName(f.FileName)}' is empty.");
            if (f.Length > MaximumPdfBytes) return BadRequest($"File '{Path.GetFileName(f.FileName)}' exceeds the 25 MB limit.");
        }

        var results = new List<HealthNetFileResult>();
        foreach (var f in fileList)
        {
            if (f.Length == 0) continue;
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms, ct);
            var res = await _coordinator.ProcessHealthNetAsync(ms.ToArray(), f.FileName, CurrentOrgId, CurrentUserId, ct);
            results.Add(res);
        }

        return Ok(results);
    }

    private async Task<bool> HasActionAsync(string action, CancellationToken ct)
    {
        if (!_tenantService.TenantId.HasValue || TenantPermissionService.IsSuperAdmin(User)) return false;
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

