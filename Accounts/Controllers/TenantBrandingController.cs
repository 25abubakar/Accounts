using Accounts.Data;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Controllers;

[ApiController]
[Route("api/tenant-branding")]
[Authorize]
public sealed class TenantBrandingController : ControllerBase
{
    private const long MaxBrandingBytes = 12 * 1024 * 1024;
    private readonly ApplicationDbContext _db;

    public TenantBrandingController(ApplicationDbContext db) => _db = db;

    [HttpGet("{tenantId:int}/content")]
    public async Task<IActionResult> GetContent(int tenantId, CancellationToken ct)
    {
        if (!CanView(tenantId)) return Forbid();

        var asset = await _db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.Id == tenantId && t.BrandingContent != null)
            .Select(t => new { t.BrandingContent, t.BrandingContentType, t.BrandingFileName })
            .SingleOrDefaultAsync(ct);

        if (asset?.BrandingContent == null || string.IsNullOrWhiteSpace(asset.BrandingContentType))
            return NotFound();

        Response.Headers.CacheControl = "private,max-age=86400";
        return File(asset.BrandingContent, asset.BrandingContentType, enableRangeProcessing: true);
    }

    [HttpPut("{tenantId:int}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxBrandingBytes)]
    public async Task<IActionResult> Update(int tenantId, IFormFile branding, CancellationToken ct)
    {
        if (!CanManage(tenantId)) return Forbid();
        if (branding == null || branding.Length == 0)
            return BadRequest(new { message = "Choose an image or animation video." });
        if (branding.Length > MaxBrandingBytes)
            return BadRequest(new { message = "Branding media must be 12 MB or smaller." });

        await using var input = branding.OpenReadStream();
        await using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, ct);
        var content = buffer.ToArray();
        var validated = ValidateMedia(branding.FileName, branding.ContentType, content);
        if (validated == null)
            return BadRequest(new { message = "Use a valid PNG, JPG, GIF, WEBP, MP4, or WEBM file." });

        var tenant = await _db.Tenants.IgnoreQueryFilters().SingleOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant == null) return NotFound(new { message = "Company account was not found." });

        tenant.BrandingFileName = Path.GetFileName(branding.FileName);
        tenant.BrandingContentType = validated.Value.ContentType;
        tenant.BrandingAssetType = validated.Value.AssetType;
        tenant.BrandingContent = content;
        tenant.BrandingUpdatedOnUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(ToResponse(tenant.Id, tenant.BrandingFileName, tenant.BrandingAssetType, tenant.BrandingUpdatedOnUtc));
    }

    [HttpDelete("{tenantId:int}")]
    public async Task<IActionResult> Delete(int tenantId, CancellationToken ct)
    {
        if (!CanManage(tenantId)) return Forbid();
        var tenant = await _db.Tenants.IgnoreQueryFilters().SingleOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant == null) return NotFound(new { message = "Company account was not found." });

        tenant.BrandingFileName = null;
        tenant.BrandingContentType = null;
        tenant.BrandingAssetType = null;
        tenant.BrandingContent = null;
        tenant.BrandingUpdatedOnUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { tenantId, brandingUrl = (string?)null, brandingAssetType = (string?)null, brandingFileName = (string?)null });
    }

    private bool CanView(int tenantId) => IsSuperAdmin() || ClaimTenantId() == tenantId;

    private bool CanManage(int tenantId) => IsSuperAdmin()
        || (ClaimTenantId() == tenantId && (User.HasClaim(ITenantService.ClaimIsTenantAdmin, "true") || User.IsInRole("TenantAdmin")));

    private bool IsSuperAdmin() => User.IsInRole("SuperAdmin")
        || User.HasClaim(ITenantService.ClaimIsSuperAdmin, "true");

    private int? ClaimTenantId() => int.TryParse(User.FindFirstValue(ITenantService.ClaimTenantId), out var id) ? id : null;

    private static object ToResponse(int tenantId, string? fileName, string? assetType, DateTime? updatedOnUtc)
    {
        var version = updatedOnUtc?.Ticks ?? 0;
        return new
        {
            tenantId,
            brandingFileName = fileName,
            brandingAssetType = assetType,
            brandingUpdatedOnUtc = updatedOnUtc,
            brandingUrl = $"/api/tenant-branding/{tenantId}/content?v={version}"
        };
    }

    private static (string ContentType, string AssetType)? ValidateMedia(string fileName, string suppliedContentType, byte[] bytes)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        bool Starts(params byte[] signature) => bytes.Length >= signature.Length && signature.Select((b, i) => bytes[i] == b).All(x => x);
        bool At(int offset, string text) => bytes.Length >= offset + text.Length
            && System.Text.Encoding.ASCII.GetString(bytes, offset, text.Length) == text;

        return extension switch
        {
            ".png" when Starts(0x89, 0x50, 0x4E, 0x47) => ("image/png", "Image"),
            ".jpg" or ".jpeg" when Starts(0xFF, 0xD8, 0xFF) => ("image/jpeg", "Image"),
            ".gif" when At(0, "GIF8") => ("image/gif", "Image"),
            ".webp" when At(0, "RIFF") && At(8, "WEBP") => ("image/webp", "Image"),
            ".mp4" when At(4, "ftyp") => ("video/mp4", "Video"),
            ".webm" when Starts(0x1A, 0x45, 0xDF, 0xA3) => ("video/webm", "Video"),
            _ => null
        };
    }
}
