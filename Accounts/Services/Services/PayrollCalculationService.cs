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
        line.AssessmentAmount = Money(Math.Max(0, line.AssessmentAmount));
        line.BonusAmount = Money(line.BonusAmount);
        line.OvertimeAmount = Money(line.OvertimeAmount);
        line.AttendanceDeduction = Money(Math.Max(0, line.AttendanceDeduction));
        line.AttendanceAdjustment = Money(line.AttendanceAdjustment);
        line.TaxAmount = Money(Math.Max(0, line.TaxAmount));
        line.EmployeeEobiAmount = Money(Math.Max(0, line.EmployeeEobiAmount));
        line.EmployerEobiAmount = Money(Math.Max(0, line.EmployerEobiAmount));
        line.OtherDeduction = Money(Math.Max(0, line.OtherDeduction));

        // Attendance adjustments are deduction corrections, not earnings. A positive
        // approved adjustment gives back up to the attendance deduction; a negative
        // adjustment is an additional deduction. Neither changes taxable gross.
        // Ordinary finalized shortage and excess-absence deductions always post to
        // payroll. The attendance engine has already excluded the configured monthly
        // absence allowance and decides separately whether the special T-Present
        // (completed-late) penalty applies. A positive approved adjustment gives relief;
        // a negative approved adjustment adds a further deduction.
        var postedAttendanceDeduction = line.AttendanceDeduction;
        var approvedAdjustment = line.IsAttendanceAdjustmentApproved ? line.AttendanceAdjustment : 0;
        var deductionRelief = Math.Min(postedAttendanceDeduction, Math.Max(0, approvedAdjustment));
        var effectiveAttendanceDeduction = postedAttendanceDeduction - deductionRelief;
        var additionalAdjustmentDeduction = Math.Max(0, -approvedAdjustment);
        line.TaxableIncome = Money(line.BasicSalary + line.AllowanceAmount + line.AssessmentAmount + line.BonusAmount + line.OvertimeAmount);
        line.GrossPay = line.TaxableIncome;
        line.TotalDeduction = Money(effectiveAttendanceDeduction + line.StaffBenefitDeduction + line.TaxAmount + line.EmployeeEobiAmount + line.OtherDeduction + additionalAdjustmentDeduction);
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
        var packageIds = profiles.Values
            .Where(x => x.SalaryPackageId.HasValue)
            .Select(x => x.SalaryPackageId!.Value)
            .Distinct()
            .ToArray();
        var packages = packageIds.Length == 0
            ? new Dictionary<int, SalaryPackage>()
            : await db.SalaryPackages.AsNoTracking()
                .Where(x => x.IsActive && packageIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
        var scales = await db.SalaryScales.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var scaleById = scales.ToDictionary(x => x.Id);
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
        var benefitRules = await db.PayrollBenefitRules.AsNoTracking()
            .Include(x => x.Parameters)
            .Include(x => x.OrganizationScopes)
            .Include(x => x.ContractScopes)
            .Where(x => x.BenefitsType != "Bonus" && x.BenefitsType != "EOBI" && !x.IsIneligible)
            .ToListAsync(cancellationToken);
        var organizationNodes = await db.OrganizationTree.AsNoTracking().ToDictionaryAsync(x => x.Id, cancellationToken);
        var employmentByPerson = await db.Persons.AsNoTracking()
            .Where(x => personIds.Contains(x.PersonId))
            .Select(x => new { x.PersonId, x.EmploymentStatus })
            .ToDictionaryAsync(x => x.PersonId, x => x.EmploymentStatus, cancellationToken);
        var bonusLines = await db.PayrollBonusLines.AsNoTracking().Include(x => x.BonusRun)
            .Where(x => x.IsApproved && !x.IsInactive && !x.IsPaid
                && x.BonusRun != null && x.BonusRun.Status == "Approved")
            .ToListAsync(cancellationToken);
        var assessmentRows = await db.StaffAssessments.AsNoTracking()
            .Where(x => personIds.Contains(x.SubjectPersonId) && x.AssessmentYear == year &&
                x.AssessmentMonth == month && x.Rating != null && x.IsLocked)
            .Select(x => new { x.SubjectPersonId, x.Amount, x.SubmittedDateUtc, x.ModifiedDateUtc, x.CreatedDateUtc })
            .ToListAsync(cancellationToken);
        var assessmentByPerson = assessmentRows.GroupBy(x => x.SubjectPersonId)
            .ToDictionary(group => group.Key, group => group
                .OrderByDescending(x => x.SubmittedDateUtc ?? x.ModifiedDateUtc ?? x.CreatedDateUtc)
                .First().Amount ?? 0m);
        var eobiSetting = await db.EobiSettings.AsNoTracking()
            .Where(x => x.IsActive && x.EffectiveFrom <= periodEnd && (x.EffectiveTo == null || x.EffectiveTo >= periodStart))
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
        var eobiBenefit = await db.PayrollBenefitRules.AsNoTracking()
            .Include(x => x.Parameters)
            .Where(x => x.BenefitsType == "EOBI"
                && !x.IsIneligible
                && (x.ValidFrom == null || x.ValidFrom <= periodEnd)
                && (x.ValidTo == null || x.ValidTo >= periodStart)
                && (x.Wef == null || x.Wef <= periodEnd))
            .OrderByDescending(x => x.Wef ?? x.ValidFrom)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var eobiParameter = eobiBenefit?.Parameters
            .Where(x => (!x.PeriodFrom.HasValue || x.PeriodFrom <= periodEnd)
                && (!x.PeriodTo.HasValue || x.PeriodTo >= periodStart))
            .OrderByDescending(x => x.PeriodFrom)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        var fixedEmployerEobi = Money(eobiParameter?.CompanyShare ?? eobiBenefit?.CompanyShare ?? 0);
        var fixedEmployeeEobi = Money(eobiParameter?.StaffShare ?? eobiBenefit?.StaffShare ?? 0);
        var hasFixedEobi = fixedEmployerEobi > 0 || fixedEmployeeEobi > 0;
        var eobiPeople = await db.EobiEligibilities.AsNoTracking()
            .Where(x => personIds.Contains(x.PersonId)
                && x.EffectiveFrom <= periodEnd
                && ((x.IsEligible && x.EffectiveTo == null)
                    || (x.EffectiveTo != null && x.EffectiveTo >= periodStart)))
            .Select(x => x.PersonId)
            .ToHashSetAsync(cancellationToken);
        var payrollTaxYear = ResolveTaxYear(year, month);
        var taxSlabs = await db.PayrollTaxSlabs.AsNoTracking()
            .Where(x => x.IsActive && x.TaxYear == payrollTaxYear)
            .OrderBy(x => x.FromAmount)
            .ToListAsync(cancellationToken);
        var attendanceByPerson = attendance.Rows.ToDictionary(x => x.PersonId);
        var now = DateTime.UtcNow;
        var result = new List<PayrollLine>(employees.Count);

        foreach (var employee in employees)
        {
            profiles.TryGetValue(employee.PersonId, out var profile);
            SalaryPackage? package = null;
            if (profile?.SalaryPackageId is int pkgId)
                packages.TryGetValue(pkgId, out package);

            SalaryScale? scale = null;
            if (package != null && scaleById.TryGetValue(package.SalaryScaleId, out var packageScale))
                scale = packageScale;
            else if (!string.IsNullOrWhiteSpace(profile?.Scale))
                scaleByName.TryGetValue(profile.Scale.Trim(), out scale);

            designationByStaff.TryGetValue(employee.StaffId, out var designationId);
            shiftByStaff.TryGetValue(employee.StaffId, out var shiftCode);
            var packageAllowanceRefs = ParsePackageRefs(package?.AllowanceReference);
            var scaleAllowances = allowances.Where(x =>
                IsAllowanceApplicable(x, designationId, shiftCode) &&
                IsAllowanceScaleApplicable(x, scale?.Id) &&
                IsPackageAllowanceAllowed(x, packageAllowanceRefs)).ToList();
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
            // TADA is cash and joins Gross via General/Allowance total (Leave stays non-cash; package LeaveReference is metadata only).
            // Phase-1 2C: attendance approved adjustment ≈ period adjustment; OtherDeduction ≈ manual; no Proficiency/Loan modules.
            if (scale != null)
            {
                var packageTadaRefs = ParsePackageRefs(package?.TadaReference);
                var scaleTadas = tadas.Where(x => x.SalaryScaleId == scale.Id);
                if (packageTadaRefs.Count > 0)
                    scaleTadas = scaleTadas.Where(x => packageTadaRefs.Contains(x.TadaReference));
                generalAllowance = Money(generalAllowance + scaleTadas.Sum(x => x.CalculatedValue));
            }
            var allowanceAmount = Money(generalAllowance + apptAllowance + shiftAllowance);

            var scaleBasic = Money(profile?.BasicSalary is > 0 ? profile.BasicSalary.Value : scale?.BasicSalary ?? 0);
            var incrementSalary = Money(profile?.IncrementSalary is > 0 ? profile.IncrementSalary.Value : scale?.YearlyIncrement ?? 0);
            var maxSalary = Money(profile?.MaxSalary is > 0 ? profile.MaxSalary.Value : scale?.MaximumSalary ?? 0);
            var currentPay = PayrollCurrentPayCalculator.Compute(
                scaleBasic,
                incrementSalary,
                maxSalary,
                profile?.ScaleDate,
                periodEnd,
                scale?.ApplyAfter,
                PayrollCurrentPayCalculator.ParseMonthsCsv(scale?.IncrementMonths));
            var basicSalary = Money(currentPay > 0 ? currentPay : scaleBasic);

            var serviceYears = profile?.JoiningDate is DateTime joining
                ? Math.Max(0, (decimal)(periodEnd.ToDateTime(TimeOnly.MinValue) - joining.Date).TotalDays / 365.2425m)
                : 0;
            var applicableBenefits = benefitRules.Where(rule =>
            {
                employmentByPerson.TryGetValue(employee.PersonId, out var employmentStatus);
                return IsBenefitApplicable(
                    rule,
                    profile,
                    employee.OrganizationId,
                    organizationNodes,
                    serviceYears,
                    periodStart,
                    periodEnd,
                    employmentStatus);
            });
            decimal employerBenefits = 0;
            decimal staffBenefits = 0;
            foreach (var rule in applicableBenefits)
            {
                var parameters = rule.Parameters.Where(parameter =>
                    (!parameter.PeriodFrom.HasValue || parameter.PeriodFrom <= periodEnd) &&
                    (!parameter.PeriodTo.HasValue || parameter.PeriodTo >= periodStart) &&
                    serviceYears * 12m >= parameter.MinimumService).ToList();
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
            assessmentByPerson.TryGetValue(employee.PersonId, out var assessmentAmount);
            attendanceByPerson.TryGetValue(employee.PersonId, out var attendanceRow);
            var overtime = attendanceRow is { IsOvertimeApproved: true, IsOvertimeBonusActive: true } ? attendanceRow.OvertimeBonusAmount : 0;
            // Attendance finalization → Deduction report → NetDeduction / approved adjustment.
            var attendanceDeduction = attendanceRow?.NetDeduction ?? 0;
            var adjustment = attendanceRow?.AdjustmentAmount ?? 0;
            var pendingDays = attendanceRow?.PendingReviewDays ?? 0;
            var taxableMonthly = basicSalary + allowanceAmount + assessmentAmount + bonusAmount + overtime;
            var tax = CalculateMonthlyTax(taxableMonthly, taxSlabs);
            decimal employeeEobi = 0;
            decimal employerEobi = 0;
            if (eobiPeople.Contains(employee.PersonId))
            {
                if (hasFixedEobi)
                {
                    employeeEobi = fixedEmployeeEobi;
                    employerEobi = fixedEmployerEobi;
                }
                else if (eobiSetting != null)
                {
                    var wageBase = basicSalary <= 0 ? 0 : Math.Max(basicSalary, eobiSetting.MinimumWage);
                    var contributionBase = eobiSetting.MaximumContributionBase > 0
                        ? Math.Min(wageBase, eobiSetting.MaximumContributionBase)
                        : wageBase;
                    employeeEobi = contributionBase * eobiSetting.EmployeeRatePercentage / 100m;
                    employerEobi = contributionBase * eobiSetting.EmployerRatePercentage / 100m;
                }
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
                AssessmentAmount = assessmentAmount,
                BonusAmount = bonusAmount,
                OvertimeAmount = overtime,
                AttendanceDeduction = attendanceDeduction,
                // Retained as a compatibility snapshot only. It must never suppress
                // ordinary shortage or excess-absence deductions.
                IsAttendanceDeductionActive = true,
                AttendanceAdjustment = adjustment,
                IsAttendanceAdjustmentApproved = attendanceRow?.IsAdjustmentApproved ?? false,
                AttendanceAdjustmentRemarks = attendanceRow?.AdjustmentRemarks,
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

    /// <summary>
    /// When package.AllowanceReference is set, GENERAL rows must match; APPT/SHIFT still apply by designation/shift on the package scale.
    /// </summary>
    private static bool IsPackageAllowanceAllowed(PayScaleAllowance allowance, IReadOnlySet<string> packageAllowanceRefs)
    {
        if (packageAllowanceRefs.Count == 0) return true;
        var category = allowance.AllowanceCategory.Trim().ToUpperInvariant();
        if (category is "APPT" or "SHIFT" or "NIGHT") return true;
        return !string.IsNullOrWhiteSpace(allowance.AllowanceReference)
            && packageAllowanceRefs.Contains(allowance.AllowanceReference.Trim());
    }

    private static HashSet<string> ParsePackageRefs(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsBenefitApplicable(
        PayrollBenefitRule rule,
        PersonHrProfile? profile,
        int? organizationId,
        IReadOnlyDictionary<int, OrganizationTree> organizationNodes,
        decimal serviceYears,
        DateOnly periodStart,
        DateOnly periodEnd,
        string? employmentStatus = null)
    {
        if (rule.ValidFrom.HasValue && rule.ValidFrom > periodEnd || rule.ValidTo.HasValue && rule.ValidTo < periodStart) return false;
        if (rule.Wef.HasValue && rule.Wef > periodEnd) return false;
        if (!string.IsNullOrWhiteSpace(rule.Scale) && !string.Equals(rule.Scale.Trim(), profile?.Scale?.Trim(), StringComparison.OrdinalIgnoreCase)) return false;

        var orgScopeIds = rule.OrganizationScopes?.Select(scope => scope.OrganizationId).Distinct().ToList()
            ?? [];
        if (orgScopeIds.Count == 0 && rule.OrganizationId.HasValue)
            orgScopeIds.Add(rule.OrganizationId.Value);
        if (orgScopeIds.Count > 0)
        {
            if (!organizationId.HasValue) return false;
            var matched = orgScopeIds.Any(scopeId => IsOrganizationDescendant(organizationId.Value, scopeId, organizationNodes));
            if (!matched) return false;
        }

        var contractNames = rule.ContractScopes?.Select(scope => scope.ContractName.Trim())
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            ?? [];
        if (contractNames.Count == 0 && !string.IsNullOrWhiteSpace(rule.Contract))
        {
            contractNames = rule.Contract.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        if (contractNames.Count > 0)
        {
            var employeeContract = ResolveEmployeeContractName(profile, employmentStatus, periodEnd);
            if (string.IsNullOrWhiteSpace(employeeContract)
                || !contractNames.Contains(employeeContract, StringComparer.OrdinalIgnoreCase))
                return false;
        }

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

    private static string? ResolveEmployeeContractName(PersonHrProfile? profile, string? employmentStatus, DateOnly periodEnd)
    {
        if (!string.IsNullOrWhiteSpace(employmentStatus))
            return employmentStatus.Trim();
        if (!string.IsNullOrWhiteSpace(profile?.InductionType))
            return profile.InductionType.Trim();

        var asOf = periodEnd.ToDateTime(TimeOnly.MinValue);
        if (profile?.ProbationFrom is DateTime probationFrom
            && probationFrom.Date <= asOf
            && (profile.ProbationTo == null || profile.ProbationTo.Value.Date >= asOf))
            return "Probation";

        return null;
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

    private static decimal CalculateMonthlyTax(decimal monthlyTaxablePay, IReadOnlyList<PayrollTaxSlab> slabs) =>
        PayrollTaxCalculator.CalculateMonthlyTax(monthlyTaxablePay, slabs);

    private static decimal Money(decimal value) => PayrollTaxCalculator.Money(value);

    private static string ResolveTaxYear(int year, int month) =>
        month >= 7 ? $"{year}-{year + 1}" : $"{year - 1}-{year}";

    private static void ValidatePeriod(int year, int month)
    {
        if (year is < 2000 or > 2200 || month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "Enter a valid payroll month and year.");
    }
}
