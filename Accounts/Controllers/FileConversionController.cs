using System.Security.Claims;
using Accounts.Data;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Route("api/library/file-converter")]
[Authorize]
public sealed class FileConversionController(
    ApplicationDbContext db,
    ITenantService tenant,
    TenantPermissionService tenantPermissions,
    RbacService rbac,
    IFileConversionService converter,
    ILogger<FileConversionController> logger) : ControllerBase
{
    private const string MenuRoute = "/library/file-converter";

    [HttpGet("capabilities")]
    public async Task<IActionResult> GetCapabilities(CancellationToken cancellationToken)
    {
        if (!await HasActionAsync("VIEW", cancellationToken)) return Forbid();
        return Ok(converter.GetCapabilities());
    }

    [HttpPost("convert")]
    [Consumes("multipart/form-data")]
    [RequestFormLimits(MultipartBodyLengthLimit = 26L * 1024 * 1024)]
    [RequestSizeLimit(26L * 1024 * 1024)]
    public async Task<IActionResult> Convert(
        [FromForm] FileConversionForm form,
        CancellationToken cancellationToken)
    {
        if (!await HasActionAsync("ADD", cancellationToken)) return Forbid();
        var file = form.File;
        var targetFormat = form.TargetFormat;
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Select a file to convert." });
        if (string.IsNullOrWhiteSpace(targetFormat))
            return BadRequest(new { message = "Select an output format." });

        try
        {
            var result = await converter.ConvertAsync(file, targetFormat, cancellationToken);
            Response.Headers["X-Conversion-Engine"] = result.Engine;
            Response.Headers["X-Conversion-Quality"] = result.Quality;
            Response.Headers["Cache-Control"] = "no-store, private";
            Response.Headers["Pragma"] = "no-cache";
            return File(result.Content, result.ContentType, result.FileName, enableRangeProcessing: false);
        }
        catch (FileConversionException exception)
        {
            return UnprocessableEntity(new { message = exception.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unexpected failure while converting {FileName}.", Path.GetFileName(file.FileName));
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "File conversion failed",
                detail: "The server could not convert this document safely.");
        }
    }

    public sealed class FileConversionForm
    {
        public IFormFile? File { get; set; }
        public string? TargetFormat { get; set; }
    }

    private async Task<bool> HasActionAsync(string action, CancellationToken cancellationToken)
    {
        if (TenantPermissionService.IsSuperAdmin(User)) return true;
        if (TenantPermissionService.IsTenantAdmin(User))
            return await tenantPermissions.HasMenuRouteAsync(User, [MenuRoute], action, cancellationToken);
        if (!tenant.TenantId.HasValue) return false;

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return false;
        var staffId = await db.Persons.AsNoTracking()
            .Where(person => person.IdentityUserId == userId && person.Staff != null)
            .Select(person => (Guid?)person.Staff!.StaffId)
            .FirstOrDefaultAsync(cancellationToken);
        if (!staffId.HasValue) return false;

        var menuId = await db.Menus.AsNoTracking()
            .Where(menu => menu.IsActive && menu.Route == MenuRoute)
            .Select(menu => (int?)menu.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (!menuId.HasValue) return false;

        var normalizedAction = action.Trim().ToUpperInvariant();
        if (normalizedAction == "VIEW" && await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}"))
            return true;
        return await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_{normalizedAction}");
    }
}
