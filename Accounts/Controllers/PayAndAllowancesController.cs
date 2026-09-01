using System.Security.Claims;
using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController, Route("api/pay-allowances"), Authorize, Produces("application/json")]
public sealed class PayAndAllowancesController(
    ApplicationDbContext db,
    ITenantService tenant,
    RbacService rbac,
    TenantPermissionService tenantPermissions) : ControllerBase
{
    [HttpGet("benefits")]
    public async Task<IActionResult> Benefits(CancellationToken ct) =>
        await Read("/pay-allowances/benefits", db.PayrollBenefitDefinitions.OrderBy(x => x.Name), ct);

    [HttpPost("benefits")]
    public async Task<IActionResult> CreateBenefit(PayBenefitSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/benefits", "ADD", ct); if (denied != null) return denied;
        var error = ValidateDefinition(dto.Code, dto.Name, dto.CalculationType, dto.Amount, dto.Percentage); if (error != null) return BadRequest(new { message = error });
        if (await db.PayrollBenefitDefinitions.AnyAsync(x => x.Code == dto.Code.Trim() || x.Name == dto.Name.Trim(), ct)) return Conflict(new { message = "Benefit code or name already exists." });
        var row = new PayrollBenefitDefinition { TenantId = tenant.RequiredTenantId, Code = dto.Code.Trim(), Name = dto.Name.Trim(), CalculationType = NormalizeCalculation(dto.CalculationType), Amount = dto.Amount, Percentage = dto.Percentage, IsTaxable = dto.IsTaxable, IsEobiContributory = dto.IsEobiContributory, IsActive = dto.IsActive, Description = Clean(dto.Description) };
        db.Add(row); await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpPut("benefits/{id:int}")]
    public async Task<IActionResult> UpdateBenefit(int id, PayBenefitSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/benefits", "EDIT", ct); if (denied != null) return denied;
        var row = await db.PayrollBenefitDefinitions.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        var error = ValidateDefinition(dto.Code, dto.Name, dto.CalculationType, dto.Amount, dto.Percentage); if (error != null) return BadRequest(new { message = error });
        if (await db.PayrollBenefitDefinitions.AnyAsync(x => x.Id != id && (x.Code == dto.Code.Trim() || x.Name == dto.Name.Trim()), ct)) return Conflict(new { message = "Benefit code or name already exists." });
        row.Code = dto.Code.Trim(); row.Name = dto.Name.Trim(); row.CalculationType = NormalizeCalculation(dto.CalculationType); row.Amount = dto.Amount; row.Percentage = dto.Percentage; row.IsTaxable = dto.IsTaxable; row.IsEobiContributory = dto.IsEobiContributory; row.IsActive = dto.IsActive; row.Description = Clean(dto.Description); row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpDelete("benefits/{id:int}")]
    public async Task<IActionResult> DeleteBenefit(int id, CancellationToken ct) =>
        await Delete("/pay-allowances/benefits", db.PayrollBenefitDefinitions, id, ct);

    [HttpGet("benefit-rules")]
    public async Task<IActionResult> BenefitRules(CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/benefits", "VIEW", ct); if (denied != null) return denied;
        var rows = await db.PayrollBenefitRules.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                BenRef = x.BenefitReference,
                x.BenefitsType,
                x.Name,
                x.Company,
                x.Entitled,
                x.Contract,
                x.Frequency,
                x.ValidFrom,
                x.ValidTo,
                MaxExp = x.MaximumExpense,
                SerStatus = x.ServiceStatus,
                x.Scale,
                x.Wef,
                MinService = x.MinimumService,
                MaxPh = x.MaximumPh,
                MinPh = x.MinimumPh,
                Ineligible = x.IsIneligible,
                x.ShareType,
                CovShare = x.CompanyShare,
                x.StaffShare,
                x.OrganizationId,
                CompName = x.CompanyName
            }).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("benefit-rules")]
    public async Task<IActionResult> CreateBenefitRule(BenefitRuleSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/benefits", "ADD", ct); if (denied != null) return denied;
        var error = ValidateBenefitRule(dto); if (error != null) return BadRequest(new { message = error });
        if (await db.PayrollBenefitRules.AnyAsync(x => x.Name == dto.Name.Trim(), ct))
            return Conflict(new { message = "A benefit rule with this name already exists." });

        var row = new PayrollBenefitRule
        {
            TenantId = tenant.RequiredTenantId,
            BenefitReference = BuildBenefitReference(dto.Scale),
        };
        ApplyBenefitRule(row, dto);
        db.PayrollBenefitRules.Add(row);
        await db.SaveChangesAsync(ct);
        return Ok(new { row.Id });
    }

    [HttpPut("benefit-rules/{id:int}")]
    public async Task<IActionResult> UpdateBenefitRule(int id, BenefitRuleSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/benefits", "EDIT", ct); if (denied != null) return denied;
        var row = await db.PayrollBenefitRules.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        var error = ValidateBenefitRule(dto); if (error != null) return BadRequest(new { message = error });
        if (await db.PayrollBenefitRules.AnyAsync(x => x.Id != id && x.Name == dto.Name.Trim(), ct))
            return Conflict(new { message = "A benefit rule with this name already exists." });
        ApplyBenefitRule(row, dto);
        row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { row.Id });
    }

    [HttpDelete("benefit-rules/{id:int}")]
    public async Task<IActionResult> DeleteBenefitRule(int id, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/benefits", "DELETE", ct); if (denied != null) return denied;
        var row = await db.PayrollBenefitRules.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        if (await db.PayrollBenefitParameters.AnyAsync(x => x.BenefitRuleId == id, ct))
            return Conflict(new { message = "Delete the linked benefit parameters before deleting this rule." });
        db.PayrollBenefitRules.Remove(row);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Benefit rule deleted successfully." });
    }

    [HttpGet("benefit-parameters")]
    public async Task<IActionResult> BenefitParameters(CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/benefits", "VIEW", ct); if (denied != null) return denied;
        var rows = await db.PayrollBenefitParameters.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                RuleName = x.BenefitRule!.Name,
                x.Name,
                Ref = x.Reference,
                Entitled = x.BenefitRule.Entitled ?? x.BenefitRule.CompanyName,
                PdFrom = x.PeriodFrom,
                PdTo = x.PeriodTo,
                BenefitId = x.BenefitRuleId,
                FreqId = x.BenefitRule.Frequency,
                MinSer = x.MinimumService,
                AmtType = x.AmountType,
                PayTypeId = x.PayType,
                MaxPh = x.BenefitRule.MaximumPh,
                MinPh = x.BenefitRule.MinimumPh,
                CoyShare = x.CompanyShare,
                x.StaffShare,
                x.BenefitRule.BenefitsType
            }).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("benefit-parameters")]
    public async Task<IActionResult> CreateBenefitParameter(BenefitParameterSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/benefits", "ADD", ct); if (denied != null) return denied;
        var error = ValidateBenefitParameter(dto); if (error != null) return BadRequest(new { message = error });
        if (!await db.PayrollBenefitRules.AnyAsync(x => x.Id == dto.BenefitRuleId, ct))
            return BadRequest(new { message = "Selected benefit rule was not found." });
        if (await db.PayrollBenefitParameters.AnyAsync(x => x.BenefitRuleId == dto.BenefitRuleId && x.Name == dto.Name.Trim(), ct))
            return Conflict(new { message = "This parameter already exists for the selected benefit rule." });

        var row = new PayrollBenefitParameter
        {
            TenantId = tenant.RequiredTenantId,
            BenefitRuleId = dto.BenefitRuleId,
            Reference = $"TMP-{Guid.NewGuid():N}"[..30],
        };
        ApplyBenefitParameter(row, dto);
        db.PayrollBenefitParameters.Add(row);
        await db.SaveChangesAsync(ct);
        row.Reference = BuildReference("P", "BEN", row.Id);
        await db.SaveChangesAsync(ct);
        return Ok(new { row.Id });
    }

    [HttpPut("benefit-parameters/{id:int}")]
    public async Task<IActionResult> UpdateBenefitParameter(int id, BenefitParameterSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/benefits", "EDIT", ct); if (denied != null) return denied;
        var row = await db.PayrollBenefitParameters.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        var error = ValidateBenefitParameter(dto); if (error != null) return BadRequest(new { message = error });
        if (!await db.PayrollBenefitRules.AnyAsync(x => x.Id == dto.BenefitRuleId, ct))
            return BadRequest(new { message = "Selected benefit rule was not found." });
        if (await db.PayrollBenefitParameters.AnyAsync(x => x.Id != id && x.BenefitRuleId == dto.BenefitRuleId && x.Name == dto.Name.Trim(), ct))
            return Conflict(new { message = "This parameter already exists for the selected benefit rule." });
        row.BenefitRuleId = dto.BenefitRuleId;
        ApplyBenefitParameter(row, dto);
        row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { row.Id });
    }

    [HttpDelete("benefit-parameters/{id:int}")]
    public async Task<IActionResult> DeleteBenefitParameter(int id, CancellationToken ct) =>
        await Delete("/pay-allowances/benefits", db.PayrollBenefitParameters, id, ct);

    [HttpGet("bonuses")]
    public async Task<IActionResult> Bonuses(CancellationToken ct) =>
        await Read("/pay-allowances/bonus", db.PayrollBonusDefinitions.OrderBy(x => x.Name), ct);

    [HttpPost("bonuses")]
    public async Task<IActionResult> CreateBonus(PayBonusSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/bonus", "ADD", ct); if (denied != null) return denied;
        var error = ValidateDefinition(dto.Code, dto.Name, dto.CalculationType, dto.Amount, dto.Percentage); if (error != null) return BadRequest(new { message = error });
        if (await db.PayrollBonusDefinitions.AnyAsync(x => x.Code == dto.Code.Trim() || x.Name == dto.Name.Trim(), ct)) return Conflict(new { message = "Bonus code or name already exists." });
        var row = new PayrollBonusDefinition { TenantId = tenant.RequiredTenantId, Code = dto.Code.Trim(), Name = dto.Name.Trim(), CalculationType = NormalizeCalculation(dto.CalculationType), Amount = dto.Amount, Percentage = dto.Percentage, Frequency = NormalizeFrequency(dto.Frequency), IsTaxable = dto.IsTaxable, IsActive = dto.IsActive, Description = Clean(dto.Description) };
        db.Add(row); await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpPut("bonuses/{id:int}")]
    public async Task<IActionResult> UpdateBonus(int id, PayBonusSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/bonus", "EDIT", ct); if (denied != null) return denied;
        var row = await db.PayrollBonusDefinitions.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        var error = ValidateDefinition(dto.Code, dto.Name, dto.CalculationType, dto.Amount, dto.Percentage); if (error != null) return BadRequest(new { message = error });
        if (await db.PayrollBonusDefinitions.AnyAsync(x => x.Id != id && (x.Code == dto.Code.Trim() || x.Name == dto.Name.Trim()), ct)) return Conflict(new { message = "Bonus code or name already exists." });
        row.Code = dto.Code.Trim(); row.Name = dto.Name.Trim(); row.CalculationType = NormalizeCalculation(dto.CalculationType); row.Amount = dto.Amount; row.Percentage = dto.Percentage; row.Frequency = NormalizeFrequency(dto.Frequency); row.IsTaxable = dto.IsTaxable; row.IsActive = dto.IsActive; row.Description = Clean(dto.Description); row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpDelete("bonuses/{id:int}")]
    public async Task<IActionResult> DeleteBonus(int id, CancellationToken ct) =>
        await Delete("/pay-allowances/bonus", db.PayrollBonusDefinitions, id, ct);

    [HttpGet("payroll-runs")]
    public async Task<IActionResult> PayrollRuns(CancellationToken ct) =>
        await Read("/pay-allowances/payroll", db.PayrollRuns.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month), ct);

    [HttpPost("payroll-runs")]
    public async Task<IActionResult> CreatePayrollRun(PayrollRunSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/payroll", "ADD", ct); if (denied != null) return denied;
        var error = ValidatePayroll(dto); if (error != null) return BadRequest(new { message = error });
        if (await db.PayrollRuns.AnyAsync(x => x.Year == dto.Year && x.Month == dto.Month, ct)) return Conflict(new { message = "A payroll run already exists for this month." });
        var row = new PayrollRun { TenantId = tenant.RequiredTenantId, Year = dto.Year, Month = dto.Month, RunNumber = string.IsNullOrWhiteSpace(dto.RunNumber) ? $"PAY-{dto.Year}{dto.Month:00}" : dto.RunNumber.Trim(), PayDate = dto.PayDate, Status = NormalizeStatus(dto.Status), Notes = Clean(dto.Notes) };
        db.Add(row); await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpPut("payroll-runs/{id:long}")]
    public async Task<IActionResult> UpdatePayrollRun(long id, PayrollRunSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/payroll", "EDIT", ct); if (denied != null) return denied;
        var row = await db.PayrollRuns.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        if (!row.Status.Equals("Draft", StringComparison.OrdinalIgnoreCase)) return Conflict(new { message = "Only a Draft payroll run can be edited." });
        var error = ValidatePayroll(dto); if (error != null) return BadRequest(new { message = error });
        if (await db.PayrollRuns.AnyAsync(x => x.Id != id && x.Year == dto.Year && x.Month == dto.Month, ct)) return Conflict(new { message = "A payroll run already exists for this month." });
        row.Year = dto.Year; row.Month = dto.Month; row.RunNumber = string.IsNullOrWhiteSpace(dto.RunNumber) ? $"PAY-{dto.Year}{dto.Month:00}" : dto.RunNumber.Trim(); row.PayDate = dto.PayDate; row.Status = NormalizeStatus(dto.Status); row.Notes = Clean(dto.Notes); row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpDelete("payroll-runs/{id:long}")]
    public async Task<IActionResult> DeletePayrollRun(long id, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/payroll", "DELETE", ct); if (denied != null) return denied;
        var row = await db.PayrollRuns.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        if (!row.Status.Equals("Draft", StringComparison.OrdinalIgnoreCase)) return Conflict(new { message = "Only a Draft payroll run can be deleted." });
        db.Remove(row); await db.SaveChangesAsync(ct); return Ok(new { message = "Payroll run deleted." });
    }

    [HttpGet("eobi-settings")]
    public async Task<IActionResult> EobiSettings(CancellationToken ct) =>
        await Read("/pay-allowances/eobi", db.EobiSettings.OrderByDescending(x => x.EffectiveFrom), ct);

    [HttpPost("eobi-settings")]
    public async Task<IActionResult> CreateEobi(EobiSettingSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/eobi", "ADD", ct); if (denied != null) return denied;
        var error = ValidateEobi(dto); if (error != null) return BadRequest(new { message = error });
        var row = new EobiSetting { TenantId = tenant.RequiredTenantId, EmployeeRatePercentage = dto.EmployeeRatePercentage, EmployerRatePercentage = dto.EmployerRatePercentage, MinimumWage = dto.MinimumWage, MaximumContributionBase = dto.MaximumContributionBase, EffectiveFrom = dto.EffectiveFrom, EffectiveTo = dto.EffectiveTo, IsActive = dto.IsActive };
        db.Add(row); await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpPut("eobi-settings/{id:int}")]
    public async Task<IActionResult> UpdateEobi(int id, EobiSettingSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/eobi", "EDIT", ct); if (denied != null) return denied;
        var row = await db.EobiSettings.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        var error = ValidateEobi(dto); if (error != null) return BadRequest(new { message = error });
        row.EmployeeRatePercentage = dto.EmployeeRatePercentage; row.EmployerRatePercentage = dto.EmployerRatePercentage; row.MinimumWage = dto.MinimumWage; row.MaximumContributionBase = dto.MaximumContributionBase; row.EffectiveFrom = dto.EffectiveFrom; row.EffectiveTo = dto.EffectiveTo; row.IsActive = dto.IsActive; row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpDelete("eobi-settings/{id:int}")]
    public async Task<IActionResult> DeleteEobi(int id, CancellationToken ct) =>
        await Delete("/pay-allowances/eobi", db.EobiSettings, id, ct);

    [HttpGet("tax-slabs")]
    public async Task<IActionResult> TaxSlabs(CancellationToken ct) =>
        await Read("/pay-allowances/tax", db.PayrollTaxSlabs.OrderByDescending(x => x.TaxYear).ThenBy(x => x.FromAmount), ct);

    [HttpPost("tax-slabs")]
    public async Task<IActionResult> CreateTax(TaxSlabSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/tax", "ADD", ct); if (denied != null) return denied;
        var error = ValidateTax(dto); if (error != null) return BadRequest(new { message = error });
        if (await TaxOverlap(dto, null, ct)) return Conflict(new { message = "This tax slab overlaps an existing active slab." });
        var row = new PayrollTaxSlab { TenantId = tenant.RequiredTenantId, TaxYear = dto.TaxYear.Trim(), FromAmount = dto.FromAmount, ToAmount = dto.ToAmount, FixedTaxAmount = dto.FixedTaxAmount, RatePercentage = dto.RatePercentage, IsActive = dto.IsActive };
        db.Add(row); await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpPut("tax-slabs/{id:int}")]
    public async Task<IActionResult> UpdateTax(int id, TaxSlabSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/tax", "EDIT", ct); if (denied != null) return denied;
        var row = await db.PayrollTaxSlabs.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        var error = ValidateTax(dto); if (error != null) return BadRequest(new { message = error });
        if (await TaxOverlap(dto, id, ct)) return Conflict(new { message = "This tax slab overlaps an existing active slab." });
        row.TaxYear = dto.TaxYear.Trim(); row.FromAmount = dto.FromAmount; row.ToAmount = dto.ToAmount; row.FixedTaxAmount = dto.FixedTaxAmount; row.RatePercentage = dto.RatePercentage; row.IsActive = dto.IsActive; row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpDelete("tax-slabs/{id:int}")]
    public async Task<IActionResult> DeleteTax(int id, CancellationToken ct) =>
        await Delete("/pay-allowances/tax", db.PayrollTaxSlabs, id, ct);

    [HttpGet("eobi-eligibility")]
    public async Task<IActionResult> Eligibility(CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/eobi-eligibility", "VIEW", ct); if (denied != null) return denied;
        var rows = await db.EobiEligibilities.AsNoTracking().OrderBy(x => x.Person!.FullName)
            .Select(x => new { x.Id, x.PersonId, PersonName = x.Person!.FullName, StaffNumber = x.Person.Staff != null ? x.Person.Staff.LoginId : null, x.EobiNumber, x.EffectiveFrom, x.EffectiveTo, x.IsEligible, x.Remarks }).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("eobi-eligibility")]
    public async Task<IActionResult> CreateEligibility(EobiEligibilitySave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/eobi-eligibility", "ADD", ct); if (denied != null) return denied;
        var error = ValidateEligibility(dto); if (error != null) return BadRequest(new { message = error });
        if (!await db.Persons.AnyAsync(x => x.PersonId == dto.PersonId, ct)) return BadRequest(new { message = "Selected employee was not found." });
        if (await db.EobiEligibilities.AnyAsync(x => x.PersonId == dto.PersonId, ct)) return Conflict(new { message = "EOBI eligibility already exists for this employee." });
        var row = new EobiEligibility { TenantId = tenant.RequiredTenantId, PersonId = dto.PersonId, EobiNumber = Clean(dto.EobiNumber), EffectiveFrom = dto.EffectiveFrom, EffectiveTo = dto.EffectiveTo, IsEligible = dto.IsEligible, Remarks = Clean(dto.Remarks) };
        db.Add(row); await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpPut("eobi-eligibility/{id:int}")]
    public async Task<IActionResult> UpdateEligibility(int id, EobiEligibilitySave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/eobi-eligibility", "EDIT", ct); if (denied != null) return denied;
        var row = await db.EobiEligibilities.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        var error = ValidateEligibility(dto); if (error != null) return BadRequest(new { message = error });
        if (!await db.Persons.AnyAsync(x => x.PersonId == dto.PersonId, ct)) return BadRequest(new { message = "Selected employee was not found." });
        if (await db.EobiEligibilities.AnyAsync(x => x.Id != id && x.PersonId == dto.PersonId, ct)) return Conflict(new { message = "EOBI eligibility already exists for this employee." });
        row.PersonId = dto.PersonId; row.EobiNumber = Clean(dto.EobiNumber); row.EffectiveFrom = dto.EffectiveFrom; row.EffectiveTo = dto.EffectiveTo; row.IsEligible = dto.IsEligible; row.Remarks = Clean(dto.Remarks); row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpDelete("eobi-eligibility/{id:int}")]
    public async Task<IActionResult> DeleteEligibility(int id, CancellationToken ct) =>
        await Delete("/pay-allowances/eobi-eligibility", db.EobiEligibilities, id, ct);

    private async Task<IActionResult> Read<T>(string route, IQueryable<T> query, CancellationToken ct) where T : class
    {
        var denied = await Guard(route, "VIEW", ct); return denied ?? Ok(await query.AsNoTracking().ToListAsync(ct));
    }

    private async Task<IActionResult> Delete<T>(string route, DbSet<T> set, int id, CancellationToken ct) where T : class
    {
        var denied = await Guard(route, "DELETE", ct); if (denied != null) return denied;
        var row = await set.FindAsync([id], ct); if (row == null) return NotFound();
        set.Remove(row); await db.SaveChangesAsync(ct); return Ok(new { message = "Record deleted successfully." });
    }

    private async Task<IActionResult?> Guard(string route, string action, CancellationToken ct)
    {
        if (!tenant.TenantId.HasValue) return Forbid();
        if (TenantPermissionService.IsSuperAdmin(User)) return null;
        if (TenantPermissionService.IsTenantAdmin(User))
            return await tenantPermissions.HasMenuRouteAsync(User, [route], action, ct) ? null : Forbid();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier); if (string.IsNullOrWhiteSpace(userId)) return Forbid();
        var staffId = await db.Persons.AsNoTracking().Where(x => x.IdentityUserId == userId && x.Staff != null).Select(x => (Guid?)x.Staff!.StaffId).FirstOrDefaultAsync(ct);
        var menuId = await db.Menus.AsNoTracking().Where(x => x.IsActive && x.Route == route).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (!staffId.HasValue || !menuId.HasValue) return Forbid();
        if (action == "VIEW" && await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}")) return null;
        return await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_{action}") ? null : Forbid();
    }

    private async Task<bool> TaxOverlap(TaxSlabSave dto, int? id, CancellationToken ct)
    {
        if (!dto.IsActive) return false;
        var upper = dto.ToAmount ?? decimal.MaxValue;
        return await db.PayrollTaxSlabs.AnyAsync(x => x.Id != id && x.IsActive && x.TaxYear == dto.TaxYear.Trim() && x.FromAmount <= upper && (x.ToAmount == null || x.ToAmount >= dto.FromAmount), ct);
    }

    private static string? ValidateDefinition(string code, string name, string calculation, decimal amount, decimal percentage)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)) return "Code and name are required.";
        if (code.Trim().Length > 30 || name.Trim().Length > 120) return "Code or name is too long.";
        if (!new[] { "Fixed", "Percentage" }.Contains(NormalizeCalculation(calculation))) return "Calculation type must be Fixed or Percentage.";
        if (amount < 0 || percentage < 0 || percentage > 100) return "Amount must be positive and percentage must be between 0 and 100.";
        if (NormalizeCalculation(calculation) == "Fixed" && amount <= 0) return "Enter a fixed amount.";
        if (NormalizeCalculation(calculation) == "Percentage" && percentage <= 0) return "Enter a percentage.";
        return null;
    }
    private static string? ValidatePayroll(PayrollRunSave x) => x.Year is < 2000 or > 2200 || x.Month is < 1 or > 12 ? "Enter a valid payroll month and year." : null;
    private static string? ValidateEobi(EobiSettingSave x) => x.EmployeeRatePercentage is < 0 or > 100 || x.EmployerRatePercentage is < 0 or > 100 || x.MinimumWage < 0 || x.MaximumContributionBase < 0 ? "Enter valid EOBI rates and amounts." : x.EffectiveTo < x.EffectiveFrom ? "Effective To cannot be before Effective From." : null;
    private static string? ValidateTax(TaxSlabSave x) => string.IsNullOrWhiteSpace(x.TaxYear) ? "Tax year is required." : x.FromAmount < 0 || x.ToAmount < x.FromAmount || x.FixedTaxAmount < 0 || x.RatePercentage is < 0 or > 100 ? "Enter a valid tax range and rate." : null;
    private static string? ValidateEligibility(EobiEligibilitySave x) => x.PersonId == Guid.Empty ? "Employee is required." : x.EffectiveTo < x.EffectiveFrom ? "Effective To cannot be before Effective From." : null;
    private static string? ValidateBenefitRule(BenefitRuleSave x)
    {
        if (string.IsNullOrWhiteSpace(x.BenefitsType) || string.IsNullOrWhiteSpace(x.Name)) return "Benefits Type and Name are required.";
        if (x.ValidTo < x.ValidFrom) return "Valid To cannot be before Valid From.";
        if (x.MaximumExpense < 0 || x.MinimumService < 0 || x.MaximumPh < 0 || x.MinimumPh < 0 || x.CompanyShare < 0 || x.StaffShare < 0)
            return "Benefit amounts and service values cannot be negative.";
        return null;
    }
    private static string? ValidateBenefitParameter(BenefitParameterSave x)
    {
        if (x.BenefitRuleId <= 0 || string.IsNullOrWhiteSpace(x.Name)) return "Benefits Rule and Name are required.";
        if (x.PeriodTo < x.PeriodFrom) return "Pd_To cannot be before Pd_From.";
        if (x.MinimumService < 0 || x.CompanyShare < 0 || x.StaffShare < 0) return "Parameter values cannot be negative.";
        return null;
    }
    private static void ApplyBenefitRule(PayrollBenefitRule row, BenefitRuleSave x)
    {
        row.BenefitReference = BuildBenefitReference(x.Scale);
        row.BenefitsType = x.BenefitsType.Trim();
        row.Name = x.Name.Trim();
        row.Company = Clean(x.Company);
        row.Entitled = Clean(x.Entitled);
        row.Contract = Clean(x.Contract);
        row.Frequency = Clean(x.Frequency);
        row.ValidFrom = x.ValidFrom;
        row.ValidTo = x.ValidTo;
        row.MaximumExpense = x.MaximumExpense;
        row.ServiceStatus = Clean(x.ServiceStatus);
        row.Scale = Clean(x.Scale);
        row.Wef = x.Wef;
        row.MinimumService = x.MinimumService;
        row.MaximumPh = x.MaximumPh;
        row.MinimumPh = x.MinimumPh;
        row.IsIneligible = x.IsIneligible;
        row.ShareType = Clean(x.ShareType);
        row.CompanyShare = x.CompanyShare;
        row.StaffShare = x.StaffShare;
        row.OrganizationId = x.OrganizationId;
        row.CompanyName = Clean(x.CompanyName);
    }
    private static void ApplyBenefitParameter(PayrollBenefitParameter row, BenefitParameterSave x)
    {
        row.Name = x.Name.Trim();
        row.PeriodFrom = x.PeriodFrom;
        row.PeriodTo = x.PeriodTo;
        row.MinimumService = x.MinimumService;
        row.AmountType = string.IsNullOrWhiteSpace(x.AmountType) ? "PH" : x.AmountType.Trim();
        row.PayType = string.IsNullOrWhiteSpace(x.PayType) ? "Basic" : x.PayType.Trim();
        row.CompanyShare = x.CompanyShare;
        row.StaffShare = x.StaffShare;
    }
    private static string BuildReference(string prefix, string value, int id)
    {
        var letters = new string(value.Where(char.IsLetterOrDigit).Take(3).ToArray()).ToUpperInvariant();
        return $"{prefix}-{(letters.Length == 0 ? "BEN" : letters)}-{id}";
    }
    private static string BuildBenefitReference(string? scale)
    {
        var normalizedScale = new string((scale ?? string.Empty)
            .Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .ToArray())
            .ToUpperInvariant();
        var reference = $"B-{(normalizedScale.Length == 0 ? "UNASSIGNED" : normalizedScale)}";
        return reference[..Math.Min(reference.Length, 30)];
    }
    private static string NormalizeCalculation(string? x) => x?.Trim().Equals("Percentage", StringComparison.OrdinalIgnoreCase) == true ? "Percentage" : "Fixed";
    private static string NormalizeFrequency(string? x) => new[] { "Monthly", "Quarterly", "Annual", "OneTime" }.FirstOrDefault(v => v.Equals(x?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Monthly";
    private static string NormalizeStatus(string? x) => new[] { "Draft", "In Review", "Approved", "Finalized" }.FirstOrDefault(v => v.Equals(x?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Draft";
    private static string? Clean(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
}

public sealed record PayBenefitSave(string Code, string Name, string CalculationType, decimal Amount, decimal Percentage, bool IsTaxable, bool IsEobiContributory, bool IsActive, string? Description);
public sealed record PayBonusSave(string Code, string Name, string CalculationType, decimal Amount, decimal Percentage, string Frequency, bool IsTaxable, bool IsActive, string? Description);
public sealed record PayrollRunSave(int Year, int Month, string? RunNumber, DateOnly PayDate, string Status, string? Notes);
public sealed record EobiSettingSave(decimal EmployeeRatePercentage, decimal EmployerRatePercentage, decimal MinimumWage, decimal MaximumContributionBase, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive);
public sealed record TaxSlabSave(string TaxYear, decimal FromAmount, decimal? ToAmount, decimal FixedTaxAmount, decimal RatePercentage, bool IsActive);
public sealed record EobiEligibilitySave(Guid PersonId, string? EobiNumber, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsEligible, string? Remarks);
public sealed record BenefitRuleSave(string BenefitsType, string Name, string? Company, string? Entitled, string? Contract, string? Frequency, DateOnly? ValidFrom, DateOnly? ValidTo, decimal MaximumExpense, string? ServiceStatus, string? Scale, DateOnly? Wef, decimal MinimumService, decimal MaximumPh, decimal MinimumPh, bool IsIneligible, string? ShareType, decimal CompanyShare, decimal StaffShare, int? OrganizationId, string? CompanyName);
public sealed record BenefitParameterSave(int BenefitRuleId, string Name, DateOnly? PeriodFrom, DateOnly? PeriodTo, decimal MinimumService, string? AmountType, string? PayType, decimal CompanyShare, decimal StaffShare);
