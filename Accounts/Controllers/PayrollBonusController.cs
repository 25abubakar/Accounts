using System.Security.Claims;
using Accounts.Data;
using Accounts.Idempotency;
using Accounts.Models;
using Accounts.Models.SpListRows;
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
    TenantPermissionService tenantPermissions,
    PayrollCalculationService payroll) : ControllerBase
{
    private const string MenuRoute = "/pay-allowances/bonus";

    [HttpGet("rules")]
    public async Task<IActionResult> Rules(CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        var rows = await SpListQuery.ExecAsync<PayBonusRuleListRow>(
            db,
            "EXEC dbo.usp_Pay_BonusRules_List @TenantId",
            ct,
            SpListQuery.TenantId(tenant.RequiredTenantId));
        return Ok(rows);
    }

    [HttpGet("run")]
    public async Task<IActionResult> Run(
        [FromQuery] int benefitRuleId,
        [FromQuery] int? year,
        [FromQuery] int? month,
        CancellationToken ct)
    {
        var denied = await Guard("VIEW", ct); if (denied != null) return denied;
        if (benefitRuleId <= 0) return BadRequest(new { message = "Select a Benefit Rule." });

        // Legacy StaffBonus: load by BenefitRuleId only. Period is resolved from Bonus Distribution
        // (InstallmentStart / Dist Month + ValidFrom). Optional year/month still accepted for API clients.
        int resolvedYear;
        int resolvedMonth;
        if (year is > 0 && month is >= 1 and <= 12)
        {
            resolvedYear = year.Value;
            resolvedMonth = month.Value;
        }
        else
        {
            var period = await ResolveBonusPeriodAsync(benefitRuleId, ct);
            if (period == null)
                return BadRequest(new { message = "Configure Bonus Distribution (Month / Inst Start) on this Benefit Rule before loading." });
            resolvedYear = period.Value.Year;
            resolvedMonth = period.Value.Month;
        }

        return await RunResponse(benefitRuleId, resolvedYear, resolvedMonth, ct);
    }

    [HttpPost("generate")]
    [Idempotent]
    public async Task<IActionResult> Generate(GenerateBonusRequest request, CancellationToken ct)
    {
        var denied = await Guard("ADD", ct); if (denied != null) return denied;

        var rule = await db.PayrollBenefitRules.Include(x => x.Parameters).ThenInclude(x => x.BonusDistribution)
            .SingleOrDefaultAsync(x => x.Id == request.BenefitRuleId && x.BenefitsType == "Bonus", ct);
        if (rule == null) return NotFound(new { message = "Selected bonus benefit rule was not found." });
        if (rule.IsIneligible) return BadRequest(new { message = "Selected bonus rule is marked ineligible." });

        var hasDistribution = rule.Parameters.Any(parameter => parameter.BonusDistribution != null);
        if (!hasDistribution)
            return BadRequest(new { message = "Configure Bonus Distribution under Benefits Parameter before generating." });

        // Year/Month optional — default from rule distribution (legacy Generate used BenefitRuleId only).
        var year = request.Year;
        var month = request.Month;
        if (!ValidPeriod(year, month))
        {
            var period = ResolveBonusPeriodFromRule(rule);
            if (period == null)
                return BadRequest(new { message = "Configure Bonus Distribution (Month / Inst Start) before generating." });
            year = period.Value.Year;
            month = period.Value.Month;
        }

        var existingRun = await db.PayrollBonusRuns
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.BenefitRuleId == request.BenefitRuleId && x.Year == year && x.Month == month, ct);

        if (existingRun != null)
        {
            if (!string.Equals(existingRun.Status, "Generated", StringComparison.OrdinalIgnoreCase))
                return Conflict(new { message = "Verified or approved bonus cannot be regenerated. Reverse approval first or select another Benefit Rule." });
            if (!request.Regenerate)
                return await RunResponse(request.BenefitRuleId, year, month, ct);

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

        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        if (rule.ValidFrom.HasValue && periodEnd < rule.ValidFrom.Value || rule.ValidTo.HasValue && periodStart > rule.ValidTo.Value)
            return BadRequest(new { message = "Selected rule is not effective for its Bonus Distribution period." });

        var candidates = await SpListQuery.ExecAsync<PayBonusGenerateCandidateRow>(
            db,
            "EXEC dbo.usp_Pay_BonusGenerate_Candidates @TenantId, @BenefitRuleId, @Year, @Month",
            ct,
            SpListQuery.TenantId(tenant.RequiredTenantId),
            SpListQuery.Int("@BenefitRuleId", request.BenefitRuleId),
            SpListQuery.Int("@Year", year),
            SpListQuery.Int("@Month", month));

        var now = DateTime.UtcNow;
        var run = existingRun ?? new PayrollBonusRun
        {
            TenantId = tenant.RequiredTenantId,
            BenefitRuleId = rule.Id,
            RunNumber = $"BON-{year}{month:00}-{rule.Id}",
            BenefitReference = rule.BenefitReference,
            RuleName = rule.Name,
            Year = year,
            Month = month,
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
            run.RunNumber = $"BON-{year}{month:00}-{rule.Id}";
        }

        foreach (var candidate in candidates)
        {
            var line = new PayrollBonusLine
            {
                TenantId = tenant.RequiredTenantId,
                PersonId = candidate.PersonId,
                StaffId = candidate.StaffId,
                EmployeeNumber = candidate.EmployeeNumber,
                FullName = candidate.FullName,
                Designation = candidate.Designation,
                Department = candidate.Department,
                DateOfJoining = candidate.DateOfJoining,
                Scale = candidate.Scale,
                IsValid = candidate.IsValid,
                ValidationMessage = string.IsNullOrWhiteSpace(candidate.ValidationMessage)
                    ? (candidate.IsValid ? "Eligible" : "Not eligible")
                    : candidate.ValidationMessage,
                BaseSalary = candidate.BaseSalary,
                BonusAmount = candidate.BonusAmount,
                BasicPercent = candidate.BasicPercent,
                ServicePercent = candidate.ServicePercent,
                AttendancePercent = candidate.AttendancePercent,
                AssessmentPercent = candidate.AssessmentPercent,
                LeavePercent = candidate.LeavePercent,
                DisciplinePercent = candidate.DisciplinePercent,
                ServiceYears = candidate.ServiceYears,
                Month = month,
                Year = year,
                Installment = Math.Max(1, candidate.Installment),
                CurrentInstallmentNo = Math.Max(1, Math.Min(Math.Max(1, candidate.Installment), Math.Max(0, candidate.CurrentInstallmentNo) > 0 ? candidate.CurrentInstallmentNo : 1)),
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
                x.BenefitRuleId == request.BenefitRuleId && x.Year == year && x.Month == month, ct);
            if (!wasCreatedConcurrently) throw;
        }
        return await RunResponse(request.BenefitRuleId, year, month, ct);
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

        /// <summary>
        /// Legacy StaffBonus Pay:
        /// - Verified → one-time Approve (schedule eligible for payroll; no re-approval later).
        /// - Approved → push the next due installment into that month's Draft Payroll.
        /// Cash settlement / PaidInstallmentCount still advances when Payroll is Finalized Pay.
        /// </summary>
        [HttpPost("runs/{id:long}/pay")]
        [Idempotent]
        public async Task<IActionResult> Pay(long id, CancellationToken ct)
        {
            var denied = await GuardProcessOrApprove(ct); if (denied != null) return denied;
            var status = await db.PayrollBonusRuns.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => x.Status)
                .FirstOrDefaultAsync(ct);
            if (status == null) return NotFound();

            if (string.Equals(status, "Verified", StringComparison.OrdinalIgnoreCase))
                return await Approve(id, ct);

            if (string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase))
                return await PayDueInstallmentToPayrollAsync(id, ct);

            return Conflict(new { message = "Process the bonus first, then Pay to approve. After approval, Pay pushes each due installment into Payroll." });
        }

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
                // IsPaid / PaidInstallmentCount advance immediately below after Draft sync (first Pay).
                if (!eligible)
                {
                    line.IsPaid = false;
                    line.PaidOnUtc = null;
                    line.PaidInstallmentCount = 0;
                }
                else
                {
                    var installments = Math.Max(1, line.Installment);
                    line.CurrentInstallmentNo = Math.Min(installments, Math.Max(1, line.PaidInstallmentCount + 1));
                }
                line.UpdatedOnUtc = now;
            }
            RefreshTotals(run);
            await db.SaveChangesAsync(ct);

            // First Pay after Process = approve + pay installment #1 (Inst_Amt → Draft Payroll).
            // Sync while PaidInstallmentCount is still 0, then advance so IS PAID / PAID INST update.
            await SyncCurrentDraftPayrollAsync(run.Year, run.Month, ct);

            var toPay = run.Lines.Where(x => x.IsApproved && !x.IsInactive && !x.IsPaid).ToList();
            AdvancePaidInstallmentOnce(toPay, DateTime.UtcNow);
            run.UpdatedOnUtc = DateTime.UtcNow;
            RefreshTotals(run);
            await db.SaveChangesAsync(ct);

            return await RunResponse(run.BenefitRuleId, run.Year, run.Month, ct);
        }

        /// <summary>
        /// After one-time approval: pay the next unpaid installment (PAID INST +1), push Inst_Amt
        /// into that month's Draft Payroll. No re-approval. Calendar months alone do not auto-count —
        /// each Pay (or Payroll Finalize Pay) advances one installment.
        /// </summary>
        private async Task<IActionResult> PayDueInstallmentToPayrollAsync(long runId, CancellationToken ct)
        {
            var run = await db.PayrollBonusRuns.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == runId, ct);
            if (run == null) return NotFound();
            if (!string.Equals(run.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                return Conflict(new { message = "Approve the bonus once before monthly installment Pay." });

            var unpaid = run.Lines.Where(x => x.IsApproved && !x.IsInactive && !x.IsPaid).ToList();
            if (unpaid.Count == 0)
                return Conflict(new { message = "All installments for this bonus are already paid. Nothing left on the active chart." });

            var paidCount = unpaid.Min(x => Math.Max(0, x.PaidInstallmentCount));
            var sample = unpaid[0];
            var installments = Math.Max(1, sample.Installment);
            if (paidCount >= installments)
                return Conflict(new { message = "All installments for this bonus are already paid." });

            var dueStart = new DateOnly(sample.Year, sample.Month, 1).AddMonths(paidCount);
            var installmentNo = paidCount + 1;

            // Sync while this installment is still due, then mark it paid on the bonus chart.
            await SyncCurrentDraftPayrollAsync(dueStart.Year, dueStart.Month, ct);
            AdvancePaidInstallmentOnce(unpaid, DateTime.UtcNow);
            run.UpdatedOnUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var draftExists = await db.PayrollRuns.AsNoTracking().AnyAsync(x =>
                x.Year == dueStart.Year && x.Month == dueStart.Month &&
                (x.Status == "Draft" || x.Status == "In Review" || x.Status == "Approved" || x.Status == "Finalized"), ct);

            var maximumExpense = await db.PayrollBenefitRules.AsNoTracking()
                .Where(x => x.Id == run.BenefitRuleId)
                .Select(x => (decimal?)x.MaximumExpense)
                .FirstOrDefaultAsync(ct) ?? 0;
            var lines = await SpListQuery.ExecAsync<PayBonusLineListRow>(
                db,
                "EXEC dbo.usp_Pay_BonusLines_List @TenantId, @BenefitRuleId, @Year, @Month",
                ct,
                SpListQuery.TenantId(tenant.RequiredTenantId),
                SpListQuery.Int("@BenefitRuleId", run.BenefitRuleId),
                SpListQuery.Int("@Year", run.Year),
                SpListQuery.Int("@Month", run.Month));
            var totalInstallmentAmount = lines.Where(x => x.IsValid && !x.IsInactive).Sum(x => x.InstallmentAmount);
            var freshRun = await db.PayrollBonusRuns.AsNoTracking()
                .SingleAsync(x => x.Id == run.Id, ct);

            return Ok(new
            {
                run = freshRun,
                lines,
                maximumExpense,
                totalInstallmentAmount,
                dueYear = dueStart.Year,
                dueMonth = dueStart.Month,
                draftPayrollSynced = draftExists,
                message = $"Installment {installmentNo}/{installments} paid for {dueStart:MMM yyyy}. PAID INST is now {installmentNo}. Next month click Pay again (no re-approval)."
                    + (draftExists ? "" : " Open Payroll for that month and Create/Recalculate if Draft was missing.")
            });
        }

        /// <summary>Advance one paid installment on each line (after Draft sync). Prevents double-count with Payroll Pay via elapsed &lt; paid check.</summary>
        private static void AdvancePaidInstallmentOnce(IEnumerable<PayrollBonusLine> lines, DateTime now)
        {
            foreach (var line in lines)
            {
                var installments = Math.Max(1, line.Installment);
                var paid = Math.Max(0, line.PaidInstallmentCount);
                if (paid >= installments) continue;

                line.PaidInstallmentCount = paid + 1;
                line.CurrentInstallmentNo = line.PaidInstallmentCount >= installments
                    ? installments
                    : line.PaidInstallmentCount + 1;
                line.UpdatedOnUtc = now;
                if (line.PaidInstallmentCount >= installments)
                {
                    line.IsPaid = true;
                    line.PaidOnUtc = now;
                }
            }
        }

        /// <summary>
        /// Bonus approval is a one-time action for the whole installment schedule. If the
        /// matching monthly payroll already exists as Draft, update its read-only Bonus
        /// amount immediately. Future payroll months resolve their due installment during
        /// normal payroll generation; no repeat bonus approval is required.
        /// </summary>
        private async Task SyncCurrentDraftPayrollAsync(int year, int month, CancellationToken ct)
        {
            var payrollLines = await db.PayrollLines.Include(line => line.PayrollRun)
                .Where(line => line.Year == year && line.Month == month &&
                    line.PayrollRun != null && line.PayrollRun.Status == "Draft")
                .ToListAsync(ct);
            if (payrollLines.Count == 0) return;

            var personIds = payrollLines.Select(line => line.PersonId).Distinct().ToArray();
            var approvedBonusLines = await db.PayrollBonusLines.AsNoTracking()
                .Include(line => line.BonusRun)
                .Where(line => personIds.Contains(line.PersonId) && line.IsApproved &&
                    !line.IsInactive && !line.IsPaid && line.BonusRun != null &&
                    line.BonusRun.Status == "Approved")
                .ToListAsync(ct);
            var taxYear = month >= 7 ? $"{year}-{year + 1}" : $"{year - 1}-{year}";
            var taxSlabs = await db.PayrollTaxSlabs.AsNoTracking()
                .Where(slab => slab.IsActive && slab.TaxYear == taxYear)
                .OrderBy(slab => slab.FromAmount)
                .ToListAsync(ct);
            var now = DateTime.UtcNow;

            foreach (var payrollLine in payrollLines)
            {
                payrollLine.BonusAmount = Money(approvedBonusLines
                    .Where(line => line.PersonId == payrollLine.PersonId && BonusInstallmentIsDue(line, year, month))
                    .Sum(line => line.InstallmentAmount > 0 ? line.InstallmentAmount : line.TotalBonus));
                PayrollCalculationService.Recalculate(payrollLine);
                payrollLine.TaxAmount = PayrollTaxCalculator.CalculateMonthlyTax(payrollLine.TaxableIncome, taxSlabs);
                PayrollCalculationService.Recalculate(payrollLine);
                payrollLine.UpdatedOnUtc = now;
                if (payrollLine.PayrollRun != null) payrollLine.PayrollRun.UpdatedOnUtc = now;
            }

            await db.SaveChangesAsync(ct);
            foreach (var payrollRunId in payrollLines.Select(line => line.PayrollRunId).Distinct())
                await payroll.RecalculateRunTotalsAsync(payrollRunId, ct);
        }

        /// <summary>
        /// Next unpaid installment is due when payroll month is on/after that slot
        /// (catch-up if a month was skipped). Still one installment amount per payroll pay.
        /// </summary>
        private static bool BonusInstallmentIsDue(PayrollBonusLine line, int year, int month)
        {
            var installments = Math.Max(1, line.Installment);
            var elapsed = (year - line.Year) * 12 + month - line.Month;
            var paid = Math.Max(0, line.PaidInstallmentCount);
            return elapsed >= 0 && elapsed < installments && elapsed >= paid;
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

        var lines = await SpListQuery.ExecAsync<PayBonusLineListRow>(
            db,
            "EXEC dbo.usp_Pay_BonusLines_List @TenantId, @BenefitRuleId, @Year, @Month",
            ct,
            SpListQuery.TenantId(tenant.RequiredTenantId),
            SpListQuery.Int("@BenefitRuleId", benefitRuleId),
            SpListQuery.Int("@Year", year),
            SpListQuery.Int("@Month", month));
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
        // Not eligible → keep staff on grid with zeros (do not mirror BASIC into BONUS AMT).
        if (!line.IsValid || line.IsInactive)
        {
            line.BonusAmount = 0;
            line.BasicBonus = 0;
            line.ServiceBonus = 0;
            line.AttendanceBonus = 0;
            line.AssessmentBonus = 0;
            line.LeaveBonus = 0;
            line.DisciplineBonus = 0;
            line.BasicPercent = 0;
            line.ServicePercent = 0;
            line.AttendancePercent = 0;
            line.AssessmentPercent = 0;
            line.LeavePercent = 0;
            line.DisciplinePercent = 0;
            line.TotalBonus = 0;
            line.InstallmentAmount = 0;
            return;
        }

        var field = changedField?.Trim().ToLowerInvariant();
        var percentTotal = line.BasicPercent + line.ServicePercent + line.AttendancePercent
            + line.AssessmentPercent + line.LeavePercent + line.DisciplinePercent;

        // Flat Figure bonus (Amount = 5000, all distribution % blank/0): T-Bonus must equal BONUS AMT.
        // Component % mode: T-Bonus = sum of BASIC-B … DISCIPLINE-B from those percentages.
        if (percentTotal <= 0 && line.BonusAmount > 0)
        {
            line.BasicPercent = 100m;
            line.ServicePercent = 0;
            line.AttendancePercent = 0;
            line.AssessmentPercent = 0;
            line.LeavePercent = 0;
            line.DisciplinePercent = 0;
            line.BasicBonus = Money(line.BonusAmount);
            line.ServiceBonus = 0;
            line.AttendanceBonus = 0;
            line.AssessmentBonus = 0;
            line.LeaveBonus = 0;
            line.DisciplineBonus = 0;
        }
        else if (string.IsNullOrWhiteSpace(field) || field == "bonusamount" || PercentFields.Contains(field))
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

        line.TotalBonus = Money(line.BasicBonus + line.ServiceBonus + line.AttendanceBonus + line.AssessmentBonus + line.LeaveBonus + line.DisciplineBonus);
        if (line.TotalBonus <= 0 && line.BonusAmount > 0)
            line.TotalBonus = Money(line.BonusAmount);
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

    /// <summary>
    /// Same EffectiveStart as usp_Pay_BonusGenerate_Candidates DistReady:
    /// InstallmentStart, else Dist Month + ValidFrom year (or UTC year).
    /// </summary>
    private async Task<(int Year, int Month)?> ResolveBonusPeriodAsync(int benefitRuleId, CancellationToken ct)
    {
        var rule = await db.PayrollBenefitRules.AsNoTracking()
            .Include(x => x.Parameters).ThenInclude(x => x.BonusDistribution)
            .SingleOrDefaultAsync(x => x.Id == benefitRuleId && x.BenefitsType == "Bonus", ct);
        return rule == null ? null : ResolveBonusPeriodFromRule(rule);
    }

    private static (int Year, int Month)? ResolveBonusPeriodFromRule(PayrollBenefitRule rule)
    {
        var dist = rule.Parameters
            .Where(p => p.BonusDistribution != null)
            .OrderByDescending(p => p.MinimumService)
            .ThenByDescending(p => p.Id)
            .Select(p => p.BonusDistribution!)
            .FirstOrDefault();
        if (dist == null) return null;

        DateOnly? start = dist.InstallmentStart;
        if (start == null && dist.Month is >= 1 and <= 12)
        {
            var y = rule.ValidFrom?.Year ?? DateTime.UtcNow.Year;
            start = new DateOnly(y, dist.Month.Value, 1);
        }
        if (start == null) return null;
        return (start.Value.Year, start.Value.Month);
    }
}

/// <summary>Year/Month optional — when omitted/0, server uses Bonus Distribution period for the rule.</summary>
public sealed record GenerateBonusRequest(int BenefitRuleId, int Year = 0, int Month = 0, bool Regenerate = false);
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
