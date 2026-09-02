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

    [HttpGet("allowances")]
    public async Task<IActionResult> Allowances([FromQuery] string? allowanceCategory, CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        var category = NormalizeAllowanceCategory(allowanceCategory);
        return Ok(await db.PayScaleAllowances.AsNoTracking()
            .Where(x => x.AllowanceCategory == category)
            .OrderBy(x => x.Id)
            .Select(x => new AllowanceRowDto(
                x.Id, x.AllowanceReference, x.Name, x.SalaryScaleId, x.SalaryScale != null ? x.SalaryScale.ScaleName : null,
                x.AllowanceTypeId, x.AllowanceType!.Name, x.ContractType, x.FrequencyType,
                x.RateType, x.PayType, x.PayValue, x.CalculatedValue, x.AllowanceCategory,
                x.DesignationId, x.Designation != null ? x.Designation.Name : null,
                x.ShiftLookupValueId, x.ShiftLookupValue != null ? x.ShiftLookupValue.ValueCode : null,
                x.ShiftLookupValue != null ? x.ShiftLookupValue.DisplayText : null))
            .ToListAsync(ct));
    }

    [HttpGet("allowance-lookups")]
    public async Task<IActionResult> AllowanceLookups([FromQuery] string? allowanceCategory, CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        var category = NormalizeAllowanceCategory(allowanceCategory);
        return Ok(new
        {
            scales = await db.SalaryScales.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.ScaleName)
                .Select(x => new { x.Id, Name = x.ScaleName }).ToListAsync(ct),
            allowanceTypes = await db.AllowanceTypes.AsNoTracking().Where(x => x.IsActive &&
                    (x.AllowanceCategory == category || (category != "GENERAL" && x.AllowanceCategory == "GENERAL")))
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(ct),
            designations = category == "APPT"
                ? await db.Designations.AsNoTracking().OrderBy(x => x.Name)
                    .Select(x => new { x.Id, x.Name }).ToListAsync(ct)
                : [],
            shifts = category == "SHIFT"
                ? await db.AppLookupValues.AsNoTracking()
                    .Where(x => x.IsActive && x.LookupType != null && x.LookupType.IsActive &&
                        x.LookupType.LookupTypeCode == "ATTENDANCE_SHIFT")
                    .OrderBy(x => x.SortOrder).ThenBy(x => x.DisplayText)
                    .Select(x => new { Id = x.LookupValueId, Code = x.ValueCode, Name = x.DisplayText })
                    .ToListAsync(ct)
                : [],
            contracts = await db.ContractTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).Select(x => x.Name).ToListAsync(ct),
            frequencies = await db.FrequencyTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).Select(x => x.Name).ToListAsync(ct),
            rates = await db.RateTypes.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).Select(x => x.Name).ToListAsync(ct),
            payTypes = new[] { "Basic" }
        });
    }

    [HttpPost("allowances")]
    public async Task<IActionResult> CreateAllowance(AllowanceSave dto, CancellationToken ct)
    {
        var denied = await Guard("ADD", ct); if (denied != null) return denied;
        var error = await ValidateAllowance(dto, null, ct); if (error != null) return BadRequest(new { message = error });
        var scale = dto.SalaryScaleId.HasValue
            ? await db.SalaryScales.SingleAsync(x => x.Id == dto.SalaryScaleId.Value, ct)
            : null;
        var targetName = await ResolveAllowanceTargetName(dto, ct);
        var row = new PayScaleAllowance { TenantId = tenant.RequiredTenantId };
        Apply(row, dto, scale, targetName);
        db.Add(row); await db.SaveChangesAsync(ct);
        return Ok(await AllowanceRow(row.Id, ct));
    }

    [HttpPut("allowances/{id:int}")]
    public async Task<IActionResult> UpdateAllowance(int id, AllowanceSave dto, CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        var row = await db.PayScaleAllowances.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        var error = await ValidateAllowance(dto, id, ct); if (error != null) return BadRequest(new { message = error });
        var scale = dto.SalaryScaleId.HasValue
            ? await db.SalaryScales.SingleAsync(x => x.Id == dto.SalaryScaleId.Value, ct)
            : null;
        var targetName = await ResolveAllowanceTargetName(dto, ct);
        Apply(row, dto, scale, targetName); row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(await AllowanceRow(row.Id, ct));
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
    private async Task<string?> ValidateAllowance(AllowanceSave x, int? id, CancellationToken ct)
    {
        var category = NormalizeAllowanceCategory(x.AllowanceCategory);
        if (category == "GENERAL" && string.IsNullOrWhiteSpace(x.Name)) return "Allowance name is required.";
        if (x.PayValue < 0) return "Pay value cannot be negative.";
        if (category != "SHIFT" && (!x.SalaryScaleId.HasValue ||
                !await db.SalaryScales.AnyAsync(p => p.Id == x.SalaryScaleId.Value && p.IsActive, ct)))
            return "Select an active Pay Scale.";
        if (!await db.AllowanceTypes.AnyAsync(p => p.Id == x.AllowanceTypeId && p.IsActive &&
                (p.AllowanceCategory == category || (category != "GENERAL" && p.AllowanceCategory == "GENERAL")), ct))
            return "Select a valid Allowance Type.";
        if (category == "APPT" && (!x.DesignationId.HasValue ||
                !await db.Designations.AnyAsync(p => p.Id == x.DesignationId.Value, ct)))
            return "Select a valid Designation.";
        if (category == "SHIFT" && (!x.ShiftLookupValueId.HasValue ||
                !await db.AppLookupValues.AnyAsync(p => p.LookupValueId == x.ShiftLookupValueId.Value && p.IsActive &&
                    p.LookupType != null && p.LookupType.IsActive && p.LookupType.LookupTypeCode == "ATTENDANCE_SHIFT", ct)))
            return "Select a valid Shift.";
        var targetName = await ResolveAllowanceTargetName(x, ct);
        if (await db.PayScaleAllowances.AnyAsync(p => p.Id != id && p.SalaryScaleId == x.SalaryScaleId &&
                p.AllowanceTypeId == x.AllowanceTypeId && p.Name == targetName && p.AllowanceCategory == category, ct))
            return "This allowance already exists for the selected scale and allowance type.";
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
        return normalized switch { "APPT" => "APPT", "SHIFT" or "NIGHT" => "SHIFT", _ => "GENERAL" };
    }
    private static void Apply(PayRule x, PayRuleSave d) { x.Code=d.Code.Trim(); x.Name=d.Name.Trim(); x.RuleType=string.IsNullOrWhiteSpace(d.RuleType) ? "Standard" : d.RuleType.Trim(); x.DateFrom=d.DateFrom; x.DateTo=d.DateTo; x.WorkingDaysBasis=d.WorkingDaysBasis.Trim(); x.FixedWorkingDays=d.FixedWorkingDays; x.WorkingHoursPerDay=d.WorkingHoursPerDay; x.OvertimeMultiplier=d.OvertimeMultiplier; x.RoundingMode=d.RoundingMode.Trim(); x.IsActive=d.IsActive; x.Description=Clean(d.Description); }
    private static void Apply(PayScaleAllowance x, AllowanceSave d, SalaryScale? scale, string targetName)
    {
        var category = NormalizeAllowanceCategory(d.AllowanceCategory);
        x.AllowanceReference = BuildAllowanceReference(category, scale?.ScaleName, targetName);
        x.Name = targetName;
        x.SalaryScaleId = category == "SHIFT" ? null : d.SalaryScaleId;
        x.AllowanceTypeId = d.AllowanceTypeId;
        x.DesignationId = category == "APPT" ? d.DesignationId : null;
        x.ShiftLookupValueId = category == "SHIFT" ? d.ShiftLookupValueId : null;
        x.ContractType = Clean(d.ContractType);
        x.FrequencyType = Clean(d.FrequencyType);
        x.RateType = Clean(d.RateType);
        x.PayType = Clean(d.PayType);
        x.PayValue = d.PayValue;
        x.AllowanceCategory = category;
        var basis = string.Equals(x.PayType, "Basic", StringComparison.OrdinalIgnoreCase) ? scale?.BasicSalary ?? 0m
            : string.Equals(x.PayType, "Gross", StringComparison.OrdinalIgnoreCase) ? scale?.GrossSalary ?? 0m
            : string.Equals(x.PayType, "CurrentPay", StringComparison.OrdinalIgnoreCase) ? scale?.CurrentPay ?? 0m : 0m;
        x.CalculatedValue = x.RateType?.Contains("Percentage", StringComparison.OrdinalIgnoreCase) == true
            ? Math.Round(basis * x.PayValue / 100m, 2, MidpointRounding.AwayFromZero)
            : Math.Round(x.PayValue, 2, MidpointRounding.AwayFromZero);
    }
    private static void Apply(PlatformTypeTableRow x, PlatformMasterSave d) { x.Code=d.Code.Trim().ToUpperInvariant(); x.Name=d.Name.Trim(); x.DisplayOrder=d.DisplayOrder; x.IsActive=d.IsActive; }
    private static void Apply(SalaryPackage x, SalaryPackageSave d) { x.Code=d.Code.Trim(); x.Name=d.Name.Trim(); x.SalaryScaleId=d.SalaryScaleId; x.PayRuleId=d.PayRuleId; x.IsActive=d.IsActive; x.Description=Clean(d.Description); }
    private static string? Clean(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
    private static string BuildAllowanceReference(string category, string? scaleName, string targetName)
    {
        if (category == "SHIFT")
            return targetName.Equals("Night", StringComparison.OrdinalIgnoreCase)
                ? "A-RLTN-"
                : $"A-SHIFT-{targetName.Trim().ToUpperInvariant().Replace(' ', '-')}";
        var normalizedScale = scaleName?.Trim() ?? string.Empty;
        if (category == "APPT" && normalizedScale.StartsWith("RLT-", StringComparison.OrdinalIgnoreCase))
            return $"A-RLTA-{normalizedScale[4..]}";
        return $"A-{normalizedScale}";
    }
    private async Task<string> ResolveAllowanceTargetName(AllowanceSave dto, CancellationToken ct)
    {
        var category = NormalizeAllowanceCategory(dto.AllowanceCategory);
        if (category == "APPT" && dto.DesignationId.HasValue)
            return await db.Designations.Where(x => x.Id == dto.DesignationId.Value).Select(x => x.Name).SingleAsync(ct);
        if (category == "SHIFT" && dto.ShiftLookupValueId.HasValue)
            return await db.AppLookupValues.Where(x => x.LookupValueId == dto.ShiftLookupValueId.Value).Select(x => x.DisplayText).SingleAsync(ct);
        return dto.Name.Trim();
    }
    private Task<AllowanceRowDto?> AllowanceRow(int id, CancellationToken ct) => db.PayScaleAllowances.AsNoTracking()
        .Where(x => x.Id == id)
        .Select(x => new AllowanceRowDto(
            x.Id, x.AllowanceReference, x.Name, x.SalaryScaleId, x.SalaryScale != null ? x.SalaryScale.ScaleName : null,
            x.AllowanceTypeId, x.AllowanceType!.Name, x.ContractType, x.FrequencyType,
            x.RateType, x.PayType, x.PayValue, x.CalculatedValue, x.AllowanceCategory,
            x.DesignationId, x.Designation != null ? x.Designation.Name : null,
            x.ShiftLookupValueId, x.ShiftLookupValue != null ? x.ShiftLookupValue.ValueCode : null,
            x.ShiftLookupValue != null ? x.ShiftLookupValue.DisplayText : null))
        .SingleOrDefaultAsync(ct);
}

public sealed record RuleRegistrationSave(string RuleType, string Name, DateTime DateFrom, DateTime DateTo);
public sealed record PayRuleSave(string Code, string Name, string WorkingDaysBasis, int FixedWorkingDays, decimal WorkingHoursPerDay, decimal OvertimeMultiplier, string RoundingMode, bool IsActive, string? Description, string? RuleType = null, DateTime? DateFrom = null, DateTime? DateTo = null);
public sealed record PlatformMasterSave(string Code, string Name, int DisplayOrder, bool IsActive, string? AllowanceCategory = null);
public sealed record SalaryPackageSave(string Code, string Name, int SalaryScaleId, int PayRuleId, bool IsActive, string? Description);
public sealed record AllowanceSave(string Name, int? SalaryScaleId, int AllowanceTypeId, string? ContractType, string? FrequencyType, string? RateType, string? PayType, decimal PayValue, string? AllowanceCategory = "GENERAL", int? DesignationId = null, int? ShiftLookupValueId = null);
public sealed record AllowanceRowDto(int Id, string AllowanceRef, string AllowName, int? SalaryScaleId, string? Scale, int AllowanceTypeId, string AllowanceType, string? ContractType, string? FrequencyType, string? RateType, string? PayType, decimal PayValue, decimal CalculatedValue, string AllowanceCategory, int? DesignationId, string? DesignationName, int? ShiftLookupValueId, string? ShiftCode, string? ShiftName);
