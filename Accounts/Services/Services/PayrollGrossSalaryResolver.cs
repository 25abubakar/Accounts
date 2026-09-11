using Accounts.Data;
using Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

/// <summary>
/// Resolves payroll-aligned monthly gross for attendance deduction rates.
/// Gross = CurrentPay (basic + ScaleDate increments) + allowances/TADA + approved bonus installment.
/// Does not include overtime or attendance adjustment (those are deduction outputs).
/// </summary>
public sealed class PayrollGrossSalaryResolver(ApplicationDbContext db)
{
    public sealed record SalaryBasis(
        decimal CurrentPay,
        decimal AllowanceAmount,
        decimal BonusAmount,
        decimal GrossSalary);

    public async Task<IReadOnlyDictionary<Guid, SalaryBasis>> ResolveAsync(
        int year,
        int month,
        IReadOnlyCollection<Guid> personIds,
        CancellationToken cancellationToken)
    {
        if (personIds.Count == 0)
            return new Dictionary<Guid, SalaryBasis>();

        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var idSet = personIds.ToHashSet();

        var employees = await db.StaffDirectoryRows.AsNoTracking()
            .Where(x => idSet.Contains(x.PersonId) && x.IsPersonActive)
            .ToListAsync(cancellationToken);
        if (employees.Count == 0)
            return idSet.ToDictionary(id => id, _ => new SalaryBasis(0, 0, 0, 0));

        var distinctPeople = employees
            .GroupBy(x => x.PersonId)
            .Select(g => g.First())
            .ToList();
        var staffIds = distinctPeople.Select(x => x.StaffId).Distinct().ToArray();
        var resolvedPersonIds = distinctPeople.Select(x => x.PersonId).ToArray();

        var profiles = await db.PersonHrProfiles.AsNoTracking()
            .Where(x => resolvedPersonIds.Contains(x.PersonId))
            .ToDictionaryAsync(x => x.PersonId, cancellationToken);
        var packages = await db.SalaryPackages.AsNoTracking()
            .Where(x => x.IsActive)
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
        var bonusLines = await db.PayrollBonusLines.AsNoTracking().Include(x => x.BonusRun)
            .Where(x => x.IsApproved && !x.IsInactive && !x.IsPaid
                && x.BonusRun != null && x.BonusRun.Status == "Approved"
                && resolvedPersonIds.Contains(x.PersonId))
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, SalaryBasis>();
        foreach (var employee in distinctPeople)
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
            var pay = Money(currentPay > 0 ? currentPay : scaleBasic);

            var bonusAmount = Money(bonusLines
                .Where(x => x.PersonId == employee.PersonId && IsBonusInstallmentDue(x, year, month))
                .Sum(x => x.InstallmentAmount > 0 ? x.InstallmentAmount : x.TotalBonus));

            var gross = Money(pay + allowanceAmount + bonusAmount);
            result[employee.PersonId] = new SalaryBasis(pay, allowanceAmount, bonusAmount, gross);
        }

        foreach (var missing in idSet.Where(id => !result.ContainsKey(id)))
            result[missing] = new SalaryBasis(0, 0, 0, 0);

        return result;
    }

    private static bool IsBonusInstallmentDue(PayrollBonusLine line, int year, int month)
    {
        var installments = Math.Max(1, line.Installment);
        var elapsed = (year - line.Year) * 12 + month - line.Month;
        return elapsed >= 0
            && elapsed < installments
            && elapsed == Math.Max(0, line.PaidInstallmentCount);
    }

    private static bool IsAllowanceApplicable(PayScaleAllowance allowance, int? designationId, string? shiftCode)
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

    private static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
