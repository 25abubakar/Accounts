using System.Security.Claims;
using System.Text.RegularExpressions;
using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Route("api/platform-types")]
[Authorize]
[Produces("application/json")]
public sealed partial class PlatformTypesController : ControllerBase
{
    private const string RoutePath = "/settings/types";
    private const string LegacyRoutePath = "/settings/statuses";
    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;
    private readonly RbacService _rbac;

    public PlatformTypesController(ApplicationDbContext db, ITenantService tenant, RbacService rbac)
    {
        _db = db;
        _tenant = tenant;
        _rbac = rbac;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (_tenant.IsSuperAdmin) return Ok(Array.Empty<PlatformTypeCategoryDto>());
        if (!_tenant.TenantId.HasValue) return Forbid();
        if (!await HasActionAsync("VIEW", ct)) return Forbid();

        var categories = await _db.PlatformTypeCategories.AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.DisplayOrder)
            .ThenBy(category => category.Name)
            .Select(category => new PlatformTypeCategoryDto
            {
                Id = category.Id,
                Code = category.Code,
                Name = category.Name,
                Icon = category.Icon,
                DisplayOrder = category.DisplayOrder,
                Values = category.Values
                    .OrderBy(value => value.DisplayOrder == 0 ? int.MaxValue : value.DisplayOrder)
                    .ThenBy(value => value.Name)
                    .Select(value => new PlatformTypeValueDto
                    {
                        Id = value.Id,
                        CategoryId = value.CategoryId,
                        Name = value.Name,
                        Code = value.Code,
                        DisplayOrder = value.DisplayOrder,
                        IsActive = value.IsActive
                    }).ToList()
            })
            .ToListAsync(ct);

        return Ok(categories);
    }

    [HttpPost("{categoryId:int}/values")]
    public async Task<IActionResult> Create(int categoryId, [FromBody] SavePlatformTypeValueDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync("ADD", ct)) return Forbid();
        var validation = Validate(dto);
        if (validation != null) return BadRequest(new { message = validation });
        if (!await _db.PlatformTypeCategories.AnyAsync(x => x.Id == categoryId && x.IsActive, ct))
            return NotFound(new { message = "Type category not found." });

        var name = dto.Name.Trim();
        var code = NormalizeCode(dto.Code, name);
        if (await _db.PlatformTypeValues.AnyAsync(x => x.CategoryId == categoryId && x.Code == code, ct))
            return BadRequest(new { message = "This value already exists in the selected type." });

        var row = new PlatformTypeValue
        {
            TenantId = _tenant.RequiredTenantId,
            CategoryId = categoryId,
            Name = name,
            Code = code,
            DisplayOrder = dto.DisplayOrder,
            IsActive = dto.IsActive,
            CreatedOnUtc = DateTime.UtcNow,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
        };
        _db.PlatformTypeValues.Add(row);
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(row));
    }

    [HttpPut("values/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SavePlatformTypeValueDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync("EDIT", ct)) return Forbid();
        var validation = Validate(dto);
        if (validation != null) return BadRequest(new { message = validation });

        var row = await _db.PlatformTypeValues.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Type value not found." });
        var name = dto.Name.Trim();
        var code = NormalizeCode(dto.Code, name);
        if (await _db.PlatformTypeValues.AnyAsync(x => x.CategoryId == row.CategoryId && x.Id != id && x.Code == code, ct))
            return BadRequest(new { message = "This value already exists in the selected type." });

        row.Name = name;
        row.Code = code;
        row.DisplayOrder = dto.DisplayOrder;
        row.IsActive = dto.IsActive;
        row.ModifiedOnUtc = DateTime.UtcNow;
        row.ModifiedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(row));
    }

    [HttpDelete("values/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync("DELETE", ct)) return Forbid();
        var row = await _db.PlatformTypeValues.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Type value not found." });
        _db.PlatformTypeValues.Remove(row);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Type value deleted successfully." });
    }

    private async Task<bool> HasActionAsync(string action, CancellationToken ct)
    {
        if (_tenant.IsTenantAdmin || User.IsInRole("Admin") || User.IsInRole("TenantAdmin")) return true;
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return false;
        var staffId = await _db.Persons.AsNoTracking().Where(x => x.IdentityUserId == userId && x.Staff != null)
            .Select(x => (Guid?)x.Staff!.StaffId).FirstOrDefaultAsync(ct);
        if (!staffId.HasValue) return false;
        var menuId = await _db.Menus.AsNoTracking()
            .Where(x => x.IsActive && (x.Route == RoutePath || x.Route == LegacyRoutePath))
            .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (!menuId.HasValue) return false;
        var normalized = action.Trim().ToUpperInvariant();
        return normalized == "VIEW" && await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}")
            || await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_{normalized}");
    }

    private static string? Validate(SavePlatformTypeValueDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return "Name is required.";
        if (dto.Name.Trim().Length > 150) return "Name must be 150 characters or less.";
        if (!string.IsNullOrWhiteSpace(dto.Code) && dto.Code.Trim().Length > 100) return "Code must be 100 characters or less.";
        if (dto.DisplayOrder < 0) return "Display order cannot be negative.";
        return null;
    }

    private static string NormalizeCode(string? code, string name)
    {
        var source = string.IsNullOrWhiteSpace(code) ? name : code.Trim();
        var normalized = NonCodeCharacter().Replace(source.ToUpperInvariant(), "_").Trim('_');
        return normalized.Length <= 100 ? normalized : normalized[..100];
    }

    private static PlatformTypeValueDto ToDto(PlatformTypeValue row) => new()
    {
        Id = row.Id, CategoryId = row.CategoryId, Name = row.Name, Code = row.Code,
        DisplayOrder = row.DisplayOrder, IsActive = row.IsActive
    };

    [GeneratedRegex("[^A-Z0-9]+")]
    private static partial Regex NonCodeCharacter();
}

public sealed class PlatformTypeCategoryDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public List<PlatformTypeValueDto> Values { get; set; } = [];
}

public sealed class PlatformTypeValueDto
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
}

public sealed class SavePlatformTypeValueDto
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
