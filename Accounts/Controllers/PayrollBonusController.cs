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
            .Where(x => x.BenefitsType == "Bonus" && !x.IsIneligible)
            .OrderByDescending(x => x.ValidFrom).ThenBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                reference = x.BenefitReference,
                x.Name,
                x.ValidFrom,
                x.ValidTo,
                x.Scale,
                x.Frequency,
                maximumExpense = x.MaximumExpense,
                x.MinimumService,
                x.OrganizationId,
                x.Company,
                x.Entitled
            })
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
    [Idempotent]
    public async Task<IActionResult> Generate(GenerateBonusRequest request, CancellationToken ct)
    {
        var denied = await Guard("ADD", ct); if (denied != null) return denied;
        if (!ValidPeriod(request.Year, request.Month)) return BadRequest(new { message = "Enter a valid bonus month." });

        var existingRun = await db.PayrollBonusRuns
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.BenefitRuleId == request.BenefitRuleId && x.Year == request.Year && x.Month == request.Month, ct);

        if (existingRun != null)
        {
            if (!string.Equals(existingRun.Status, "Generated", StringComparison.OrdinalIgnoreCase))
                return Conflict(new { message = "Verified or approved bonus cannot be regenerated. Select another month or reverse approval first." });
            if (!request.Regenerate)
                return await RunResponse(request.BenefitRuleId, request.Year, request.Month, ct);

            db.PayrollBonusLines.RemoveRange(existingRun.Lines);
            existingRun.Lines.Clear();
            existingRun.VerifiedByUserId = null;
            existingRun.VerifiedByName = null;
            existingRun.VerifiedOnUtc = null;
            existingRun.ApprovedByUserId = null;
            existingRun.ApprovedByName = null;
            existingRun.ApprovedOnUtc = null;
            existingRun.Status = "Generated";
            existingRun.UpdatedOnUtc = DateTime.UtcNow;
        }

        var rule = await db.PayrollBenefitRules.Include(x => x.Parameters).ThenInclude(x => x.BonusDistribution)
            .SingleOrDefaultAsync(x => x.Id == request.BenefitRuleId && x.BenefitsType == "Bonus", ct);
        if (rule == null) return NotFound(new { message = "Selected bonus benefit rule was not found." });
        if (rule.IsIneligible) return BadRequest(new { message = "Selected bonus rule is marked ineligible." });

        var periodStart = new DateOnly(request.Year, request.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        if (rule.ValidFrom.HasValue && periodEnd < rule.ValidFrom.Value || rule.ValidTo.HasValue && periodStart > rule.ValidTo.Value)
            return BadRequest(new { message = "Selected rule is not effective for this month." });

        var hasDistribution = rule.Parameters.Any(parameter => parameter.BonusDistribution != null);
        if (!hasDistribution)
            return BadRequest(new { message = "Configure Bonus Distribution under Benefits Parameter before generating." });

        var staff = await db.StaffDirectoryRows.AsNoTracking().Where(x => x.IsPersonActive).OrderBy(x => x.FullName).ToListAsync(ct);
        var personIds = staff.Select(x => x.PersonId).Distinct().ToArray();
        var profiles = await db.PersonHrProfiles.AsNoTracking().Where(x => personIds.Contains(x.PersonId)).ToDictionaryAsync(x => x.PersonId, ct);
        var employmentByPerson = await db.Persons.AsNoTracking()
            .Where(x => personIds.Contains(x.PersonId))
            .Select(x => new { x.PersonId, x.EmploymentStatus })
            .ToDictionaryAsync(x => x.PersonId, x => x.EmploymentStatus, ct);
        var defaultPercent = ResolveDefaultPercent(rule);
        var organizationNodes = await db.OrganizationTree.AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var now = DateTime.UtcNow;

        var run = existingRun ?? new PayrollBonusRun
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

        if (existingRun == null)
        {
            run.CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            run.CreatedByName = ActorName();
            run.CreatedOnUtc = now;
        }
        else
        {
            run.BenefitReference = rule.BenefitReference;
            run.RuleName = rule.Name;
            run.RunNumber = $"BON-{request.Year}{request.Month:00}-{rule.Id}";
        }

        foreach (var employee in staff)
        {
            profiles.TryGetValue(employee.PersonId, out var profile);
            var reasons = new List<string>();
            var salary = profile?.CurrentPay is > 0 ? profile.CurrentPay.Value : profile?.BasicSalary ?? 0;
            var joining = profile?.JoiningDate;
            var serviceYears = joining.HasValue
                ? Math.Max(0, (decimal)(periodEnd.ToDateTime(TimeOnly.MinValue) - joining.Value.Date).TotalDays / 365.2425m)
                : 0;
            if (profile == null) reasons.Add("HR profile is missing");
            if (salary <= 0) reasons.Add("Salary is missing");
            if (!string.IsNullOrWhiteSpace(rule.Scale)
                && !string.Equals(rule.Scale.Trim(), profile?.Scale?.Trim(), StringComparison.OrdinalIgnoreCase))
                reasons.Add($"Requires scale {rule.Scale}");
            if (rule.OrganizationId.HasValue
                && (!employee.OrganizationId.HasValue
                    || !IsOrganizationDescendant(employee.OrganizationId.Value, rule.OrganizationId.Value, organizationNodes)))
                reasons.Add("Organization / entitled scope does not match");
            if (joining.HasValue && joining.Value.Date > periodEnd.ToDateTime(TimeOnly.MinValue))
                reasons.Add("Joined after this bonus period");
            if (serviceYears < rule.MinimumService)
                reasons.Add($"Minimum service is {rule.MinimumService:0.##} year(s)");
            if (rule.IsIneligible) reasons.Add("Rule is marked ineligible");
            if (!string.IsNullOrWhiteSpace(rule.ServiceStatus)
                && !rule.ServiceStatus.Equals("All", StringComparison.OrdinalIgnoreCase)
                && !rule.ServiceStatus.Equals("Active", StringComparison.OrdinalIgnoreCase))
            {
                employmentByPerson.TryGetValue(employee.PersonId, out var employmentStatus);
                if (string.IsNullOrWhiteSpace(employmentStatus)
                    || !string.Equals(rule.ServiceStatus.Trim(), employmentStatus.Trim(), StringComparison.OrdinalIgnoreCase))
                    reasons.Add($"Requires service status {rule.ServiceStatus}");
            }

            var valid = reasons.Count == 0;
            var bonusAmount = rule.MaximumExpense > 0 ? Math.Min(salary, rule.MaximumExpense) : salary;
            var distribution = ResolveBonusDistribution(rule, request.Month, periodStart, periodEnd, serviceYears);
            if (distribution == null) reasons.Add("No matching bonus distribution for this month/service");
            valid = reasons.Count == 0;

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
                Installment = Math.Max(1, distribution?.Installments ?? 1),
                PaidInstallmentCount = 0,
                CreatedOnUtc = now
            };
            ApplyLineRule(line, null);
            run.Lines.Add(line);
        }

        RefreshTotals(run);
        var expenseError = ValidateMaximumExpense(rule.MaximumExpense, run);
        if (expenseError != null) return BadRequest(new { message = expenseError });

        if (existingRun == null) db.PayrollBonusRuns.Add(run);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var wasCreatedConcurrently = await db.PayrollBonusRuns.AsNoTracking().AnyAsync(x =>
                x.BenefitRuleId == request.BenefitRuleId && x.Year == request.Year && x.Month == request.Month, ct);
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

        var maxExpense = await db.PayrollBenefitRules.AsNoTracking()
            .Where(x => x.Id == line.BonusRun.BenefitRuleId)
            .Select(x => x.MaximumExpense)
            .SingleAsync(ct);
        var run = await db.PayrollBonusRuns.Include(x => x.Lines).SingleAsync(x => x.Id == line.BonusRunId, ct);
        var expenseError = ValidateMaximumExpense(maxExpense, run);
        if (expenseError != null) return BadRequest(new { message = expenseError });

        await db.SaveChangesAsync(ct);
        return await RunResponse(line.BonusRun.BenefitRuleId, line.Year, line.Month, ct);
    }

    /// <summary>Process = Verify (legacy Staff Bonus Process button).</summary>
    [HttpPost("runs/{id:long}/process")]
    [Idempotent]
    public Task<IActionResult> Process(long id, CancellationToken ct) => Verify(id, ct);

    [HttpPost("runs/{id:long}/verify")]
    [Idempotent]
    public async Task<IActionResult> Verify(long id, CancellationToken ct)
    {
        var denied = await GuardProcessOrApprove(ct); if (denied != null) return denied;
        var run = await db.PayrollBonusRuns.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (run == null) return NotFound();
        if (run.Status != "Generated") return Conflict(new { message = "Only a generated bonus can be processed / verified." });
        if (!run.Lines.Any(x => x.IsValid && !x.IsInactive))
            return BadRequest(new { message = "There are no eligible active bonus rows to process." });

        var maxExpense = await db.PayrollBenefitRules.AsNoTracking()
            .Where(x => x.Id == run.BenefitRuleId)
            .Select(x => x.MaximumExpense)
            .SingleAsync(ct);
        RefreshTotals(run);
        var expenseError = ValidateMaximumExpense(maxExpense, run);
        if (expenseError != null) return BadRequest(new { message = expenseError });

        run.Status = "Verified";
        run.VerifiedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        run.VerifiedByName = ActorName();
        run.VerifiedOnUtc = DateTime.UtcNow;
        run.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await RunResponse(run.BenefitRuleId, run.Year, run.Month, ct);
    }

    /// <summary>Pay alias = Approve for payroll eligibility (cash pay remains Payroll Pay).</summary>
    [HttpPost("runs/{id:long}/pay")]
    [Idempotent]
    public Task<IActionResult> Pay(long id, CancellationToken ct) => Approve(id, ct);

    [HttpPost("runs/{id:long}/approve")]
    [Idempotent]
    public async Task<IActionResult> Approve(long id, CancellationToken ct)
    {
        var denied = await GuardProcessOrApprove(ct); if (denied != null) return denied;
        var run = await db.PayrollBonusRuns.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (run == null) return NotFound();
        if (run.Status != "Verified") return Conflict(new { message = "Process / verify the bonus before approval." });

        var maxExpense = await db.PayrollBenefitRules.AsNoTracking()
            .Where(x => x.Id == run.BenefitRuleId)
            .Select(x => x.MaximumExpense)
            .SingleAsync(ct);
        RefreshTotals(run);
        var expenseError = ValidateMaximumExpense(maxExpense, run);
        if (expenseError != null) return BadRequest(new { message = expenseError });

        var now = DateTime.UtcNow;
        run.Status = "Approved";
        run.ApprovedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        run.ApprovedByName = ActorName();
        run.ApprovedOnUtc = now;
        run.UpdatedOnUtc = now;
        foreach (var line in run.Lines)
        {
            var eligible = line.IsValid && !line.IsInactive;
            line.IsApproved = eligible;
            // Approval makes installments eligible for payroll. IsPaid / PaidInstallmentCount
            // advance only when monthly payroll is finalized.
            if (!eligible)
            {
                line.IsPaid = false;
                line.PaidOnUtc = null;
                line.PaidInstallmentCount = 0;
            }
            line.UpdatedOnUtc = now;
        }
        RefreshTotals(run);
        await db.SaveChangesAsync(ct);
        return await RunResponse(run.BenefitRuleId, run.Year, run.Month, ct);
    }

    private async Task<IActionResult> RunResponse(int benefitRuleId, int year, int month, CancellationToken ct)
    {
        var run = await db.PayrollBonusRuns.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BenefitRuleId == benefitRuleId && x.Year == year && x.Month == month, ct);
        var maximumExpense = await db.PayrollBenefitRules.AsNoTracking()
            .Where(x => x.Id == benefitRuleId)
            .Select(x => (decimal?)x.MaximumExpense)
            .FirstOrDefaultAsync(ct) ?? 0;
        if (run == null)
            return Ok(new { run = (object?)null, lines = Array.Empty<object>(), maximumExpense, totalInstallmentAmount = 0m });

        var lines = await db.PayrollBonusLines.AsNoTracking()
            .Where(x => x.BonusRunId == run.Id)
            .OrderBy(x => x.FullName)
            .ToListAsync(ct);
        var totalInstallmentAmount = lines.Where(x => x.IsValid && !x.IsInactive).Sum(x => x.InstallmentAmount);
        return Ok(new { run, lines, maximumExpense, totalInstallmentAmount });
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

    /// <summary>
    /// Legacy StaffBonus compared Max_Exp to installment total when present, else T-Bonus total.
    /// </summary>
    private static string? ValidateMaximumExpense(decimal maximumExpense, PayrollBonusRun run)
    {
        if (maximumExpense <= 0) return null;
        var eligible = run.Lines.Where(x => x.IsValid && !x.IsInactive).ToList();
        var installmentTotal = eligible.Sum(x => x.InstallmentAmount);
        var compare = installmentTotal > 0 ? installmentTotal : eligible.Sum(x => x.TotalBonus);
        return compare > maximumExpense
            ? $"Exceed Total Limit....! Installment/total bonus {compare:0.##} is above Max Expense {maximumExpense:0.##}."
            : null;
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

        line.TotalBonus = line.IsValid && !line.IsInactive
            ? Money(line.BasicBonus + line.ServiceBonus + line.AttendanceBonus + line.AssessmentBonus + line.LeaveBonus + line.DisciplineBonus)
            : 0;
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
        "basicpercent", "servicepercent", "attendancepercent", "assessmentpercent", "leavepercent", "disciplinepercent"
    };

    private static readonly HashSet<string> AmountFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "basicbonus", "servicebonus", "attendancebonus", "assessmentbonus", "leavebonus", "disciplinebonus"
    };

    private async Task<IActionResult?> GuardProcessOrApprove(CancellationToken ct)
    {
        if (!tenant.TenantId.HasValue) return Forbid();
        if (TenantPermissionService.IsSuperAdmin(User)) return null;
        if (TenantPermissionService.IsTenantAdmin(User))
        {
            if (await tenantPermissions.HasMenuRouteAsync(User, [MenuRoute], "APPROVE", ct)) return null;
            return await tenantPermissions.HasMenuRouteAsync(User, [MenuRoute], "EDIT", ct) ? null : Forbid();
        }
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier); if (string.IsNullOrWhiteSpace(userId)) return Forbid();
        var staffId = await db.Persons.AsNoTracking().Where(x => x.IdentityUserId == userId && x.Staff != null).Select(x => (Guid?)x.Staff!.StaffId).FirstOrDefaultAsync(ct);
        var menuId = await db.Menus.AsNoTracking().Where(x => x.IsActive && x.Route == MenuRoute).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (!staffId.HasValue || !menuId.HasValue) return Forbid();
        if (await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_APPROVE")) return null;
        return await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_EDIT") ? null : Forbid();
    }

    private async Task<IActionResult?> Guard(string action, CancellationToken ct)
    {
        if (!tenant.TenantId.HasValue) return Forbid();
        if (TenantPermissionService.IsSuperAdmin(User)) return null;
        if (TenantPermissionService.IsTenantAdmin(User))
            return await tenantPermissions.HasMenuRouteAsync(User, [MenuRoute], action, ct) ? null : Forbid();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier); if (string.IsNullOrWhiteSpace(userId)) return Forbid();
        var staffId = await db.Persons.AsNoTracking().Where(x => x.IdentityUserId == userId && x.Staff != null).Select(x => (Guid?)x.Staff!.StaffId).FirstOrDefaultAsync(ct);
        var menuId = await db.Menus.AsNoTracking().Where(x => x.IsActive && x.Route == MenuRoute).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        if (!staffId.HasValue || !menuId.HasValue) return Forbid();
        if (action == "VIEW" && await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}")) return null;
        return await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}_{action}") ? null : Forbid();
    }
}

public sealed record GenerateBonusRequest(int BenefitRuleId, int Year, int Month, bool Regenerate = false);
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
