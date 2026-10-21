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
        var query = db.RoznamchaEntries.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsShow && x.TransDate >= from && x.TransDate <= to);
        if (typeId.HasValue) query = query.Where(x => x.RoznamchaTypeId == typeId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        var entries = await query.OrderBy(x => x.TransDate).ThenBy(x => x.Id).Select(x => new
        {
            x.Id, x.TransDate, x.RoznamchaTypeId, x.FromAccountId, x.ToAccountId,
            PkrAmount = (x.Amount ?? 0) + (x.Adjustment ?? 0), UsdAmount = x.UsdAmount ?? 0,
            x.Ref, x.Descriptions, x.SNo, x.OldRef, x.TransTypeId, x.TransModeId,
            x.CreatedOnUtc, x.InstrumentNo, x.TaxTypeId, x.TaxRate, x.TaxAmt,
            x.Adjustment, x.Qty, x.Rate, x.EnterStatusId, x.Attachment, x.Remarks
        }).ToListAsync(ct);
        var referencedIds = entries.SelectMany(x => new[] { x.FromAccountId, x.ToAccountId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var accounts = await (from account in db.AccountsChartAccounts.AsNoTracking()
                              join category in db.AccountsCategories.AsNoTracking() on account.CategoryId equals category.Id into categoryRows
                              from category in categoryRows.DefaultIfEmpty()
                              where referencedIds.Contains(account.Id)
                              select new { account.Id, account.AccountName, account.AccountNumber, account.CategoryId, Category = category == null ? null : category.Name })
            .ToDictionaryAsync(x => x.Id, ct);
        var types = await db.AccountsRoznamchaTypes.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var typeCodes = await db.AccountsRoznamchaTypes.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var transTypes = await db.AccountsTransTypes.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var transModes = await db.AccountsTransModes.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var taxTypes = await db.AccountsTaxTypes.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var statuses = await db.PlatformSettingStatuses.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var statusStyles = (await db.PlatformSettingProcessStatuses.AsNoTracking().Select(x => new
        {
            ProcessName = x.Process.Name, x.StatusId,
            ColorCode = x.Color == null ? null : x.Color.ColorCode,
            FontColor = x.Color == null ? null : x.Color.FontColor
        }).ToListAsync(ct)).ToDictionary(x => (x.ProcessName.ToUpperInvariant(), x.StatusId));
        var rows = new List<(long EntryId, ReportRowDto Row)>();
        foreach (var entry in entries)
        {
            void AddLine(int? id, bool debit)
            {
                if (!id.HasValue || !accounts.TryGetValue(id.Value, out var account)) return;
                if (categoryIds is { Count: > 0 } && (!account.CategoryId.HasValue || !categoryIds.Contains(account.CategoryId.Value))) return;
                if (accountIds is { Count: > 0 } && !accountIds.Contains(account.Id)) return;
                var typeName = entry.RoznamchaTypeId.HasValue && types.TryGetValue(entry.RoznamchaTypeId.Value, out var resolvedType) ? resolvedType : null;
                var typeCode = entry.RoznamchaTypeId.HasValue ? typeCodes.GetValueOrDefault(entry.RoznamchaTypeId.Value)?.ToUpperInvariant() : null;
                var processName = typeCode is "PAYMENT" or "RECEIPT" ? typeCode : typeName?.ToUpperInvariant();
                var style = entry.EnterStatusId.HasValue && processName != null ? statusStyles.GetValueOrDefault((processName, entry.EnterStatusId.Value)) : null;
                var row = new ReportRowDto
                {
                    Id = entry.Id, SNo = entry.SNo, OldRef = entry.OldRef,
                    CreatedOnUtc = entry.CreatedOnUtc, InstrumentNo = entry.InstrumentNo,
                    TransType = entry.TransTypeId.HasValue ? transTypes.GetValueOrDefault(entry.TransTypeId.Value) : null,
                    TransMode = entry.TransModeId.HasValue ? transModes.GetValueOrDefault(entry.TransModeId.Value) : null,
                    TaxType = entry.TaxTypeId.HasValue ? taxTypes.GetValueOrDefault(entry.TaxTypeId.Value) : null,
                    TaxRate = entry.TaxRate, TaxAmount = entry.TaxAmt, Adjustment = entry.Adjustment,
                    Qty = entry.Qty, Rate = entry.Rate, Attachment = entry.Attachment, Remarks = entry.Remarks,
                    Status = entry.EnterStatusId.HasValue ? statuses.GetValueOrDefault(entry.EnterStatusId.Value) : null,
                    StatusColorCode = style?.ColorCode, StatusFontColor = style?.FontColor,
                    FromAccountNumber = entry.FromAccountId.HasValue && accounts.TryGetValue(entry.FromAccountId.Value, out var sourceAccount) ? sourceAccount.AccountNumber : null,
                    ToAccountNumber = entry.ToAccountId.HasValue && accounts.TryGetValue(entry.ToAccountId.Value, out var targetAccount) ? targetAccount.AccountNumber : null,
                    ToCategory = entry.ToAccountId.HasValue && accounts.TryGetValue(entry.ToAccountId.Value, out var targetCategory) ? targetCategory.Category : null,
                    Date = entry.TransDate, CategoryId = account.CategoryId, Category = account.Category,
                    AccountId = account.Id, Account = account.AccountName, EntryTypeId = entry.RoznamchaTypeId ?? 0,
                    EntryType = typeName, Debit = debit ? entry.PkrAmount : 0, Credit = debit ? 0 : entry.PkrAmount,
                    UsdDebit = debit ? entry.UsdAmount : 0, UsdCredit = debit ? 0 : entry.UsdAmount,
                    ReferenceNo = entry.Ref, Description = entry.Descriptions,
                    FromAccount = entry.FromAccountId.HasValue && accounts.TryGetValue(entry.FromAccountId.Value, out var fromAccount) ? fromAccount.AccountName : null,
                    ToAccount = entry.ToAccountId.HasValue && accounts.TryGetValue(entry.ToAccountId.Value, out var toAccount) ? toAccount.AccountName : null
                };
                rows.Add((entry.Id, row));
            }
            AddLine(entry.FromAccountId, true);
            AddLine(entry.ToAccountId, false);
        }
        var term = name?.Trim();
        var filtered = string.IsNullOrWhiteSpace(term) ? rows : rows.Where(x =>
            (x.Row.Category?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (x.Row.Account?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (x.Row.ReferenceNo?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (x.Row.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        foreach (var group in filtered.GroupBy(x => x.Row.AccountId))
        {
            decimal balance = 0, usdBalance = 0;
            foreach (var item in group.OrderBy(x => x.Row.Date).ThenBy(x => x.EntryId))
            {
                balance += item.Row.Credit - item.Row.Debit;
                usdBalance += item.Row.UsdCredit - item.Row.UsdDebit;
                item.Row.Balance = balance;
                item.Row.UsdBalance = usdBalance;
            }
        }
        return filtered.OrderBy(x => x.Row.Date).ThenBy(x => x.EntryId).Select(x => x.Row).ToList();
    }

    public Task<IReadOnlyList<ReportRowDto>> DailyAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        MonthlyAsync(null, from, to, null, null, null, null, ct);

    public async Task<IReadOnlyList<AccountsDailyUpdateDto>> DailyUpdatesAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) throw new InvalidOperationException("toDate must be on or after todayDate.");
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return await (from log in db.RoznamchaEntryProcessLogs.AsNoTracking()
                      join entry in db.RoznamchaEntries.AsNoTracking() on log.RoznamchaEntryId equals entry.Id
                      join type in db.AccountsRoznamchaTypes.AsNoTracking() on entry.RoznamchaTypeId equals type.Id into typeRows
                      from type in typeRows.DefaultIfEmpty()
                      join fromAccount in db.AccountsChartAccounts.AsNoTracking() on entry.FromAccountId equals fromAccount.Id into fromRows
                      from fromAccount in fromRows.DefaultIfEmpty()
                      join toAccount in db.AccountsChartAccounts.AsNoTracking() on entry.ToAccountId equals toAccount.Id into toRows
                      from toAccount in toRows.DefaultIfEmpty()
                      where log.ProcessedOnUtc >= start && log.ProcessedOnUtc < end
                      orderby log.ProcessedOnUtc descending, log.Id descending
                      select new AccountsDailyUpdateDto
                      {
                          LogId = log.Id, EntryId = entry.Id, ReferenceNo = entry.Ref,
                          TransactionDate = entry.TransDate, EntryType = type == null ? null : type.Name,
                          FromAccount = fromAccount == null ? null : fromAccount.AccountName,
                          ToAccount = toAccount == null ? null : toAccount.AccountName,
                          Amount = (entry.Amount ?? 0) + (entry.Adjustment ?? 0), UsdAmount = entry.UsdAmount ?? 0,
                          Action = log.Action, Notes = log.Notes, ChangedByUserId = log.ProcessedByUserId,
                          ChangedOnUtc = log.ProcessedOnUtc, Description = entry.Descriptions
                      }).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ReportRowDto>> BankAccountAsync(
        int accountId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) throw new InvalidOperationException("dateTo must be on or after dateFrom.");
        var account = await db.AccountsChartAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == accountId && x.IsActive, ct)
            ?? throw new KeyNotFoundException("Account was not found.");
        var category = account.CategoryId.HasValue
            ? await db.AccountsCategories.AsNoTracking().Where(x => x.Id == account.CategoryId.Value).Select(x => x.Name).FirstOrDefaultAsync(ct)
            : null;
        var openingEntries = await db.RoznamchaEntries.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsShow && x.TransDate < from
                && (x.FromAccountId == accountId || x.ToAccountId == accountId))
            .Select(x => new
            {
                x.FromAccountId, x.ToAccountId,
                Pkr = (x.Amount ?? 0) + (x.Adjustment ?? 0), Usd = x.UsdAmount ?? 0
            }).ToListAsync(ct);
        var opening = openingEntries.Sum(x => x.ToAccountId == accountId ? x.Pkr : -x.Pkr);
        var usdOpening = openingEntries.Sum(x => x.ToAccountId == accountId ? x.Usd : -x.Usd);
        var entries = await db.RoznamchaEntries.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsShow && x.TransDate >= from && x.TransDate <= to
                && (x.FromAccountId == accountId || x.ToAccountId == accountId))
            .OrderBy(x => x.TransDate).ThenBy(x => x.Id)
            .Select(x => new
            {
                x.TransDate, x.RoznamchaTypeId, x.TransTypeId, x.FromAccountId, x.ToAccountId,
                Pkr = (x.Amount ?? 0) + (x.Adjustment ?? 0), Usd = x.UsdAmount ?? 0,
                x.Ref, x.Descriptions
            }).ToListAsync(ct);
        var relatedIds = entries.SelectMany(x => new[] { x.FromAccountId, x.ToAccountId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var accountNames = await db.AccountsChartAccounts.AsNoTracking().Where(x => relatedIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.AccountName, ct);
        var typeNames = await db.AccountsRoznamchaTypes.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        decimal balance = opening, usdBalance = usdOpening;
        var result = new List<ReportRowDto>();
        if (opening != 0 || usdOpening != 0)
        {
            result.Add(new ReportRowDto
            {
                Date = from, CategoryId = account.CategoryId, Category = category,
                AccountId = account.Id, Account = account.AccountName, ReferenceNo = "OPENING",
                Description = "Opening balance", Balance = opening, UsdBalance = usdOpening
            });
        }
        result.AddRange(entries.Select(entry =>
        {
            var debit = entry.FromAccountId == accountId ? entry.Pkr : 0;
            var credit = entry.ToAccountId == accountId ? entry.Pkr : 0;
            var usdDebit = entry.FromAccountId == accountId ? entry.Usd : 0;
            var usdCredit = entry.ToAccountId == accountId ? entry.Usd : 0;
            balance += credit - debit;
            usdBalance += usdCredit - usdDebit;
            return new ReportRowDto
            {
                Date = entry.TransDate,
                CategoryId = account.CategoryId,
                Category = category,
                AccountId = account.Id,
                Account = account.AccountName,
                EntryTypeId = entry.RoznamchaTypeId ?? entry.TransTypeId ?? 0,
                EntryType = entry.RoznamchaTypeId.HasValue && typeNames.TryGetValue(entry.RoznamchaTypeId.Value, out var typeName) ? typeName : null,
                Debit = debit,
                Credit = credit,
                Balance = balance,
                UsdDebit = usdDebit, UsdCredit = usdCredit, UsdBalance = usdBalance,
                ReferenceNo = entry.Ref,
                Description = entry.Descriptions,
                FromAccount = entry.FromAccountId.HasValue && accountNames.TryGetValue(entry.FromAccountId.Value, out var fromName) ? fromName : null,
                ToAccount = entry.ToAccountId.HasValue && accountNames.TryGetValue(entry.ToAccountId.Value, out var toName) ? toName : null
            };
        }));
        return result;
    }

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
        if (name.Length > 160) throw new InvalidOperationException("Filter name must not exceed 160 characters.");
        if (request.TypeId.HasValue && !await db.AccountsRoznamchaTypes.AsNoTracking().AnyAsync(x => x.Id == request.TypeId.Value && x.IsActive, ct))
            throw new InvalidOperationException("Select a valid active report type.");
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
