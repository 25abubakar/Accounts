using System.Security.Claims;
using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Accounts.Services.Ocr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers;

[ApiController]
[Route("api/ocr/healthnet")]
[Authorize]
public sealed class OcrHealthNetController : ControllerBase
{
    private readonly OcrPipelineCoordinator _coordinator;
    private readonly ITenantService _tenantService;
    private readonly ILogger<OcrHealthNetController> _logger;

    public OcrHealthNetController(
        OcrPipelineCoordinator coordinator,
        ITenantService tenantService,
        ILogger<OcrHealthNetController> logger)
    {
        _coordinator = coordinator;
        _tenantService = tenantService;
        _logger = logger;
    }

    private int CurrentOrgId => _tenantService.TenantId ?? 1;
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpPost]
    public async Task<ActionResult<List<HealthNetFileResult>>> UploadHealthNet(
        [FromForm] IFormFileCollection? files,
        [FromForm] IFormFile? file,
        CancellationToken ct)
    {
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

        // Rule: Reject any file that is not .pdf
        foreach (var f in fileList)
        {
            var ext = Path.GetExtension(f.FileName);
            if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest($"File '{f.FileName}' is not a valid PDF. Only .pdf files are accepted.");
            }
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
}
