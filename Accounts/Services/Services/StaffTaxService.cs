using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class StaffTaxService(
    ApplicationDbContext db,
    ITenantService tenant)
{
    public async Task<IReadOnlyList<PayrollStaffTax>> ListAsync(CancellationToken cancellationToken) =>
        await db.PayrollStaffTaxes.AsNoTracking()
            .OrderBy(x => x.FullName)
            .ThenByDescending(x => x.DateFrom)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StaffTaxCandidateDto>> CandidatesAsync(CancellationToken cancellationToken)
    {
        var employees = await db.StaffDirectoryRows.AsNoTracking()
            .Where(x => x.IsPersonActive)
            .OrderBy(x => x.FullName)
            .ToListAsync(cancellationToken);
        if (employees.Count == 0)
            return Array.Empty<StaffTaxCandidateDto>();

        var personIds = employees.Select(x => x.PersonId).Distinct().ToArray();
        var profiles = await db.PersonHrProfiles.AsNoTracking()
            .Where(x => personIds.Contains(x.PersonId))
            .ToDictionaryAsync(x => x.PersonId, cancellationToken);
        var scales = await db.SalaryScales.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var scaleByName = scales
            .Where(x => !string.IsNullOrWhiteSpace(x.ScaleName))
            .GroupBy(x => x.ScaleName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        return employees
            .GroupBy(x => x.PersonId)
            .Select(g => g.First())
            .Select(employee =>
            {
                profiles.TryGetValue(employee.PersonId, out var profile);
                SalaryScale? scale = null;
                if (!string.IsNullOrWhiteSpace(profile?.Scale))
                    scaleByName.TryGetValue(profile.Scale.Trim(), out scale);
                return new StaffTaxCandidateDto(
                    employee.PersonId,
                    employee.StaffId,
                    employee.EmployeeId,
                    employee.FullName,
                    employee.Department,
                    employee.Designation,
                    ResolveMonthlyPay(profile, scale));
            })
            .ToList();
    }

    public async Task<StaffTaxCalculationResult> CalculateAsync(StaffTaxCalculateRequest request, CancellationToken cancellationToken)
    {
        var (employee, monthlyPayHint) = await ResolveEmployeeAsync(request.PersonId, cancellationToken);
        var parameter = await ResolveActiveParameterAsync(cancellationToken);
        var monthlyPay = request.MonthlyPay > 0 ? Money(request.MonthlyPay) : monthlyPayHint;
        var units = request.TotMonth <= 0 ? 12 : request.TotMonth;
        var payMonths = request.PayMonth <= 0 ? units : request.PayMonth;
        var dedPercentage = request.DedPercentage > 0
            ? request.DedPercentage
            : parameter?.DedPercentage > 0 ? parameter.DedPercentage : 100m;
        var extra = Money(Math.Max(0, request.ExtraAmount));
        var adjustment = Money(request.TaxAdjustment);

        var totalIncome = Money(monthlyPay * units);
        var incomePay = Money(totalIncome * dedPercentage / 100m);
        var taxableIncome = Money(incomePay + extra);
        var taxYear = ResolveTaxYear(request.DateFrom);
        var slabs = await LoadSlabsAsync(taxYear, cancellationToken);
        var grossTax = CalculateAnnualTax(taxableIncome, slabs);
        if (parameter is { MinTaxAmt: > 0 } && taxableIncome > 0 && grossTax < parameter.MinTaxAmt)
            grossTax = Money(parameter.MinTaxAmt);

        var netTax = Money(grossTax + adjustment);
        var monthlyTaxAmt = payMonths > 0 ? Money(netTax / payMonths) : 0;

        return new StaffTaxCalculationResult(
            employee.PersonId,
            employee.StaffId,
            employee.EmployeeId,
            employee.FullName,
            employee.Department,
            employee.Designation,
            request.DateFrom,
            request.DateTo,
            string.IsNullOrWhiteSpace(request.Frequency) ? "Monthly" : request.Frequency.Trim(),
            monthlyPay,
            units,
            incomePay,
            extra,
            taxableIncome,
            grossTax,
            adjustment,
            netTax,
            payMonths,
            monthlyTaxAmt,
            monthlyTaxAmt,
            dedPercentage,
            taxYear,
            parameter?.MinTaxAmt ?? 0);
    }

    public async Task<PayrollStaffTax> SaveAsync(StaffTaxSaveRequest request, CancellationToken cancellationToken)
    {
        if (request.DateTo < request.DateFrom)
            throw new InvalidOperationException("Date To must be on or after Date From.");

        var calc = await CalculateAsync(new StaffTaxCalculateRequest(
            request.PersonId,
            request.DateFrom,
            request.DateTo,
            request.Frequency,
            request.MonthlyPay,
            request.TotMonth,
            request.ExtraAmount,
            request.TaxAdjustment,
            request.PayMonth,
            request.DedPercentage), cancellationToken);

        var now = DateTime.UtcNow;
        PayrollStaffTax row;
        if (request.Id is > 0)
        {
            row = await db.PayrollStaffTaxes.SingleOrDefaultAsync(x => x.Id == request.Id.Value, cancellationToken)
                ?? throw new KeyNotFoundException("Staff tax row was not found.");
        }
        else
        {
            row = new PayrollStaffTax
            {
                TenantId = tenant.RequiredTenantId,
                CreatedOnUtc = now,
                TaxRef = await NextTaxRefAsync(cancellationToken)
            };
            db.PayrollStaffTaxes.Add(row);
        }

        row.PersonId = calc.PersonId;
        row.StaffId = calc.StaffGuid;
        row.StaffNumber = calc.StaffId;
        row.FullName = calc.FullName;
        row.Department = calc.Department;
        row.Designation = calc.Designation;
        row.DateFrom = request.DateFrom;
        row.DateTo = request.DateTo;
        row.Frequency = calc.Frequency;
        row.MonthlyPay = calc.MonthlyPay;
        row.TotMonth = calc.TotMonth;
        row.IncomePay = calc.IncomePay;
        row.ExtraAmount = calc.ExtraAmount;
        row.TaxableIncome = calc.TaxableIncome;
        row.TaxAmount = calc.TaxAmount;
        row.TaxAdjustment = calc.TaxAdjustment;
        row.NetTax = calc.NetTax;
        row.PayMonth = calc.PayMonth;
        row.MonthlyTaxAmt = calc.MonthlyTaxAmt;
        row.MonthlyNetTax = calc.MonthlyNetTax;
        row.DedPercentage = calc.DedPercentage;
        row.IsActive = request.IsActive;
        row.UpdatedOnUtc = now;

        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    public async Task<PayrollStaffTax> PatchAsync(long id, StaffTaxPatchRequest request, CancellationToken cancellationToken)
    {
        var row = await db.PayrollStaffTaxes.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Staff tax row was not found.");

        if (request.NetTax.HasValue)
        {
            row.NetTax = Money(request.NetTax.Value);
            row.MonthlyTaxAmt = row.PayMonth > 0 ? Money(row.NetTax / row.PayMonth) : 0;
            row.MonthlyNetTax = row.MonthlyTaxAmt;
        }

        if (request.TaxAdjustment.HasValue)
            row.TaxAdjustment = Money(request.TaxAdjustment.Value);

        if (request.MonthlyTaxAmt.HasValue)
        {
            row.MonthlyTaxAmt = Money(request.MonthlyTaxAmt.Value);
            row.MonthlyNetTax = row.MonthlyTaxAmt;
        }

        if (request.IsActive.HasValue)
            row.IsActive = request.IsActive.Value;

        row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var row = await db.PayrollStaffTaxes.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Staff tax row was not found.");
        db.PayrollStaffTaxes.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollTaxParameter>> ListParametersAsync(CancellationToken cancellationToken) =>
        await db.PayrollTaxParameters.AsNoTracking()
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<PayrollTaxParameter> SaveParameterAsync(int? id, decimal minTaxAmt, decimal dedPercentage, bool isActive, CancellationToken cancellationToken)
    {
        if (minTaxAmt < 0 || dedPercentage is < 0 or > 100)
            throw new InvalidOperationException("Enter a valid minimum tax amount and deductible percentage (0-100).");

        PayrollTaxParameter row;
        if (id is > 0)
        {
            row = await db.PayrollTaxParameters.SingleOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
                ?? throw new KeyNotFoundException("Tax parameter was not found.");
        }
        else
        {
            row = new PayrollTaxParameter
            {
                TenantId = tenant.RequiredTenantId,
                CreatedOnUtc = DateTime.UtcNow
            };
            db.PayrollTaxParameters.Add(row);
        }

        if (isActive)
        {
            var others = await db.PayrollTaxParameters
                .Where(x => x.IsActive && (id == null || x.Id != id.Value))
                .ToListAsync(cancellationToken);
            foreach (var other in others)
            {
                other.IsActive = false;
                other.UpdatedOnUtc = DateTime.UtcNow;
            }
        }

        row.MinTaxAmt = Money(minTaxAmt);
        row.DedPercentage = dedPercentage;
        row.IsActive = isActive;
        row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    public async Task DeleteParameterAsync(int id, CancellationToken cancellationToken)
    {
        var row = await db.PayrollTaxParameters.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Tax parameter was not found.");
        db.PayrollTaxParameters.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(StaffDirectoryRow Employee, decimal MonthlyPay)> ResolveEmployeeAsync(
        Guid personId,
        CancellationToken cancellationToken)
    {
        var employee = await db.StaffDirectoryRows.AsNoTracking()
            .Where(x => x.PersonId == personId && x.IsPersonActive)
            .OrderBy(x => x.FullName)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Selected staff member was not found in the active directory.");

        var profile = await db.PersonHrProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PersonId == personId, cancellationToken);
        SalaryScale? scale = null;
        if (!string.IsNullOrWhiteSpace(profile?.Scale))
        {
            scale = await db.SalaryScales.AsNoTracking()
                .FirstOrDefaultAsync(x => x.IsActive && x.ScaleName == profile.Scale, cancellationToken);
        }

        return (employee, ResolveMonthlyPay(profile, scale));
    }

    private async Task<PayrollTaxParameter?> ResolveActiveParameterAsync(CancellationToken cancellationToken) =>
        await db.PayrollTaxParameters.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<List<PayrollTaxSlab>> LoadSlabsAsync(string taxYear, CancellationToken cancellationToken)
    {
        var exact = await db.PayrollTaxSlabs.AsNoTracking()
            .Where(x => x.IsActive && x.TaxYear == taxYear)
            .OrderBy(x => x.FromAmount)
            .ToListAsync(cancellationToken);
        if (exact.Count > 0)
            return exact;

        return await db.PayrollTaxSlabs.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.TaxYear)
            .ThenBy(x => x.FromAmount)
            .ToListAsync(cancellationToken);
    }

    private async Task<string> NextTaxRefAsync(CancellationToken cancellationToken)
    {
        var existing = await db.PayrollStaffTaxes.AsNoTracking()
            .Select(x => x.TaxRef)
            .ToListAsync(cancellationToken);
        var next = 1;
        foreach (var taxRef in existing)
        {
            if (taxRef.StartsWith("TX-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(taxRef[3..], out var number)
                && number >= next)
            {
                next = number + 1;
            }
        }

        return $"TX-{next}";
    }

    private static decimal ResolveMonthlyPay(PersonHrProfile? profile, SalaryScale? scale)
    {
        var current = profile?.CurrentPay is > 0 ? profile.CurrentPay.Value
            : scale?.CurrentPay is > 0 ? scale.CurrentPay
            : 0;
        var basic = profile?.BasicSalary is > 0 ? profile.BasicSalary.Value
            : scale?.BasicSalary ?? 0;
        return Money(current > 0 ? current : basic);
    }

    private static string ResolveTaxYear(DateOnly from)
    {
        var year = from.Year;
        return from.Month >= 7 ? $"{year}-{year + 1}" : $"{year - 1}-{year}";
    }

    private static decimal CalculateAnnualTax(decimal annualTaxable, IReadOnlyList<PayrollTaxSlab> slabs)
    {
        if (annualTaxable <= 0 || slabs.Count == 0)
            return 0;

        var ordered = slabs.OrderBy(x => x.FromAmount).ToList();
        var slab = ordered.LastOrDefault(x =>
            annualTaxable >= x.FromAmount && (!x.ToAmount.HasValue || annualTaxable <= x.ToAmount.Value));
        if (slab == null)
            return 0;

        var excess = Math.Max(0, annualTaxable - slab.FromAmount);
        return Money(slab.FixedTaxAmount + excess * slab.RatePercentage / 100m);
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed record StaffTaxCandidateDto(
    Guid PersonId,
    Guid StaffGuid,
    string StaffId,
    string FullName,
    string Department,
    string Designation,
    decimal MonthlyPay);

public sealed record StaffTaxCalculationResult(
    Guid PersonId,
    Guid StaffGuid,
    string StaffId,
    string FullName,
    string Department,
    string Designation,
    DateOnly DateFrom,
    DateOnly DateTo,
    string Frequency,
    decimal MonthlyPay,
    int TotMonth,
    decimal IncomePay,
    decimal ExtraAmount,
    decimal TaxableIncome,
    decimal TaxAmount,
    decimal TaxAdjustment,
    decimal NetTax,
    int PayMonth,
    decimal MonthlyTaxAmt,
    decimal MonthlyNetTax,
    decimal DedPercentage,
    string TaxYear,
    decimal MinTaxAmt);

public sealed record StaffTaxCalculateRequest(
    Guid PersonId,
    DateOnly DateFrom,
    DateOnly DateTo,
    string? Frequency,
    decimal MonthlyPay,
    int TotMonth,
    decimal ExtraAmount,
    decimal TaxAdjustment,
    int PayMonth,
    decimal DedPercentage);

public sealed record StaffTaxSaveRequest(
    long? Id,
    Guid PersonId,
    DateOnly DateFrom,
    DateOnly DateTo,
    string? Frequency,
    decimal MonthlyPay,
    int TotMonth,
    decimal ExtraAmount,
    decimal TaxAdjustment,
    int PayMonth,
    decimal DedPercentage,
    bool IsActive);

public sealed record StaffTaxPatchRequest(decimal? NetTax, decimal? TaxAdjustment, decimal? MonthlyTaxAmt, bool? IsActive);
