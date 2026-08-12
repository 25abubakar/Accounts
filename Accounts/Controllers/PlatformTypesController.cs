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
[AutoValidateAntiforgeryToken]
[Produces("application/json")]
public sealed partial class PlatformTypesController : ControllerBase
{
    private const string RoutePath = "/settings/types";
    private const string LegacyRoutePath = "/settings/statuses";
    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenant;
    private readonly RbacService _rbac;
    private readonly TenantPermissionService _tenantPermissions;

    public PlatformTypesController(ApplicationDbContext db, ITenantService tenant, RbacService rbac, TenantPermissionService tenantPermissions)
    {
        _db = db;
        _tenant = tenant;
        _rbac = rbac;
        _tenantPermissions = tenantPermissions;
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
            .ToListAsync(ct);
        var values = await _db.Set<PlatformTypeTableRow>().AsNoTracking()
            .OrderBy(value => value.DisplayOrder == 0 ? int.MaxValue : value.DisplayOrder)
            .ThenBy(value => value.Name)
            .ToListAsync(ct);
        var designations = await _db.JobTitles.AsNoTracking()
            .OrderBy(title => title.TitleName)
            .ToListAsync(ct);

        var result = categories.Select(category => new PlatformTypeCategoryDto
        {
            Id = category.Id,
            Code = category.Code,
            Name = category.Name,
            Icon = category.Icon,
            DisplayOrder = category.DisplayOrder,
            Values = category.Code == "DESIGNATION"
                ? designations.Select((title, index) => new PlatformTypeValueDto
                {
                    Id = title.Id,
                    CategoryId = category.Id,
                    Name = title.TitleName,
                    Code = $"DESIGNATION_{title.Id}",
                    DisplayOrder = index + 1,
                    IsActive = true
                }).ToList()
                : values.Where(value => CategoryCodeFor(value) == category.Code)
                    .Select(value => ToDto(value, category.Id))
                    .ToList()
        }).ToList();

        return Ok(result);
    }

    [HttpPost("{categoryId:int}/values")]
    public async Task<IActionResult> Create(int categoryId, [FromBody] SavePlatformTypeValueDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync("ADD", ct)) return Forbid();
        var validation = Validate(dto);
        if (validation != null) return BadRequest(new { message = validation });
        var category = await _db.PlatformTypeCategories.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == categoryId && x.IsActive, ct);
        if (category == null)
            return NotFound(new { message = "Type category not found." });
        if (category.Code == "DESIGNATION")
            return BadRequest(new { message = "Designations are managed through the designation master." });

        var name = dto.Name.Trim();
        var code = NormalizeCode(dto.Code, name);
        if (await CodeExistsAsync(category.Code, code, null, ct))
            return BadRequest(new { message = "This value already exists in the selected type." });

        var row = CreateRow(category.Code, name, code, dto);
        if (row == null) return BadRequest(new { message = "This type category is not configured." });
        _db.Add(row);
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(row, categoryId));
    }

    [HttpPut("values/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SavePlatformTypeValueDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync("EDIT", ct)) return Forbid();
        var validation = Validate(dto);
        if (validation != null) return BadRequest(new { message = validation });

        var row = await _db.Set<PlatformTypeTableRow>().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Type value not found." });
        var categoryCode = CategoryCodeFor(row);
        var categoryId = await _db.PlatformTypeCategories.AsNoTracking()
            .Where(x => x.Code == categoryCode)
            .Select(x => x.Id)
            .SingleAsync(ct);
        var name = dto.Name.Trim();
        var code = NormalizeCode(dto.Code, name);
        if (await CodeExistsAsync(categoryCode, code, id, ct))
            return BadRequest(new { message = "This value already exists in the selected type." });

        row.Name = name;
        row.Code = code;
        row.DisplayOrder = dto.DisplayOrder;
        row.IsActive = dto.IsActive;
        row.ModifiedOnUtc = DateTime.UtcNow;
        row.ModifiedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(row, categoryId));
    }

    [HttpDelete("values/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue || !await HasActionAsync("DELETE", ct)) return Forbid();
        var row = await _db.Set<PlatformTypeTableRow>().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return NotFound(new { message = "Type value not found." });
        _db.Remove(row);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Type value deleted successfully." });
    }

    private async Task<bool> HasActionAsync(string action, CancellationToken ct)
    {
        if (TenantPermissionService.IsSuperAdmin(User)) return true;
        if (TenantPermissionService.IsTenantAdmin(User))
            return await _tenantPermissions.HasMenuRouteAsync(User, [RoutePath, LegacyRoutePath], action, ct);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return false;
        var staffId = await _db.Persons.AsNoTracking().Where(x => x.IdentityUserId == userId && x.Staff != null)
            .Select(x => (Guid?)x.Staff!.StaffId).FirstOrDefaultAsync(ct);
        if (!staffId.HasValue) return false;
        var menuIds = await _db.Menus.AsNoTracking()
            .Where(x => x.IsActive && (x.Route == RoutePath || x.Route == LegacyRoutePath))
            .Select(x => x.Id)
            .ToListAsync(ct);
        if (menuIds.Count == 0) return false;
        var normalized = action.Trim().ToUpperInvariant();

        // A tier-1 menu grant is sufficient to read the screen. Some existing
        // users were granted the menu before explicit VIEW feature rows were
        // introduced, so requiring only a feature key leaves the menu visible
        // while its API incorrectly returns 403.
        if (normalized == "VIEW" && await _db.StaffMenuAccesses.AsNoTracking()
                .AnyAsync(x => x.StaffId == staffId.Value && menuIds.Contains(x.MenuId) && x.IsAllow, ct))
            return true;

        foreach (var menuId in menuIds)
        {
            if (normalized == "VIEW" && await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId}"))
                return true;
            if (await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId}_{normalized}"))
                return true;
        }
        return false;
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

    private PlatformTypeTableRow? CreateRow(string categoryCode, string name, string code, SavePlatformTypeValueDto dto)
    {
        PlatformTypeTableRow? row = categoryCode switch
        {
            "CONTRACT" => new ContractType(),
            "FREQUENCY" => new FrequencyType(),
            "RATE" => new RateType(),
            "ALLOWANCE_TYPE" => new AllowanceType(),
            "TADA_TYPE" => new TadaType(),
            "LEAVE_TYPE" => new LeaveType(),
            "ANNOUNCEMENT_TYPE" => new AnnouncementType(),
            "ASSESSMENT_TYPE" => new AssessmentType(),
            "ATTENDANCE_TYPE" => new AttendanceType(),
            "BENEFITS_TYPE" => new BenefitType(),
            _ => null
        };
        if (row == null) return null;
        row.TenantId = _tenant.RequiredTenantId;
        row.Name = name;
        row.Code = code;
        row.DisplayOrder = dto.DisplayOrder;
        row.IsActive = dto.IsActive;
        row.CreatedOnUtc = DateTime.UtcNow;
        row.CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return row;
    }

    private Task<bool> CodeExistsAsync(string categoryCode, string code, int? excludeId, CancellationToken ct) => categoryCode switch
    {
        "CONTRACT" => ExistsInAsync<ContractType>(code, excludeId, ct),
        "FREQUENCY" => ExistsInAsync<FrequencyType>(code, excludeId, ct),
        "RATE" => ExistsInAsync<RateType>(code, excludeId, ct),
        "ALLOWANCE_TYPE" => ExistsInAsync<AllowanceType>(code, excludeId, ct),
        "TADA_TYPE" => ExistsInAsync<TadaType>(code, excludeId, ct),
        "LEAVE_TYPE" => ExistsInAsync<LeaveType>(code, excludeId, ct),
        "ANNOUNCEMENT_TYPE" => ExistsInAsync<AnnouncementType>(code, excludeId, ct),
        "ASSESSMENT_TYPE" => ExistsInAsync<AssessmentType>(code, excludeId, ct),
        "ATTENDANCE_TYPE" => ExistsInAsync<AttendanceType>(code, excludeId, ct),
        "BENEFITS_TYPE" => ExistsInAsync<BenefitType>(code, excludeId, ct),
        _ => Task.FromResult(false)
    };

    private Task<bool> ExistsInAsync<TEntity>(string code, int? excludeId, CancellationToken ct)
        where TEntity : PlatformTypeTableRow =>
        _db.Set<TEntity>().AnyAsync(row => row.Code == code && (!excludeId.HasValue || row.Id != excludeId.Value), ct);

    private static string CategoryCodeFor(PlatformTypeTableRow row) => row switch
    {
        ContractType => "CONTRACT",
        FrequencyType => "FREQUENCY",
        RateType => "RATE",
        AllowanceType => "ALLOWANCE_TYPE",
        TadaType => "TADA_TYPE",
        LeaveType => "LEAVE_TYPE",
        AnnouncementType => "ANNOUNCEMENT_TYPE",
        AssessmentType => "ASSESSMENT_TYPE",
        AttendanceType => "ATTENDANCE_TYPE",
        BenefitType => "BENEFITS_TYPE",
        _ => throw new InvalidOperationException("Unknown platform type table.")
    };

    private static PlatformTypeValueDto ToDto(PlatformTypeTableRow row, int categoryId) => new()
    {
        Id = row.Id, CategoryId = categoryId, Name = row.Name, Code = row.Code,
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
