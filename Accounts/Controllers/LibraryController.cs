using System.Security.Claims;
using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Route("api/library")]
[Authorize]
[Produces("application/json")]
public sealed class LibraryController : ControllerBase
{
    private const string TypesRoute = "/library/types";
    private const string DocumentsRoute = "/library";
    private const string InvoicesRoute = "/library/generate-invoice";
    private const string DocumentKind = "Document";
    private const string PictureKind = "Picture";
    private const long MaximumFileSize = 50L * 1024 * 1024;
    private const int MaximumTemplateLength = 200_000;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".txt", ".csv", ".json", ".png", ".jpg", ".jpeg", ".webp", ".zip"
    };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp"
    };
    private static readonly HashSet<string> InvoiceStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Draft", "Issued", "Paid", "Cancelled"
    };

    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;
    private readonly TenantPermissionService _tenantPermissions;
    private readonly RbacService _rbac;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<LibraryController> _logger;

    public LibraryController(
        ApplicationDbContext db,
        ITenantService tenant,
        TenantPermissionService tenantPermissions,
        RbacService rbac,
        IWebHostEnvironment environment,
        ILogger<LibraryController> logger)
    {
        _db = db;
        _tenant = tenant;
        _tenantPermissions = tenantPermissions;
        _rbac = rbac;
        _environment = environment;
        _logger = logger;
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin) return Ok(Array.Empty<LibraryCategoryDto>());
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(TypesRoute, "VIEW", ct)) return Forbid();
        var rows = await _db.LibraryCategories.AsNoTracking()
            .OrderBy(x => x.DisplayOrder == 0 ? int.MaxValue : x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new LibraryCategoryDto(x.Id, x.Code, x.Name, x.Description, x.DisplayOrder, x.IsActive))
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] SaveLibraryCategoryDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(TypesRoute, "ADD", ct)) return Forbid();
        var validation = ValidateCategory(dto);
        if (validation != null) return BadRequest(new { message = validation });
        var code = dto.Code.Trim().ToUpperInvariant();
        var name = dto.Name.Trim();
        if (await _db.LibraryCategories.AnyAsync(x => x.Code == code || x.Name == name, ct))
            return Conflict(new { message = "A library category with this code or name already exists." });
        var row = new LibraryCategory { TenantId = _tenant.RequiredTenantId, Code = code, Name = name, Description = Clean(dto.Description), DisplayOrder = Math.Max(0, dto.DisplayOrder), IsActive = dto.IsActive };
        _db.LibraryCategories.Add(row);
        await _db.SaveChangesAsync(ct);
        return Ok(ToCategoryDto(row));
    }

    [HttpPut("categories/{id:int}")]
    public async Task<IActionResult> UpdateCategory(int id, [FromBody] SaveLibraryCategoryDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(TypesRoute, "EDIT", ct)) return Forbid();
        var validation = ValidateCategory(dto);
        if (validation != null) return BadRequest(new { message = validation });
        var row = await _db.LibraryCategories.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Library category not found." });
        var code = dto.Code.Trim().ToUpperInvariant();
        var name = dto.Name.Trim();
        if (await _db.LibraryCategories.AnyAsync(x => x.Id != id && (x.Code == code || x.Name == name), ct))
            return Conflict(new { message = "A library category with this code or name already exists." });
        row.Code = code; row.Name = name; row.Description = Clean(dto.Description); row.DisplayOrder = Math.Max(0, dto.DisplayOrder); row.IsActive = dto.IsActive; row.UpdatedOnUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(ToCategoryDto(row));
    }

    [HttpDelete("categories/{id:int}")]
    public async Task<IActionResult> DeleteCategory(int id, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(TypesRoute, "DELETE", ct)) return Forbid();
        var row = await _db.LibraryCategories.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Library category not found." });
        _db.LibraryCategories.Remove(row);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Library category deleted successfully." });
    }

    [HttpGet("types")]
    public async Task<IActionResult> GetTypes(CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin) return Ok(Array.Empty<LibraryTypeDto>());
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(TypesRoute, "VIEW", ct)) return Forbid();

        var rows = await _db.LibraryTypes.AsNoTracking()
            .OrderBy(x => x.DisplayOrder == 0 ? int.MaxValue : x.DisplayOrder)
            .ThenBy(x => x.Name)
            .Select(x => new LibraryTypeDto(x.Id, x.Code, x.Name, x.Description, x.DisplayOrder, x.IsHardCopyRequired, x.IsActive,
                x.Documents.Count(document => document.AssetKind == DocumentKind && document.IsActive), x.SubTypes.Count(subType => subType.IsActive)))
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("types")]
    public async Task<IActionResult> CreateType([FromBody] SaveLibraryTypeDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(TypesRoute, "ADD", ct)) return Forbid();
        var validation = ValidateType(dto);
        if (validation != null) return BadRequest(new { message = validation });

        var code = dto.Code.Trim().ToUpperInvariant();
        var name = dto.Name.Trim();
        var duplicate = await _db.LibraryTypes.AsNoTracking().AnyAsync(
            x => x.TenantId == _tenant.RequiredTenantId && (x.Code == code || x.Name == name), ct);
        if (duplicate) return Conflict(new { message = "A library type with this code or name already exists." });

        var row = new LibraryType
        {
            TenantId = _tenant.RequiredTenantId,
            Code = code,
            Name = name,
            Description = Clean(dto.Description),
            DisplayOrder = Math.Max(0, dto.DisplayOrder),
            IsHardCopyRequired = dto.IsHardCopyRequired,
            IsActive = dto.IsActive,
            CreatedOnUtc = DateTime.UtcNow
        };
        _db.LibraryTypes.Add(row);
        await _db.SaveChangesAsync(ct);
        return Ok(ToTypeDto(row, 0));
    }

    [HttpPut("types/{id:int}")]
    public async Task<IActionResult> UpdateType(int id, [FromBody] SaveLibraryTypeDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(TypesRoute, "EDIT", ct)) return Forbid();
        var validation = ValidateType(dto);
        if (validation != null) return BadRequest(new { message = validation });

        var row = await _db.LibraryTypes.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Library type not found." });

        var code = dto.Code.Trim().ToUpperInvariant();
        var name = dto.Name.Trim();
        var duplicate = await _db.LibraryTypes.AsNoTracking().AnyAsync(
            x => x.Id != id && x.TenantId == _tenant.RequiredTenantId && (x.Code == code || x.Name == name), ct);
        if (duplicate) return Conflict(new { message = "A library type with this code or name already exists." });

        row.Code = code;
        row.Name = name;
        row.Description = Clean(dto.Description);
        row.DisplayOrder = Math.Max(0, dto.DisplayOrder);
        row.IsHardCopyRequired = dto.IsHardCopyRequired;
        row.IsActive = dto.IsActive;
        row.UpdatedOnUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        var count = await _db.LibraryDocuments.CountAsync(x => x.LibraryTypeId == row.Id && x.IsActive, ct);
        var subTypeCount = await _db.LibrarySubTypes.CountAsync(x => x.LibraryTypeId == row.Id && x.IsActive, ct);
        return Ok(ToTypeDto(row, count, subTypeCount));
    }

    [HttpDelete("types/{id:int}")]
    public async Task<IActionResult> DeleteType(int id, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(TypesRoute, "DELETE", ct)) return Forbid();
        var row = await _db.LibraryTypes.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Library type not found." });
        if (await _db.LibraryDocuments.AnyAsync(x => x.LibraryTypeId == id, ct))
            return Conflict(new { message = "This type cannot be deleted while library documents are using it." });
        if (await _db.LibrarySubTypes.AnyAsync(x => x.LibraryTypeId == id, ct))
            return Conflict(new { message = "This type cannot be deleted while sub types are using it." });
        if (await _db.LibraryTemplates.AnyAsync(x => x.LibraryTypeId == id, ct))
            return Conflict(new { message = "This type cannot be deleted while library templates are using it." });
        _db.LibraryTypes.Remove(row);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Library type deleted successfully." });
    }

    [HttpGet("sub-types")]
    public async Task<IActionResult> GetSubTypes([FromQuery] int? typeId, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin) return Ok(Array.Empty<LibrarySubTypeDto>());
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(TypesRoute, "VIEW", ct)) return Forbid();
        var query = _db.LibrarySubTypes.AsNoTracking().Include(x => x.LibraryType).AsQueryable();
        if (typeId.HasValue) query = query.Where(x => x.LibraryTypeId == typeId.Value);
        var rows = await query.OrderBy(x => x.DisplayOrder == 0 ? int.MaxValue : x.DisplayOrder).ThenBy(x => x.Name).ToListAsync(ct);
        return Ok(rows.Select(ToSubTypeDto));
    }

    [HttpPost("sub-types")]
    public async Task<IActionResult> CreateSubType([FromBody] SaveLibrarySubTypeDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(TypesRoute, "ADD", ct)) return Forbid();
        var validation = await ValidateSubTypeAsync(dto, ct);
        if (validation != null) return BadRequest(new { message = validation });
        var code = dto.Code.Trim().ToUpperInvariant();
        var name = dto.Name.Trim();
        if (await _db.LibrarySubTypes.AnyAsync(x => x.LibraryTypeId == dto.LibraryTypeId && (x.Code == code || x.Name == name), ct))
            return Conflict(new { message = "This sub type code or name already exists under the selected library type." });
        var row = new LibrarySubType { TenantId = _tenant.RequiredTenantId, LibraryTypeId = dto.LibraryTypeId, Code = code, Name = name, Description = Clean(dto.Description), DisplayOrder = Math.Max(0, dto.DisplayOrder), IsActive = dto.IsActive };
        _db.LibrarySubTypes.Add(row);
        await _db.SaveChangesAsync(ct);
        row.LibraryType = await _db.LibraryTypes.AsNoTracking().SingleAsync(x => x.Id == row.LibraryTypeId, ct);
        return Ok(ToSubTypeDto(row));
    }

    [HttpPut("sub-types/{id:int}")]
    public async Task<IActionResult> UpdateSubType(int id, [FromBody] SaveLibrarySubTypeDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(TypesRoute, "EDIT", ct)) return Forbid();
        var validation = await ValidateSubTypeAsync(dto, ct);
        if (validation != null) return BadRequest(new { message = validation });
        var row = await _db.LibrarySubTypes.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Library sub type not found." });
        var code = dto.Code.Trim().ToUpperInvariant();
        var name = dto.Name.Trim();
        if (await _db.LibrarySubTypes.AnyAsync(x => x.Id != id && x.LibraryTypeId == dto.LibraryTypeId && (x.Code == code || x.Name == name), ct))
            return Conflict(new { message = "This sub type code or name already exists under the selected library type." });
        row.LibraryTypeId = dto.LibraryTypeId; row.Code = code; row.Name = name; row.Description = Clean(dto.Description); row.DisplayOrder = Math.Max(0, dto.DisplayOrder); row.IsActive = dto.IsActive; row.UpdatedOnUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        row.LibraryType = await _db.LibraryTypes.AsNoTracking().SingleAsync(x => x.Id == row.LibraryTypeId, ct);
        return Ok(ToSubTypeDto(row));
    }

    [HttpDelete("sub-types/{id:int}")]
    public async Task<IActionResult> DeleteSubType(int id, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(TypesRoute, "DELETE", ct)) return Forbid();
        var row = await _db.LibrarySubTypes.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Library sub type not found." });
        _db.LibrarySubTypes.Remove(row);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Library sub type deleted successfully." });
    }

    [HttpGet("documents")]
    public async Task<IActionResult> GetDocuments([FromQuery] int? typeId, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin) return Ok(Array.Empty<LibraryDocumentDto>());
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(DocumentsRoute, "VIEW", ct)) return Forbid();

        var query = _db.LibraryDocuments.AsNoTracking().Include(x => x.LibraryType).Where(x => x.AssetKind == DocumentKind);
        if (typeId.HasValue) query = query.Where(x => x.LibraryTypeId == typeId.Value);
        var rows = await query.OrderByDescending(x => x.CreatedOnUtc).ToListAsync(ct);
        return Ok(rows.Select(ToDocumentDto));
    }

    [HttpPost("documents")]
    [RequestSizeLimit(MaximumFileSize + 1024 * 1024)]
    public async Task<IActionResult> CreateDocument([FromForm] SaveLibraryDocumentForm form, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(DocumentsRoute, "ADD", ct)) return Forbid();
        var validation = await ValidateDocumentAsync(form, requireFile: true, ct);
        if (validation != null) return BadRequest(new { message = validation });

        var storedFileName = await SaveFileAsync(form.File!, ct);
        try
        {
            var extension = Path.GetExtension(form.File!.FileName).ToLowerInvariant();
            var row = new LibraryDocument
            {
                TenantId = _tenant.RequiredTenantId,
                LibraryTypeId = form.LibraryTypeId,
                AssetKind = DocumentKind,
                Title = form.Title.Trim(),
                Description = Clean(form.Description),
                OriginalFileName = Path.GetFileName(form.File.FileName),
                StoredFileName = storedFileName,
                ContentType = CleanContentType(form.File.ContentType),
                FileExtension = extension,
                FileSizeBytes = form.File.Length,
                IsActive = form.IsActive,
                UploadedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                CreatedOnUtc = DateTime.UtcNow
            };
            _db.LibraryDocuments.Add(row);
            await _db.SaveChangesAsync(ct);
            row.LibraryType = await _db.LibraryTypes.AsNoTracking().SingleAsync(x => x.Id == row.LibraryTypeId, ct);
            return Ok(ToDocumentDto(row));
        }
        catch
        {
            DeleteStoredFile(storedFileName);
            throw;
        }
    }

    [HttpPut("documents/{id:long}")]
    [RequestSizeLimit(MaximumFileSize + 1024 * 1024)]
    public async Task<IActionResult> UpdateDocument(long id, [FromForm] SaveLibraryDocumentForm form, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(DocumentsRoute, "EDIT", ct)) return Forbid();
        var validation = await ValidateDocumentAsync(form, requireFile: false, ct);
        if (validation != null) return BadRequest(new { message = validation });
        var row = await _db.LibraryDocuments.Include(x => x.LibraryType).SingleOrDefaultAsync(x => x.Id == id && x.AssetKind == DocumentKind, ct);
        if (row == null) return NotFound(new { message = "Library document not found." });

        string? newStoredFileName = null;
        var previousStoredFileName = row.StoredFileName;
        try
        {
            if (form.File is { Length: > 0 })
            {
                newStoredFileName = await SaveFileAsync(form.File, ct);
                row.OriginalFileName = Path.GetFileName(form.File.FileName);
                row.StoredFileName = newStoredFileName;
                row.ContentType = CleanContentType(form.File.ContentType);
                row.FileExtension = Path.GetExtension(form.File.FileName).ToLowerInvariant();
                row.FileSizeBytes = form.File.Length;
            }
            row.LibraryTypeId = form.LibraryTypeId;
            row.Title = form.Title.Trim();
            row.Description = Clean(form.Description);
            row.IsActive = form.IsActive;
            row.UpdatedOnUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            if (newStoredFileName != null) DeleteStoredFile(previousStoredFileName);
            row.LibraryType = await _db.LibraryTypes.AsNoTracking().SingleAsync(x => x.Id == row.LibraryTypeId, ct);
            return Ok(ToDocumentDto(row));
        }
        catch
        {
            if (newStoredFileName != null) DeleteStoredFile(newStoredFileName);
            throw;
        }
    }

    [HttpGet("documents/{id:long}/download")]
    public async Task<IActionResult> DownloadDocument(long id, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(DocumentsRoute, "VIEW", ct)) return Forbid();
        var row = await _db.LibraryDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.AssetKind == DocumentKind, ct);
        if (row == null) return NotFound(new { message = "Library document not found." });
        var path = GetStoredPath(row.StoredFileName);
        if (!System.IO.File.Exists(path))
        {
            _logger.LogWarning("Library file {DocumentId} is missing from {Path}.", row.Id, path);
            return NotFound(new { message = "The stored file is unavailable. Please contact an administrator." });
        }
        return PhysicalFile(path, row.ContentType, row.OriginalFileName, enableRangeProcessing: true);
    }

    [HttpDelete("documents/{id:long}")]
    public async Task<IActionResult> DeleteDocument(long id, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(DocumentsRoute, "DELETE", ct)) return Forbid();
        var row = await _db.LibraryDocuments.SingleOrDefaultAsync(x => x.Id == id && x.AssetKind == DocumentKind, ct);
        if (row == null) return NotFound(new { message = "Library document not found." });
        var storedFileName = row.StoredFileName;
        _db.LibraryDocuments.Remove(row);
        await _db.SaveChangesAsync(ct);
        DeleteStoredFile(storedFileName);
        return Ok(new { message = "Library document deleted successfully." });
    }

    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates([FromQuery] int? typeId, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin) return Ok(Array.Empty<LibraryTemplateDto>());
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(DocumentsRoute, "VIEW", ct)) return Forbid();
        var query = _db.LibraryTemplates.AsNoTracking().Include(x => x.LibraryType).AsQueryable();
        if (typeId.HasValue) query = query.Where(x => x.LibraryTypeId == typeId.Value);
        var rows = await query.OrderByDescending(x => x.CreatedOnUtc).ToListAsync(ct);
        return Ok(rows.Select(ToTemplateDto));
    }

    [HttpPost("templates")]
    public async Task<IActionResult> CreateTemplate([FromBody] SaveLibraryTemplateDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(DocumentsRoute, "ADD", ct)) return Forbid();
        var validation = await ValidateTemplateAsync(dto, ct);
        if (validation != null) return BadRequest(new { message = validation });
        var row = new LibraryTemplate
        {
            TenantId = _tenant.RequiredTenantId,
            LibraryTypeId = dto.LibraryTypeId,
            Name = dto.Name.Trim(),
            Description = Clean(dto.Description),
            Content = dto.Content,
            IsActive = dto.IsActive,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            CreatedOnUtc = DateTime.UtcNow
        };
        _db.LibraryTemplates.Add(row);
        await _db.SaveChangesAsync(ct);
        row.LibraryType = await _db.LibraryTypes.AsNoTracking().SingleAsync(x => x.Id == row.LibraryTypeId, ct);
        return Ok(ToTemplateDto(row));
    }

    [HttpPut("templates/{id:long}")]
    public async Task<IActionResult> UpdateTemplate(long id, [FromBody] SaveLibraryTemplateDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(DocumentsRoute, "EDIT", ct)) return Forbid();
        var validation = await ValidateTemplateAsync(dto, ct);
        if (validation != null) return BadRequest(new { message = validation });
        var row = await _db.LibraryTemplates.Include(x => x.LibraryType).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Library template not found." });
        row.LibraryTypeId = dto.LibraryTypeId;
        row.Name = dto.Name.Trim();
        row.Description = Clean(dto.Description);
        row.Content = dto.Content;
        row.IsActive = dto.IsActive;
        row.UpdatedOnUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        row.LibraryType = await _db.LibraryTypes.AsNoTracking().SingleAsync(x => x.Id == row.LibraryTypeId, ct);
        return Ok(ToTemplateDto(row));
    }

    [HttpDelete("templates/{id:long}")]
    public async Task<IActionResult> DeleteTemplate(long id, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(DocumentsRoute, "DELETE", ct)) return Forbid();
        var row = await _db.LibraryTemplates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Library template not found." });
        _db.LibraryTemplates.Remove(row);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Library template deleted successfully." });
    }

    [HttpGet("pictures")]
    public async Task<IActionResult> GetPictures([FromQuery] int? typeId, CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin) return Ok(Array.Empty<LibraryPictureDto>());
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(DocumentsRoute, "VIEW", ct)) return Forbid();
        var query = _db.LibraryDocuments.AsNoTracking().Include(x => x.LibraryType).Where(x => x.AssetKind == PictureKind);
        if (typeId.HasValue) query = query.Where(x => x.LibraryTypeId == typeId.Value);
        var rows = await query.OrderByDescending(x => x.CreatedOnUtc).ToListAsync(ct);
        return Ok(rows.Select(ToPictureDto));
    }

    [HttpPost("pictures")]
    [RequestSizeLimit(MaximumFileSize + 1024 * 1024)]
    public async Task<IActionResult> CreatePicture([FromForm] SaveLibraryDocumentForm form, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(DocumentsRoute, "ADD", ct)) return Forbid();
        var validation = await ValidatePictureAsync(form, requireFile: true, ct);
        if (validation != null) return BadRequest(new { message = validation });
        var storedFileName = await SaveFileAsync(form.File!, ct);
        try
        {
            var row = new LibraryDocument
            {
                TenantId = _tenant.RequiredTenantId,
                LibraryTypeId = form.LibraryTypeId,
                AssetKind = PictureKind,
                Title = form.Title.Trim(),
                Description = Clean(form.Description),
                OriginalFileName = Path.GetFileName(form.File!.FileName),
                StoredFileName = storedFileName,
                ContentType = PictureContentType(Path.GetExtension(form.File.FileName)),
                FileExtension = Path.GetExtension(form.File.FileName).ToLowerInvariant(),
                FileSizeBytes = form.File.Length,
                IsActive = form.IsActive,
                UploadedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                CreatedOnUtc = DateTime.UtcNow
            };
            _db.LibraryDocuments.Add(row);
            await _db.SaveChangesAsync(ct);
            row.LibraryType = await _db.LibraryTypes.AsNoTracking().SingleAsync(x => x.Id == row.LibraryTypeId, ct);
            return Ok(ToPictureDto(row));
        }
        catch
        {
            DeleteStoredFile(storedFileName);
            throw;
        }
    }

    [HttpPut("pictures/{id:long}")]
    [RequestSizeLimit(MaximumFileSize + 1024 * 1024)]
    public async Task<IActionResult> UpdatePicture(long id, [FromForm] SaveLibraryDocumentForm form, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(DocumentsRoute, "EDIT", ct)) return Forbid();
        var validation = await ValidatePictureAsync(form, requireFile: false, ct);
        if (validation != null) return BadRequest(new { message = validation });
        var row = await _db.LibraryDocuments.Include(x => x.LibraryType).SingleOrDefaultAsync(x => x.Id == id && x.AssetKind == PictureKind, ct);
        if (row == null) return NotFound(new { message = "Library picture not found." });
        string? newStoredFileName = null;
        var previousStoredFileName = row.StoredFileName;
        try
        {
            if (form.File is { Length: > 0 })
            {
                newStoredFileName = await SaveFileAsync(form.File, ct);
                row.OriginalFileName = Path.GetFileName(form.File.FileName);
                row.StoredFileName = newStoredFileName;
                row.ContentType = PictureContentType(Path.GetExtension(form.File.FileName));
                row.FileExtension = Path.GetExtension(form.File.FileName).ToLowerInvariant();
                row.FileSizeBytes = form.File.Length;
            }
            row.LibraryTypeId = form.LibraryTypeId;
            row.Title = form.Title.Trim();
            row.Description = Clean(form.Description);
            row.IsActive = form.IsActive;
            row.UpdatedOnUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            if (newStoredFileName != null) DeleteStoredFile(previousStoredFileName);
            row.LibraryType = await _db.LibraryTypes.AsNoTracking().SingleAsync(x => x.Id == row.LibraryTypeId, ct);
            return Ok(ToPictureDto(row));
        }
        catch
        {
            if (newStoredFileName != null) DeleteStoredFile(newStoredFileName);
            throw;
        }
    }

    [HttpGet("pictures/{id:long}/content")]
    public async Task<IActionResult> GetPictureContent(long id, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(DocumentsRoute, "VIEW", ct)) return Forbid();
        var row = await _db.LibraryDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.AssetKind == PictureKind, ct);
        if (row == null) return NotFound(new { message = "Library picture not found." });
        var path = GetStoredPath(row.StoredFileName);
        if (!System.IO.File.Exists(path)) return NotFound(new { message = "The stored picture is unavailable." });
        return PhysicalFile(path, row.ContentType, enableRangeProcessing: true);
    }

    [HttpGet("pictures/{id:long}/download")]
    public async Task<IActionResult> DownloadPicture(long id, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(DocumentsRoute, "VIEW", ct)) return Forbid();
        var row = await _db.LibraryDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.AssetKind == PictureKind, ct);
        if (row == null) return NotFound(new { message = "Library picture not found." });
        var path = GetStoredPath(row.StoredFileName);
        if (!System.IO.File.Exists(path)) return NotFound(new { message = "The stored picture is unavailable." });
        return PhysicalFile(path, row.ContentType, row.OriginalFileName, enableRangeProcessing: true);
    }

    [HttpDelete("pictures/{id:long}")]
    public async Task<IActionResult> DeletePicture(long id, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(DocumentsRoute, "DELETE", ct)) return Forbid();
        var row = await _db.LibraryDocuments.SingleOrDefaultAsync(x => x.Id == id && x.AssetKind == PictureKind, ct);
        if (row == null) return NotFound(new { message = "Library picture not found." });
        var storedFileName = row.StoredFileName;
        _db.LibraryDocuments.Remove(row);
        await _db.SaveChangesAsync(ct);
        DeleteStoredFile(storedFileName);
        return Ok(new { message = "Library picture deleted successfully." });
    }

    [HttpGet("invoices")]
    public async Task<IActionResult> GetInvoices(CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin) return Ok(Array.Empty<InvoiceDto>());
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(InvoicesRoute, "VIEW", ct)) return Forbid();
        var rows = await _db.GeneratedInvoices.AsNoTracking().Include(x => x.Lines)
            .OrderByDescending(x => x.IssueDate).ThenByDescending(x => x.Id)
            .ToListAsync(ct);
        return Ok(rows.Select(ToInvoiceDto));
    }

    [HttpGet("invoices/{id:long}")]
    public async Task<IActionResult> GetInvoice(long id, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(InvoicesRoute, "VIEW", ct)) return Forbid();
        var row = await _db.GeneratedInvoices.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
        return row == null ? NotFound(new { message = "Invoice not found." }) : Ok(ToInvoiceDto(row));
    }

    [HttpPost("invoices")]
    public async Task<IActionResult> CreateInvoice([FromBody] SaveInvoiceDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(InvoicesRoute, "ADD", ct)) return Forbid();
        var validation = ValidateInvoice(dto);
        if (validation != null) return BadRequest(new { message = validation });
        var invoiceNumber = string.IsNullOrWhiteSpace(dto.InvoiceNumber)
            ? $"INV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..23].ToUpperInvariant()
            : dto.InvoiceNumber.Trim().ToUpperInvariant();
        if (await _db.GeneratedInvoices.AnyAsync(x => x.InvoiceNumber == invoiceNumber, ct))
            return Conflict(new { message = "This invoice number already exists." });

        var row = BuildInvoice(dto, invoiceNumber);
        row.TenantId = _tenant.RequiredTenantId;
        row.CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        row.CreatedOnUtc = DateTime.UtcNow;
        _db.GeneratedInvoices.Add(row);
        await _db.SaveChangesAsync(ct);
        return Ok(ToInvoiceDto(row));
    }

    [HttpPut("invoices/{id:long}")]
    public async Task<IActionResult> UpdateInvoice(long id, [FromBody] SaveInvoiceDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(InvoicesRoute, "EDIT", ct)) return Forbid();
        var validation = ValidateInvoice(dto);
        if (validation != null) return BadRequest(new { message = validation });
        var row = await _db.GeneratedInvoices.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Invoice not found." });
        var number = string.IsNullOrWhiteSpace(dto.InvoiceNumber) ? row.InvoiceNumber : dto.InvoiceNumber.Trim().ToUpperInvariant();
        if (await _db.GeneratedInvoices.AsNoTracking().AnyAsync(x => x.Id != id && x.InvoiceNumber == number, ct))
            return Conflict(new { message = "This invoice number already exists." });

        _db.GeneratedInvoiceLines.RemoveRange(row.Lines);
        ApplyInvoice(row, dto, number);
        _db.GeneratedInvoiceLines.AddRange(row.Lines);
        row.UpdatedOnUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(ToInvoiceDto(row));
    }

    [HttpDelete("invoices/{id:long}")]
    public async Task<IActionResult> DeleteInvoice(long id, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync(InvoicesRoute, "DELETE", ct)) return Forbid();
        var row = await _db.GeneratedInvoices.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Invoice not found." });
        _db.GeneratedInvoices.Remove(row);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Invoice deleted successfully." });
    }

    private async Task<bool> HasActionAsync(string route, string action, CancellationToken ct)
    {
        if (TenantPermissionService.IsSuperAdmin(User)) return true;
        if (TenantPermissionService.IsTenantAdmin(User))
            return await _tenantPermissions.HasMenuRouteAsync(User, [route], action, ct);
        if (!_tenant.TenantId.HasValue) return false;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return false;
        var staffId = await _db.Persons.AsNoTracking().Where(x => x.IdentityUserId == userId && x.Staff != null)
            .Select(x => (Guid?)x.Staff!.StaffId).FirstOrDefaultAsync(ct);
        if (!staffId.HasValue) return false;
        var menuId = await _db.Menus.AsNoTracking().Where(x => x.IsActive && x.Route == route)
            .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (!menuId.HasValue) return false;
        var normalized = action.Trim().ToUpperInvariant();
        if (normalized == "VIEW" && await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}")) return true;
        return await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_{normalized}");
    }

    private async Task<string?> ValidateDocumentAsync(SaveLibraryDocumentForm form, bool requireFile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(form.Title)) return "Document title is required.";
        if (form.Title.Trim().Length > 200) return "Document title must be 200 characters or less.";
        if (!string.IsNullOrWhiteSpace(form.Description) && form.Description.Trim().Length > 1000) return "Description must be 1000 characters or less.";
        if (!await _db.LibraryTypes.AnyAsync(x => x.Id == form.LibraryTypeId && x.IsActive, ct)) return "Select an active library type.";
        if (requireFile && (form.File == null || form.File.Length == 0)) return "Select a file to upload.";
        if (form.File is not { Length: > 0 }) return null;
        if (form.File.Length > MaximumFileSize) return "The selected file is larger than the 50 MB limit.";
        var extension = Path.GetExtension(form.File.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
            return $"File type {extension} is not supported.";
        return null;
    }

    private async Task<string?> ValidatePictureAsync(SaveLibraryDocumentForm form, bool requireFile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(form.Title)) return "Picture name is required.";
        if (form.Title.Trim().Length > 200) return "Picture name must be 200 characters or less.";
        if (!string.IsNullOrWhiteSpace(form.Description) && form.Description.Trim().Length > 1000)
            return "Remarks must be 1000 characters or less.";
        if (!await _db.LibraryTypes.AnyAsync(x => x.Id == form.LibraryTypeId && x.IsActive, ct))
            return "Select an active library type.";
        if (requireFile && (form.File == null || form.File.Length == 0)) return "Select a picture to upload.";
        if (form.File is not { Length: > 0 }) return null;
        if (form.File.Length > MaximumFileSize) return "The selected picture is larger than the 50 MB limit.";
        var extension = Path.GetExtension(form.File.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !ImageExtensions.Contains(extension))
            return "Only PNG, JPG, JPEG, and WEBP pictures are supported.";
        return null;
    }

    private async Task<string?> ValidateTemplateAsync(SaveLibraryTemplateDto dto, CancellationToken ct)
    {
        if (!await _db.LibraryTypes.AnyAsync(x => x.Id == dto.LibraryTypeId && x.IsActive, ct))
            return "Select an active library type.";
        if (string.IsNullOrWhiteSpace(dto.Name)) return "Template name is required.";
        if (dto.Name.Trim().Length > 200) return "Template name must be 200 characters or less.";
        if (!string.IsNullOrWhiteSpace(dto.Description) && dto.Description.Trim().Length > 1000)
            return "Description must be 1000 characters or less.";
        if (string.IsNullOrWhiteSpace(dto.Content)) return "Template content is required.";
        if (dto.Content.Length > MaximumTemplateLength)
            return $"Template content must be {MaximumTemplateLength:N0} characters or less.";
        return null;
    }

    private async Task<string> SaveFileAsync(IFormFile file, CancellationToken ct)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var path = GetStoredPath(storedFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await file.CopyToAsync(stream, ct);
        return storedFileName;
    }

    private string GetStoredPath(string storedFileName)
    {
        if (!_tenant.TenantId.HasValue) throw new InvalidOperationException("A tenant context is required for library storage.");
        var safeName = Path.GetFileName(storedFileName);
        if (!string.Equals(safeName, storedFileName, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid stored library file name.");
        return Path.Combine(_environment.ContentRootPath, "App_Data", "library", _tenant.RequiredTenantId.ToString(), safeName);
    }

    private void DeleteStoredFile(string storedFileName)
    {
        try
        {
            var path = GetStoredPath(storedFileName);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not delete stored library file {StoredFileName}.", storedFileName);
        }
    }

    private static string? ValidateType(SaveLibraryTypeDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code)) return "Type code is required.";
        if (dto.Code.Trim().Length > 30) return "Type code must be 30 characters or less.";
        if (string.IsNullOrWhiteSpace(dto.Name)) return "Type name is required.";
        if (dto.Name.Trim().Length > 120) return "Type name must be 120 characters or less.";
        if (!string.IsNullOrWhiteSpace(dto.Description) && dto.Description.Trim().Length > 500) return "Description must be 500 characters or less.";
        return null;
    }

    private static string? ValidateCategory(SaveLibraryCategoryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code)) return "Category code is required.";
        if (dto.Code.Trim().Length > 30) return "Category code must be 30 characters or less.";
        if (string.IsNullOrWhiteSpace(dto.Name)) return "Category name is required.";
        if (dto.Name.Trim().Length > 120) return "Category name must be 120 characters or less.";
        if (!string.IsNullOrWhiteSpace(dto.Description) && dto.Description.Trim().Length > 500) return "Description must be 500 characters or less.";
        return null;
    }

    private async Task<string?> ValidateSubTypeAsync(SaveLibrarySubTypeDto dto, CancellationToken ct)
    {
        if (!await _db.LibraryTypes.AnyAsync(x => x.Id == dto.LibraryTypeId && x.IsActive, ct)) return "Select an active library type.";
        if (string.IsNullOrWhiteSpace(dto.Code)) return "Sub type code is required.";
        if (dto.Code.Trim().Length > 30) return "Sub type code must be 30 characters or less.";
        if (string.IsNullOrWhiteSpace(dto.Name)) return "Sub type name is required.";
        if (dto.Name.Trim().Length > 120) return "Sub type name must be 120 characters or less.";
        if (!string.IsNullOrWhiteSpace(dto.Description) && dto.Description.Trim().Length > 500) return "Description must be 500 characters or less.";
        return null;
    }

    private static string? ValidateInvoice(SaveInvoiceDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.InvoiceNumber) && dto.InvoiceNumber.Trim().Length > 50) return "Invoice number must be 50 characters or less.";
        if (string.IsNullOrWhiteSpace(dto.CustomerName)) return "Customer name is required.";
        if (dto.CustomerName.Trim().Length > 200) return "Customer name must be 200 characters or less.";
        if (dto.DueDate.HasValue && dto.DueDate.Value < dto.IssueDate) return "Due date cannot be earlier than issue date.";
        if (dto.Lines.Count == 0) return "Add at least one invoice line.";
        if (dto.Lines.Count > 100) return "An invoice cannot contain more than 100 lines.";
        if (dto.Lines.Any(x => string.IsNullOrWhiteSpace(x.Description) || x.Quantity <= 0 || x.UnitPrice < 0))
            return "Every invoice line requires a description, a quantity greater than zero, and a non-negative unit price.";
        if (dto.TaxRate is < 0 or > 100) return "Tax rate must be between 0 and 100 percent.";
        if (dto.DiscountAmount < 0) return "Discount cannot be negative.";
        if (!InvoiceStatuses.Contains(dto.Status)) return "Invoice status must be Draft, Issued, Paid, or Cancelled.";
        return null;
    }

    private GeneratedInvoice BuildInvoice(SaveInvoiceDto dto, string number)
    {
        var row = new GeneratedInvoice();
        ApplyInvoice(row, dto, number);
        return row;
    }

    private static void ApplyInvoice(GeneratedInvoice row, SaveInvoiceDto dto, string number)
    {
        row.InvoiceNumber = number;
        row.CustomerName = dto.CustomerName.Trim();
        row.CustomerEmail = Clean(dto.CustomerEmail);
        row.CustomerAddress = Clean(dto.CustomerAddress);
        row.IssueDate = dto.IssueDate;
        row.DueDate = dto.DueDate;
        row.Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "PKR" : dto.Currency.Trim().ToUpperInvariant()[..Math.Min(10, dto.Currency.Trim().Length)];
        row.TaxRate = decimal.Round(dto.TaxRate, 4, MidpointRounding.AwayFromZero);
        row.DiscountAmount = decimal.Round(dto.DiscountAmount, 2, MidpointRounding.AwayFromZero);
        row.Status = InvoiceStatuses.First(x => x.Equals(dto.Status, StringComparison.OrdinalIgnoreCase));
        row.Notes = Clean(dto.Notes);
        row.Lines = dto.Lines.Select((line, index) =>
        {
            var quantity = decimal.Round(line.Quantity, 4, MidpointRounding.AwayFromZero);
            var unitPrice = decimal.Round(line.UnitPrice, 2, MidpointRounding.AwayFromZero);
            return new GeneratedInvoiceLine
            {
                Description = line.Description.Trim(),
                Quantity = quantity,
                UnitPrice = unitPrice,
                LineTotal = decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero),
                DisplayOrder = index + 1
            };
        }).ToList();
        row.Subtotal = row.Lines.Sum(x => x.LineTotal);
        row.TaxAmount = decimal.Round(row.Subtotal * row.TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
        row.TotalAmount = Math.Max(0, row.Subtotal + row.TaxAmount - row.DiscountAmount);
    }

    private static LibraryCategoryDto ToCategoryDto(LibraryCategory row) =>
        new(row.Id, row.Code, row.Name, row.Description, row.DisplayOrder, row.IsActive);

    private static LibraryTypeDto ToTypeDto(LibraryType row, int documentCount, int subTypeCount = 0) =>
        new(row.Id, row.Code, row.Name, row.Description, row.DisplayOrder, row.IsHardCopyRequired, row.IsActive, documentCount, subTypeCount);

    private static LibrarySubTypeDto ToSubTypeDto(LibrarySubType row) =>
        new(row.Id, row.LibraryTypeId, row.LibraryType?.Name ?? string.Empty, row.Code, row.Name, row.Description, row.DisplayOrder, row.IsActive);

    private static LibraryDocumentDto ToDocumentDto(LibraryDocument row) => new(
        row.Id, row.LibraryTypeId, row.LibraryType?.Name ?? string.Empty, row.Title, row.Description,
        row.OriginalFileName, row.ContentType, row.FileExtension, row.FileSizeBytes, row.IsActive,
        row.CreatedOnUtc, $"/api/library/documents/{row.Id}/download");

    private static LibraryTemplateDto ToTemplateDto(LibraryTemplate row) => new(
        row.Id, row.LibraryTypeId, row.LibraryType?.Name ?? string.Empty, row.Name, row.Description,
        row.Content, row.IsActive, row.CreatedOnUtc);

    private static LibraryPictureDto ToPictureDto(LibraryDocument row) => new(
        row.Id, row.LibraryTypeId, row.LibraryType?.Name ?? string.Empty, row.Title, row.Description,
        row.OriginalFileName, row.ContentType, row.FileExtension, row.FileSizeBytes, row.IsActive,
        row.CreatedOnUtc, $"/api/library/pictures/{row.Id}/content", $"/api/library/pictures/{row.Id}/download");

    private static InvoiceDto ToInvoiceDto(GeneratedInvoice row) => new(
        row.Id, row.InvoiceNumber, row.CustomerName, row.CustomerEmail, row.CustomerAddress,
        row.IssueDate, row.DueDate, row.Currency, row.Subtotal, row.TaxRate, row.TaxAmount,
        row.DiscountAmount, row.TotalAmount, row.Status, row.Notes, row.CreatedOnUtc,
        row.Lines.OrderBy(x => x.DisplayOrder).Select(x => new InvoiceLineDto(x.Id, x.Description, x.Quantity, x.UnitPrice, x.LineTotal, x.DisplayOrder)).ToList());

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string CleanContentType(string? value) => string.IsNullOrWhiteSpace(value) ? "application/octet-stream" : value.Trim()[..Math.Min(150, value.Trim().Length)];
    private static string PictureContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };
}

public sealed record LibraryCategoryDto(int Id, string Code, string Name, string? Description, int DisplayOrder, bool IsActive);
public sealed record SaveLibraryCategoryDto(string Code, string Name, string? Description, int DisplayOrder, bool IsActive = true);
public sealed record LibraryTypeDto(int Id, string Code, string Name, string? Description, int DisplayOrder, bool IsHardCopyRequired, bool IsActive, int DocumentCount, int SubTypeCount);
public sealed record SaveLibraryTypeDto(string Code, string Name, string? Description, int DisplayOrder, bool IsHardCopyRequired, bool IsActive = true);
public sealed record LibrarySubTypeDto(int Id, int LibraryTypeId, string LibraryTypeName, string Code, string Name, string? Description, int DisplayOrder, bool IsActive);
public sealed record SaveLibrarySubTypeDto(int LibraryTypeId, string Code, string Name, string? Description, int DisplayOrder, bool IsActive = true);

public sealed class SaveLibraryDocumentForm
{
    public int LibraryTypeId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public IFormFile? File { get; set; }
}

public sealed record LibraryDocumentDto(long Id, int LibraryTypeId, string LibraryTypeName, string Title, string? Description,
    string OriginalFileName, string ContentType, string FileExtension, long FileSizeBytes, bool IsActive, DateTime CreatedOnUtc, string DownloadUrl);

public sealed record SaveLibraryTemplateDto(int LibraryTypeId, string Name, string? Description, string Content, bool IsActive = true);
public sealed record LibraryTemplateDto(long Id, int LibraryTypeId, string LibraryTypeName, string Name, string? Description,
    string Content, bool IsActive, DateTime CreatedOnUtc);
public sealed record LibraryPictureDto(long Id, int LibraryTypeId, string LibraryTypeName, string Title, string? Description,
    string OriginalFileName, string ContentType, string FileExtension, long FileSizeBytes, bool IsActive,
    DateTime CreatedOnUtc, string ContentUrl, string DownloadUrl);

public sealed class SaveInvoiceDto
{
    public string? InvoiceNumber { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public string? CustomerAddress { get; set; }
    public DateOnly IssueDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly? DueDate { get; set; }
    public string Currency { get; set; } = "PKR";
    public decimal TaxRate { get; set; }
    public decimal DiscountAmount { get; set; }
    public string Status { get; set; } = "Draft";
    public string? Notes { get; set; }
    public List<SaveInvoiceLineDto> Lines { get; set; } = [];
}

public sealed record SaveInvoiceLineDto(string Description, decimal Quantity, decimal UnitPrice);
public sealed record InvoiceLineDto(long Id, string Description, decimal Quantity, decimal UnitPrice, decimal LineTotal, int DisplayOrder);
public sealed record InvoiceDto(long Id, string InvoiceNumber, string CustomerName, string? CustomerEmail, string? CustomerAddress,
    DateOnly IssueDate, DateOnly? DueDate, string Currency, decimal Subtotal, decimal TaxRate, decimal TaxAmount,
    decimal DiscountAmount, decimal TotalAmount, string Status, string? Notes, DateTime CreatedOnUtc, IReadOnlyList<InvoiceLineDto> Lines);
