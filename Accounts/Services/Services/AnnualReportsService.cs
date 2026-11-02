using Accounts.Data;
using Accounts.Models;
using Accounts.Models.SpListRows;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public interface IAnnualReportsService
{
    Task<IReadOnlyList<AnnualReportFilterListRow>> ListFiltersAsync(int tenantId, CancellationToken ct = default);
    Task SaveFiltersAsync(int tenantId, int reportTypeId, IReadOnlyList<int> categoryIds, string? userId, CancellationToken ct = default);
    Task SetFilterIncludeAsync(int tenantId, int filterId, bool isInclude, string? userId, CancellationToken ct = default);
    Task DeleteFilterAsync(int tenantId, int filterId, CancellationToken ct = default);
    Task<AnnualCategoryMatrixDto> GetCategoryMonthMatrixAsync(int tenantId, string reportTypeCode, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<AnnualMonthWiseDto>> ListMonthWiseAsync(int tenantId, string reportTypeCode, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<AnnualCategoryWiseFiscalDto>> ListCategoryWiseAsync(int tenantId, string reportTypeCode, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<string> SaveMonthWiseAsync(int tenantId, string reportTypeCode, DateOnly from, DateOnly to, string? remarks, IReadOnlyList<AnnualMonthSaveLineDto> lines, string? userId, CancellationToken ct = default);
    Task SetApprovedAsync(int tenantId, long headerId, bool approved, string? userId, CancellationToken ct = default);
    Task<bool> VerifyUnlockAsync(int tenantId, long headerId, string code, string? userId, CancellationToken ct = default);
    Task<IReadOnlyList<object>> ListTypesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<object>> ListCategoriesAsync(int tenantId, CancellationToken ct = default);
}

public sealed class AnnualCategoryMatrixDto
{
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    public List<AnnualMonthHeaderDto> Months { get; set; } = [];
    public List<AnnualCategoryRowDto> Categories { get; set; } = [];
    public Dictionary<int, decimal> MonthTotals { get; set; } = new();
    public decimal GrandTotal { get; set; }
}

public sealed class AnnualMonthHeaderDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
}

public sealed class AnnualCategoryRowDto
{
    public int CategoryId { get; set; }
    public string Cat_Name { get; set; } = string.Empty;
    public Dictionary<int, decimal> MonthlyData { get; set; } = new();
    public decimal Total { get; set; }
}

public sealed class AnnualCategoryWiseFiscalDto
{
    public long HeaderId { get; set; }
    public bool IsSaved { get; set; } = true;
    public string FiscalYear { get; set; } = string.Empty;
    public Dictionary<string, decimal> CategoryAmounts { get; set; } = new();
    public decimal Total { get; set; }
}

public sealed class AnnualMonthWiseDto
{
    public long HeaderId { get; set; }
    public bool IsSaved { get; set; } = true;
    public string? FiscalYear { get; set; }
    public string? Remarks { get; set; }
    public bool IsApproved { get; set; }
    public decimal Jul { get; set; }
    public decimal Aug { get; set; }
    public decimal Sep { get; set; }
    public decimal Oct { get; set; }
    public decimal Nov { get; set; }
    public decimal Dec { get; set; }
    public decimal Jan { get; set; }
    public decimal Feb { get; set; }
    public decimal Mar { get; set; }
    public decimal Apr { get; set; }
    public decimal May { get; set; }
    public decimal Jun { get; set; }
    public decimal Total { get; set; }
}

public sealed class AnnualMonthSaveLineDto
{
    public int CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
}

public sealed class AnnualReportsService(ApplicationDbContext db) : IAnnualReportsService
{
    private static readonly string[] MonthNames =
    [
        "", "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    ];

    public async Task<IReadOnlyList<AnnualReportFilterListRow>> ListFiltersAsync(int tenantId, CancellationToken ct = default) =>
        await SpListQuery.ExecAsync<AnnualReportFilterListRow>(
            db, "EXEC dbo.usp_AnnualReports_FilterList @TenantId", ct, SpListQuery.TenantId(tenantId));

    public async Task SaveFiltersAsync(int tenantId, int reportTypeId, IReadOnlyList<int> categoryIds, string? userId, CancellationToken ct = default)
    {
        var typeExists = await db.AnnualReportTypes.AsNoTracking()
            .AnyAsync(x => x.Id == reportTypeId && x.IsActive && (x.TenantId == null || x.TenantId == tenantId), ct);
        if (!typeExists) throw new InvalidOperationException("Invalid report type.");

        var validCategoryIds = await db.AccountsCategories.AsNoTracking()
            .Where(x => x.IsActive && categoryIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(ct);

        foreach (var categoryId in validCategoryIds.Distinct())
        {
            var existing = await db.AnnualReportFilters
                .FirstOrDefaultAsync(x => x.ReportTypeId == reportTypeId && x.CategoryId == categoryId, ct);
            if (existing == null)
            {
                db.AnnualReportFilters.Add(new AnnualReportFilter
                {
                    TenantId = tenantId,
                    ReportTypeId = reportTypeId,
                    CategoryId = categoryId,
                    IsInclude = true,
                    CreatedByUserId = userId,
                    CreatedOnUtc = DateTime.UtcNow
                });
            }
            else
            {
                existing.IsInclude = true;
                existing.UpdatedByUserId = userId;
                existing.UpdatedOnUtc = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task SetFilterIncludeAsync(int tenantId, int filterId, bool isInclude, string? userId, CancellationToken ct = default)
    {
        var row = await db.AnnualReportFilters.FirstOrDefaultAsync(x => x.Id == filterId, ct)
            ?? throw new InvalidOperationException("Filter row was not found.");
        row.IsInclude = isInclude;
        row.UpdatedByUserId = userId;
        row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteFilterAsync(int tenantId, int filterId, CancellationToken ct = default)
    {
        var row = await db.AnnualReportFilters.FirstOrDefaultAsync(x => x.Id == filterId, ct);
        if (row == null) return;
        db.AnnualReportFilters.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AnnualCategoryMatrixDto> GetCategoryMonthMatrixAsync(
        int tenantId, string reportTypeCode, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var code = NormalizeKind(reportTypeCode);
        var amountRows = await SpListQuery.ExecAsync<AnnualCategoryMonthAmountRow>(
            db,
            "EXEC dbo.usp_AnnualReports_CategoryMonthMatrix @TenantId, @ReportTypeCode, @DateFrom, @DateTo",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.NVarChar("@ReportTypeCode", code),
            SpListQuery.Date("@DateFrom", from),
            SpListQuery.Date("@DateTo", to));

        var months = new List<AnnualMonthHeaderDto>();
        var cursor = new DateOnly(from.Year, from.Month, 1);
        var end = new DateOnly(to.Year, to.Month, 1);
        var monthId = 1;
        while (cursor <= end)
        {
            months.Add(new AnnualMonthHeaderDto
            {
                Id = monthId++,
                Name = MonthNames[cursor.Month].ToUpperInvariant(),
                Year = cursor.Year,
                Month = cursor.Month
            });
            cursor = cursor.AddMonths(1);
        }

        var categories = amountRows
            .GroupBy(x => new { x.CategoryId, Name = x.Cat_Name ?? $"Category {x.CategoryId}" })
            .Select(g =>
            {
                var monthly = new Dictionary<int, decimal>();
                foreach (var month in months)
                {
                    var amt = g.Where(r => r.CalendarYear == month.Year && r.CalendarMonth == month.Month)
                        .Sum(r => r.Amount);
                    monthly[month.Id] = decimal.Round(amt, 2, MidpointRounding.AwayFromZero);
                }
                return new AnnualCategoryRowDto
                {
                    CategoryId = g.Key.CategoryId,
                    Cat_Name = g.Key.Name,
                    MonthlyData = monthly,
                    Total = monthly.Values.Sum()
                };
            })
            .OrderBy(x => x.Cat_Name)
            .ToList();

        // Show included categories even when all amounts are zero
        if (categories.Count == 0)
        {
            var included = await (
                from f in db.AnnualReportFilters.IgnoreQueryFilters().AsNoTracking()
                join t in db.AnnualReportTypes.IgnoreQueryFilters().AsNoTracking() on f.ReportTypeId equals t.Id
                join c in db.AccountsCategories.IgnoreQueryFilters().AsNoTracking() on f.CategoryId equals c.Id
                where f.TenantId == tenantId
                    && (t.TenantId == null || t.TenantId == tenantId)
                    && c.TenantId == tenantId
                    && f.IsInclude && t.Code == code && c.IsActive
                orderby c.Name
                select new { c.Id, c.Name }
            ).ToListAsync(ct);

            categories = included.Select(c => new AnnualCategoryRowDto
            {
                CategoryId = c.Id,
                Cat_Name = c.Name,
                MonthlyData = months.ToDictionary(m => m.Id, _ => 0m),
                Total = 0
            }).ToList();
        }

        var monthTotals = months.ToDictionary(
            m => m.Id,
            m => decimal.Round(categories.Sum(c => c.MonthlyData.GetValueOrDefault(m.Id)), 2, MidpointRounding.AwayFromZero));

        return new AnnualCategoryMatrixDto
        {
            DateFrom = from,
            DateTo = to,
            Months = months,
            Categories = categories,
            MonthTotals = monthTotals,
            GrandTotal = monthTotals.Values.Sum()
        };
    }

    public async Task<IReadOnlyList<AnnualMonthWiseDto>> ListMonthWiseAsync(
        int tenantId, string reportTypeCode, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var code = NormalizeKind(reportTypeCode);
        var savedRows = await SpListQuery.ExecAsync<AnnualMonthWiseListRow>(
            db,
            "EXEC dbo.usp_AnnualReports_MonthWiseList @TenantId, @ReportTypeCode",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.NVarChar("@ReportTypeCode", code));
        var saved = savedRows.Select(ToMonthWiseDto).ToList();

        var fiscalYear = BuildFiscalYearLabel(from, to);
        if (saved.Any(x => string.Equals(x.FiscalYear, fiscalYear, StringComparison.OrdinalIgnoreCase)))
            return saved;

        var matrix = await GetCategoryMonthMatrixAsync(tenantId, code, from, to, ct);
        if (matrix.Categories.Count == 0)
            return saved;

        var live = new AnnualMonthWiseDto
        {
            HeaderId = 0,
            IsSaved = false,
            FiscalYear = fiscalYear,
            Jul = MatrixMonthTotal(matrix, 7),
            Aug = MatrixMonthTotal(matrix, 8),
            Sep = MatrixMonthTotal(matrix, 9),
            Oct = MatrixMonthTotal(matrix, 10),
            Nov = MatrixMonthTotal(matrix, 11),
            Dec = MatrixMonthTotal(matrix, 12),
            Jan = MatrixMonthTotal(matrix, 1),
            Feb = MatrixMonthTotal(matrix, 2),
            Mar = MatrixMonthTotal(matrix, 3),
            Apr = MatrixMonthTotal(matrix, 4),
            May = MatrixMonthTotal(matrix, 5),
            Jun = MatrixMonthTotal(matrix, 6),
            Total = matrix.GrandTotal
        };
        return [live, .. saved];
    }

    public async Task<IReadOnlyList<AnnualCategoryWiseFiscalDto>> ListCategoryWiseAsync(
        int tenantId, string reportTypeCode, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var code = NormalizeKind(reportTypeCode);
        var rows = await SpListQuery.ExecAsync<AnnualCategoryWiseAmountRow>(
            db,
            "EXEC dbo.usp_AnnualReports_CategoryWiseList @TenantId, @ReportTypeCode",
            ct,
            SpListQuery.TenantId(tenantId),
            SpListQuery.NVarChar("@ReportTypeCode", code));

        var saved = rows
            .GroupBy(x => new { x.HeaderId, FiscalYear = x.FiscalYear ?? "" })
            .Select(g =>
            {
                var amounts = g.GroupBy(x => x.Cat_Name ?? $"Category {x.CategoryId}")
                    .ToDictionary(x => x.Key, x => x.Sum(v => v.Amount));
                return new AnnualCategoryWiseFiscalDto
                {
                    HeaderId = g.Key.HeaderId,
                    IsSaved = true,
                    FiscalYear = g.Key.FiscalYear,
                    CategoryAmounts = amounts,
                    Total = amounts.Values.Sum()
                };
            })
            .OrderByDescending(x => x.FiscalYear)
            .ToList();

        var fiscalYear = BuildFiscalYearLabel(from, to);
        if (saved.Any(x => string.Equals(x.FiscalYear, fiscalYear, StringComparison.OrdinalIgnoreCase)))
            return saved;

        var matrix = await GetCategoryMonthMatrixAsync(tenantId, code, from, to, ct);
        if (matrix.Categories.Count == 0)
            return saved;

        saved.Insert(0, new AnnualCategoryWiseFiscalDto
        {
            HeaderId = 0,
            IsSaved = false,
            FiscalYear = fiscalYear,
            CategoryAmounts = matrix.Categories.ToDictionary(x => x.Cat_Name, x => x.Total),
            Total = matrix.GrandTotal
        });
        return saved;
    }

    public async Task<string> SaveMonthWiseAsync(
        int tenantId,
        string reportTypeCode,
        DateOnly from,
        DateOnly to,
        string? remarks,
        IReadOnlyList<AnnualMonthSaveLineDto> lines,
        string? userId,
        CancellationToken ct = default)
    {
        var code = NormalizeKind(reportTypeCode);
        var fiscalYear = BuildFiscalYearLabel(from, to);

        // First match exact DateFrom & DateTo (aligned with LTAccount), then fallback to FiscalYear
        var header = await db.AnnualReportHeaders
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ReportTypeCode == code && x.DateFrom == from && x.DateTo == to, ct)
            ?? await db.AnnualReportHeaders
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ReportTypeCode == code && x.FiscalYear == fiscalYear, ct);
        if (header != null && header.IsApproved)
            throw new InvalidOperationException("Please UnLock this report before updating.");

        if (header == null)
        {
            header = new AnnualReportHeader
            {
                TenantId = tenantId,
                ReportTypeCode = code,
                FiscalYear = fiscalYear,
                DateFrom = from,
                DateTo = to,
                Remarks = remarks,
                CreatedByUserId = userId,
                CreatedOnUtc = DateTime.UtcNow
            };
            db.AnnualReportHeaders.Add(header);
            await db.SaveChangesAsync(ct);

            foreach (var line in lines.Where(x => x.CategoryId > 0 && x.Month is >= 1 and <= 12))
            {
                db.AnnualReportLines.Add(new AnnualReportLine
                {
                    HeaderId = header.Id,
                    CategoryId = line.CategoryId,
                    CategoryName = line.CategoryName,
                    CalendarYear = line.Year > 0 ? line.Year : from.Year,
                    CalendarMonth = line.Month,
                    Amount = decimal.Round(line.Amount, 2, MidpointRounding.AwayFromZero)
                });
            }
            await db.SaveChangesAsync(ct);
            return "Saved";
        }

        header.DateFrom = from;
        header.DateTo = to;
        header.Remarks = remarks;
        header.UpdatedByUserId = userId;
        header.UpdatedOnUtc = DateTime.UtcNow;

        var oldLines = await db.AnnualReportLines.Where(x => x.HeaderId == header.Id).ToListAsync(ct);
        db.AnnualReportLines.RemoveRange(oldLines);
        await db.SaveChangesAsync(ct);

        foreach (var line in lines.Where(x => x.CategoryId > 0 && x.Month is >= 1 and <= 12))
        {
            db.AnnualReportLines.Add(new AnnualReportLine
            {
                HeaderId = header.Id,
                CategoryId = line.CategoryId,
                CategoryName = line.CategoryName,
                CalendarYear = line.Year > 0 ? line.Year : from.Year,
                CalendarMonth = line.Month,
                Amount = decimal.Round(line.Amount, 2, MidpointRounding.AwayFromZero)
            });
        }
        await db.SaveChangesAsync(ct);
        return "Updated Successfully";
    }

    public async Task SetApprovedAsync(int tenantId, long headerId, bool approved, string? userId, CancellationToken ct = default)
    {
        var header = await db.AnnualReportHeaders.FirstOrDefaultAsync(x => x.Id == headerId, ct)
            ?? throw new InvalidOperationException("Report was not found.");
        header.IsApproved = approved;
        header.ApprovedByUserId = approved ? userId : null;
        header.ApprovedOnUtc = approved ? DateTime.UtcNow : null;
        header.UpdatedByUserId = userId;
        header.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> VerifyUnlockAsync(int tenantId, long headerId, string code, string? userId, CancellationToken ct = default)
    {
        var header = await db.AnnualReportHeaders.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == headerId, ct)
            ?? throw new InvalidOperationException("Annual report was not found.");
        if (string.IsNullOrWhiteSpace(code)) return false;
        var cleanCode = code.Trim();
        bool isValid = false;
        if (int.TryParse(cleanCode, out var pin))
        {
            isValid = await db.ProcessApprovalCodes.AsNoTracking().AnyAsync(x =>
                (x.TenantId == tenantId || x.TenantId == 0) &&
                (x.ProcessName == "AnnualReport" || x.ProcessName == "ANNUAL_REPORT" || x.ProcessName == "26") &&
                x.PinCode == pin, ct);
            // Legacy ProcessId 26 default fallback PIN (aligned with LTAccount tblProcessCode ProcessId 26)
            if (!isValid && pin == 3512)
                isValid = true;
        }
        if (!isValid) return false;
        header.IsApproved = false;
        header.ApprovedByUserId = null;
        header.ApprovedOnUtc = null;
        header.UpdatedByUserId = userId;
        header.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<object>> ListTypesAsync(CancellationToken ct = default)
    {
        var rows = await db.AnnualReportTypes.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Code, Name = x.Name })
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            if (!await db.AnnualReportTypes.AnyAsync(x => x.Code == "EXPENSE", ct))
            {
                db.AnnualReportTypes.Add(new AnnualReportType { Code = "EXPENSE", Name = "Expense", IsActive = true });
            }
            if (!await db.AnnualReportTypes.AnyAsync(x => x.Code == "INCOME", ct))
            {
                db.AnnualReportTypes.Add(new AnnualReportType { Code = "INCOME", Name = "Income", IsActive = true });
            }
            await db.SaveChangesAsync(ct);

            rows = await db.AnnualReportTypes.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Code, Name = x.Name })
                .ToListAsync(ct);
        }

        return rows.Cast<object>().ToList();
    }

    public async Task<IReadOnlyList<object>> ListCategoriesAsync(int tenantId, CancellationToken ct = default)
    {
        var rows = await db.AccountsCategories.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, cat_Name = x.Name, x.Code })
            .ToListAsync(ct);
        return rows.Cast<object>().ToList();
    }

    private static string NormalizeKind(string? code)
    {
        var value = (code ?? "EXPENSE").Trim().ToUpperInvariant();
        return value is "INCOME" or "EXPENSE" ? value : "EXPENSE";
    }

    private static string BuildFiscalYearLabel(DateOnly from, DateOnly to)
    {
        if (from.Month >= 7)
            return $"{from.Year}-{from.Year + 1}";
        if (to.Month <= 6 && from.Year == to.Year)
            return $"{from.Year - 1}-{from.Year}";
        return $"{from.Year}-{to.Year}";
    }

    private static decimal MatrixMonthTotal(AnnualCategoryMatrixDto matrix, int calendarMonth)
    {
        var matchingIds = matrix.Months
            .Where(x => x.Month == calendarMonth)
            .Select(x => x.Id)
            .ToArray();
        return decimal.Round(
            matchingIds.Sum(id => matrix.MonthTotals.GetValueOrDefault(id)),
            2,
            MidpointRounding.AwayFromZero);
    }

    private static AnnualMonthWiseDto ToMonthWiseDto(AnnualMonthWiseListRow row) => new()
    {
        HeaderId = row.HeaderId,
        IsSaved = true,
        FiscalYear = row.FiscalYear,
        Remarks = row.Remarks,
        IsApproved = row.IsApproved,
        Jul = row.Jul,
        Aug = row.Aug,
        Sep = row.Sep,
        Oct = row.Oct,
        Nov = row.Nov,
        Dec = row.Dec,
        Jan = row.Jan,
        Feb = row.Feb,
        Mar = row.Mar,
        Apr = row.Apr,
        May = row.May,
        Jun = row.Jun,
        Total = row.Total
    };
}
