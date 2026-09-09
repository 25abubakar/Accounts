using System.Security.Claims;
using Accounts.Data;
using Accounts.Idempotency;
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
    TenantPermissionService tenantPermissions,
    PayrollCalculationService payroll,
    StaffMonthlyEobiService staffMonthlyEobi,
    StaffTaxService staffTax) : ControllerBase
{
    [HttpGet("benefits")]
    public async Task<IActionResult> Benefits(CancellationToken ct) =>
        await Read("/pay-allowances/benefits", db.PayrollBenefitDefinitions.OrderBy(x => x.Name), ct);

    [HttpPost("benefits")]
    public async Task<IActionResult> CreateBenefit(PayBenefitSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/benefits", "ADD", ct); if (denied != null) return denied;
        var error = await ValidateDefinition(dto.Code, dto.Name, dto.CalculationType, dto.Amount, dto.Percentage, ct); if (error != null) return BadRequest(new { message = error });
        if (await db.PayrollBenefitDefinitions.AnyAsync(x => x.Code == dto.Code.Trim() || x.Name == dto.Name.Trim(), ct)) return Conflict(new { message = "Benefit code or name already exists." });
        var calc = await NormalizeCalculationAsync(dto.CalculationType, ct) ?? "Fixed";
        var row = new PayrollBenefitDefinition { TenantId = tenant.RequiredTenantId, Code = dto.Code.Trim(), Name = dto.Name.Trim(), CalculationType = calc, Amount = dto.Amount, Percentage = dto.Percentage, IsTaxable = dto.IsTaxable, IsEobiContributory = dto.IsEobiContributory, IsActive = dto.IsActive, Description = Clean(dto.Description) };
        db.Add(row); await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpPut("benefits/{id:int}")]
    public async Task<IActionResult> UpdateBenefit(int id, PayBenefitSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/benefits", "EDIT", ct); if (denied != null) return denied;
        var row = await db.PayrollBenefitDefinitions.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        var error = await ValidateDefinition(dto.Code, dto.Name, dto.CalculationType, dto.Amount, dto.Percentage, ct); if (error != null) return BadRequest(new { message = error });
        if (await db.PayrollBenefitDefinitions.AnyAsync(x => x.Id != id && (x.Code == dto.Code.Trim() || x.Name == dto.Name.Trim()), ct)) return Conflict(new { message = "Benefit code or name already exists." });
        row.Code = dto.Code.Trim(); row.Name = dto.Name.Trim(); row.CalculationType = await NormalizeCalculationAsync(dto.CalculationType, ct) ?? "Fixed"; row.Amount = dto.Amount; row.Percentage = dto.Percentage; row.IsTaxable = dto.IsTaxable; row.IsEobiContributory = dto.IsEobiContributory; row.IsActive = dto.IsActive; row.Description = Clean(dto.Description); row.UpdatedOnUtc = DateTime.UtcNow;
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

    [HttpGet("benefit-lookups")]
    public async Task<IActionResult> BenefitLookups(CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/benefits", "VIEW", ct); if (denied != null) return denied;
        var tenantRow = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Id == tenant.RequiredTenantId)
            .Select(x => new { x.OrganizationTreeId })
            .SingleAsync(ct);
        // Org tree is flexible: Group/Company/Country/Branch/SubBranch/Department
        // may appear in any order. Company options = Label "Company" only (any name).
        var allNodes = await db.OrganizationTree.AsNoTracking().ToListAsync(ct);
        var companyNodes = ResolveBenefitCompanyNodes(allNodes, tenantRow.OrganizationTreeId);
        var scopeIds = CollectBenefitOrganizationScope(tenantRow.OrganizationTreeId, allNodes, companyNodes);
        var scopedNodes = allNodes.Where(x => scopeIds.Contains(x.Id)).ToList();
        var departmentNodes = ResolveBenefitDepartmentNodes(scopedNodes, companyNodes);
        var defaultCompany = ResolveDefaultBenefitCompany(companyNodes, allNodes, tenantRow.OrganizationTreeId);

        return Ok(new
        {
            benefitTypes = await db.BenefitTypes.AsNoTracking().Where(x => x.IsActive)
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
                .Select(x => new { x.Id, x.Name }).ToListAsync(ct),
            scales = await db.SalaryScales.AsNoTracking().Where(x => x.IsActive)
                .OrderBy(x => x.ScaleName).Select(x => new { x.Id, Name = x.ScaleName }).ToListAsync(ct),
            contracts = await db.ContractTypes.AsNoTracking().Where(x => x.IsActive)
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).Select(x => x.Name).ToListAsync(ct),
            frequencies = await db.FrequencyTypes.AsNoTracking().Where(x => x.IsActive)
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).Select(x => x.Name).ToListAsync(ct),
            serviceStatuses = await LookupNamesAsync("BENEFIT_SERVICE_STATUS", ct),
            amountTypes = await LookupNamesAsync("BENEFIT_AMOUNT_TYPE", ct),
            payTypes = await LookupNamesAsync("BENEFIT_PAY_TYPE", ct),
            shareTypes = await LookupNamesAsync("BENEFIT_SHARE_TYPE", ct),
            companies = companyNodes.Select(x => new { x.Id, x.Name, x.Label }).ToList(),
            departments = departmentNodes.Select(x => new { x.Id, x.ParentId, x.Name, x.Label }).ToList(),
            entitlements = scopedNodes.OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.ParentId, x.Name, x.Label }).ToList(),
            tenantCompany = defaultCompany == null ? null : new { defaultCompany.Id, defaultCompany.Name }
        });
    }

    private static string NormalizeOrgLabel(string? label) =>
        (label ?? string.Empty).Trim().Replace("-", " ").Replace("_", " ");

    private static bool IsCompanyLabel(string? label) =>
        NormalizeOrgLabel(label).Equals("Company", StringComparison.OrdinalIgnoreCase);

    private static bool IsGroupLabel(string? label) =>
        NormalizeOrgLabel(label).Equals("Group", StringComparison.OrdinalIgnoreCase);

    /// <summary>Branch / Sub Branch — never a Benefits Company option.</summary>
    private static bool IsBranchLikeLabel(string? label)
    {
        var normalized = NormalizeOrgLabel(label);
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        return normalized.Equals("Branch", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Sub Branch", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("SubBranch", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Office", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDepartmentLikeLabel(string? label)
    {
        var normalized = NormalizeOrgLabel(label);
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        return normalized.Equals("Department", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Sub Department", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("SubDepartment", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Unit", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Team", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Section", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLocationOnlyLabel(string? label)
    {
        var normalized = NormalizeOrgLabel(label);
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        return normalized.Equals("Country", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Region", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("State", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("City", StringComparison.OrdinalIgnoreCase);
    }

    private static List<OrganizationTree> PreferActiveOrgNodes(IEnumerable<OrganizationTree> source) =>
        source.Where(x => x.IsActive).OrderBy(x => x.Name).ToList() is { Count: > 0 } active
            ? active
            : source.OrderBy(x => x.Name).ToList();

    /// <summary>
    /// Company dropdown = every in-scope org node whose Label is "Company".
    /// Names are never hardcoded. Tree position is free-form, e.g.:
    /// Group→Company→Country→Branch, or Group→Country→Company→Branch,
    /// or Company at the root — all valid.
    /// Never treats Branch / Sub Branch / Country / Department as Company.
    /// </summary>
    private static List<OrganizationTree> ResolveBenefitCompanyNodes(
        IReadOnlyList<OrganizationTree> allNodes,
        int tenantRootId)
    {
        var byId = allNodes.ToDictionary(x => x.Id);
        var companyIds = new HashSet<int>();
        int? groupAncestorId = null;

        // Walk UP: Company may sit above Country/Branch/SubBranch (any depth).
        var currentId = tenantRootId;
        var visited = new HashSet<int>();
        while (byId.TryGetValue(currentId, out var node) && visited.Add(currentId))
        {
            if (IsCompanyLabel(node.Label)) companyIds.Add(node.Id);
            if (IsGroupLabel(node.Label)) groupAncestorId ??= node.Id;
            if (!node.ParentId.HasValue) break;
            currentId = node.ParentId.Value;
        }

        // Walk DOWN from tenant root: Company may sit below Country/Group.
        foreach (var id in CollectOrganizationDescendants(tenantRootId, allNodes))
        {
            if (byId.TryGetValue(id, out var node) && IsCompanyLabel(node.Label))
                companyIds.Add(node.Id);
        }

        // Still none, but a Group is on the chain: collect every Company under that Group
        // (covers Country→Company siblings / nested company under country).
        if (companyIds.Count == 0 && groupAncestorId.HasValue)
        {
            foreach (var id in CollectOrganizationDescendants(groupAncestorId.Value, allNodes))
            {
                if (byId.TryGetValue(id, out var node) && IsCompanyLabel(node.Label))
                    companyIds.Add(node.Id);
            }
        }

        return PreferActiveOrgNodes(allNodes.Where(x => companyIds.Contains(x.Id)));
    }

    /// <summary>
    /// Default company = tenant org node if it is a Company, else nearest Company ancestor.
    /// Does not match on a fixed display name.
    /// </summary>
    private static OrganizationTree? ResolveDefaultBenefitCompany(
        IReadOnlyList<OrganizationTree> companyNodes,
        IReadOnlyList<OrganizationTree> allNodes,
        int tenantRootId)
    {
        if (companyNodes.Count == 0) return null;
        var companyById = companyNodes.ToDictionary(x => x.Id);
        if (companyById.TryGetValue(tenantRootId, out var atRoot)) return atRoot;

        var byId = allNodes.ToDictionary(x => x.Id);
        var currentId = tenantRootId;
        var visited = new HashSet<int>();
        while (byId.TryGetValue(currentId, out var node) && visited.Add(currentId))
        {
            if (companyById.TryGetValue(node.Id, out var company)) return company;
            if (!node.ParentId.HasValue) break;
            currentId = node.ParentId.Value;
        }

        return companyNodes[0];
    }

    /// <summary>
    /// Scope for entitled/departments: union of each resolved Company subtree.
    /// Falls back to tenant-root descendants when no Company label exists yet.
    /// </summary>
    private static HashSet<int> CollectBenefitOrganizationScope(
        int tenantRootId,
        IReadOnlyList<OrganizationTree> allNodes,
        IReadOnlyList<OrganizationTree> companyNodes)
    {
        if (companyNodes.Count == 0)
            return CollectOrganizationDescendants(tenantRootId, allNodes);

        var scope = new HashSet<int>();
        foreach (var company in companyNodes)
        {
            foreach (var id in CollectOrganizationDescendants(company.Id, allNodes))
                scope.Add(id);
        }
        return scope;
    }

    private static List<OrganizationTree> ResolveBenefitDepartmentNodes(
        IReadOnlyList<OrganizationTree> scopedNodes,
        IReadOnlyList<OrganizationTree> companyNodes)
    {
        var companyIds = companyNodes.Select(x => x.Id).ToHashSet();
        // Entitled options = Department (and similar) only — not Branch / Sub Branch / Country / Shift.
        return PreferActiveOrgNodes(scopedNodes
            .Where(x => IsDepartmentLikeLabel(x.Label) && !companyIds.Contains(x.Id)
                        && !IsBranchLikeLabel(x.Label)
                        && !IsLocationOnlyLabel(x.Label)
                        && !IsGroupLabel(x.Label)
                        && !IsCompanyLabel(x.Label)));
    }

    private Task<List<string>> LookupNamesAsync(string lookupTypeCode, CancellationToken ct) =>
        db.AppLookupValues.AsNoTracking()
            .Where(x => x.IsActive && x.LookupType != null && x.LookupType.IsActive &&
                        x.LookupType.LookupTypeCode == lookupTypeCode)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.DisplayText)
            .Select(x => x.DisplayText)
            .ToListAsync(ct);

    [HttpPost("benefit-rules")]
    public async Task<IActionResult> CreateBenefitRule(BenefitRuleSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/benefits", "ADD", ct); if (denied != null) return denied;
        var error = ValidateBenefitRule(dto); if (error != null) return BadRequest(new { message = error });
        var referenceError = await ValidateBenefitRuleReferences(dto, ct); if (referenceError != null) return BadRequest(new { message = referenceError });
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
        var referenceError = await ValidateBenefitRuleReferences(dto, ct); if (referenceError != null) return BadRequest(new { message = referenceError });
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
        if (await db.PayrollBonusRuns.AnyAsync(x => x.BenefitRuleId == id, ct))
            return Conflict(new { message = "Delete the generated bonus runs for this rule before deleting it." });
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
                x.Amount,
                x.Percentage,
                MaxPh = x.BenefitRule.MaximumPh,
                MinPh = x.BenefitRule.MinimumPh,
                CoyShare = x.CompanyShare,
                x.StaffShare,
                x.BenefitRule.BenefitsType,
                BonusMonth = x.BonusDistribution == null ? null : x.BonusDistribution.Month,
                BasicPercentage = x.BonusDistribution == null ? 0 : x.BonusDistribution.BasicPercentage,
                ServicePercentage = x.BonusDistribution == null ? 0 : x.BonusDistribution.ServicePercentage,
                ServiceYears = x.BonusDistribution == null ? 0 : x.BonusDistribution.ServiceYears,
                AssessmentPercentage = x.BonusDistribution == null ? 0 : x.BonusDistribution.AssessmentPercentage,
                AttendancePercentage = x.BonusDistribution == null ? 0 : x.BonusDistribution.AttendancePercentage,
                LeavePercentage = x.BonusDistribution == null ? 0 : x.BonusDistribution.LeavePercentage,
                DisciplinePercentage = x.BonusDistribution == null ? 0 : x.BonusDistribution.DisciplinePercentage,
                Installments = x.BonusDistribution == null ? 1 : x.BonusDistribution.Installments
            }).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("benefit-parameters")]
    public async Task<IActionResult> CreateBenefitParameter(BenefitParameterSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/benefits", "ADD", ct); if (denied != null) return denied;
        var error = ValidateBenefitParameter(dto); if (error != null) return BadRequest(new { message = error });
        var benefitRule = await db.PayrollBenefitRules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == dto.BenefitRuleId, ct);
        if (benefitRule == null)
            return BadRequest(new { message = "Selected benefit rule was not found." });
        var paymentError = ValidateBenefitParameterPaymentSelection(benefitRule.BenefitsType, dto); if (paymentError != null) return BadRequest(new { message = paymentError });
        var distributionError = ValidateBonusDistribution(benefitRule.BenefitsType, dto.BonusDistribution); if (distributionError != null) return BadRequest(new { message = distributionError });
        if (await db.PayrollBenefitParameters.AnyAsync(x => x.BenefitRuleId == dto.BenefitRuleId && x.Name == dto.Name.Trim(), ct))
            return Conflict(new { message = "This parameter already exists for the selected benefit rule." });

        var row = new PayrollBenefitParameter
        {
            TenantId = tenant.RequiredTenantId,
            BenefitRuleId = dto.BenefitRuleId,
            Reference = $"TMP-{Guid.NewGuid():N}"[..30],
        };
        ApplyBenefitParameter(row, dto, benefitRule.BenefitsType);
        db.PayrollBenefitParameters.Add(row);
        await db.SaveChangesAsync(ct);
        row.Reference = BuildReference("P", "BEN", row.Id);
        if (benefitRule.BenefitsType.Equals("Bonus", StringComparison.OrdinalIgnoreCase) && dto.BonusDistribution != null)
            db.PayrollBonusDistributions.Add(CreateBonusDistribution(row.Id, dto.BonusDistribution));
        await db.SaveChangesAsync(ct);
        return Ok(new { row.Id });
    }

    [HttpPut("benefit-parameters/{id:int}")]
    public async Task<IActionResult> UpdateBenefitParameter(int id, BenefitParameterSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/benefits", "EDIT", ct); if (denied != null) return denied;
        var row = await db.PayrollBenefitParameters.Include(x => x.BonusDistribution).SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        var error = ValidateBenefitParameter(dto); if (error != null) return BadRequest(new { message = error });
        var benefitRule = await db.PayrollBenefitRules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == dto.BenefitRuleId, ct);
        if (benefitRule == null)
            return BadRequest(new { message = "Selected benefit rule was not found." });
        var paymentError = ValidateBenefitParameterPaymentSelection(benefitRule.BenefitsType, dto); if (paymentError != null) return BadRequest(new { message = paymentError });
        var distributionError = ValidateBonusDistribution(benefitRule.BenefitsType, dto.BonusDistribution); if (distributionError != null) return BadRequest(new { message = distributionError });
        if (await db.PayrollBenefitParameters.AnyAsync(x => x.Id != id && x.BenefitRuleId == dto.BenefitRuleId && x.Name == dto.Name.Trim(), ct))
            return Conflict(new { message = "This parameter already exists for the selected benefit rule." });
        row.BenefitRuleId = dto.BenefitRuleId;
        ApplyBenefitParameter(row, dto, benefitRule.BenefitsType);
        if (benefitRule.BenefitsType.Equals("Bonus", StringComparison.OrdinalIgnoreCase) && dto.BonusDistribution != null)
        {
            row.BonusDistribution ??= CreateBonusDistribution(row.Id, dto.BonusDistribution);
            ApplyBonusDistribution(row.BonusDistribution, dto.BonusDistribution);
        }
        else if (row.BonusDistribution != null)
        {
            db.PayrollBonusDistributions.Remove(row.BonusDistribution);
            row.BonusDistribution = null;
        }
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
        var error = await ValidateDefinition(dto.Code, dto.Name, dto.CalculationType, dto.Amount, dto.Percentage, ct); if (error != null) return BadRequest(new { message = error });
        if (await db.PayrollBonusDefinitions.AnyAsync(x => x.Code == dto.Code.Trim() || x.Name == dto.Name.Trim(), ct)) return Conflict(new { message = "Bonus code or name already exists." });
        var row = new PayrollBonusDefinition { TenantId = tenant.RequiredTenantId, Code = dto.Code.Trim(), Name = dto.Name.Trim(), CalculationType = await NormalizeCalculationAsync(dto.CalculationType, ct) ?? "Fixed", Amount = dto.Amount, Percentage = dto.Percentage, Frequency = await NormalizeFrequencyAsync(dto.Frequency, ct), IsTaxable = dto.IsTaxable, IsActive = dto.IsActive, Description = Clean(dto.Description) };
        db.Add(row); await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpPut("bonuses/{id:int}")]
    public async Task<IActionResult> UpdateBonus(int id, PayBonusSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/bonus", "EDIT", ct); if (denied != null) return denied;
        var row = await db.PayrollBonusDefinitions.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        var error = await ValidateDefinition(dto.Code, dto.Name, dto.CalculationType, dto.Amount, dto.Percentage, ct); if (error != null) return BadRequest(new { message = error });
        if (await db.PayrollBonusDefinitions.AnyAsync(x => x.Id != id && (x.Code == dto.Code.Trim() || x.Name == dto.Name.Trim()), ct)) return Conflict(new { message = "Bonus code or name already exists." });
        row.Code = dto.Code.Trim(); row.Name = dto.Name.Trim(); row.CalculationType = await NormalizeCalculationAsync(dto.CalculationType, ct) ?? "Fixed"; row.Amount = dto.Amount; row.Percentage = dto.Percentage; row.Frequency = await NormalizeFrequencyAsync(dto.Frequency, ct); row.IsTaxable = dto.IsTaxable; row.IsActive = dto.IsActive; row.Description = Clean(dto.Description); row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpDelete("bonuses/{id:int}")]
    public async Task<IActionResult> DeleteBonus(int id, CancellationToken ct) =>
        await Delete("/pay-allowances/bonus", db.PayrollBonusDefinitions, id, ct);

    [HttpGet("payroll-runs")]
    public async Task<IActionResult> PayrollRuns(CancellationToken ct) =>
        await Read("/pay-allowances/payroll", db.PayrollRuns.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month), ct);

    [HttpGet("payroll-workspace")]
    public async Task<IActionResult> PayrollWorkspace([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/payroll", "VIEW", ct); if (denied != null) return denied;
        if (year is < 2000 or > 2200 || month is < 1 or > 12)
            return BadRequest(new { message = "Enter a valid payroll month and year." });
        var run = await db.PayrollRuns.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Year == year && x.Month == month, ct);
        // Saved lines are authoritative. Live preview only when there is no run,
        // or a Draft run still has no lines (userculate pending).
        if (run != null && run.Lines.Count > 0)
            return Ok(await PayrollResponseAsync(run, run.Lines, ct));
        if (run != null && !run.Status.Equals("Draft", StringComparison.OrdinalIgnoreCase))
            return Ok(await PayrollResponseAsync(run, Array.Empty<PayrollLine>(), ct));
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Forbid();
        var preview = await payroll.PreviewAsync(userId, year, month, ct);
        return Ok(await PayrollResponseAsync(run, preview, ct));
    }

    [HttpPost("payroll-runs")]
    [Idempotent]
    public async Task<IActionResult> CreatePayrollRun(PayrollRunSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/payroll", "VIEW", ct); if (denied != null) return denied;
        var authorityDenied = await GuardPayrollAuthority("CREATE", ct); if (authorityDenied != null) return authorityDenied;
        var error = await ValidatePayrollAsync(dto, ct); if (error != null) return BadRequest(new { message = error });
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier); if (string.IsNullOrWhiteSpace(userId)) return Forbid();
        try
        {
            var run = await payroll.GenerateAsync(userId, await ActorNameAsync(ct), dto.Year, dto.Month, dto.PayDate, ct);
            return Ok(await PayrollResponseAsync(run, run.Lines, ct));
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPut("payroll-runs/{id:long}")]
    [Idempotent]
    public async Task<IActionResult> UpdatePayrollRun(long id, PayrollRunSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/payroll", "VIEW", ct); if (denied != null) return denied;
        var authorityDenied = await GuardPayrollAuthority("CREATE", ct); if (authorityDenied != null) return authorityDenied;
        var row = await db.PayrollRuns.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        if (!row.Status.Equals("Draft", StringComparison.OrdinalIgnoreCase)) return Conflict(new { message = "Only a Draft payroll run can be edited." });
        var error = await ValidatePayrollAsync(dto, ct); if (error != null) return BadRequest(new { message = error });
        if (await db.PayrollRuns.AnyAsync(x => x.Id != id && x.Year == dto.Year && x.Month == dto.Month, ct)) return Conflict(new { message = "A payroll run already exists for this month." });
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier); if (string.IsNullOrWhiteSpace(userId)) return Forbid();
        try
        {
            var run = await payroll.GenerateAsync(userId, await ActorNameAsync(ct), dto.Year, dto.Month, dto.PayDate, ct);
            return Ok(await PayrollResponseAsync(run, run.Lines, ct));
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPut("payroll-lines/{id:long}")]
    public async Task<IActionResult> UpdatePayrollLine(long id, PayrollLineSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/payroll", "EDIT", ct); if (denied != null) return denied;
        var line = await db.PayrollLines.Include(x => x.PayrollRun).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (line?.PayrollRun == null) return NotFound();
        if (!line.PayrollRun.Status.Equals("Draft", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { message = "Only a Draft payroll line can be edited." });
        if (new[] { dto.AllowanceAmount, dto.EmployerBenefitAmount, dto.StaffBenefitDeduction, dto.BonusAmount, dto.OvertimeAmount, dto.AttendanceDeduction, dto.TaxAmount, dto.EmployeeEobiAmount, dto.EmployerEobiAmount, dto.OtherDeduction }.Any(x => x < 0))
            return BadRequest(new { message = "Payroll amounts other than the attendance adjustment cannot be negative." });
        if (dto.AllowanceAmount != line.AllowanceAmount ||
            dto.EmployerBenefitAmount != line.EmployerBenefitAmount ||
            dto.StaffBenefitDeduction != line.StaffBenefitDeduction ||
            dto.BonusAmount != line.BonusAmount ||
            dto.OvertimeAmount != line.OvertimeAmount ||
            dto.AttendanceDeduction != line.AttendanceDeduction ||
            dto.TaxAmount != line.TaxAmount ||
            dto.EmployeeEobiAmount != line.EmployeeEobiAmount ||
            dto.EmployerEobiAmount != line.EmployerEobiAmount)
            return BadRequest(new { message = "Calculated payroll values are read-only. Update their source module and regenerate the Draft payroll." });

        var adjustmentRemarks = Clean(dto.AttendanceAdjustmentRemarks);
        if (dto.AttendanceAdjustment != line.AttendanceAdjustment || adjustmentRemarks != line.AttendanceAdjustmentRemarks)
        {
            if (adjustmentRemarks == null)
                return BadRequest(new { message = "Adjustment comments are required." });

            var settlement = await db.AttendanceMonthlySettlements.SingleOrDefaultAsync(x =>
                x.PersonId == line.PersonId &&
                x.SettlementYear == line.PayrollRun.Year &&
                x.SettlementMonth == line.PayrollRun.Month, ct);
            if (settlement == null)
            {
                settlement = new AttendanceMonthlySettlement
                {
                    TenantId = tenant.RequiredTenantId,
                    PersonId = line.PersonId,
                    SettlementYear = line.PayrollRun.Year,
                    SettlementMonth = line.PayrollRun.Month
                };
                db.AttendanceMonthlySettlements.Add(settlement);
            }

            settlement.AdjustmentAmount = dto.AttendanceAdjustment;
            settlement.AdjustmentRemarks = adjustmentRemarks;
            settlement.IsAdjustmentApproved = false;
            settlement.AdjustmentSubmittedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            settlement.AdjustmentSubmittedDateUtc = DateTime.UtcNow;
            settlement.AdjustmentApprovedByUserId = null;
            settlement.AdjustmentApprovedDateUtc = null;
            line.AttendanceAdjustment = dto.AttendanceAdjustment;
            line.IsAttendanceAdjustmentApproved = false;
            line.AttendanceAdjustmentRemarks = adjustmentRemarks;
        }
        line.OtherDeduction = dto.OtherDeduction;
        line.Remarks = Clean(dto.Remarks);
        line.UpdatedOnUtc = DateTime.UtcNow;
        PayrollCalculationService.Recalculate(line);
        await db.SaveChangesAsync(ct);
        return Ok(new
        {
            line.Id,
            line.PersonId,
            line.StaffId,
            line.EmployeeNumber,
            line.FullName,
            line.Designation,
            line.Department,
            line.DateOfJoining,
            line.ScaleDate,
            line.Scale,
            line.ContractType,
            line.Month,
            line.Year,
            line.ScaleBasicSalary,
            line.IncrementSalary,
            line.MaxSalary,
            line.CurrentPay,
            line.BasicSalary,
            line.GeneralAllowanceAmount,
            line.ApptAllowanceAmount,
            line.ShiftAllowanceAmount,
            line.AllowanceAmount,
            line.EmployerBenefitAmount,
            line.StaffBenefitDeduction,
            line.BonusAmount,
            line.OvertimeAmount,
            line.AttendanceDeduction,
            line.IsAttendanceDeductionActive,
            line.AttendanceAdjustment,
            line.IsAttendanceAdjustmentApproved,
            line.AttendanceAdjustmentRemarks,
            line.TaxableIncome,
            line.TaxAmount,
            line.EmployeeEobiAmount,
            line.EmployerEobiAmount,
            line.OtherDeduction,
            line.GrossPay,
            line.TotalDeduction,
            line.NetPay,
            line.IsPending,
            line.PendingReviewDays,
            line.IsApproved,
            line.IsPaid,
            line.PaidOnUtc,
            line.Remarks
        });
    }

    [HttpPost("payroll-runs/{id:long}/process")]
    [Idempotent]
    public async Task<IActionResult> ProcessPayroll(long id, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/payroll", "VIEW", ct); if (denied != null) return denied;
        var authorityDenied = await GuardPayrollAuthority("VERIFY", ct); if (authorityDenied != null) return authorityDenied;
        var run = await db.PayrollRuns.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (run == null) return NotFound();
        if (!run.Status.Equals("Draft", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { message = "Only a Draft payroll can be processed." });
        if (run.Lines.Count == 0) return BadRequest(new { message = "Generate payroll lines before processing." });
        var pendingAdjustments = run.Lines.Count(x => x.AttendanceAdjustment != 0 && !x.IsAttendanceAdjustmentApproved);
        if (pendingAdjustments > 0)
            return Conflict(new { message = $"Payroll cannot be processed: {pendingAdjustments} monthly adjustment(s) still require approval." });
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Forbid();
        if (string.Equals(run.CreatedByUserId, userId, StringComparison.Ordinal))
            return Conflict(new { message = "The payroll creator cannot verify/process the same payroll. A different authorized finance supervisor must verify it." });
        var pending = await payroll.CountPendingReviewEmployeesAsync(userId, run.Year, run.Month, ct);
        if (pending > 0)
            return Conflict(new { message = $"Payroll cannot be processed: {pending} employee(s) still have Pending Review attendance for this month." });
        run.Status = "In Review";
        run.VerifiedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        run.VerifiedByName = await ActorNameAsync(ct);
        run.VerifiedOnUtc = DateTime.UtcNow;
        run.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(await PayrollResponseAsync(run, run.Lines, ct));
    }

    [HttpPost("payroll-runs/{id:long}/approve")]
    [Idempotent]
    public async Task<IActionResult> ApprovePayroll(long id, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/payroll", "VIEW", ct); if (denied != null) return denied;
        var authorityDenied = await GuardPayrollAuthority("APPROVE", ct); if (authorityDenied != null) return authorityDenied;
        var run = await db.PayrollRuns.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (run == null) return NotFound();
        if (!run.Status.Equals("In Review", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { message = "Only an In Review payroll can be approved." });

        var approverUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(approverUserId)) return Forbid();
        if (string.Equals(run.CreatedByUserId, approverUserId, StringComparison.Ordinal))
            return Conflict(new { message = "The payroll creator cannot approve the same payroll. A different authorized higher authority must approve it." });
        if (string.Equals(run.VerifiedByUserId, approverUserId, StringComparison.Ordinal))
            return Conflict(new { message = "The person who processed/verified this payroll cannot approve it. A different authorized higher authority must approve it." });

        var now = DateTime.UtcNow;
        run.Status = "Approved";
        run.ApprovedByUserId = approverUserId;
        run.ApprovedByName = await ActorNameAsync(ct);
        run.ApprovedOnUtc = now;
        run.UpdatedOnUtc = now;
        foreach (var line in run.Lines)
        {
            line.IsApproved = true;
            line.UpdatedOnUtc = now;
        }

        await db.SaveChangesAsync(ct);
        return Ok(await PayrollResponseAsync(run, run.Lines, ct));
    }

    [HttpPost("payroll-runs/{id:long}/pay")]
    [Idempotent]
    public async Task<IActionResult> PayPayroll(long id, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/payroll", "VIEW", ct); if (denied != null) return denied;
        var authorityDenied = await GuardPayrollAuthority("PAY", ct); if (authorityDenied != null) return authorityDenied;
        var run = await db.PayrollRuns.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (run == null) return NotFound();
        if (!run.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { message = "Payroll must be approved by an authorized higher authority before payment." });
        var payUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(payUserId)) return Forbid();
        if (string.Equals(run.ApprovedByUserId, payUserId, StringComparison.Ordinal))
            return Conflict(new { message = "The payroll approver cannot dispatch the same payroll. An authorized finance user must complete salary dispatch." });
        var pendingPay = await payroll.CountPendingReviewEmployeesAsync(payUserId, run.Year, run.Month, ct);
        if (pendingPay > 0)
            return Conflict(new { message = $"Payroll cannot be paid: {pendingPay} employee(s) still have Pending Review attendance for this month." });
        var now = DateTime.UtcNow;
        run.Status = "Finalized";
        run.PaidByUserId = payUserId;
        run.PaidByName = await ActorNameAsync(ct);
        run.PaidOnUtc = now;
        run.UpdatedOnUtc = now;
        foreach (var line in run.Lines)
        {
            line.IsApproved = true;
            line.IsPaid = true;
            line.PaidOnUtc = now;
            line.UpdatedOnUtc = now;
        }
        var paidPeople = run.Lines.Select(x => x.PersonId).ToArray();
        var approvedBonuses = await db.PayrollBonusLines.Include(x => x.BonusRun)
            .Where(x => paidPeople.Contains(x.PersonId) && x.IsApproved && !x.IsInactive && !x.IsPaid
                && x.BonusRun != null && x.BonusRun.Status == "Approved")
            .ToListAsync(ct);
        foreach (var bonus in approvedBonuses)
        {
            var installments = Math.Max(1, bonus.Installment);
            var elapsed = (run.Year - bonus.Year) * 12 + run.Month - bonus.Month;
            if (elapsed < 0 || elapsed >= installments || elapsed != Math.Max(0, bonus.PaidInstallmentCount))
                continue;

            bonus.PaidInstallmentCount = Math.Min(installments, bonus.PaidInstallmentCount + 1);
            bonus.UpdatedOnUtc = now;
            if (bonus.PaidInstallmentCount >= installments)
            {
                bonus.IsPaid = true;
                bonus.PaidOnUtc = now;
            }
        }
        await db.SaveChangesAsync(ct);
        return Ok(await PayrollResponseAsync(run, run.Lines, ct));
    }

    [HttpDelete("payroll-runs/{id:long}")]
    [Idempotent]
    public async Task<IActionResult> DeletePayrollRun(long id, CancellationToken ct)
    {
        var deleteDenied = await Guard("/pay-allowances/payroll", "DELETE", ct);
        var editDenied = await Guard("/pay-allowances/payroll", "EDIT", ct);
        if (deleteDenied != null && editDenied != null) return deleteDenied;
        var row = await db.PayrollRuns.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        if (!row.Status.Equals("Draft", StringComparison.OrdinalIgnoreCase)) return Conflict(new { message = "Only a Draft payroll run can be cleared." });
        db.PayrollLines.RemoveRange(row.Lines);
        db.Remove(row);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Draft payroll cleared." });
    }

    [HttpGet("eobi-settings")]
    public async Task<IActionResult> EobiSettings(CancellationToken ct) =>
        await Read("/pay-allowances/eobi-settings", db.EobiSettings.OrderByDescending(x => x.EffectiveFrom), ct);

    [HttpPost("eobi-settings")]
    public async Task<IActionResult> CreateEobi(EobiSettingSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/eobi-settings", "ADD", ct); if (denied != null) return denied;
        var error = ValidateEobi(dto); if (error != null) return BadRequest(new { message = error });
        var row = new EobiSetting { TenantId = tenant.RequiredTenantId, EmployeeRatePercentage = dto.EmployeeRatePercentage, EmployerRatePercentage = dto.EmployerRatePercentage, MinimumWage = dto.MinimumWage, MaximumContributionBase = dto.MaximumContributionBase, EffectiveFrom = dto.EffectiveFrom, EffectiveTo = dto.EffectiveTo, IsActive = dto.IsActive };
        db.Add(row); await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpPut("eobi-settings/{id:int}")]
    public async Task<IActionResult> UpdateEobi(int id, EobiSettingSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/eobi-settings", "EDIT", ct); if (denied != null) return denied;
        var row = await db.EobiSettings.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        var error = ValidateEobi(dto); if (error != null) return BadRequest(new { message = error });
        row.EmployeeRatePercentage = dto.EmployeeRatePercentage; row.EmployerRatePercentage = dto.EmployerRatePercentage; row.MinimumWage = dto.MinimumWage; row.MaximumContributionBase = dto.MaximumContributionBase; row.EffectiveFrom = dto.EffectiveFrom; row.EffectiveTo = dto.EffectiveTo; row.IsActive = dto.IsActive; row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return Ok(row);
    }

    [HttpDelete("eobi-settings/{id:int}")]
    public async Task<IActionResult> DeleteEobi(int id, CancellationToken ct) =>
        await Delete("/pay-allowances/eobi-settings", db.EobiSettings, id, ct);

    [HttpGet("staff-monthly-eobi")]
    public async Task<IActionResult> StaffMonthlyEobiList([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/eobi", "VIEW", ct); if (denied != null) return denied;
        if (year is < 2000 or > 2200 || month is < 1 or > 12)
            return BadRequest(new { message = "Enter a valid EOBI month and year." });
        var rows = await staffMonthlyEobi.ListAsync(year, month, ct);
        return Ok(rows.Select(MapStaffMonthlyEobi));
    }

    [HttpPost("staff-monthly-eobi/create")]
    [Idempotent]
    public async Task<IActionResult> CreateStaffMonthlyEobi([FromBody] StaffMonthlyEobiCreateSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/eobi", "ADD", ct); if (denied != null) return denied;
        if (dto.Year is < 2000 or > 2200 || dto.Month is < 1 or > 12)
            return BadRequest(new { message = "Enter a valid EOBI month and year." });
        try
        {
            var rows = await staffMonthlyEobi.CreateOrRefreshAsync(dto.Year, dto.Month, ct);
            return Ok(new { message = "Created Successfully", rows = rows.Select(MapStaffMonthlyEobi) });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut("staff-monthly-eobi/{id:long}")]
    public async Task<IActionResult> UpdateStaffMonthlyEobi(long id, [FromBody] StaffMonthlyEobiUpdateSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/eobi", "EDIT", ct); if (denied != null) return denied;
        try
        {
            var row = await staffMonthlyEobi.UpdateAsync(id, dto.EobiRef, ct);
            return Ok(MapStaffMonthlyEobi(row));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    private static object MapStaffMonthlyEobi(StaffMonthlyEobi row) => new
    {
        id = row.Id,
        personId = row.PersonId,
        staffGuid = row.StaffId,
        staffId = row.StaffNumber,
        eobiRef = row.EobiRef,
        fullName = row.FullName,
        department = row.Department,
        designation = row.Designation,
        doj = row.DateOfJoining,
        salaryBase = row.SalaryBase,
        coyShare = row.CompanyShare,
        staffShare = row.StaffShare,
        totAmount = row.TotalAmount,
        month = row.Month,
        year = row.Year,
        remarks = row.Remarks,
        isApproved = row.IsApproved,
        isPaid = row.IsPaid
    };

    private static object MapStaffTax(PayrollStaffTax row) => new
    {
        id = row.Id,
        taxId = row.TaxRef,
        personId = row.PersonId,
        staffGuid = row.StaffId,
        staffId = row.StaffNumber,
        fullName = row.FullName,
        department = row.Department,
        designation = row.Designation,
        dateFrom = row.DateFrom,
        dateTo = row.DateTo,
        frequency = row.Frequency,
        min = row.MonthlyPay,
        net = row.MonthlyPay,
        maxSalary = row.IncomePay,
        incomePay = row.IncomePay,
        taxMonths = row.TotMonth,
        adjustment = row.TaxAdjustment,
        taxableIncome = row.TaxableIncome,
        taxAmount = row.TaxAmount,
        monthlyTaxAmt = row.MonthlyTaxAmt,
        payMonth = row.PayMonth,
        netTax = row.NetTax,
        monthlyNetTax = row.MonthlyNetTax,
        extraAmount = row.ExtraAmount,
        monthlyPay = row.MonthlyPay,
        totMonth = row.TotMonth,
        dedPercentage = row.DedPercentage,
        isActive = row.IsActive
    };

    private static object MapStaffTaxCalculation(StaffTaxCalculationResult row) => new
    {
        personId = row.PersonId,
        staffGuid = row.StaffGuid,
        staffId = row.StaffId,
        fullName = row.FullName,
        department = row.Department,
        designation = row.Designation,
        dateFrom = row.DateFrom,
        dateTo = row.DateTo,
        frequency = row.Frequency,
        monthlyPay = row.MonthlyPay,
        totMonth = row.TotMonth,
        incomePay = row.IncomePay,
        annual = row.IncomePay,
        extraAmount = row.ExtraAmount,
        taxableIncome = row.TaxableIncome,
        taxAmount = row.TaxAmount,
        grossTax = row.TaxAmount,
        taxAdjustment = row.TaxAdjustment,
        netTax = row.NetTax,
        payMonth = row.PayMonth,
        monthlyTaxAmt = row.MonthlyTaxAmt,
        monthlyNetTax = row.MonthlyNetTax,
        dedPercentage = row.DedPercentage,
        taxYear = row.TaxYear,
        minTaxAmt = row.MinTaxAmt
    };

    private static object MapTaxParameter(PayrollTaxParameter row) => new
    {
        id = row.Id,
        minTaxAmt = row.MinTaxAmt,
        dedPercentage = row.DedPercentage,
        isActive = row.IsActive
    };

    private static StaffTaxCalculateRequest ToCalculateRequest(StaffTaxCalculateSave dto) =>
        new(dto.PersonId, dto.DateFrom, dto.DateTo, dto.Frequency, dto.MonthlyPay, dto.TotMonth,
            dto.ExtraAmount, dto.TaxAdjustment, dto.PayMonth, dto.DedPercentage);

    private static StaffTaxSaveRequest ToSaveRequest(long? id, StaffTaxUpsertSave dto) =>
        new(id, dto.PersonId, dto.DateFrom, dto.DateTo, dto.Frequency, dto.MonthlyPay, dto.TotMonth,
            dto.ExtraAmount, dto.TaxAdjustment, dto.PayMonth, dto.DedPercentage, dto.IsActive);

    [HttpGet("staff-taxes")]
    public async Task<IActionResult> StaffTaxes(CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/tax", "VIEW", ct); if (denied != null) return denied;
        var rows = await staffTax.ListAsync(ct);
        return Ok(rows.Select(MapStaffTax));
    }

    [HttpGet("staff-taxes/candidates")]
    public async Task<IActionResult> StaffTaxCandidates(CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/tax", "VIEW", ct); if (denied != null) return denied;
        return Ok(await staffTax.CandidatesAsync(ct));
    }

    [HttpPost("staff-taxes/calculate")]
    public async Task<IActionResult> CalculateStaffTax([FromBody] StaffTaxCalculateSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/tax", "VIEW", ct); if (denied != null) return denied;
        try
        {
            var result = await staffTax.CalculateAsync(ToCalculateRequest(dto), ct);
            return Ok(MapStaffTaxCalculation(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("staff-taxes")]
    public async Task<IActionResult> CreateStaffTax([FromBody] StaffTaxUpsertSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/tax", "ADD", ct); if (denied != null) return denied;
        try
        {
            var row = await staffTax.SaveAsync(ToSaveRequest(null, dto), ct);
            return Ok(MapStaffTax(row));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("staff-taxes/{id:long}")]
    public async Task<IActionResult> UpdateStaffTax(long id, [FromBody] StaffTaxUpsertSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/tax", "EDIT", ct); if (denied != null) return denied;
        try
        {
            var row = await staffTax.SaveAsync(ToSaveRequest(id, dto), ct);
            return Ok(MapStaffTax(row));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("staff-taxes/{id:long}")]
    public async Task<IActionResult> PatchStaffTax(long id, [FromBody] StaffTaxPatchSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/tax", "EDIT", ct); if (denied != null) return denied;
        try
        {
            var row = await staffTax.PatchAsync(id, new StaffTaxPatchRequest(dto.NetTax, dto.TaxAdjustment, dto.MonthlyTaxAmt, dto.IsActive), ct);
            return Ok(MapStaffTax(row));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("staff-taxes/{id:long}")]
    public async Task<IActionResult> DeleteStaffTax(long id, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/tax", "DELETE", ct); if (denied != null) return denied;
        try
        {
            await staffTax.DeleteAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("tax-parameters")]
    public async Task<IActionResult> TaxParameters(CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/tax", "VIEW", ct); if (denied != null) return denied;
        var rows = await staffTax.ListParametersAsync(ct);
        return Ok(rows.Select(MapTaxParameter));
    }

    [HttpPost("tax-parameters")]
    public async Task<IActionResult> CreateTaxParameter([FromBody] TaxParameterSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/tax", "ADD", ct); if (denied != null) return denied;
        try
        {
            var row = await staffTax.SaveParameterAsync(null, dto.MinTaxAmt, dto.DedPercentage, dto.IsActive, ct);
            return Ok(MapTaxParameter(row));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("tax-parameters/{id:int}")]
    public async Task<IActionResult> UpdateTaxParameter(int id, [FromBody] TaxParameterSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/tax", "EDIT", ct); if (denied != null) return denied;
        try
        {
            var row = await staffTax.SaveParameterAsync(id, dto.MinTaxAmt, dto.DedPercentage, dto.IsActive, ct);
            return Ok(MapTaxParameter(row));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("tax-parameters/{id:int}")]
    public async Task<IActionResult> PatchTaxParameter(int id, [FromBody] TaxParameterPatchSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/tax", "EDIT", ct); if (denied != null) return denied;
        try
        {
            var existing = await staffTax.ListParametersAsync(ct);
            var current = existing.FirstOrDefault(x => x.Id == id);
            if (current == null) return NotFound();
            var row = await staffTax.SaveParameterAsync(
                id,
                dto.MinTaxAmt ?? current.MinTaxAmt,
                dto.DedPercentage ?? current.DedPercentage,
                dto.IsActive ?? current.IsActive,
                ct);
            return Ok(MapTaxParameter(row));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("tax-parameters/{id:int}")]
    public async Task<IActionResult> DeleteTaxParameter(int id, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/tax", "DELETE", ct); if (denied != null) return denied;
        try
        {
            await staffTax.DeleteParameterAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("tax-slabs")]
    public async Task<IActionResult> TaxSlabs(CancellationToken ct) =>
        await Read("/pay-allowances/tax", db.PayrollTaxSlabs.OrderByDescending(x => x.TaxYear).ThenBy(x => x.FromAmount), ct);

    [HttpGet("tax-lookups")]
    public async Task<IActionResult> TaxLookups(CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/tax", "VIEW", ct); if (denied != null) return denied;
        var rates = await db.RateTypes.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => x.Name)
            .Distinct()
            .ToListAsync(ct);
        return Ok(new { rateTypes = rates });
    }

    [HttpPost("tax-slabs")]
    public async Task<IActionResult> CreateTax([FromBody] TaxSlabSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/tax", "ADD", ct); if (denied != null) return denied;
        if (dto is null) return BadRequest(new { message = "Tax bracket payload is required." });
        var normalized = NormalizeTaxSlab(dto);
        var error = await ValidateTaxAsync(normalized, ct); if (error != null) return BadRequest(new { message = error });
        if (await TaxOverlap(normalized, null, ct)) return Conflict(new { message = "This tax slab overlaps an existing active slab." });
        var row = new PayrollTaxSlab
        {
            TenantId = tenant.RequiredTenantId,
            TaxYear = normalized.TaxYear!.Trim(),
            SlabName = normalized.SlabName.Trim(),
            FromAmount = normalized.FromAmount,
            ToAmount = normalized.ToAmount,
            RateType = Clean(normalized.RateType),
            FixedTaxAmount = normalized.FixedTaxAmount,
            RatePercentage = normalized.RatePercentage,
            TotTax = normalized.TotTax,
            IsActive = normalized.IsActive,
        };
        try
        {
            db.Add(row);
            await db.SaveChangesAsync(ct);
            return Ok(row);
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(new { message = ex.InnerException?.Message ?? ex.Message });
        }
    }

    [HttpPut("tax-slabs/{id:int}")]
    public async Task<IActionResult> UpdateTax(int id, [FromBody] TaxSlabSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/tax", "EDIT", ct); if (denied != null) return denied;
        if (dto is null) return BadRequest(new { message = "Tax bracket payload is required." });
        var row = await db.PayrollTaxSlabs.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();
        var normalized = NormalizeTaxSlab(dto, row.TaxYear);
        var error = await ValidateTaxAsync(normalized, ct); if (error != null) return BadRequest(new { message = error });
        if (await TaxOverlap(normalized, id, ct)) return Conflict(new { message = "This tax slab overlaps an existing active slab." });
        row.TaxYear = normalized.TaxYear!.Trim();
        row.SlabName = normalized.SlabName.Trim();
        row.FromAmount = normalized.FromAmount;
        row.ToAmount = normalized.ToAmount;
        row.RateType = Clean(normalized.RateType);
        row.FixedTaxAmount = normalized.FixedTaxAmount;
        row.RatePercentage = normalized.RatePercentage;
        row.TotTax = normalized.TotTax;
        row.IsActive = normalized.IsActive;
        row.UpdatedOnUtc = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
            return Ok(row);
        }
        catch (DbUpdateException ex)
        {
            return BadRequest(new { message = ex.InnerException?.Message ?? ex.Message });
        }
    }

    [HttpPatch("tax-slabs/{id:int}")]
    public async Task<IActionResult> PatchTax(int id, TaxSlabPatchSave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/tax", "EDIT", ct); if (denied != null) return denied;
        var row = await db.PayrollTaxSlabs.SingleOrDefaultAsync(x => x.Id == id, ct); if (row == null) return NotFound();

        if (dto.SlabName != null) row.SlabName = dto.SlabName.Trim();
        if (dto.FromAmount.HasValue) row.FromAmount = dto.FromAmount.Value;
        if (dto.ToAmount.HasValue) row.ToAmount = dto.ToAmount.Value;
        if (dto.RateType != null) row.RateType = Clean(dto.RateType);
        if (dto.FixedTaxAmount.HasValue) row.FixedTaxAmount = dto.FixedTaxAmount.Value;
        if (dto.RatePercentage.HasValue) row.RatePercentage = dto.RatePercentage.Value;
        if (dto.TotTax.HasValue) row.TotTax = dto.TotTax.Value;
        if (dto.IsActive.HasValue) row.IsActive = dto.IsActive.Value;

        var check = new TaxSlabSave
        {
            TaxYear = row.TaxYear,
            SlabName = row.SlabName,
            FromAmount = row.FromAmount,
            ToAmount = row.ToAmount,
            RateType = row.RateType,
            FixedTaxAmount = row.FixedTaxAmount,
            RatePercentage = row.RatePercentage,
            TotTax = row.TotTax,
            IsActive = row.IsActive,
        };
        var error = await ValidateTaxAsync(check, ct); if (error != null) return BadRequest(new { message = error });
        if (await TaxOverlap(check, id, ct)) return Conflict(new { message = "This tax slab overlaps an existing active slab." });

        row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(row);
    }

    [HttpDelete("tax-slabs/{id:int}")]
    public async Task<IActionResult> DeleteTax(int id, CancellationToken ct) =>
        await Delete("/pay-allowances/tax", db.PayrollTaxSlabs, id, ct);

    [HttpGet("eobi-eligibility")]
    public async Task<IActionResult> Eligibility(CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/eobi-eligibility", "VIEW", ct); if (denied != null) return denied;
        var rows = await (
            from dir in db.StaffDirectoryRows.AsNoTracking()
            where dir.IsPersonActive
            join e0 in db.EobiEligibilities.AsNoTracking() on dir.PersonId equals e0.PersonId into eligibilityJoin
            from e in eligibilityJoin.DefaultIfEmpty()
            join hr in db.PersonHrProfiles.AsNoTracking() on dir.PersonId equals hr.PersonId into hrJoin
            from hr in hrJoin.DefaultIfEmpty()
            orderby dir.FullName
            select new
            {
                id = e != null ? e.Id : 0,
                personId = dir.PersonId,
                staffId = dir.EmployeeId,
                fullName = dir.FullName,
                eobiNo = e != null ? e.EobiNumber : null,
                eobiNumber = e != null ? e.EobiNumber : null,
                department = dir.Department,
                doj = hr != null && hr.JoiningDate != null ? DateOnly.FromDateTime(hr.JoiningDate.Value) : (DateOnly?)null,
                isOn = e != null && e.IsEligible,
                isEligible = e != null && e.IsEligible,
                effectiveFrom = e != null ? e.EffectiveFrom : (hr != null && hr.JoiningDate != null ? DateOnly.FromDateTime(hr.JoiningDate.Value) : DateOnly.FromDateTime(DateTime.UtcNow)),
                effectiveTo = e != null ? e.EffectiveTo : null,
                remarks = e != null ? e.Remarks : null,
            }).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("eobi-eligibility/reload")]
    [Idempotent]
    public async Task<IActionResult> ReloadEligibility(CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/eobi-eligibility", "ADD", ct); if (denied != null) return denied;
        var staff = await db.StaffDirectoryRows.AsNoTracking()
            .Where(x => x.IsPersonActive)
            .GroupBy(x => x.PersonId)
            .Select(x => new { PersonId = x.Key })
            .ToListAsync(ct);
        var personIds = staff.Select(x => x.PersonId).ToArray();
        var existing = await db.EobiEligibilities
            .Where(x => personIds.Contains(x.PersonId))
            .Select(x => x.PersonId)
            .ToHashSetAsync(ct);
        var joiningDates = await db.PersonHrProfiles.AsNoTracking()
            .Where(x => personIds.Contains(x.PersonId))
            .ToDictionaryAsync(x => x.PersonId, x => x.JoiningDate, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var added = 0;
        foreach (var employee in staff.Where(x => !existing.Contains(x.PersonId)))
        {
            joiningDates.TryGetValue(employee.PersonId, out var joiningDate);
            db.EobiEligibilities.Add(new EobiEligibility
            {
                TenantId = tenant.RequiredTenantId,
                PersonId = employee.PersonId,
                EffectiveFrom = joiningDate.HasValue ? DateOnly.FromDateTime(joiningDate.Value) : today,
                IsEligible = false,
                CreatedOnUtc = DateTime.UtcNow
            });
            added++;
        }
        if (added > 0)
            await db.SaveChangesAsync(ct);
        return Ok(new { message = $"Staff reloaded successfully. {staff.Count} active staff available; {added} new eligibility record(s) added as Off.", total = staff.Count, added });
    }

    [HttpPost("eobi-eligibility")]
    public async Task<IActionResult> CreateEligibility(EobiEligibilitySave dto, CancellationToken ct)
    {
        var denied = await Guard("/pay-allowances/eobi-eligibility", "ADD", ct); if (denied != null) return denied;
        var error = ValidateEligibility(dto); if (error != null) return BadRequest(new { message = error });
        if (!await db.Persons.AnyAsync(x => x.PersonId == dto.PersonId, ct)) return BadRequest(new { message = "Selected employee was not found." });
        if (await db.EobiEligibilities.AnyAsync(x => x.PersonId == dto.PersonId, ct)) return Conflict(new { message = "EOBI eligibility already exists for this employee." });
        var row = new EobiEligibility { TenantId = tenant.RequiredTenantId, PersonId = dto.PersonId, EobiNumber = Clean(dto.EobiNumber), EffectiveFrom = dto.EffectiveFrom, EffectiveTo = dto.EffectiveTo, IsEligible = dto.IsEligible, Remarks = Clean(dto.Remarks) };
        db.Add(row);
        await SyncPersonJoiningDateAsync(dto.PersonId, dto.EffectiveFrom, ct);
        await db.SaveChangesAsync(ct); return Ok(row);
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
        await SyncPersonJoiningDateAsync(dto.PersonId, dto.EffectiveFrom, ct);
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

    private async Task<IActionResult?> GuardPayrollAuthority(string actionCode, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Forbid();
        var staffId = await db.Persons.AsNoTracking()
            .Where(person => person.IdentityUserId == userId && person.Staff != null)
            .Select(person => (Guid?)person.Staff!.StaffId)
            .FirstOrDefaultAsync(ct);
        if (!staffId.HasValue)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "No active staff profile is linked to this account." });

        var normalizedAction = actionCode.Trim().ToUpperInvariant();
        var assigned = await db.ProcessActionAuthorities.AsNoTracking().AnyAsync(authority =>
            authority.ProcessCode == "PAYROLL" &&
            authority.ActionCode == normalizedAction &&
            authority.StaffId == staffId.Value &&
            authority.IsActive, ct);
        return assigned
            ? null
            : StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = $"You are not assigned to the Payroll {normalizedAction} stage. Configure this person in HR Management > Process > Reports > Approve Process."
            });
    }

    private async Task<bool> TaxOverlap(TaxSlabSave dto, int? id, CancellationToken ct)
    {
        if (!dto.IsActive) return false;
        var taxYear = (dto.TaxYear ?? string.Empty).Trim();
        // Adjacent slabs may share a boundary (for example 3,200,001-4,100,000
        // followed by 4,100,000 and above). Payroll resolution deliberately uses
        // the slab with the highest matching lower bound at that exact boundary.
        // Keep an unlimited upper bound as NULL. decimal.MaxValue cannot be sent
        // to SQL Server's decimal(18,2) column without a conversion overflow.
        var candidates = db.PayrollTaxSlabs.Where(x =>
            x.Id != id &&
            x.IsActive &&
            x.TaxYear == taxYear &&
            (x.ToAmount == null || x.ToAmount > dto.FromAmount));
        return dto.ToAmount.HasValue
            ? await candidates.AnyAsync(x => x.FromAmount < dto.ToAmount.Value, ct)
            : await candidates.AnyAsync(ct);
    }

    private static TaxSlabSave NormalizeTaxSlab(TaxSlabSave dto, string? existingTaxYear = null)
    {
        var year = DateTime.UtcNow.Year;
        var fallback = string.IsNullOrWhiteSpace(existingTaxYear) ? $"{year}-{year + 1}" : existingTaxYear.Trim();
        dto.TaxYear = string.IsNullOrWhiteSpace(dto.TaxYear) ? fallback : dto.TaxYear.Trim();
        dto.SlabName = (dto.SlabName ?? string.Empty).Trim();
        dto.RateType = string.IsNullOrWhiteSpace(dto.RateType) ? null : dto.RateType.Trim();
        return dto;
    }

    private async Task<string?> ValidateTaxAsync(TaxSlabSave x, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(x.TaxYear)) return "Tax year is required.";
        if (string.IsNullOrWhiteSpace(x.SlabName)) return "Slab name is required.";
        if (x.FromAmount < 0 || (x.ToAmount.HasValue && x.ToAmount.Value < x.FromAmount) || x.FixedTaxAmount < 0 || x.RatePercentage is < 0 or > 100)
            return "Enter a valid tax range and rate.";
        if (x.TotTax is < 0) return "Total tax cannot be negative.";
        if (!string.IsNullOrWhiteSpace(x.RateType)
            && !await db.RateTypes.AsNoTracking().AnyAsync(type => type.IsActive && type.Name == x.RateType.Trim(), ct))
            return "Select a valid rate type.";
        return null;
    }

    private async Task<string?> ValidateDefinition(string code, string name, string calculation, decimal amount, decimal percentage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)) return "Code and name are required.";
        if (code.Trim().Length > 30 || name.Trim().Length > 120) return "Code or name is too long.";
        var normalized = await NormalizeCalculationAsync(calculation, ct);
        if (normalized == null) return "Calculation type must be a valid lookup value.";
        if (amount < 0 || percentage < 0 || percentage > 100) return "Amount must be positive and percentage must be between 0 and 100.";
        if (normalized == "Fixed" && amount <= 0) return "Enter a fixed amount.";
        if (normalized == "Percentage" && percentage <= 0) return "Enter a percentage.";
        return null;
    }
    private static string? ValidatePayroll(PayrollRunSave x) => x.Year is < 2000 or > 2200 || x.Month is < 1 or > 12 ? "Enter a valid payroll month and year." : null;

    private async Task<string?> ValidatePayrollAsync(PayrollRunSave x, CancellationToken ct)
    {
        var basic = ValidatePayroll(x);
        if (basic != null) return basic;
        if (string.IsNullOrWhiteSpace(x.Status)) return null;
        var normalized = await NormalizeStatusAsync(x.Status, ct);
        return normalized == null ? "Select a valid payroll status." : null;
    }
    private static string? ValidateEobi(EobiSettingSave x) => x.EmployeeRatePercentage is < 0 or > 100 || x.EmployerRatePercentage is < 0 or > 100 || x.MinimumWage < 0 || x.MaximumContributionBase < 0 ? "Enter valid EOBI rates and amounts." : x.EffectiveTo < x.EffectiveFrom ? "Effective To cannot be before Effective From." : null;
    private static string? ValidateEligibility(EobiEligibilitySave x) => x.PersonId == Guid.Empty ? "Employee is required." : x.EffectiveTo < x.EffectiveFrom ? "Effective To cannot be before Effective From." : null;

    private async Task SyncPersonJoiningDateAsync(Guid personId, DateOnly joiningDate, CancellationToken ct)
    {
        var profile = await db.PersonHrProfiles.SingleOrDefaultAsync(x => x.PersonId == personId, ct);
        if (profile == null)
        {
            var personTenantId = await db.Persons.AsNoTracking()
                .Where(x => x.PersonId == personId)
                .Select(x => x.TenantId)
                .FirstOrDefaultAsync(ct);
            if (personTenantId == 0) personTenantId = tenant.RequiredTenantId;
            db.PersonHrProfiles.Add(new PersonHrProfile
            {
                PersonId = personId,
                TenantId = personTenantId,
                JoiningDate = joiningDate.ToDateTime(TimeOnly.MinValue),
                CreatedDate = DateTime.UtcNow,
            });
            return;
        }

        profile.JoiningDate = joiningDate.ToDateTime(TimeOnly.MinValue);
        profile.ModifiedDate = DateTime.UtcNow;
    }

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
        if (x.MinimumService < 0 || x.Amount < 0 || x.Percentage < 0 || x.CompanyShare < 0 || x.StaffShare < 0) return "Parameter values cannot be negative.";
        if (x.Percentage > 100) return "Percentage cannot be greater than 100.";
        return null;
    }
    private static string? ValidateBonusDistribution(string benefitType, BonusDistributionSave? x)
    {
        if (!benefitType.Equals("Bonus", StringComparison.OrdinalIgnoreCase))
            return x == null ? null : "Bonus Distribution can only be saved against a Bonus benefit rule.";
        if (x == null) return "Bonus Distribution is required when Benefits Type is Bonus.";
        if (x.Month is null or < 1 or > 12) return "Bonus month must be between 1 and 12.";
        if (x.ServiceYears < 0) return "Service years cannot be negative.";
        if (x.Installments is < 1 or > 120) return "Installments must be between 1 and 120.";
        var percentages = new[] { x.BasicPercentage, x.ServicePercentage, x.AssessmentPercentage, x.AttendancePercentage, x.LeavePercentage, x.DisciplinePercentage };
        return percentages.Any(value => value is < 0 or > 100) ? "Bonus distribution percentages must be between 0 and 100." : null;
    }
    private static string? ValidateBenefitParameterPaymentSelection(string benefitType, BenefitParameterSave x)
    {
        if (benefitType.Equals("EOBI", StringComparison.OrdinalIgnoreCase)) return null;
        var amountType = x.AmountType?.Trim() ?? string.Empty;
        if (amountType.Equals("Figure", StringComparison.OrdinalIgnoreCase))
            return x.Amount > 0 ? null : "Amount (PH) must be greater than zero when AmtType is Figure.";
        if (!amountType.Equals("Pay Ref", StringComparison.OrdinalIgnoreCase))
            return "Select Figure or Pay Ref as AmtType.";
        if (string.IsNullOrWhiteSpace(x.PayType)) return "PayType is required when AmtType is Pay Ref.";
        if (!new[] { "Basic", "Current", "CurrentPay", "Net", "Gross" }.Contains(x.PayType.Trim(), StringComparer.OrdinalIgnoreCase))
            return "Selected PayType is invalid.";
        return x.Percentage is > 0 and <= 100 ? null : "Percentage must be between 0.0001 and 100 when AmtType is Pay Ref.";
    }
    private async Task<string?> ValidateBenefitRuleReferences(BenefitRuleSave x, CancellationToken ct)
    {
        if (!await db.BenefitTypes.AsNoTracking().AnyAsync(type => type.IsActive && type.Name == x.BenefitsType.Trim(), ct))
            return "Selected benefit type was not found.";
        if (!string.IsNullOrWhiteSpace(x.Scale) && !await db.SalaryScales.AsNoTracking().AnyAsync(scale => scale.IsActive && scale.ScaleName == x.Scale.Trim(), ct))
            return "Selected salary scale was not found.";
        if (!string.IsNullOrWhiteSpace(x.Contract) && !await db.ContractTypes.AsNoTracking().AnyAsync(type => type.IsActive && type.Name == x.Contract.Trim(), ct))
            return "Selected contract type was not found.";
        if (!string.IsNullOrWhiteSpace(x.Frequency) && !await db.FrequencyTypes.AsNoTracking().AnyAsync(type => type.IsActive && type.Name == x.Frequency.Trim(), ct))
            return "Selected frequency was not found.";
        if (!x.OrganizationId.HasValue) return null;
        var tenantRootId = await db.Tenants.IgnoreQueryFilters().AsNoTracking().Where(row => row.Id == tenant.RequiredTenantId)
            .Select(row => row.OrganizationTreeId).SingleAsync(ct);
        var nodes = await db.OrganizationTree.AsNoTracking().ToListAsync(ct);
        var companies = ResolveBenefitCompanyNodes(nodes, tenantRootId);
        var scope = CollectBenefitOrganizationScope(tenantRootId, nodes, companies);
        return scope.Contains(x.OrganizationId.Value)
            ? null
            : "Selected entitlement is outside the current company.";
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
    private static void ApplyBenefitParameter(PayrollBenefitParameter row, BenefitParameterSave x, string benefitType)
    {
        row.Name = x.Name.Trim();
        row.PeriodFrom = x.PeriodFrom;
        row.PeriodTo = x.PeriodTo;
        row.MinimumService = x.MinimumService;
        var isEobi = benefitType.Equals("EOBI", StringComparison.OrdinalIgnoreCase);
        // EOBI shares are fixed values and do not use the generic Amount Type or
        // Pay Type selectors. Ignore stale/client-supplied values for this type.
        var amountType = x.AmountType?.Trim() ?? string.Empty;
        var isFigure = amountType.Equals("Figure", StringComparison.OrdinalIgnoreCase);
        var isPayReference = amountType.Equals("Pay Ref", StringComparison.OrdinalIgnoreCase);
        row.AmountType = isEobi ? string.Empty : amountType;
        row.Amount = !isEobi && isFigure ? x.Amount : 0;
        row.Percentage = !isEobi && isPayReference ? x.Percentage : 0;
        row.PayType = !isEobi && isPayReference ? x.PayType?.Trim() ?? string.Empty : string.Empty;
        row.CompanyShare = x.CompanyShare;
        row.StaffShare = x.StaffShare;
    }
    private PayrollBonusDistribution CreateBonusDistribution(int benefitParameterId, BonusDistributionSave source)
    {
        var row = new PayrollBonusDistribution
        {
            TenantId = tenant.RequiredTenantId,
            BenefitParameterId = benefitParameterId
        };
        ApplyBonusDistribution(row, source);
        return row;
    }
    private static void ApplyBonusDistribution(PayrollBonusDistribution row, BonusDistributionSave source)
    {
        row.Month = source.Month;
        row.BasicPercentage = source.BasicPercentage;
        row.ServicePercentage = source.ServicePercentage;
        row.ServiceYears = source.ServiceYears;
        row.AssessmentPercentage = source.AssessmentPercentage;
        row.AttendancePercentage = source.AttendancePercentage;
        row.LeavePercentage = source.LeavePercentage;
        row.DisciplinePercentage = source.DisciplinePercentage;
        row.Installments = source.Installments;
        row.UpdatedOnUtc = DateTime.UtcNow;
    }
    private static HashSet<int> CollectOrganizationDescendants(int rootId, IReadOnlyCollection<OrganizationTree> nodes)
    {
        var children = nodes.Where(x => x.ParentId.HasValue).GroupBy(x => x.ParentId!.Value)
            .ToDictionary(x => x.Key, x => x.Select(node => node.Id).ToArray());
        var result = new HashSet<int> { rootId };
        var pending = new Stack<int>();
        pending.Push(rootId);
        while (pending.TryPop(out var parentId))
        {
            if (!children.TryGetValue(parentId, out var childIds)) continue;
            foreach (var childId in childIds)
                if (result.Add(childId)) pending.Push(childId);
        }
        return result;
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
    private async Task<string?> NormalizeCalculationAsync(string? x, CancellationToken ct)
    {
        var value = x?.Trim() ?? "";
        var codes = await LookupCodesAsync("CALCULATION_TYPE", ct);
        if (codes.Count == 0) codes = ["Fixed", "Percentage"];
        return codes.FirstOrDefault(c => c.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> NormalizeFrequencyAsync(string? x, CancellationToken ct)
    {
        var value = x?.Trim() ?? "";
        var fromLookup = await LookupCodesAsync("PAY_FREQUENCY", ct);
        var fromPlatform = await db.FrequencyTypes.AsNoTracking().Where(f => f.IsActive).Select(f => f.Name).ToListAsync(ct);
        var codes = fromLookup.Concat(fromPlatform).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (codes.Count == 0) codes = ["Monthly", "Quarterly", "Annual", "OneTime"];
        return codes.FirstOrDefault(c => c.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? codes[0];
    }

    private async Task<string?> NormalizeStatusAsync(string? x, CancellationToken ct)
    {
        var value = x?.Trim() ?? "";
        var codes = await LookupCodesAsync("PAYROLL_RUN_STATUS", ct);
        if (codes.Count == 0) codes = ["Draft", "In Review", "Approved", "Finalized"];
        return codes.FirstOrDefault(c => c.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    private Task<List<string>> LookupCodesAsync(string lookupTypeCode, CancellationToken ct) =>
        db.AppLookupValues.AsNoTracking()
            .Where(v => v.IsActive && v.LookupType != null && v.LookupType.IsActive &&
                        v.LookupType.LookupTypeCode == lookupTypeCode)
            .OrderBy(v => v.SortOrder)
            .Select(v => v.ValueCode)
            .ToListAsync(ct);
    private static string? Clean(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
    private async Task<string> ActorNameAsync(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var fullName = await db.Persons.AsNoTracking()
                .Where(person => person.IdentityUserId == userId)
                .Select(person => person.FullName)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(fullName)) return fullName.Trim();
        }

        return User.FindFirstValue(ClaimTypes.Name)
            ?? User.FindFirstValue(ClaimTypes.Email)
            ?? User.Identity?.Name
            ?? "User";
    }

    private async Task<object> PayrollResponseAsync(PayrollRun? run, IEnumerable<PayrollLine> lines, CancellationToken ct)
    {
        if (run != null)
        {
            var actorIds = new[] { run.CreatedByUserId, run.VerifiedByUserId, run.ApprovedByUserId, run.PaidByUserId }
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (actorIds.Length > 0)
            {
                var names = await db.Persons.AsNoTracking()
                    .Where(person => actorIds.Contains(person.IdentityUserId))
                    .Select(person => new { person.IdentityUserId, person.FullName })
                    .ToDictionaryAsync(person => person.IdentityUserId, person => person.FullName, StringComparer.Ordinal, ct);

                if (run.CreatedByUserId != null && names.TryGetValue(run.CreatedByUserId, out var createdBy))
                    run.CreatedByName = createdBy;
                if (run.VerifiedByUserId != null && names.TryGetValue(run.VerifiedByUserId, out var verifiedBy))
                    run.VerifiedByName = verifiedBy;
                if (run.ApprovedByUserId != null && names.TryGetValue(run.ApprovedByUserId, out var approvedBy))
                    run.ApprovedByName = approvedBy;
                if (run.PaidByUserId != null && names.TryGetValue(run.PaidByUserId, out var paidBy))
                    run.PaidByName = paidBy;
            }
        }

        return PayrollResponse(run, lines);
    }

    private static object PayrollResponse(PayrollRun? run, IEnumerable<PayrollLine> lines) => new
    {
        run = run == null ? null : new
        {
            run.Id,
            run.Year,
            run.Month,
            run.RunNumber,
            run.PayDate,
            run.Status,
            run.Notes,
            run.CreatedByUserId,
            run.CreatedByName,
            run.CreatedOnUtc,
            run.VerifiedByUserId,
            run.VerifiedByName,
            run.VerifiedOnUtc,
            run.ApprovedByUserId,
            run.ApprovedByName,
            run.ApprovedOnUtc,
            run.PaidByUserId,
            run.PaidByName,
            run.PaidOnUtc,
            run.UpdatedOnUtc
        },
        lines = lines.OrderBy(x => x.FullName).Select(x => new
        {
            x.Id,
            x.PersonId,
            x.StaffId,
            x.EmployeeNumber,
            x.FullName,
            x.Designation,
            x.Department,
            x.DateOfJoining,
            x.ScaleDate,
            x.Scale,
            x.ContractType,
            Month = x.Month > 0 ? x.Month : run?.Month ?? 0,
            Year = x.Year > 0 ? x.Year : run?.Year ?? 0,
            Ref = run?.RunNumber,
            x.ScaleBasicSalary,
            x.IncrementSalary,
            x.MaxSalary,
            x.CurrentPay,
            x.BasicSalary,
            x.GeneralAllowanceAmount,
            x.ApptAllowanceAmount,
            x.ShiftAllowanceAmount,
            x.AllowanceAmount,
            x.EmployerBenefitAmount,
            x.StaffBenefitDeduction,
            x.BonusAmount,
            x.OvertimeAmount,
            x.AttendanceDeduction,
            x.IsAttendanceDeductionActive,
            x.AttendanceAdjustment,
            x.IsAttendanceAdjustmentApproved,
            x.AttendanceAdjustmentRemarks,
            x.TaxableIncome,
            x.TaxAmount,
            x.EmployeeEobiAmount,
            x.EmployerEobiAmount,
            x.OtherDeduction,
            x.GrossPay,
            x.TotalDeduction,
            x.NetPay,
            x.IsPending,
            x.PendingReviewDays,
            x.IsApproved,
            x.IsPaid,
            x.PaidOnUtc,
            x.Remarks
        })
    };
}

public sealed record PayBenefitSave(string Code, string Name, string CalculationType, decimal Amount, decimal Percentage, bool IsTaxable, bool IsEobiContributory, bool IsActive, string? Description);
public sealed record PayBonusSave(string Code, string Name, string CalculationType, decimal Amount, decimal Percentage, string Frequency, bool IsTaxable, bool IsActive, string? Description);
public sealed record PayrollRunSave(int Year, int Month, string? RunNumber, DateOnly PayDate, string Status, string? Notes);
public sealed record PayrollLineSave(decimal AllowanceAmount, decimal EmployerBenefitAmount, decimal StaffBenefitDeduction, decimal BonusAmount, decimal OvertimeAmount, decimal AttendanceDeduction, decimal AttendanceAdjustment, string? AttendanceAdjustmentRemarks, decimal TaxAmount, decimal EmployeeEobiAmount, decimal EmployerEobiAmount, decimal OtherDeduction, string? Remarks);
public sealed record EobiSettingSave(decimal EmployeeRatePercentage, decimal EmployerRatePercentage, decimal MinimumWage, decimal MaximumContributionBase, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive);
public sealed class TaxSlabSave
{
    public string? TaxYear { get; set; }
    public string SlabName { get; set; } = string.Empty;
    public decimal FromAmount { get; set; }
    public decimal? ToAmount { get; set; }
    public string? RateType { get; set; }
    public decimal FixedTaxAmount { get; set; }
    public decimal RatePercentage { get; set; }
    public decimal? TotTax { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class TaxSlabPatchSave
{
    public string? SlabName { get; set; }
    public decimal? FromAmount { get; set; }
    public decimal? ToAmount { get; set; }
    public string? RateType { get; set; }
    public decimal? FixedTaxAmount { get; set; }
    public decimal? RatePercentage { get; set; }
    public decimal? TotTax { get; set; }
    public bool? IsActive { get; set; }
}
public sealed record TaxParameterSave(decimal MinTaxAmt, decimal DedPercentage = 100, bool IsActive = true);
public sealed record TaxParameterPatchSave(decimal? MinTaxAmt, decimal? DedPercentage, bool? IsActive);
public sealed record StaffTaxCalculateSave(Guid PersonId, DateOnly DateFrom, DateOnly DateTo, string? Frequency, decimal MonthlyPay, int TotMonth, decimal ExtraAmount, decimal TaxAdjustment, int PayMonth, decimal DedPercentage);
public sealed record StaffTaxUpsertSave(Guid PersonId, DateOnly DateFrom, DateOnly DateTo, string? Frequency, decimal MonthlyPay, int TotMonth, decimal ExtraAmount, decimal TaxAdjustment, int PayMonth, decimal DedPercentage, bool IsActive = true);
public sealed record StaffTaxPatchSave(decimal? NetTax, decimal? TaxAdjustment, decimal? MonthlyTaxAmt, bool? IsActive);
public sealed record EobiEligibilitySave(Guid PersonId, string? EobiNumber, DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsEligible, string? Remarks);
public sealed record StaffMonthlyEobiCreateSave(int Year, int Month);
public sealed record StaffMonthlyEobiUpdateSave(string? EobiRef);
public sealed record BenefitRuleSave(string BenefitsType, string Name, string? Company, string? Entitled, string? Contract, string? Frequency, DateOnly? ValidFrom, DateOnly? ValidTo, decimal MaximumExpense, string? ServiceStatus, string? Scale, DateOnly? Wef, decimal MinimumService, decimal MaximumPh, decimal MinimumPh, bool IsIneligible, string? ShareType, decimal CompanyShare, decimal StaffShare, int? OrganizationId, string? CompanyName);
public sealed record BenefitParameterSave(int BenefitRuleId, string Name, DateOnly? PeriodFrom, DateOnly? PeriodTo, decimal MinimumService, string? AmountType, string? PayType, decimal Amount, decimal Percentage, decimal CompanyShare, decimal StaffShare, BonusDistributionSave? BonusDistribution);
public sealed record BonusDistributionSave(int? Month, decimal BasicPercentage, decimal ServicePercentage, decimal ServiceYears, decimal AssessmentPercentage, decimal AttendancePercentage, decimal LeavePercentage, decimal DisciplinePercentage, int Installments);
