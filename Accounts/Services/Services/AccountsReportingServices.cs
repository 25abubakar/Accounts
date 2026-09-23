using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class AccountsReportService(ApplicationDbContext db, ICurrentUserService current) : IReportService
{
    public async Task<IReadOnlyList<ReportRowDto>> MonthlyAsync(
        string? name, DateOnly from, DateOnly to, int? typeId, int? projectId,
        IReadOnlyList<int>? categoryIds, IReadOnlyList<int>? accountIds, CancellationToken ct = default)
    {
        if (to < from) throw new InvalidOperationException("dateTo must be on or after dateFrom.");
        var query = BaseReportQuery().Where(x => x.Date >= from && x.Date <= to);
        if (typeId.HasValue) query = query.Where(x => x.EntryTypeId == typeId.Value);
        if (categoryIds is { Count: > 0 }) query = query.Where(x => x.CategoryId.HasValue && categoryIds.Contains(x.CategoryId.Value));
        if (accountIds is { Count: > 0 }) query = query.Where(x => x.AccountId.HasValue && accountIds.Contains(x.AccountId.Value));
        if (!string.IsNullOrWhiteSpace(name))
        {
            var term = name.Trim();
            query = query.Where(x => (x.Category ?? "").Contains(term) || (x.Account ?? "").Contains(term) || (x.ReferenceNo ?? "").Contains(term));
        }
        if (projectId.HasValue)
        {
            var entryIds = db.RoznamchaEntries.AsNoTracking().Where(x => x.ProjectId == projectId).Select(x => x.Id);
            query = query.Where(x => entryIds.Contains(x.InternalEntryId));
        }
        return await query.OrderBy(x => x.Date).ThenBy(x => x.InternalEntryId)
            .Select(x => new ReportRowDto
            {
                Date = x.Date, CategoryId = x.CategoryId, Category = x.Category,
                AccountId = x.AccountId, Account = x.Account, EntryTypeId = x.EntryTypeId,
                Debit = x.Debit, Credit = x.Credit, Balance = x.Debit - x.Credit,
                ReferenceNo = x.ReferenceNo, Description = x.Description
            }).ToListAsync(ct);
    }

    public Task<IReadOnlyList<ReportRowDto>> DailyAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        MonthlyAsync(null, from, to, null, null, null, null, ct);

    public async Task<IReadOnlyList<ReportFilterDto>> ListFiltersAsync(CancellationToken ct = default)
    {
        var rows = await db.AccountsReportFilters.AsNoTracking()
            .Include(x => x.Categories).Include(x => x.Accounts).Include(x => x.SubAccounts)
            .OrderBy(x => x.Name).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<ReportFilterDto> SaveFilterAsync(int? id, ReportFilterRequest request, CancellationToken ct = default)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Filter name is required.");
        if (await db.AccountsReportFilters.AsNoTracking().AnyAsync(x => x.Name == name && (!id.HasValue || x.Id != id.Value), ct))
            throw new InvalidOperationException("A report filter with this name already exists.");

        var categoryIds = request.CategoryIds.AppendIf(request.CategoryId).Distinct().ToList();
        var accountIds = request.AccountIds.AppendIf(request.AccountId).Distinct().ToList();
        var subAccountIds = request.SubAccountIds.Distinct().ToList();
        if (await db.AccountsCategories.AsNoTracking().CountAsync(x => categoryIds.Contains(x.Id), ct) != categoryIds.Count)
            throw new InvalidOperationException("One or more selected categories are invalid.");
        var allAccountIds = accountIds.Concat(subAccountIds).Distinct().ToList();
        if (await db.AccountsChartAccounts.AsNoTracking().CountAsync(x => allAccountIds.Contains(x.Id), ct) != allAccountIds.Count)
            throw new InvalidOperationException("One or more selected accounts are invalid.");

        AccountsReportFilter row;
        if (id.HasValue)
        {
            row = await db.AccountsReportFilters.Include(x => x.Categories).Include(x => x.Accounts).Include(x => x.SubAccounts)
                .FirstOrDefaultAsync(x => x.Id == id.Value, ct) ?? throw new KeyNotFoundException("Report filter was not found.");
            db.AccountsReportFilterCategories.RemoveRange(row.Categories);
            db.AccountsReportFilterAccounts.RemoveRange(row.Accounts);
            db.AccountsReportFilterSubAccounts.RemoveRange(row.SubAccounts);
            row.UpdatedByUserId = current.UserId;
            row.UpdatedOnUtc = DateTime.UtcNow;
        }
        else
        {
            row = new AccountsReportFilter { TenantId = current.TenantId, CreatedByUserId = current.UserId };
            db.AccountsReportFilters.Add(row);
        }
        row.Name = name;
        row.CategoryId = request.CategoryId;
        row.AccountId = request.AccountId;
        row.TypeId = request.TypeId;
        row.Categories = categoryIds.Select(x => new AccountsReportFilterCategory { CategoryId = x }).ToList();
        row.Accounts = accountIds.Select(x => new AccountsReportFilterAccount { AccountId = x }).ToList();
        row.SubAccounts = subAccountIds.Select(x => new AccountsReportFilterSubAccount { SubAccountId = x }).ToList();
        await db.SaveChangesAsync(ct);
        return (await db.AccountsReportFilters.AsNoTracking().Include(x => x.Categories).Include(x => x.Accounts).Include(x => x.SubAccounts)
            .FirstAsync(x => x.Id == row.Id, ct)).MapFilter();
    }

    public async Task DeleteFilterAsync(int id, CancellationToken ct = default)
    {
        var row = await db.AccountsReportFilters.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Report filter was not found.");
        db.AccountsReportFilters.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    private IQueryable<InternalReportRow> BaseReportQuery() =>
        from entry in db.RoznamchaEntries.AsNoTracking()
        join category in db.AccountsCategories.AsNoTracking() on entry.CategoryId equals category.Id into categories
        from category in categories.DefaultIfEmpty()
        join account in db.AccountsChartAccounts.AsNoTracking() on entry.FromAccountId equals account.Id into accounts
        from account in accounts.DefaultIfEmpty()
        where !entry.IsDeleted && entry.IsShow
        let typeId = entry.RoznamchaTypeId ?? entry.TransTypeId ?? 0
        let amount = (entry.Amount ?? 0) + (entry.UsdAmount ?? 0) + (entry.Adjustment ?? 0)
        select new InternalReportRow
        {
            InternalEntryId = entry.Id,
            Date = entry.TransDate,
            CategoryId = entry.CategoryId,
            Category = category == null ? null : category.Name,
            AccountId = entry.FromAccountId,
            Account = account == null ? null : account.AccountName,
            EntryTypeId = typeId,
            Debit = typeId == 1 ? amount : 0,
            Credit = typeId == 2 ? amount : 0,
            ReferenceNo = entry.Ref,
            Description = entry.Descriptions
        };

    private static ReportFilterDto Map(AccountsReportFilter row) => row.MapFilter();

    private sealed class InternalReportRow
    {
        public long InternalEntryId { get; set; }
        public DateOnly Date { get; set; }
        public int? CategoryId { get; set; }
        public string? Category { get; set; }
        public int? AccountId { get; set; }
        public string? Account { get; set; }
        public int EntryTypeId { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string? ReferenceNo { get; set; }
        public string? Description { get; set; }
    }
}

public sealed class ProjectCostService(ApplicationDbContext db, ICurrentUserService current, IReportService reports) : IProjectCostService
{
    public async Task<IReadOnlyList<ProjectCostRuleDto>> ListRulesAsync(CancellationToken ct = default) =>
        await db.AccountsProjectCostRules.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new ProjectCostRuleDto
            {
                Id = x.Id, Name = x.Name, TypeId = x.TypeId, CategoryId = x.CategoryId,
                AccountId = x.AccountId, EntryId = x.EntryId, PercentageValue = x.PercentageValue
            }).ToListAsync(ct);

    public async Task<ProjectCostRuleDto> SaveRuleAsync(int? id, ProjectCostRuleRequest request, CancellationToken ct = default)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Rule name is required.");
        if (request.PercentageValue is < 0 or > 100) throw new InvalidOperationException("Percentage value must be between 0 and 100.");
        if (request.CategoryId.HasValue && !await db.AccountsCategories.AsNoTracking().AnyAsync(x => x.Id == request.CategoryId, ct))
            throw new InvalidOperationException("Category is invalid.");
        if (request.AccountId.HasValue && !await db.AccountsChartAccounts.AsNoTracking().AnyAsync(x => x.Id == request.AccountId, ct))
            throw new InvalidOperationException("Account is invalid.");
        if (request.EntryId.HasValue && !await db.RoznamchaEntries.AsNoTracking().AnyAsync(x => x.Id == request.EntryId, ct))
            throw new InvalidOperationException("Entry is invalid.");

        AccountsProjectCostRule row;
        if (id.HasValue)
        {
            row = await db.AccountsProjectCostRules.FirstOrDefaultAsync(x => x.Id == id.Value, ct)
                ?? throw new KeyNotFoundException("Project cost rule was not found.");
            row.UpdatedByUserId = current.UserId;
            row.UpdatedOnUtc = DateTime.UtcNow;
        }
        else
        {
            row = new AccountsProjectCostRule { TenantId = current.TenantId, CreatedByUserId = current.UserId };
            db.AccountsProjectCostRules.Add(row);
        }
        row.Name = name;
        row.TypeId = request.TypeId;
        row.CategoryId = request.CategoryId;
        row.AccountId = request.AccountId;
        row.EntryId = request.EntryId;
        row.PercentageValue = decimal.Round(request.PercentageValue, 4);
        await db.SaveChangesAsync(ct);
        return new ProjectCostRuleDto
        {
            Id = row.Id, Name = row.Name, TypeId = row.TypeId, CategoryId = row.CategoryId,
            AccountId = row.AccountId, EntryId = row.EntryId, PercentageValue = row.PercentageValue
        };
    }

    public async Task DeleteRuleAsync(int id, CancellationToken ct = default)
    {
        var row = await db.AccountsProjectCostRules.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Project cost rule was not found.");
        db.AccountsProjectCostRules.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ReportRowDto>> ReportAsync(string? name, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rulesQuery = db.AccountsProjectCostRules.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(name)) rulesQuery = rulesQuery.Where(x => x.Name.Contains(name.Trim()));
        var rules = await rulesQuery.ToListAsync(ct);
        var categoryIds = rules.Where(x => x.CategoryId.HasValue).Select(x => x.CategoryId!.Value).Distinct().ToList();
        var accountIds = rules.Where(x => x.AccountId.HasValue).Select(x => x.AccountId!.Value).Distinct().ToList();
        var rows = await reports.MonthlyAsync(null, from, to, null, null, categoryIds, accountIds, ct);
        return rows.Select(row =>
        {
            var percentages = rules.Where(rule =>
                (!rule.CategoryId.HasValue || rule.CategoryId == row.CategoryId) &&
                (!rule.AccountId.HasValue || rule.AccountId == row.AccountId) &&
                (!rule.TypeId.HasValue || rule.TypeId == row.EntryTypeId))
                .Select(rule => rule.PercentageValue).ToList();
            var factor = percentages.Count == 0 ? 1m : percentages.Max() / 100m;
            row.Debit = decimal.Round(row.Debit * factor, 2);
            row.Credit = decimal.Round(row.Credit * factor, 2);
            row.Balance = row.Debit - row.Credit;
            return row;
        }).ToList();
    }
}

internal static class AccountsReportMappingExtensions
{
    public static IEnumerable<int> AppendIf(this IEnumerable<int> source, int? value) =>
        value.HasValue ? source.Append(value.Value) : source;

    public static ReportFilterDto MapFilter(this AccountsReportFilter row) => new()
    {
        Id = row.Id,
        Name = row.Name,
        CategoryId = row.CategoryId,
        AccountId = row.AccountId,
        TypeId = row.TypeId,
        CategoryIds = row.Categories.Select(x => x.CategoryId).ToList(),
        AccountIds = row.Accounts.Select(x => x.AccountId).ToList(),
        SubAccountIds = row.SubAccounts.Select(x => x.SubAccountId).ToList()
    };
}
