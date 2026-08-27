using System.Security.Claims;
using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController, Route("api/pay-allowances/pay-scale-setup"), Authorize, Produces("application/json")]
public sealed class PayScaleSetupController(
    ApplicationDbContext db, ITenantService tenant, RbacService rbac,
    TenantPermissionService tenantPermissions) : ControllerBase
{
    private const string RoutePath = "/pay-allowances/pay-scale";

    [HttpGet("rule-registrations")]
    public async Task<IActionResult> RuleRegistrations(CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        return Ok(await db.PayScaleRuleRegistrations.AsNoTracking()
            .OrderByDescending(x => x.DateFrom).ThenBy(x => x.RuleType).ThenBy(x => x.Name)
            .Select(x => new { x.Id, x.RuleType, x.Name, x.DateFrom, x.DateTo })
            .ToListAsync(ct));
    }

    [HttpPost("rule-registrations")]
    public async Task<IActionResult> CreateRuleRegistration(RuleRegistrationSave dto, CancellationToken ct)
    {
        var denied = await Guard("ADD", ct); if (denied != null) return denied;
        var error = ValidateRuleRegistration(dto); if (error != null) return BadRequest(new { message = error });
        var type = dto.RuleType.Trim(); var name = dto.Name.Trim();
        if (await db.PayScaleRuleRegistrations.AnyAsync(x => x.RuleType == type && x.Name == name, ct))
            return Conflict(new { message = "A rule with the same type and name already exists." });
        var row = new PayScaleRuleRegistration { TenantId = tenant.RequiredTenantId, RuleType = type, Name = name, DateFrom = dto.DateFrom, DateTo = dto.DateTo };
        db.Add(row); await db.SaveChangesAsync(ct);
        return Ok(new { row.Id, row.RuleType, row.Name, row.DateFrom, row.DateTo });
    }

    [HttpPut("rule-registrations/{id:int}")]
    public async Task<IActionResult> UpdateRuleRegistration(int id, RuleRegistrationSave dto, CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        var row = await db.PayScaleRuleRegistrations.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        var error = ValidateRuleRegistration(dto); if (error != null) return BadRequest(new { message = error });
        var type = dto.RuleType.Trim(); var name = dto.Name.Trim();
        if (await db.PayScaleRuleRegistrations.AnyAsync(x => x.Id != id && x.RuleType == type && x.Name == name, ct))
            return Conflict(new { message = "A rule with the same type and name already exists." });
        row.RuleType = type; row.Name = name; row.DateFrom = dto.DateFrom; row.DateTo = dto.DateTo; row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { row.Id, row.RuleType, row.Name, row.DateFrom, row.DateTo });
    }

    [HttpGet("rules")]
    public async Task<IActionResult> Rules(CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        return Ok(await db.PayRules.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct));
    }

    [HttpPost("rules")]
    public async Task<IActionResult> CreateRule(PayRuleSave dto, CancellationToken ct)
    {
        var denied = await Guard("ADD", ct); if (denied != null) return denied;
        var error = ValidateRule(dto); if (error != null) return BadRequest(new { message = error });
        if (await db.PayRules.AnyAsync(x => x.Code == dto.Code.Trim() || x.Name == dto.Name.Trim(), ct)) return Conflict(new { message = "Rule code or name already exists." });
        var row = new PayRule { TenantId = tenant.RequiredTenantId }; Apply(row, dto);
        db.Add(row); await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpPut("rules/{id:int}")]
    public async Task<IActionResult> UpdateRule(int id, PayRuleSave dto, CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        var row = await db.PayRules.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        var error = ValidateRule(dto); if (error != null) return BadRequest(new { message = error });
        if (await db.PayRules.AnyAsync(x => x.Id != id && (x.Code == dto.Code.Trim() || x.Name == dto.Name.Trim()), ct)) return Conflict(new { message = "Rule code or name already exists." });
        Apply(row, dto); row.UpdatedOnUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpGet("masters/{kind}")]
    public async Task<IActionResult> Masters(string kind, [FromQuery] string? allowanceCategory, CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        var normalized = Kind(kind);
        if (normalized == "allowances")
        {
            var category = NormalizeAllowanceCategory(allowanceCategory);
            return Ok(await db.AllowanceTypes.AsNoTracking()
                .Where(x => x.AllowanceCategory == category)
                .OrderBy(x => x.DisplayOrder == 0 ? int.MaxValue : x.DisplayOrder).ThenBy(x => x.Name)
                .Select(x => new { x.Id, x.Code, x.Name, x.DisplayOrder, x.IsActive, x.AllowanceCategory })
                .ToListAsync(ct));
        }
        IQueryable<PlatformTypeTableRow>? query = normalized switch
        {
            "tada" => db.TadaTypes, "leave" => db.LeaveTypes, _ => null
        };
        if (query == null) return NotFound(new { message = "Master type not found." });
        return Ok(await query.AsNoTracking().OrderBy(x => x.DisplayOrder == 0 ? int.MaxValue : x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new { x.Id, x.Code, x.Name, x.DisplayOrder, x.IsActive }).ToListAsync(ct));
    }

    [HttpPost("masters/{kind}")]
    public async Task<IActionResult> CreateMaster(string kind, PlatformMasterSave dto, CancellationToken ct)
    {
        var denied = await Guard("ADD", ct); if (denied != null) return denied;
        var error = ValidateMaster(dto); if (error != null) return BadRequest(new { message = error });
        var normalized = Kind(kind);
        if (await MasterExists(normalized, dto.Code.Trim(), dto.Name.Trim(), null, ct)) return Conflict(new { message = "Code or name already exists in this master." });
        PlatformTypeTableRow? row = normalized switch { "allowances" => new AllowanceType(), "tada" => new TadaType(), "leave" => new LeaveType(), _ => null };
        if (row == null) return NotFound(new { message = "Master type not found." });
        row.TenantId = tenant.RequiredTenantId; Apply(row, dto); row.CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (row is AllowanceType allowance) allowance.AllowanceCategory = NormalizeAllowanceCategory(dto.AllowanceCategory);
        db.Add(row); await db.SaveChangesAsync(ct); return Ok(new { row.Id, row.Code, row.Name, row.DisplayOrder, row.IsActive });
    }

    [HttpPut("masters/{kind}/{id:int}")]
    public async Task<IActionResult> UpdateMaster(string kind, int id, PlatformMasterSave dto, CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        var error = ValidateMaster(dto); if (error != null) return BadRequest(new { message = error });
        var normalized = Kind(kind); var row = await FindMaster(normalized, id, ct); if (row == null) return NotFound();
        if (await MasterExists(normalized, dto.Code.Trim(), dto.Name.Trim(), id, ct)) return Conflict(new { message = "Code or name already exists in this master." });
        Apply(row, dto); row.ModifiedOnUtc = DateTime.UtcNow; row.ModifiedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (row is AllowanceType allowance) allowance.AllowanceCategory = NormalizeAllowanceCategory(dto.AllowanceCategory);
        await db.SaveChangesAsync(ct); return Ok(new { row.Id, row.Code, row.Name, row.DisplayOrder, row.IsActive });
    }

    [HttpGet("packages")]
    public async Task<IActionResult> Packages(CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        return Ok(await db.SalaryPackages.AsNoTracking().OrderBy(x => x.Name).Select(x => new
        {
            x.Id, x.Code, x.Name, x.SalaryScaleId, SalaryScaleName = x.SalaryScale!.ScaleName,
            x.PayRuleId, PayRuleName = x.PayRule!.Name, x.IsActive, x.Description
        }).ToListAsync(ct));
    }

    [HttpGet("package-lookups")]
    public async Task<IActionResult> Lookups(CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        return Ok(new
        {
            scales = await db.SalaryScales.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.ScaleName).Select(x => new { x.Id, Name = x.ScaleName }).ToListAsync(ct),
            rules = await db.PayRules.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(ct)
        });
    }

    [HttpPost("packages")]
    public async Task<IActionResult> CreatePackage(SalaryPackageSave dto, CancellationToken ct)
    {
        var denied = await Guard("ADD", ct); if (denied != null) return denied;
        var error = await ValidatePackage(dto, null, ct); if (error != null) return BadRequest(new { message = error });
        var row = new SalaryPackage { TenantId = tenant.RequiredTenantId }; Apply(row, dto);
        db.Add(row); await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpPut("packages/{id:int}")]
    public async Task<IActionResult> UpdatePackage(int id, SalaryPackageSave dto, CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        var row = await db.SalaryPackages.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        var error = await ValidatePackage(dto, id, ct); if (error != null) return BadRequest(new { message = error });
        Apply(row, dto); row.UpdatedOnUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); return Ok(row);
    }

    private async Task<IActionResult?> Guard(string action, CancellationToken ct)
    {
        if (!tenant.TenantId.HasValue) return Forbid();
        if (TenantPermissionService.IsSuperAdmin(User)) return null;
        if (TenantPermissionService.IsTenantAdmin(User)) return await tenantPermissions.HasMenuRouteAsync(User, [RoutePath], action, ct) ? null : Forbid();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier); if (string.IsNullOrWhiteSpace(userId)) return Forbid();
        var staffId = await db.Persons.AsNoTracking().Where(x => x.IdentityUserId == userId && x.Staff != null).Select(x => (Guid?)x.Staff!.StaffId).FirstOrDefaultAsync(ct);
        var menuId = await db.Menus.AsNoTracking().Where(x => x.IsActive && x.Route == RoutePath).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (!staffId.HasValue || !menuId.HasValue) return Forbid();
        if (action == "VIEW" && await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}")) return null;
        return await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_{action}") ? null : Forbid();
    }

    private async Task<PlatformTypeTableRow?> FindMaster(string kind, int id, CancellationToken ct) => kind switch
    {
        "allowances" => await db.AllowanceTypes.SingleOrDefaultAsync(x => x.Id == id, ct),
        "tada" => await db.TadaTypes.SingleOrDefaultAsync(x => x.Id == id, ct),
        "leave" => await db.LeaveTypes.SingleOrDefaultAsync(x => x.Id == id, ct), _ => null
    };
    private Task<bool> MasterExists(string kind, string code, string name, int? id, CancellationToken ct) => kind switch
    {
        "allowances" => db.AllowanceTypes.AnyAsync(x => x.Id != id && (x.Code == code || x.Name == name), ct),
        "tada" => db.TadaTypes.AnyAsync(x => x.Id != id && (x.Code == code || x.Name == name), ct),
        "leave" => db.LeaveTypes.AnyAsync(x => x.Id != id && (x.Code == code || x.Name == name), ct), _ => Task.FromResult(false)
    };
    private async Task<string?> ValidatePackage(SalaryPackageSave x, int? id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.Name)) return "Package code and name are required.";
        if (await db.SalaryPackages.AnyAsync(p => p.Id != id && (p.Code == x.Code.Trim() || p.Name == x.Name.Trim()), ct)) return "Package code or name already exists.";
        if (!await db.SalaryScales.AnyAsync(p => p.Id == x.SalaryScaleId && p.IsActive, ct)) return "Select an active Pay Scale.";
        if (!await db.PayRules.AnyAsync(p => p.Id == x.PayRuleId && p.IsActive, ct)) return "Select an active Pay Rule.";
        return null;
    }
    private static string? ValidateRuleRegistration(RuleRegistrationSave x)
    {
        var allowedTypes = new[] { "PayScale", "Allowances", "TADA", "Leave" };
        if (!allowedTypes.Contains(x.RuleType?.Trim(), StringComparer.OrdinalIgnoreCase)) return "Select a valid rule type.";
        if (string.IsNullOrWhiteSpace(x.Name)) return "Rule name is required.";
        if (x.DateTo.Date < x.DateFrom.Date) return "DateTo must be on or after DateFrom.";
        return null;
    }
    private static string? ValidateRule(PayRuleSave x) => string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.Name) ? "Rule code and name are required." : x.FixedWorkingDays is < 0 or > 31 || x.WorkingHoursPerDay is <= 0 or > 24 || x.OvertimeMultiplier is < 0 or > 10 ? "Enter valid working days, hours and overtime multiplier." : null;
    private static string? ValidateMaster(PlatformMasterSave x) => string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.Name) ? "Code and name are required." : x.DisplayOrder < 0 ? "Display order cannot be negative." : null;
    private static string Kind(string x) => x.Trim().ToLowerInvariant();
    private static string NormalizeAllowanceCategory(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return normalized is "APPT" or "NIGHT" ? normalized : "GENERAL";
    }
    private static void Apply(PayRule x, PayRuleSave d) { x.Code=d.Code.Trim(); x.Name=d.Name.Trim(); x.RuleType=string.IsNullOrWhiteSpace(d.RuleType) ? "Standard" : d.RuleType.Trim(); x.DateFrom=d.DateFrom; x.DateTo=d.DateTo; x.WorkingDaysBasis=d.WorkingDaysBasis.Trim(); x.FixedWorkingDays=d.FixedWorkingDays; x.WorkingHoursPerDay=d.WorkingHoursPerDay; x.OvertimeMultiplier=d.OvertimeMultiplier; x.RoundingMode=d.RoundingMode.Trim(); x.IsActive=d.IsActive; x.Description=Clean(d.Description); }
    private static void Apply(PlatformTypeTableRow x, PlatformMasterSave d) { x.Code=d.Code.Trim().ToUpperInvariant(); x.Name=d.Name.Trim(); x.DisplayOrder=d.DisplayOrder; x.IsActive=d.IsActive; }
    private static void Apply(SalaryPackage x, SalaryPackageSave d) { x.Code=d.Code.Trim(); x.Name=d.Name.Trim(); x.SalaryScaleId=d.SalaryScaleId; x.PayRuleId=d.PayRuleId; x.IsActive=d.IsActive; x.Description=Clean(d.Description); }
    private static string? Clean(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
}

public sealed record RuleRegistrationSave(string RuleType, string Name, DateTime DateFrom, DateTime DateTo);
public sealed record PayRuleSave(string Code, string Name, string WorkingDaysBasis, int FixedWorkingDays, decimal WorkingHoursPerDay, decimal OvertimeMultiplier, string RoundingMode, bool IsActive, string? Description, string? RuleType = null, DateTime? DateFrom = null, DateTime? DateTo = null);
public sealed record PlatformMasterSave(string Code, string Name, int DisplayOrder, bool IsActive, string? AllowanceCategory = null);
public sealed record SalaryPackageSave(string Code, string Name, int SalaryScaleId, int PayRuleId, bool IsActive, string? Description);
