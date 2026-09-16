using System.Data;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.Data.SqlClient;
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
        // Preview must never persist HR scale upgrades — display-only reconstruction.
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
        string? identityUserId,
        string? actorName,
        int year,
        int month,
        DateOnly payDate,
        CancellationToken cancellationToken)
    {
        ValidatePeriod(year, month);
        var tenantId = tenant.RequiredTenantId;

        // Phase 1: persist + resolve components in dbo.usp_Payroll_GenerateMonthly
        // (RunNumber PayRoll-{month}-{year}, identity PayrollLines.Id like old tblStaffPayRoll).
        // PreviewAsync still uses BuildLinesAsync (no persist). Scale auto-upgrade / HR write
        // remains C#-only in Preview reconstruction for now — Generate SP uses stored CurrentPay.
        var runIdParam = new SqlParameter("@PayrollRunId", SqlDbType.BigInt)
        {
            Direction = ParameterDirection.Output
        };

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                EXEC dbo.usp_Payroll_GenerateMonthly
                    @TenantId,
                    @Year,
                    @Month,
                    @PayDate,
                    @CreatedByUserId,
                    @CreatedByName,
                    @PayrollRunId OUTPUT
                """,
                [
                    new SqlParameter("@TenantId", tenantId),
                    new SqlParameter("@Year", year),
                    new SqlParameter("@Month", month),
                    new SqlParameter("@PayDate", SqlDbType.Date) { Value = payDate.ToDateTime(TimeOnly.MinValue) },
                    new SqlParameter("@CreatedByUserId", (object?)identityUserId ?? DBNull.Value),
                    new SqlParameter("@CreatedByName", (object?)actorName ?? DBNull.Value),
                    runIdParam
                ],
                cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 51010 or 51011 or 2812)
        {
            if (ex.Number == 2812)
            {
                throw new InvalidOperationException(
                    "The payroll generate procedure is not installed. Apply pending database migrations before generating payroll.",
                    ex);
            }

            throw new InvalidOperationException(ex.Message, ex);
        }

        var payrollRunId = runIdParam.Value is null or DBNull
            ? 0L
            : Convert.ToInt64(runIdParam.Value);

        if (payrollRunId <= 0)
            throw new InvalidOperationException("Payroll generate did not return a run id.");

        // SP mutated rows outside the change tracker — detach stale payroll entities then reload.
        foreach (var entry in db.ChangeTracker.Entries<PayrollLine>().ToArray())
            entry.State = EntityState.Detached;
        foreach (var entry in db.ChangeTracker.Entries<PayrollRun>().ToArray())
            entry.State = EntityState.Detached;

        var run = await db.PayrollRuns
            .Include(x => x.Lines)
            .AsNoTracking()
            .SingleAsync(x => x.Id == payrollRunId && x.TenantId == tenantId, cancellationToken);
        return run;
    }

    /// <summary>
    /// Recalculates persisted Draft line totals in SQL. Component resolution remains
    /// separate; this procedure is the authoritative final arithmetic before a line
    /// is returned or advanced to review.
    /// </summary>
    public async Task RecalculateRunTotalsAsync(long payrollRunId, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync(
            "EXEC dbo.usp_Payroll_RecalculateRunTotals @TenantId, @PayrollRunId",
            [
                new SqlParameter("@TenantId", tenant.RequiredTenantId),
                new SqlParameter("@PayrollRunId", payrollRunId)
            ],
            cancellationToken);

        var trackedLines = db.ChangeTracker.Entries<PayrollLine>()
            .Where(entry => entry.Entity.PayrollRunId == payrollRunId)
            .ToArray();
        foreach (var entry in trackedLines)
            await entry.ReloadAsync(cancellationToken);
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
        line.MedicalAllowanceAmount = Money(Math.Max(0, line.MedicalAllowanceAmount));
        line.NightAllowanceAmount = Money(Math.Max(0, line.NightAllowanceAmount));
        line.TelephoneAllowanceAmount = Money(Math.Max(0, line.TelephoneAllowanceAmount));
        line.TransportAllowanceAmount = Money(Math.Max(0, line.TransportAllowanceAmount));
        var splitTotal = line.GeneralAllowanceAmount + line.ApptAllowanceAmount + line.ShiftAllowanceAmount
            + line.MedicalAllowanceAmount + line.NightAllowanceAmount
            + line.TelephoneAllowanceAmount + line.TransportAllowanceAmount;
        line.AllowanceAmount = Money(splitTotal > 0 ? splitTotal : Math.Max(0, line.AllowanceAmount));
        line.SalaryAdjustment = Money(Math.Max(0, line.SalaryAdjustment));
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
        line.TaxableIncome = Money(line.BasicSalary + line.AllowanceAmount + line.SalaryAdjustment + line.AssessmentAmount + line.BonusAmount + line.OvertimeAmount);
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
            .Include(x => x.AllowanceType)
            .Include(x => x.ShiftLookupValue)
            .ToListAsync(cancellationToken);
        var contractTypes = await db.ContractTypes.AsNoTracking()
            .Where(x => x.IsActive)
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
        var bonusLines = await db.PayrollBonusLines.AsNoTracking().Include(x => x.BonusRun)
            .Where(x => x.IsApproved && !x.IsInactive && !x.IsPaid
                && x.BonusRun != null && x.BonusRun.Status == "Approved")
            .ToListAsync(cancellationToken);
        var assessmentRows = await db.StaffAssessments.AsNoTracking()
            .Where(x => personIds.Contains(x.SubjectPersonId) && x.AssessmentYear == year &&
                x.AssessmentMonth == month && x.IsLocked && x.Amount != null &&
                x.IsFinalApproved && x.IsPostedToPayroll)
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
        var eobiBenefitRules = await db.PayrollBenefitRules.AsNoTracking()
            .Include(x => x.Parameters)
            .Where(x => x.BenefitsType == "EOBI"
                && !x.IsIneligible
                && (x.ValidFrom == null || x.ValidFrom <= periodEnd)
                && (x.ValidTo == null || x.ValidTo >= periodStart)
                && (x.Wef == null || x.Wef <= periodEnd))
            .OrderByDescending(x => x.Wef ?? x.ValidFrom)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
        // Eligible staff only (IsEligible=1). Do not charge closed/off rows that still overlap by date.
        var eobiPeople = await db.EobiEligibilities.AsNoTracking()
            .Where(x => personIds.Contains(x.PersonId)
                && x.IsEligible
                && x.EffectiveFrom <= periodEnd
                && (x.EffectiveTo == null || x.EffectiveTo >= periodStart))
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

            var incrementAnchor = PayrollScaleProgression.ResolveIncrementAnchor(
                profile?.ScaleDate,
                profile?.JoiningDate);
            // First service year earns 1× INC; at Max the next scale-year auto-upgrades (e.g. RLT-10 → RLT-11).
            // Years count from ScaleDate when set (when this scale was applied), not full DOJ tenure.
            var progression = PayrollScaleProgression.Resolve(
                scale,
                scales,
                incrementAnchor,
                periodEnd,
                profile?.BasicSalary,
                profile?.IncrementSalary,
                profile?.MaxSalary);
            scale = progression.EffectiveScale ?? scale;
            var scaleBasic = progression.ScaleBasic;
            var incrementSalary = progression.YearlyIncrement;
            var maxSalary = progression.MaxSalary;
            var currentPay = progression.CurrentPay;
            var basicSalary = Money(currentPay > 0 ? currentPay : scaleBasic);
            var effectiveScaleName = scale?.ScaleName ?? profile?.Scale;
            var lineScaleDate = progression.Upgraded && progression.EffectiveScaleDate.HasValue
                ? progression.EffectiveScaleDate
                : profile?.ScaleDate is DateTime existingScaleDate
                    ? DateOnly.FromDateTime(existingScaleDate)
                    : null;
            var contractName = !string.IsNullOrWhiteSpace(profile?.InductionType)
                ? profile!.InductionType!.Trim()
                : scale?.ContractType;
            var contractId = contractTypes.FirstOrDefault(contract =>
                string.Equals(contract.Name, contractName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(contract.Code, contractName, StringComparison.OrdinalIgnoreCase))?.Id;

            designationByStaff.TryGetValue(employee.StaffId, out var designationId);
            shiftByStaff.TryGetValue(employee.StaffId, out var shiftCode);
            var packageAllowanceRefs = ParsePackageRefs(package?.AllowanceReference);
            var scaleAllowances = allowances.Where(x =>
                IsPayrollCashAllowance(x) &&
                IsAllowanceApplicable(x, designationId, shiftCode) &&
                IsAllowanceScaleApplicable(x, scale?.Id) &&
                IsPackageAllowanceAllowed(x, packageAllowanceRefs)).ToList();
            var hasScaleAllowanceConfiguration = scale != null && allowances.Any(x =>
                x.SalaryScaleId == scale.Id &&
                IsPayrollCashAllowance(x) &&
                !x.AllowanceCategory.Equals("SHIFT", StringComparison.OrdinalIgnoreCase) &&
                !x.AllowanceCategory.Equals("NIGHT", StringComparison.OrdinalIgnoreCase));

            var apptAllowance = Money(scaleAllowances
                .Where(x => x.AllowanceCategory.Equals("APPT", StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.CalculatedValue));
            var nightAllowance = Money(scaleAllowances
                .Where(x => IsAllowanceType(x, "NIGHT") || x.AllowanceCategory.Equals("NIGHT", StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.CalculatedValue));
            var shiftAllowance = Money(scaleAllowances
                .Where(x =>
                    x.AllowanceCategory.Equals("SHIFT", StringComparison.OrdinalIgnoreCase) &&
                    !IsAllowanceType(x, "NIGHT"))
                .Sum(x => x.CalculatedValue));
            var medicalAllowance = Money(scaleAllowances
                .Where(x => IsAllowanceType(x, "MED"))
                .Sum(x => x.CalculatedValue));
            var telephoneAllowance = Money(scaleAllowances
                .Where(x => IsAllowanceType(x, "TEL"))
                .Sum(x => x.CalculatedValue));
            var transportAllowance = Money(scaleAllowances
                .Where(x => IsAllowanceType(x, "TPT"))
                .Sum(x => x.CalculatedValue));
            var hasMedicalAllowanceConfiguration = scaleAllowances.Any(x => IsAllowanceType(x, "MED"));
            var hasTransportAllowanceConfiguration = scaleAllowances.Any(x => IsAllowanceType(x, "TPT"));
            var generalAllowance = Money(scaleAllowances
                .Where(x =>
                    !x.AllowanceCategory.Equals("APPT", StringComparison.OrdinalIgnoreCase) &&
                    !x.AllowanceCategory.Equals("SHIFT", StringComparison.OrdinalIgnoreCase) &&
                    !x.AllowanceCategory.Equals("NIGHT", StringComparison.OrdinalIgnoreCase) &&
                    !IsAllowanceType(x, "MED") &&
                    !IsAllowanceType(x, "TEL") &&
                    !IsAllowanceType(x, "TPT"))
                .Sum(x => x.CalculatedValue));
            // Legacy salary scales carry MED/TPT directly. A typed Allowance rule
            // overrides that component; otherwise keep the saved scale component.
            if (!hasMedicalAllowanceConfiguration)
                medicalAllowance = Money(scale?.MedicalAllowance ?? 0);
            if (!hasTransportAllowanceConfiguration)
                transportAllowance = Money(scale?.TravellingAllowance ?? 0);
            if (!hasScaleAllowanceConfiguration)
            {
                generalAllowance = Money(generalAllowance + (scale?.Other ?? 0));
            }
            // TADA is cash and joins Gross via General/Allowance total (Leave stays non-cash; package LeaveReference is metadata only).
            // PROFICIENCY is excluded from allowances — Final Assessment → AssessmentAmount is the payroll path.
            if (scale != null)
            {
                var packageTadaRefs = ParsePackageRefs(package?.TadaReference);
                var scaleTadas = tadas.Where(x => x.SalaryScaleId == scale.Id);
                if (packageTadaRefs.Count > 0)
                    scaleTadas = scaleTadas.Where(x => packageTadaRefs.Contains(x.TadaReference));
                generalAllowance = Money(generalAllowance + scaleTadas.Sum(x => x.CalculatedValue));
            }
            var allowanceAmount = Money(generalAllowance + apptAllowance + shiftAllowance +
                medicalAllowance + nightAllowance + telephoneAllowance + transportAllowance);

            var serviceMonths = ServiceMonthsCompleted(profile?.JoiningDate, periodEnd);
            var applicableBenefits = benefitRules.Where(rule => IsBenefitApplicable(
                rule,
                effectiveScaleName,
                basicSalary,
                contractName,
                employee.OrganizationId,
                organizationNodes,
                serviceMonths,
                periodStart,
                periodEnd));
            decimal employerBenefits = 0;
            decimal staffBenefits = 0;
            foreach (var rule in applicableBenefits)
            {
                var parameters = rule.Parameters.Where(parameter =>
                    serviceMonths >= parameter.MinimumService).ToList();
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
            // Gross deduction is posted with adjustment as a separate approval value.
            // Do not consume the already-adjusted UI NetDeduct here.
            var attendanceDeduction = attendanceRow?.GrossDeduction ?? 0;
            var adjustment = attendanceRow?.AdjustmentAmount ?? 0;
            var pendingDays = attendanceRow?.PendingReviewDays ?? 0;
            var salaryAdjustment = Money(Math.Max(0, profile?.SalaryAdjustment ?? 0));
            var taxableMonthly = basicSalary + allowanceAmount + salaryAdjustment + assessmentAmount + bonusAmount + overtime;
            var tax = CalculateMonthlyTax(taxableMonthly, taxSlabs);
            decimal employeeEobi = 0;
            decimal employerEobi = 0;
            if (eobiPeople.Contains(employee.PersonId))
            {
                var eobiShares = ResolveEobiShares(eobiBenefitRules, eobiSetting, effectiveScaleName, basicSalary);
                employeeEobi = eobiShares.Employee;
                employerEobi = eobiShares.Employer;
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
                ScaleDate = lineScaleDate,
                Scale = effectiveScaleName,
                ContractType = contractName,
                ContractId = contractId,
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
                MedicalAllowanceAmount = medicalAllowance,
                NightAllowanceAmount = nightAllowance,
                TelephoneAllowanceAmount = telephoneAllowance,
                TransportAllowanceAmount = transportAllowance,
                AllowanceAmount = allowanceAmount,
                SalaryAdjustment = salaryAdjustment,
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

    /// <summary>
    /// PROFICIENCY is paid only via Final Assessment → AssessmentAmount.
    /// COMMISSION and other typed allowances remain cash payroll components.
    /// </summary>
    public static bool IsPayrollCashAllowance(PayScaleAllowance allowance) =>
        !IsAllowanceType(allowance, "PROFICIENCY");

    private static (decimal Employee, decimal Employer) ResolveEobiShares(
        IReadOnlyList<PayrollBenefitRule> eobiRules,
        EobiSetting? eobiSetting,
        string? effectiveScaleName,
        decimal basicSalary)
    {
        var matching = eobiRules
            .Where(rule => ScaleCsvMatches(rule.Scale, effectiveScaleName))
            .ToList();
        var scaleSpecific = matching
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Scale))
            .ToList();
        var chosen = (scaleSpecific.Count > 0 ? scaleSpecific : matching)
            .OrderByDescending(rule => rule.Wef ?? rule.ValidFrom)
            .ThenByDescending(rule => rule.Id)
            .FirstOrDefault();

        if (chosen != null)
        {
            var parameter = chosen.Parameters.OrderByDescending(x => x.Id).FirstOrDefault();
            var employee = Money(parameter?.StaffShare ?? chosen.StaffShare);
            var employer = Money(parameter?.CompanyShare ?? chosen.CompanyShare);
            if (employee > 0 || employer > 0)
                return (employee, employer);
        }

        if (eobiSetting == null || basicSalary <= 0)
            return (0, 0);

        var wageBase = Math.Max(basicSalary, eobiSetting.MinimumWage);
        var contributionBase = eobiSetting.MaximumContributionBase > 0
            ? Math.Min(wageBase, eobiSetting.MaximumContributionBase)
            : wageBase;
        return (
            Money(contributionBase * eobiSetting.EmployeeRatePercentage / 100m),
            Money(contributionBase * eobiSetting.EmployerRatePercentage / 100m));
    }

    private static bool ScaleCsvMatches(string? scaleCsv, string? effectiveScaleName)
    {
        if (string.IsNullOrWhiteSpace(scaleCsv))
            return true;
        if (string.IsNullOrWhiteSpace(effectiveScaleName))
            return false;
        return scaleCsv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(name => name.Equals(effectiveScaleName.Trim(), StringComparison.OrdinalIgnoreCase));
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

    private static bool IsAllowanceType(PayScaleAllowance allowance, string code) =>
        string.Equals(allowance.AllowanceType?.Code, code, StringComparison.OrdinalIgnoreCase);

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
        string? effectiveScaleName,
        decimal effectiveCurrentPay,
        string? contractName,
        int? organizationId,
        IReadOnlyDictionary<int, OrganizationTree> organizationNodes,
        int serviceMonths,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        if (rule.ValidFrom.HasValue && rule.ValidFrom > periodEnd || rule.ValidTo.HasValue && rule.ValidTo < periodStart) return false;
        if (rule.Wef.HasValue && rule.Wef > periodEnd) return false;
        if (!ScaleCsvMatches(rule.Scale, effectiveScaleName))
            return false;

        if (rule.OrganizationScopes.Count > 0)
        {
            if (!organizationId.HasValue)
                return false;
            if (!rule.OrganizationScopes.Any(scope =>
                    IsOrganizationDescendant(organizationId.Value, scope.OrganizationId, organizationNodes)))
                return false;
        }
        else if (rule.OrganizationId.HasValue
                 && (!organizationId.HasValue
                     || !IsOrganizationDescendant(organizationId.Value, rule.OrganizationId.Value, organizationNodes)))
        {
            return false;
        }

        if (rule.ContractScopes.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(contractName))
                return false;
            if (!rule.ContractScopes.Any(scope =>
                    scope.ContractName.Equals(contractName.Trim(), StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        else if (!string.IsNullOrWhiteSpace(rule.Contract))
        {
            var allowedContracts = rule.Contract.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (string.IsNullOrWhiteSpace(contractName)
                || !allowedContracts.Any(name => name.Equals(contractName.Trim(), StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        // Min_Service is completed months from DOJ (e.g. 6 / 12 / 24).
        if (serviceMonths < rule.MinimumService) return false;
        if (rule.MinimumSalary > 0 && effectiveCurrentPay < rule.MinimumSalary)
            return false;

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

    /// <summary>Completed calendar months from DOJ through period end (day-adjusted).</summary>
    private static int ServiceMonthsCompleted(DateTime? joiningDate, DateOnly asOf)
    {
        if (!joiningDate.HasValue) return 0;
        var doj = DateOnly.FromDateTime(joiningDate.Value);
        if (doj > asOf) return 0;
        var months = (asOf.Year - doj.Year) * 12 + (asOf.Month - doj.Month);
        if (asOf.Day < doj.Day) months--;
        return Math.Max(0, months);
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
        // elapsed >= paid allows catch-up if an intervening payroll month was skipped.
        return elapsed >= 0
            && elapsed < installments
            && elapsed >= Math.Max(0, line.PaidInstallmentCount);
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
