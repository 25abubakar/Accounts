using System.Security.Claims;
using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController, Route("api/pay-allowances/bonus-workspace"), Authorize, Produces("application/json")]
public sealed class PayrollBonusController(
    ApplicationDbContext db,
    ITenantService tenant,
    RbacService rbac,
    TenantPermissionService tenantPermissions) : ControllerBase
{
    private const string MenuRoute = "/pay-allowances/bonus";

    [HttpGet("rules")]
    public async Task<IActionResult> Rules(CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        var rows = await db.PayrollBenefitRules.AsNoTracking()
            .Where(x => x.BenefitsType == "Bonus")
            .OrderByDescending(x => x.ValidFrom).ThenBy(x => x.Name)
            .Select(x => new { x.Id, reference = x.BenefitReference, x.Name, x.ValidFrom, x.ValidTo, x.Scale, x.Frequency })
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("run")]
    public async Task<IActionResult> Run([FromQuery] int benefitRuleId, [FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        if (!ValidPeriod(year, month)) return BadRequest(new { message = "Enter a valid bonus month." });
        return await RunResponse(benefitRuleId, year, month, ct);
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate(GenerateBonusRequest request, CancellationToken ct)
    {
        var denied = await Guard("ADD", ct); if (denied != null) return denied;
        if (!ValidPeriod(request.Year, request.Month)) return BadRequest(new { message = "Enter a valid bonus month." });

        var existing = await db.PayrollBonusRuns.AsNoTracking().AnyAsync(x =>
            x.BenefitRuleId == request.BenefitRuleId && x.Year == request.Year && x.Month == request.Month, ct);
        if (existing) return await RunResponse(request.BenefitRuleId, request.Year, request.Month, ct);

        var rule = await db.PayrollBenefitRules.Include(x => x.Parameters).ThenInclude(x => x.BonusDistribution)
            .SingleOrDefaultAsync(x => x.Id == request.BenefitRuleId && x.BenefitsType == "Bonus", ct);
        if (rule == null) return NotFound(new { message = "Selected bonus benefit rule was not found." });

        var periodStart = new DateOnly(request.Year, request.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        if (rule.ValidFrom.HasValue && periodEnd < rule.ValidFrom.Value || rule.ValidTo.HasValue && periodStart > rule.ValidTo.Value)
            return BadRequest(new { message = "Selected rule is not effective for this month." });

        var staff = await db.StaffDirectoryRows.AsNoTracking().Where(x => x.IsPersonActive).OrderBy(x => x.FullName).ToListAsync(ct);
        var personIds = staff.Select(x => x.PersonId).Distinct().ToArray();
        var profiles = await db.PersonHrProfiles.AsNoTracking().Where(x => personIds.Contains(x.PersonId)).ToDictionaryAsync(x => x.PersonId, ct);
        var defaultPercent = ResolveDefaultPercent(rule);
        var organizationNodes = await db.OrganizationTree.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var now = DateTime.UtcNow;
        var run = new PayrollBonusRun
        {
            TenantId = tenant.RequiredTenantId,
            BenefitRuleId = rule.Id,
            RunNumber = $"BON-{request.Year}{request.Month:00}-{rule.Id}",
            BenefitReference = rule.BenefitReference,
            RuleName = rule.Name,
            Year = request.Year,
            Month = request.Month,
            Status = "Generated",
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            CreatedByName = ActorName(),
            CreatedOnUtc = now
        };

        foreach (var employee in staff)
        {
            profiles.TryGetValue(employee.PersonId, out var profile);
            var reasons = new List<string>();
            var salary = profile?.CurrentPay is > 0 ? profile.CurrentPay.Value : profile?.BasicSalary ?? 0;
            var joining = profile?.JoiningDate;
            var serviceYears = joining.HasValue ? Math.Max(0, (decimal)(periodEnd.ToDateTime(TimeOnly.MinValue) - joining.Value.Date).TotalDays / 365.2425m) : 0;
            if (profile == null) reasons.Add("HR profile is missing");
            if (salary <= 0) reasons.Add("Salary is missing");
            if (!string.IsNullOrWhiteSpace(rule.Scale) && !string.Equals(rule.Scale.Trim(), profile?.Scale?.Trim(), StringComparison.OrdinalIgnoreCase)) reasons.Add($"Requires scale {rule.Scale}");
            if (rule.OrganizationId.HasValue && (!employee.OrganizationId.HasValue || !IsOrganizationDescendant(employee.OrganizationId.Value, rule.OrganizationId.Value, organizationNodes))) reasons.Add("Organization does not match");
            if (joining.HasValue && joining.Value.Date > periodEnd.ToDateTime(TimeOnly.MinValue)) reasons.Add("Joined after this bonus period");
            if (serviceYears < rule.MinimumService) reasons.Add($"Minimum service is {rule.MinimumService:0.##} year(s)");
            if (rule.IsIneligible) reasons.Add("Rule is marked ineligible");

            var valid = reasons.Count == 0;
            var bonusAmount = rule.MaximumExpense > 0 ? Math.Min(salary, rule.MaximumExpense) : salary;
            var distribution = ResolveBonusDistribution(rule, request.Month, periodStart, periodEnd, serviceYears);
            var line = new PayrollBonusLine
            {
                TenantId = tenant.RequiredTenantId,
                PersonId = employee.PersonId,
                StaffId = employee.StaffId,
                EmployeeNumber = employee.EmployeeId,
                FullName = employee.FullName,
                Designation = employee.Designation,
                Department = employee.Department,
                DateOfJoining = joining.HasValue ? DateOnly.FromDateTime(joining.Value) : null,
                Scale = profile?.Scale,
                IsValid = valid,
                ValidationMessage = valid ? "Eligible" : string.Join("; ", reasons),
                BaseSalary = salary,
                BonusAmount = bonusAmount,
                BasicPercent = distribution?.BasicPercentage ?? defaultPercent,
                ServicePercent = distribution != null && serviceYears >= distribution.ServiceYears ? distribution.ServicePercentage : 0,
                AttendancePercent = distribution?.AttendancePercentage ?? 0,
                AssessmentPercent = distribution?.AssessmentPercentage ?? 0,
                LeavePercent = distribution?.LeavePercentage ?? 0,
                DisciplinePercent = distribution?.DisciplinePercentage ?? 0,
                ServiceYears = Math.Round(serviceYears, 2),
                Month = request.Month,
                Year = request.Year,
                Installment = distribution?.Installments ?? 1,
                CreatedOnUtc = now
            };
            ApplyLineRule(line, null);
            run.Lines.Add(line);
        }

        RefreshTotals(run);
        db.PayrollBonusRuns.Add(run);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var wasCreatedConcurrently = await db.PayrollBonusRuns.AsNoTracking().AnyAsync(x => x.BenefitRuleId == request.BenefitRuleId && x.Year == request.Year && x.Month == request.Month, ct);
            if (!wasCreatedConcurrently) throw;
        }
        return await RunResponse(request.BenefitRuleId, request.Year, request.Month, ct);
    }

    [HttpPut("lines/{id:long}")]
    public async Task<IActionResult> UpdateLine(long id, UpdateBonusLineRequest request, CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        var line = await db.PayrollBonusLines.Include(x => x.BonusRun).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (line?.BonusRun == null) return NotFound();
        if (line.BonusRun.Status != "Generated") return Conflict(new { message = "Only a generated bonus can be edited." });
        var percentages = new[] { request.BasicPercent, request.ServicePercent, request.AttendancePercent, request.AssessmentPercent, request.LeavePercent, request.DisciplinePercent };
        var amounts = new[] { request.BonusAmount, request.BasicBonus, request.ServiceBonus, request.AttendanceBonus, request.AssessmentBonus, request.LeaveBonus, request.DisciplineBonus };
        if (amounts.Any(x => x < 0) || percentages.Any(x => x is < 0 or > 100) || request.Installment is < 1 or > 120)
            return BadRequest(new { message = "Amounts must be positive, percentages 0-100, and installments 1-120." });

        line.BonusAmount = request.BonusAmount;
        line.BasicBonus = request.BasicBonus;
        line.ServiceBonus = request.ServiceBonus;
        line.AttendanceBonus = request.AttendanceBonus;
        line.AssessmentBonus = request.AssessmentBonus;
        line.LeaveBonus = request.LeaveBonus;
        line.DisciplineBonus = request.DisciplineBonus;
        line.BasicPercent = request.BasicPercent;
        line.ServicePercent = request.ServicePercent;
        line.AttendancePercent = request.AttendancePercent;
        line.AssessmentPercent = request.AssessmentPercent;
        line.LeavePercent = request.LeavePercent;
        line.DisciplinePercent = request.DisciplinePercent;
        line.Installment = request.Installment;
        line.IsValid = request.IsValid;
        line.IsInactive = request.IsInactive;
        line.Remarks = Clean(request.Remarks);
        line.UpdatedOnUtc = DateTime.UtcNow;
        ApplyLineRule(line, Clean(request.ChangedField));
        await RefreshTotals(line.BonusRunId, ct);
        await db.SaveChangesAsync(ct);
        return await RunResponse(line.BonusRun.BenefitRuleId, line.Year, line.Month, ct);
    }

    [HttpPost("runs/{id:long}/verify")]
    public async Task<IActionResult> Verify(long id, CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        var run = await db.PayrollBonusRuns.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (run == null) return NotFound();
        if (run.Status != "Generated") return Conflict(new { message = "Only a generated bonus can be verified." });
        if (!run.Lines.Any(x => x.IsValid && !x.IsInactive)) return BadRequest(new { message = "There are no eligible active bonus rows to verify." });
        run.Status = "Verified"; run.VerifiedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier); run.VerifiedByName = ActorName(); run.VerifiedOnUtc = DateTime.UtcNow; run.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await RunResponse(run.BenefitRuleId, run.Year, run.Month, ct);
    }

    [HttpPost("runs/{id:long}/approve")]
    public async Task<IActionResult> Approve(long id, CancellationToken ct)
    {
        var denied = await Guard("EDIT", ct); if (denied != null) return denied;
        var run = await db.PayrollBonusRuns.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (run == null) return NotFound();
        if (run.Status != "Verified") return Conflict(new { message = "Verify the bonus before approval." });
        var now = DateTime.UtcNow;
        run.Status = "Approved"; run.ApprovedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier); run.ApprovedByName = ActorName(); run.ApprovedOnUtc = now; run.UpdatedOnUtc = now;
        foreach (var line in run.Lines)
        {
            var eligible = line.IsValid && !line.IsInactive;
            line.IsApproved = eligible;
            // Approval makes the installment eligible for payroll. Payment is
            // recorded only when the linked monthly payroll is finalized.
            line.IsPaid = false;
            line.PaidOnUtc = null;
        }
        RefreshTotals(run);
        await db.SaveChangesAsync(ct);
        return await RunResponse(run.BenefitRuleId, run.Year, run.Month, ct);
    }

    [HttpPost("runs/{id:long}/process")]
    public Task<IActionResult> Process(long id, CancellationToken ct) => Verify(id, ct);

    [HttpPost("runs/{id:long}/pay")]
    public Task<IActionResult> Pay(long id, CancellationToken ct) => Approve(id, ct);
    private async Task<IActionResult> RunResponse(int benefitRuleId, int year, int month, CancellationToken ct)
    {
        var run = await db.PayrollBonusRuns.AsNoTracking().SingleOrDefaultAsync(x => x.BenefitRuleId == benefitRuleId && x.Year == year && x.Month == month, ct);
        if (run == null) return Ok(new { run = (object?)null, lines = Array.Empty<object>() });
        var lines = await db.PayrollBonusLines.AsNoTracking().Where(x => x.BonusRunId == run.Id).OrderBy(x => x.FullName).ToListAsync(ct);
        return Ok(new { run, lines });
    }

    private async Task RefreshTotals(long runId, CancellationToken ct)
    {
        var run = await db.PayrollBonusRuns.Include(x => x.Lines).SingleAsync(x => x.Id == runId, ct);
        RefreshTotals(run);
    }

    private static void RefreshTotals(PayrollBonusRun run)
    {
        run.TotalEmployees = run.Lines.Count;
        run.TotalEligibleEmployees = run.Lines.Count(x => x.IsValid && !x.IsInactive);
        run.TotalBonus = run.Lines.Where(x => x.IsValid && !x.IsInactive).Sum(x => x.TotalBonus);
        run.UpdatedOnUtc = DateTime.UtcNow;
    }

    private static void ApplyLineRule(PayrollBonusLine line, string? changedField)
    {
        var field = changedField?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(field) || field == "bonusamount" || PercentFields.Contains(field))
        {
            line.BasicBonus = Money(line.BonusAmount * line.BasicPercent / 100m);
            line.ServiceBonus = Money(line.BonusAmount * line.ServicePercent / 100m);
            line.AttendanceBonus = Money(line.BonusAmount * line.AttendancePercent / 100m);
            line.AssessmentBonus = Money(line.BonusAmount * line.AssessmentPercent / 100m);
            line.LeaveBonus = Money(line.BonusAmount * line.LeavePercent / 100m);
            line.DisciplineBonus = Money(line.BonusAmount * line.DisciplinePercent / 100m);
        }
        else if (AmountFields.Contains(field))
        {
            SyncPercentFromAmount(line, field);
        }

        line.TotalBonus = line.IsValid && !line.IsInactive ? Money(line.BasicBonus + line.ServiceBonus + line.AttendanceBonus + line.AssessmentBonus + line.LeaveBonus + line.DisciplineBonus) : 0;
        line.InstallmentAmount = line.Installment > 0 ? Money(line.TotalBonus / line.Installment) : 0;
    }

    private static void SyncPercentFromAmount(PayrollBonusLine line, string field)
    {
        var basis = line.BonusAmount;
        var percent = basis <= 0 ? 0 : 100m / basis;
        switch (field)
        {
            case "basicbonus":
                line.BasicPercent = Money(line.BasicBonus * percent);
                break;
            case "servicebonus":
                line.ServicePercent = Money(line.ServiceBonus * percent);
                break;
            case "attendancebonus":
                line.AttendancePercent = Money(line.AttendanceBonus * percent);
                break;
            case "assessmentbonus":
                line.AssessmentPercent = Money(line.AssessmentBonus * percent);
                break;
            case "leavebonus":
                line.LeavePercent = Money(line.LeaveBonus * percent);
                break;
            case "disciplinebonus":
                line.DisciplinePercent = Money(line.DisciplineBonus * percent);
                break;
        }
    }

    private static decimal ResolveDefaultPercent(PayrollBenefitRule rule)
    {
        var value = rule.Parameters.Where(x => x.CompanyShare > 0 && x.CompanyShare <= 100).Select(x => x.CompanyShare).FirstOrDefault();
        if (value <= 0 && rule.CompanyShare is > 0 and <= 100) value = rule.CompanyShare;
        return value > 0 ? value : 100m;
    }

    private static PayrollBonusDistribution? ResolveBonusDistribution(
        PayrollBenefitRule rule,
        int month,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal serviceYears) =>
        rule.Parameters
            .Where(parameter => parameter.BonusDistribution != null
                && (!parameter.PeriodFrom.HasValue || parameter.PeriodFrom <= periodEnd)
                && (!parameter.PeriodTo.HasValue || parameter.PeriodTo >= periodStart)
                && serviceYears >= parameter.MinimumService
                && (!parameter.BonusDistribution!.Month.HasValue || parameter.BonusDistribution.Month == month))
            .OrderByDescending(parameter => parameter.MinimumService)
            .Select(parameter => parameter.BonusDistribution)
            .FirstOrDefault();

    private static bool IsOrganizationDescendant(int candidateId, int ancestorId, IReadOnlyDictionary<int, OrganizationTree> nodes)
    {
        var currentId = (int?)candidateId;
        var visited = new HashSet<int>();
        while (currentId.HasValue && visited.Add(currentId.Value) && nodes.TryGetValue(currentId.Value, out var node))
        {
            if (node.Id == ancestorId) return true;
            currentId = node.ParentId;
        }
        return false;
    }

    private string ActorName() => User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "User";
    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static bool ValidPeriod(int year, int month) => year is >= 2000 and <= 2200 && month is >= 1 and <= 12;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static readonly HashSet<string> PercentFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "basicpercent",
        "servicepercent",
        "attendancepercent",
        "assessmentpercent",
        "leavepercent",
        "disciplinepercent"
    };

    private static readonly HashSet<string> AmountFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "basicbonus",
        "servicebonus",
        "attendancebonus",
        "assessmentbonus",
        "leavebonus",
        "disciplinebonus"
    };

    private async Task<IActionResult?> Guard(string action, CancellationToken ct)
    {
        if (!tenant.TenantId.HasValue) return Forbid();
        if (TenantPermissionService.IsSuperAdmin(User)) return null;
        if (TenantPermissionService.IsTenantAdmin(User)) return await tenantPermissions.HasMenuRouteAsync(User, [MenuRoute], action, ct) ? null : Forbid();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier); if (string.IsNullOrWhiteSpace(userId)) return Forbid();
        var staffId = await db.Persons.AsNoTracking().Where(x => x.IdentityUserId == userId && x.Staff != null).Select(x => (Guid?)x.Staff!.StaffId).FirstOrDefaultAsync(ct);
        var menuId = await db.Menus.AsNoTracking().Where(x => x.IsActive && x.Route == MenuRoute).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (!staffId.HasValue || !menuId.HasValue) return Forbid();
        if (action == "VIEW" && await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}")) return null;
        return await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_{action}") ? null : Forbid();
    }
}

public sealed record GenerateBonusRequest(int BenefitRuleId, int Year, int Month);
public sealed record UpdateBonusLineRequest(
    decimal BonusAmount,
    decimal BasicBonus,
    decimal ServiceBonus,
    decimal AttendanceBonus,
    decimal AssessmentBonus,
    decimal LeaveBonus,
    decimal DisciplineBonus,
    decimal BasicPercent,
    decimal ServicePercent,
    decimal AttendancePercent,
    decimal AssessmentPercent,
    decimal LeavePercent,
    decimal DisciplinePercent,
    int Installment,
    bool IsValid,
    bool IsInactive,
    string? Remarks,
    string? ChangedField);



