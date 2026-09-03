using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class PayrollCalculationService(
    ApplicationDbContext db,
    ITenantService tenant,
    IAttendanceService attendanceService)
{
    public async Task<IReadOnlyList<PayrollLine>> PreviewAsync(
        string identityUserId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        ValidatePeriod(year, month);
        var attendance = await attendanceService.GetDeductionReportAsync(
            identityUserId,
            organizationWide: true,
            year,
            month,
            cancellationToken);
        return await BuildLinesAsync(year, month, attendance, cancellationToken);
    }

    public async Task<int> CountPendingReviewEmployeesAsync(
        string identityUserId,
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        ValidatePeriod(year, month);
        var attendance = await attendanceService.GetDeductionReportAsync(
            identityUserId,
            organizationWide: true,
            year,
            month,
            cancellationToken);
        return attendance.Rows.Count(row => row.PendingReviewDays > 0);
    }

    public async Task<PayrollRun> GenerateAsync(
        string identityUserId,
        string actorName,
        int year,
        int month,
        DateOnly payDate,
        CancellationToken cancellationToken)
    {
        ValidatePeriod(year, month);
        var attendance = await attendanceService.GetDeductionReportAsync(
            identityUserId,
            organizationWide: true,
            year,
            month,
            cancellationToken);
        var lines = await BuildLinesAsync(year, month, attendance, cancellationToken);
        var run = await db.PayrollRuns.Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Year == year && x.Month == month, cancellationToken);

        if (run != null && !run.Status.Equals("Draft", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only a Draft payroll can be regenerated.");

        var now = DateTime.UtcNow;
        if (run == null)
        {
            run = new PayrollRun
            {
                TenantId = tenant.RequiredTenantId,
                Year = year,
                Month = month,
                RunNumber = $"PAY-{year}{month:00}",
                PayDate = payDate,
                Status = "Draft",
                CreatedByUserId = identityUserId,
                CreatedByName = actorName,
                CreatedOnUtc = now
            };
            db.PayrollRuns.Add(run);
        }
        else
        {
            db.PayrollLines.RemoveRange(run.Lines);
            run.PayDate = payDate;
            run.UpdatedOnUtc = now;
            run.VerifiedByUserId = null;
            run.VerifiedByName = null;
            run.VerifiedOnUtc = null;
            run.ApprovedByUserId = null;
            run.ApprovedByName = null;
            run.ApprovedOnUtc = null;
        }

        foreach (var line in lines)
        {
            line.TenantId = tenant.RequiredTenantId;
            run.Lines.Add(line);
        }

        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public static void Recalculate(PayrollLine line)
    {
        line.ScaleBasicSalary = Money(line.ScaleBasicSalary);
        line.IncrementSalary = Money(line.IncrementSalary);
        line.MaxSalary = Money(line.MaxSalary);
        line.CurrentPay = Money(line.CurrentPay);
        line.BasicSalary = Money(line.BasicSalary);
        line.GeneralAllowanceAmount = Money(Math.Max(0, line.GeneralAllowanceAmount));
        line.ApptAllowanceAmount = Money(Math.Max(0, line.ApptAllowanceAmount));
        line.ShiftAllowanceAmount = Money(Math.Max(0, line.ShiftAllowanceAmount));
        var splitTotal = line.GeneralAllowanceAmount + line.ApptAllowanceAmount + line.ShiftAllowanceAmount;
        line.AllowanceAmount = Money(splitTotal > 0 ? splitTotal : Math.Max(0, line.AllowanceAmount));
        line.EmployerBenefitAmount = Money(line.EmployerBenefitAmount);
        line.StaffBenefitDeduction = Money(line.StaffBenefitDeduction);
        line.BonusAmount = Money(line.BonusAmount);
        line.OvertimeAmount = Money(line.OvertimeAmount);
        line.AttendanceDeduction = Money(Math.Max(0, line.AttendanceDeduction));
        line.AttendanceAdjustment = Money(line.AttendanceAdjustment);
        line.TaxAmount = Money(Math.Max(0, line.TaxAmount));
        line.EmployeeEobiAmount = Money(Math.Max(0, line.EmployeeEobiAmount));
        line.EmployerEobiAmount = Money(Math.Max(0, line.EmployerEobiAmount));
        line.OtherDeduction = Money(Math.Max(0, line.OtherDeduction));

        var positiveAdjustment = Math.Max(0, line.AttendanceAdjustment);
        var negativeAdjustment = Math.Max(0, -line.AttendanceAdjustment);
        // AGENTS: Gross = Basic + Allowances + Bonus + OT + positive approved adjustment.
        line.TaxableIncome = Money(line.BasicSalary + line.AllowanceAmount + line.BonusAmount + line.OvertimeAmount + positiveAdjustment);
        line.GrossPay = line.TaxableIncome;
        line.TotalDeduction = Money(line.AttendanceDeduction + line.StaffBenefitDeduction + line.TaxAmount + line.EmployeeEobiAmount + line.OtherDeduction + negativeAdjustment);
        line.NetPay = Money(Math.Max(0, line.GrossPay - line.TotalDeduction));
    }

    private async Task<IReadOnlyList<PayrollLine>> BuildLinesAsync(
        int year,
        int month,
        AttendanceDeductionReportDto attendance,
        CancellationToken cancellationToken)
    {
        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        var employees = await db.StaffDirectoryRows.AsNoTracking()
            .Where(x => x.IsPersonActive)
            .OrderBy(x => x.FullName)
            .ToListAsync(cancellationToken);
        var personIds = employees.Select(x => x.PersonId).Distinct().ToArray();
        var staffIds = employees.Select(x => x.StaffId).Distinct().ToArray();
        var profiles = await db.PersonHrProfiles.AsNoTracking()
            .Where(x => personIds.Contains(x.PersonId))
            .ToDictionaryAsync(x => x.PersonId, cancellationToken);
        var scales = await db.SalaryScales.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var scaleByName = scales
            .Where(x => !string.IsNullOrWhiteSpace(x.ScaleName))
            .GroupBy(x => x.ScaleName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var allowances = await db.PayScaleAllowances.AsNoTracking()
            .Include(x => x.ShiftLookupValue)
            .ToListAsync(cancellationToken);
        var tadas = await db.PayScaleTadas.AsNoTracking().ToListAsync(cancellationToken);
        var designationByStaff = await db.StaffVacancies.AsNoTracking()
            .Where(x => staffIds.Contains(x.StaffId) && x.Vacancy != null)
            .Select(x => new { x.StaffId, x.Vacancy!.DesignationId })
            .ToDictionaryAsync(x => x.StaffId, x => x.DesignationId, cancellationToken);
        var shiftByStaff = await db.AttendanceMapRules.AsNoTracking()
            .Where(x => staffIds.Contains(x.StaffId))
            .GroupBy(x => x.StaffId)
            .Select(group => new
            {
                StaffId = group.Key,
                ShiftCode = group.OrderByDescending(x => x.ModifiedDate ?? x.CreatedDate)
                    .Select(x => x.ShiftCode)
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.StaffId, x => x.ShiftCode, cancellationToken);
        var benefitRules = await db.PayrollBenefitRules.AsNoTracking().Include(x => x.Parameters)
            .Where(x => x.BenefitsType != "Bonus" && !x.IsIneligible)
            .ToListAsync(cancellationToken);
        var organizationNodes = await db.OrganizationTree.AsNoTracking().ToDictionaryAsync(x => x.Id, cancellationToken);
        var bonusLines = await db.PayrollBonusLines.AsNoTracking().Include(x => x.BonusRun)
            .Where(x => x.IsApproved && !x.IsInactive && !x.IsPaid
                && x.BonusRun != null && x.BonusRun.Status == "Approved")
            .ToListAsync(cancellationToken);
        var eobiSetting = await db.EobiSettings.AsNoTracking()
            .Where(x => x.IsActive && x.EffectiveFrom <= periodEnd && (x.EffectiveTo == null || x.EffectiveTo >= periodStart))
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
        var eobiPeople = await db.EobiEligibilities.AsNoTracking()
            .Where(x => x.IsEligible && personIds.Contains(x.PersonId) && x.EffectiveFrom <= periodEnd && (x.EffectiveTo == null || x.EffectiveTo >= periodStart))
            .Select(x => x.PersonId)
            .ToHashSetAsync(cancellationToken);
        var taxSlabs = await db.PayrollTaxSlabs.AsNoTracking()
            .Where(x => x.IsActive && x.TaxYear == year.ToString())
            .OrderBy(x => x.FromAmount)
            .ToListAsync(cancellationToken);
        var attendanceByPerson = attendance.Rows.ToDictionary(x => x.PersonId);
        var now = DateTime.UtcNow;
        var result = new List<PayrollLine>(employees.Count);

        foreach (var employee in employees)
        {
            profiles.TryGetValue(employee.PersonId, out var profile);
            SalaryScale? scale = null;
            if (!string.IsNullOrWhiteSpace(profile?.Scale)) scaleByName.TryGetValue(profile.Scale.Trim(), out scale);

            designationByStaff.TryGetValue(employee.StaffId, out var designationId);
            shiftByStaff.TryGetValue(employee.StaffId, out var shiftCode);
            var scaleAllowances = allowances.Where(x =>
                IsAllowanceApplicable(x, designationId, shiftCode) &&
                IsAllowanceScaleApplicable(x, scale?.Id)).ToList();
            var hasScaleAllowanceConfiguration = scale != null && allowances.Any(x =>
                x.SalaryScaleId == scale.Id &&
                !x.AllowanceCategory.Equals("SHIFT", StringComparison.OrdinalIgnoreCase) &&
                !x.AllowanceCategory.Equals("NIGHT", StringComparison.OrdinalIgnoreCase));

            var apptAllowance = Money(scaleAllowances
                .Where(x => x.AllowanceCategory.Equals("APPT", StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.CalculatedValue));
            var shiftAllowance = Money(scaleAllowances
                .Where(x =>
                    x.AllowanceCategory.Equals("SHIFT", StringComparison.OrdinalIgnoreCase) ||
                    x.AllowanceCategory.Equals("NIGHT", StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.CalculatedValue));
            var generalAllowance = Money(scaleAllowances
                .Where(x =>
                    !x.AllowanceCategory.Equals("APPT", StringComparison.OrdinalIgnoreCase) &&
                    !x.AllowanceCategory.Equals("SHIFT", StringComparison.OrdinalIgnoreCase) &&
                    !x.AllowanceCategory.Equals("NIGHT", StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.CalculatedValue));
            if (!hasScaleAllowanceConfiguration)
                generalAllowance = Money(generalAllowance + (scale?.MedicalAllowance ?? 0) + (scale?.TravellingAllowance ?? 0) + (scale?.Other ?? 0));
            // TADA is cash and joins Gross via General/Allowance total (Leave stays non-cash).
            if (scale != null)
                generalAllowance = Money(generalAllowance + tadas.Where(x => x.SalaryScaleId == scale.Id).Sum(x => x.CalculatedValue));
            var allowanceAmount = Money(generalAllowance + apptAllowance + shiftAllowance);

            var scaleBasic = Money(profile?.BasicSalary is > 0 ? profile.BasicSalary.Value : scale?.BasicSalary ?? 0);
            var incrementSalary = Money(profile?.IncrementSalary is > 0 ? profile.IncrementSalary.Value : scale?.YearlyIncrement ?? 0);
            var maxSalary = Money(profile?.MaxSalary is > 0 ? profile.MaxSalary.Value : scale?.MaximumSalary ?? 0);
            var currentPay = Money(profile?.CurrentPay is > 0 ? profile.CurrentPay.Value
                : scale?.CurrentPay is > 0 ? scale.CurrentPay
                : scaleBasic);
            var basicSalary = Money(currentPay > 0 ? currentPay : scaleBasic);

            var serviceYears = profile?.JoiningDate is DateTime joining
                ? Math.Max(0, (decimal)(periodEnd.ToDateTime(TimeOnly.MinValue) - joining.Date).TotalDays / 365.2425m)
                : 0;
            var applicableBenefits = benefitRules.Where(rule => IsBenefitApplicable(rule, profile, employee.OrganizationId, organizationNodes, serviceYears, periodStart, periodEnd));
            decimal employerBenefits = 0;
            decimal staffBenefits = 0;
            foreach (var rule in applicableBenefits)
            {
                var parameters = rule.Parameters.Where(parameter =>
                    (!parameter.PeriodFrom.HasValue || parameter.PeriodFrom <= periodEnd) &&
                    (!parameter.PeriodTo.HasValue || parameter.PeriodTo >= periodStart) &&
                    serviceYears >= parameter.MinimumService).ToList();
                if (parameters.Count == 0)
                {
                    employerBenefits += ResolveShare(rule.CompanyShare, rule.ShareType, basicSalary);
                    staffBenefits += ResolveShare(rule.StaffShare, rule.ShareType, basicSalary);
                }
                else
                {
                    foreach (var parameter in parameters)
                    {
                        employerBenefits += ResolveShare(parameter.CompanyShare, parameter.AmountType, basicSalary);
                        staffBenefits += ResolveShare(parameter.StaffShare, parameter.AmountType, basicSalary);
                    }
                }
            }

            // Bonus → payroll: approved run lines, due installment only (PaidInstallmentCount).
            var bonusAmount = bonusLines.Where(x => x.PersonId == employee.PersonId && IsBonusInstallmentDue(x, year, month))
                .Sum(x => x.InstallmentAmount > 0 ? x.InstallmentAmount : x.TotalBonus);
            attendanceByPerson.TryGetValue(employee.PersonId, out var attendanceRow);
            var overtime = attendanceRow is { IsOvertimeApproved: true, IsOvertimeBonusActive: true } ? attendanceRow.OvertimeBonusAmount : 0;
            // Attendance finalization → Deduction report → NetDeduction / approved adjustment.
            var attendanceDeduction = attendanceRow?.NetDeduction ?? 0;
            var adjustment = attendanceRow is { IsAdjustmentApproved: true } ? attendanceRow.AdjustmentAmount : 0;
            var pendingDays = attendanceRow?.PendingReviewDays ?? 0;
            var taxableMonthly = basicSalary + allowanceAmount + bonusAmount + overtime + Math.Max(0, adjustment);
            var tax = CalculateMonthlyTax(taxableMonthly, taxSlabs);
            decimal employeeEobi = 0;
            decimal employerEobi = 0;
            if (eobiSetting != null && eobiPeople.Contains(employee.PersonId))
            {
                var wageBase = basicSalary <= 0 ? 0 : Math.Max(basicSalary, eobiSetting.MinimumWage);
                var contributionBase = eobiSetting.MaximumContributionBase > 0
                    ? Math.Min(wageBase, eobiSetting.MaximumContributionBase)
                    : wageBase;
                employeeEobi = contributionBase * eobiSetting.EmployeeRatePercentage / 100m;
                employerEobi = contributionBase * eobiSetting.EmployerRatePercentage / 100m;
            }

            var remarks = new List<string>();
            if (basicSalary <= 0) remarks.Add("Review: missing salary configuration (current/basic pay is zero).");
            if (pendingDays > 0) remarks.Add($"Pending Review attendance: {pendingDays} day(s) — blocks Process/Pay.");

            var line = new PayrollLine
            {
                TenantId = tenant.RequiredTenantId,
                PersonId = employee.PersonId,
                StaffId = employee.StaffId,
                EmployeeNumber = employee.EmployeeId,
                FullName = employee.FullName,
                Designation = employee.Designation,
                Department = employee.Department,
                DateOfJoining = profile?.JoiningDate is DateTime joined ? DateOnly.FromDateTime(joined) : null,
                ScaleDate = profile?.ScaleDate is DateTime scaleDt ? DateOnly.FromDateTime(scaleDt) : null,
                Scale = profile?.Scale ?? scale?.ScaleName,
                ContractType = scale?.ContractType,
                Month = month,
                Year = year,
                ScaleBasicSalary = scaleBasic,
                IncrementSalary = incrementSalary,
                MaxSalary = maxSalary,
                CurrentPay = currentPay,
                BasicSalary = basicSalary,
                GeneralAllowanceAmount = generalAllowance,
                ApptAllowanceAmount = apptAllowance,
                ShiftAllowanceAmount = shiftAllowance,
                AllowanceAmount = allowanceAmount,
                EmployerBenefitAmount = employerBenefits,
                StaffBenefitDeduction = staffBenefits,
                BonusAmount = bonusAmount,
                OvertimeAmount = overtime,
                AttendanceDeduction = attendanceDeduction,
                AttendanceAdjustment = adjustment,
                TaxableIncome = Money(taxableMonthly),
                TaxAmount = tax,
                EmployeeEobiAmount = employeeEobi,
                EmployerEobiAmount = employerEobi,
                IsPending = pendingDays > 0,
                PendingReviewDays = pendingDays,
                Remarks = remarks.Count == 0 ? null : string.Join(" ", remarks),
                CreatedOnUtc = now
            };
            Recalculate(line);
            result.Add(line);
        }
        return result;
    }

    private static bool IsAllowanceApplicable(
        PayScaleAllowance allowance,
        int? designationId,
        string? shiftCode)
    {
        var category = allowance.AllowanceCategory.Trim().ToUpperInvariant();
        return category switch
        {
            "APPT" => allowance.DesignationId.HasValue && allowance.DesignationId == designationId,
            "SHIFT" or "NIGHT" => allowance.ShiftLookupValueId.HasValue &&
                !string.IsNullOrWhiteSpace(shiftCode) &&
                string.Equals(allowance.ShiftLookupValue?.ValueCode, shiftCode.Trim(), StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private static bool IsAllowanceScaleApplicable(PayScaleAllowance allowance, int? salaryScaleId)
    {
        var category = allowance.AllowanceCategory.Trim().ToUpperInvariant();
        if (category is "SHIFT" or "NIGHT")
            return !allowance.SalaryScaleId.HasValue || allowance.SalaryScaleId == salaryScaleId;
        return salaryScaleId.HasValue && allowance.SalaryScaleId == salaryScaleId;
    }

    private static bool IsBenefitApplicable(
        PayrollBenefitRule rule,
        PersonHrProfile? profile,
        int? organizationId,
        IReadOnlyDictionary<int, OrganizationTree> organizationNodes,
        decimal serviceYears,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        if (rule.ValidFrom.HasValue && rule.ValidFrom > periodEnd || rule.ValidTo.HasValue && rule.ValidTo < periodStart) return false;
        if (rule.Wef.HasValue && rule.Wef > periodEnd) return false;
        if (!string.IsNullOrWhiteSpace(rule.Scale) && !string.Equals(rule.Scale.Trim(), profile?.Scale?.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (rule.OrganizationId.HasValue && (!organizationId.HasValue || !IsOrganizationDescendant(organizationId.Value, rule.OrganizationId.Value, organizationNodes))) return false;
        if (serviceYears < rule.MinimumService) return false;
        var anchor = rule.Wef ?? rule.ValidFrom ?? periodStart;
        var elapsedMonths = (periodStart.Year - anchor.Year) * 12 + periodStart.Month - anchor.Month;
        if (elapsedMonths < 0) return false;
        return rule.Frequency?.Trim().ToLowerInvariant() switch
        {
            "annual" or "annually" or "yearly" => elapsedMonths % 12 == 0,
            "quarterly" => elapsedMonths % 3 == 0,
            "onetime" or "one time" => elapsedMonths == 0,
            _ => true
        };
    }

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

    private static bool IsBonusInstallmentDue(PayrollBonusLine line, int year, int month)
    {
        var installments = Math.Max(1, line.Installment);
        var elapsed = (year - line.Year) * 12 + month - line.Month;
        // Next unpaid installment only (PaidInstallmentCount advances on payroll Pay).
        return elapsed >= 0
            && elapsed < installments
            && elapsed == Math.Max(0, line.PaidInstallmentCount);
    }

    private static decimal ResolveShare(decimal value, string? amountType, decimal basis)
    {
        if (value <= 0) return 0;
        var normalized = amountType?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized.Contains("percent") || normalized.Contains('%')
            ? Money(basis * value / 100m)
            : Money(value);
    }

    private static decimal CalculateMonthlyTax(decimal monthlyTaxablePay, IReadOnlyList<PayrollTaxSlab> slabs)
    {
        if (monthlyTaxablePay <= 0 || slabs.Count == 0) return 0;
        var annualPay = monthlyTaxablePay * 12m;
        var slab = slabs.LastOrDefault(x => annualPay >= x.FromAmount && (!x.ToAmount.HasValue || annualPay <= x.ToAmount.Value));
        if (slab == null) return 0;
        var excess = Math.Max(0, annualPay - slab.FromAmount);
        return Money((slab.FixedTaxAmount + excess * slab.RatePercentage / 100m) / 12m);
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static void ValidatePeriod(int year, int month)
    {
        if (year is < 2000 or > 2200 || month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "Enter a valid payroll month and year.");
    }
}
