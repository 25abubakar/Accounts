using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class StaffMonthlyEobiService(
    ApplicationDbContext db,
    ITenantService tenant)
{
    public async Task<IReadOnlyList<StaffMonthlyEobi>> ListAsync(
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        ValidatePeriod(year, month);
        return await db.StaffMonthlyEobis.AsNoTracking()
            .Where(row => row.Year == year && row.Month == month)
            .OrderBy(row => row.FullName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StaffMonthlyEobi>> CreateOrRefreshAsync(
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        ValidatePeriod(year, month);
        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        var eobiSetting = await db.EobiSettings.AsNoTracking()
            .Where(x => x.IsActive
                && x.EffectiveFrom <= periodEnd
                && (x.EffectiveTo == null || x.EffectiveTo >= periodStart))
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No active EOBI setting covers this month. Configure EOBI Settings first.");

        var eligibilities = await db.EobiEligibilities.AsNoTracking()
            .Where(x => x.IsEligible
                && x.EffectiveFrom <= periodEnd
                && (x.EffectiveTo == null || x.EffectiveTo >= periodStart))
            .ToListAsync(cancellationToken);
        if (eligibilities.Count == 0)
            throw new InvalidOperationException("No eligible employees for this month. Maintain EOBI Eligibility List first.");

        var eligiblePersonIds = eligibilities.Select(x => x.PersonId).Distinct().ToArray();
        var eligibilityByPerson = eligibilities
            .GroupBy(x => x.PersonId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.EffectiveFrom).First());

        var employees = await db.StaffDirectoryRows.AsNoTracking()
            .Where(x => x.IsPersonActive && eligiblePersonIds.Contains(x.PersonId))
            .OrderBy(x => x.FullName)
            .ToListAsync(cancellationToken);
        if (employees.Count == 0)
            throw new InvalidOperationException("Eligible employees were not found in the active staff directory.");

        var personIds = employees.Select(x => x.PersonId).Distinct().ToArray();
        var profiles = await db.PersonHrProfiles.AsNoTracking()
            .Where(x => personIds.Contains(x.PersonId))
            .ToDictionaryAsync(x => x.PersonId, cancellationToken);
        var scales = await db.SalaryScales.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var scaleByName = scales
            .Where(x => !string.IsNullOrWhiteSpace(x.ScaleName))
            .GroupBy(x => x.ScaleName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var existing = await db.StaffMonthlyEobis
            .Where(row => row.Year == year && row.Month == month && personIds.Contains(row.PersonId))
            .ToListAsync(cancellationToken);
        var existingByPerson = existing
            .GroupBy(row => row.PersonId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

        var now = DateTime.UtcNow;
        var createdOrUpdated = 0;
        var skippedPaid = 0;

        foreach (var employee in employees
                     .GroupBy(x => x.PersonId)
                     .Select(g => g.First()))
        {
            profiles.TryGetValue(employee.PersonId, out var profile);
            SalaryScale? scale = null;
            if (!string.IsNullOrWhiteSpace(profile?.Scale))
                scaleByName.TryGetValue(profile.Scale.Trim(), out scale);

            var scaleBasic = Money(profile?.BasicSalary is > 0 ? profile.BasicSalary.Value : scale?.BasicSalary ?? 0);
            var currentPay = Money(profile?.CurrentPay is > 0 ? profile.CurrentPay.Value
                : scale?.CurrentPay is > 0 ? scale.CurrentPay
                : scaleBasic);
            var basicSalary = Money(currentPay > 0 ? currentPay : scaleBasic);

            var wageBase = basicSalary <= 0 ? 0 : Math.Max(basicSalary, eobiSetting.MinimumWage);
            var contributionBase = eobiSetting.MaximumContributionBase > 0
                ? Math.Min(wageBase, eobiSetting.MaximumContributionBase)
                : wageBase;
            var staffShare = Money(contributionBase * eobiSetting.EmployeeRatePercentage / 100m);
            var companyShare = Money(contributionBase * eobiSetting.EmployerRatePercentage / 100m);
            var total = Money(staffShare + companyShare);

            eligibilityByPerson.TryGetValue(employee.PersonId, out var eligibility);
            var eobiRef = string.IsNullOrWhiteSpace(eligibility?.EobiNumber)
                ? $"EOBI-{year}{month:00}-{employee.EmployeeId}"
                : eligibility!.EobiNumber!.Trim();

            var remarks = basicSalary <= 0
                ? "Review: missing salary configuration (current/basic pay is zero)."
                : null;

            if (existingByPerson.TryGetValue(employee.PersonId, out var row))
            {
                if (row.IsPaid)
                {
                    skippedPaid++;
                    continue;
                }

                row.StaffId = employee.StaffId;
                row.StaffNumber = employee.EmployeeId;
                row.FullName = employee.FullName;
                row.Department = employee.Department;
                row.Designation = employee.Designation;
                row.DateOfJoining = profile?.JoiningDate is DateTime joined
                    ? DateOnly.FromDateTime(joined)
                    : null;
                if (string.IsNullOrWhiteSpace(row.EobiRef))
                    row.EobiRef = eobiRef;
                row.SalaryBase = basicSalary;
                row.CompanyShare = companyShare;
                row.StaffShare = staffShare;
                row.TotalAmount = total;
                row.Remarks = remarks;
                row.UpdatedOnUtc = now;
                createdOrUpdated++;
                continue;
            }

            db.StaffMonthlyEobis.Add(new StaffMonthlyEobi
            {
                TenantId = tenant.RequiredTenantId,
                PersonId = employee.PersonId,
                StaffId = employee.StaffId,
                StaffNumber = employee.EmployeeId,
                FullName = employee.FullName,
                Department = employee.Department,
                Designation = employee.Designation,
                DateOfJoining = profile?.JoiningDate is DateTime doj
                    ? DateOnly.FromDateTime(doj)
                    : null,
                EobiRef = eobiRef,
                SalaryBase = basicSalary,
                CompanyShare = companyShare,
                StaffShare = staffShare,
                TotalAmount = total,
                Month = month,
                Year = year,
                Remarks = remarks,
                IsApproved = false,
                IsPaid = false,
                CreatedOnUtc = now
            });
            createdOrUpdated++;
        }

        if (createdOrUpdated == 0 && skippedPaid > 0)
            throw new InvalidOperationException("All EOBI rows for this month are already paid and cannot be regenerated.");

        if (createdOrUpdated == 0)
            throw new InvalidOperationException("No EOBI rows were created for this month.");

        await db.SaveChangesAsync(cancellationToken);
        return await ListAsync(year, month, cancellationToken);
    }

    public async Task<StaffMonthlyEobi> UpdateAsync(
        long id,
        string? eobiRef,
        CancellationToken cancellationToken)
    {
        var row = await db.StaffMonthlyEobis.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Staff monthly EOBI row was not found.");
        if (row.IsPaid)
            throw new InvalidOperationException("Paid EOBI rows cannot be edited.");

        row.EobiRef = string.IsNullOrWhiteSpace(eobiRef) ? null : eobiRef.Trim();
        row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    private static void ValidatePeriod(int year, int month)
    {
        if (year is < 2000 or > 2200 || month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "Enter a valid EOBI month and year.");
    }

    private static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
