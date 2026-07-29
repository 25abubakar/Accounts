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
[Route("api/salary-scales")]
[Authorize]
[Produces("application/json")]
public sealed class SalaryScalesController : ControllerBase
{
    private const string RoutePath = "/settings/scales";
    private readonly ApplicationDbContext _db;
    private readonly ITenantService _tenantService;
    private readonly RbacService _rbac;

    public SalaryScalesController(ApplicationDbContext db, ITenantService tenantService, RbacService rbac)
    {
        _db = db;
        _tenantService = tenantService;
        _rbac = rbac;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        if (_tenantService.IsSuperAdmin) return Ok(Array.Empty<SalaryScaleDto>());
        if (!_tenantService.TenantId.HasValue) return Forbid();
        if (!await HasScaleActionAsync("VIEW", ct)) return Forbid();

        var rows = await _db.SalaryScales.AsNoTracking()
            .OrderBy(scale => scale.DisplayOrder == 0 ? int.MaxValue : scale.DisplayOrder)
            .ThenBy(scale => scale.ScaleName)
            .Select(scale => ToDto(scale))
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveSalaryScaleDto dto, CancellationToken ct)
    {
        if (!_tenantService.TenantId.HasValue) return Forbid();
        if (!await HasScaleActionAsync("ADD", ct)) return Forbid();

        var validation = Validate(dto);
        if (validation != null) return BadRequest(new { message = validation });

        var scaleName = dto.ScaleName.Trim();
        var duplicate = await _db.SalaryScales.AsNoTracking()
            .AnyAsync(scale => scale.TenantId == _tenantService.RequiredTenantId && scale.ScaleName == scaleName, ct);
        if (duplicate) return BadRequest(new { message = "This scale name already exists." });

        var scale = new SalaryScale
        {
            TenantId = _tenantService.RequiredTenantId,
            ScaleName = scaleName,
            DisplayOrder = dto.DisplayOrder,
            ScaleType = NormalizeText(dto.ScaleType, "Regular", 50),
            PayMode = NormalizeText(dto.PayMode, "PM", 20),
            BasicSalary = dto.BasicSalary,
            MaximumSalary = dto.MaximumSalary,
            YearlyIncrement = dto.YearlyIncrement,
            GrossSalary = dto.GrossSalary,
            MedicalAllowance = dto.MedicalAllowance,
            TravellingAllowance = dto.TravellingAllowance,
            Other = dto.Other,
            IsActive = dto.IsActive,
            CreatedDate = DateTime.UtcNow
        };
        _db.SalaryScales.Add(scale);
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(scale));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveSalaryScaleDto dto, CancellationToken ct)
    {
        if (!_tenantService.TenantId.HasValue) return Forbid();
        if (!await HasScaleActionAsync("EDIT", ct)) return Forbid();

        var validation = Validate(dto);
        if (validation != null) return BadRequest(new { message = validation });

        var scale = await _db.SalaryScales.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (scale == null) return NotFound(new { message = "Scale not found." });

        var scaleName = dto.ScaleName.Trim();
        var duplicate = await _db.SalaryScales.AsNoTracking()
            .AnyAsync(item => item.TenantId == _tenantService.RequiredTenantId && item.Id != id && item.ScaleName == scaleName, ct);
        if (duplicate) return BadRequest(new { message = "This scale name already exists." });

        scale.ScaleName = scaleName;
        scale.DisplayOrder = dto.DisplayOrder;
        scale.ScaleType = NormalizeText(dto.ScaleType, "Regular", 50);
        scale.PayMode = NormalizeText(dto.PayMode, "PM", 20);
        scale.BasicSalary = dto.BasicSalary;
        scale.MaximumSalary = dto.MaximumSalary;
        scale.YearlyIncrement = dto.YearlyIncrement;
        scale.GrossSalary = dto.GrossSalary;
        scale.MedicalAllowance = dto.MedicalAllowance;
        scale.TravellingAllowance = dto.TravellingAllowance;
        scale.Other = dto.Other;
        scale.IsActive = dto.IsActive;
        scale.ModifiedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(scale));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (!_tenantService.TenantId.HasValue) return Forbid();
        if (!await HasScaleActionAsync("DELETE", ct)) return Forbid();

        var scale = await _db.SalaryScales.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (scale == null) return NotFound(new { message = "Scale not found." });

        _db.SalaryScales.Remove(scale);
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Scale deleted successfully." });
    }

    private async Task<bool> HasScaleActionAsync(string action, CancellationToken ct)
    {
        if (_tenantService.IsTenantAdmin || User.IsInRole("Admin") || User.IsInRole("TenantAdmin"))
            return true;
        if (!_tenantService.TenantId.HasValue) return false;

        var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(identityUserId)) return false;

        var staffId = await _db.Persons.AsNoTracking()
            .Where(person => person.IdentityUserId == identityUserId && person.Staff != null)
            .Select(person => (Guid?)person.Staff!.StaffId)
            .FirstOrDefaultAsync(ct);
        if (!staffId.HasValue) return false;

        var menuId = await _db.Menus.AsNoTracking()
            .Where(menu => menu.IsActive && menu.Route == RoutePath)
            .Select(menu => (int?)menu.Id)
            .FirstOrDefaultAsync(ct);
        if (!menuId.HasValue) return false;

        var normalizedAction = action.Trim().ToUpperInvariant();
        if (normalizedAction == "VIEW" && await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}"))
            return true;
        return await _rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_{normalizedAction}");
    }

    private static string? Validate(SaveSalaryScaleDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ScaleName)) return "Scale name is required.";
        if (dto.ScaleName.Trim().Length > 100) return "Scale name must be 100 characters or less.";
        if (!string.IsNullOrWhiteSpace(dto.ScaleType) && dto.ScaleType.Trim().Length > 50) return "Scale type must be 50 characters or less.";
        if (!string.IsNullOrWhiteSpace(dto.PayMode) && dto.PayMode.Trim().Length > 20) return "Pay mode must be 20 characters or less.";
        if (dto.MaximumSalary > 0 && dto.MaximumSalary < dto.BasicSalary) return "Maximum salary must be greater than or equal to basic salary.";
        if (new[] { dto.BasicSalary, dto.MaximumSalary, dto.YearlyIncrement, dto.GrossSalary, dto.MedicalAllowance, dto.TravellingAllowance, dto.Other }.Any(value => value < 0))
            return "Salary and allowance values cannot be negative.";
        return null;
    }

    private static string NormalizeText(string? value, string fallback, int maxLength)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static SalaryScaleDto ToDto(SalaryScale scale) => new()
    {
        Id = scale.Id,
        ScaleName = scale.ScaleName,
        DisplayOrder = scale.DisplayOrder,
        ScaleType = scale.ScaleType,
        PayMode = scale.PayMode,
        BasicSalary = scale.BasicSalary,
        MaximumSalary = scale.MaximumSalary,
        YearlyIncrement = scale.YearlyIncrement,
        GrossSalary = scale.GrossSalary,
        MedicalAllowance = scale.MedicalAllowance,
        TravellingAllowance = scale.TravellingAllowance,
        Other = scale.Other,
        IsActive = scale.IsActive
    };
}

public sealed class SalaryScaleDto
{
    public int Id { get; set; }
    public string ScaleName { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public string ScaleType { get; set; } = "Regular";
    public string PayMode { get; set; } = "PM";
    public decimal BasicSalary { get; set; }
    public decimal MaximumSalary { get; set; }
    public decimal YearlyIncrement { get; set; }
    public decimal GrossSalary { get; set; }
    public decimal MedicalAllowance { get; set; }
    public decimal TravellingAllowance { get; set; }
    public decimal Other { get; set; }
    public bool IsActive { get; set; }
}

public sealed class SaveSalaryScaleDto
{
    public string ScaleName { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public string? ScaleType { get; set; }
    public string? PayMode { get; set; }
    public decimal BasicSalary { get; set; }
    public decimal MaximumSalary { get; set; }
    public decimal YearlyIncrement { get; set; }
    public decimal GrossSalary { get; set; }
    public decimal MedicalAllowance { get; set; }
    public decimal TravellingAllowance { get; set; }
    public decimal Other { get; set; }
    public bool IsActive { get; set; } = true;
}
