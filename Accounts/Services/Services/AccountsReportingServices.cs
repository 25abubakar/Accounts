using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class AccountsReportService(ApplicationDbContext db, ICurrentUserService current, ITenantService tenantService) : IReportService
{
    public async Task<IReadOnlyList<ReportRowDto>> MonthlyAsync(
        string? name, DateOnly from, DateOnly to, int? typeId, int? projectId,
        IReadOnlyList<int>? categoryIds, IReadOnlyList<int>? accountIds, CancellationToken ct = default)
    {
        if (to < from) throw new InvalidOperationException("dateTo must be on or after dateFrom.");

        var tenantId = tenantService.TenantId ?? current.TenantId;

        // Bug Fix 1: Resolve name as Saved Report Rule Template
        // When the frontend passes name (e.g. "Expense", "Hospital Expenses"), do NOT treat it as a text search string.
        AccountsReportFilter? rule = null;
        if (!string.IsNullOrWhiteSpace(name))
        {
            var trimmedName = name.Trim();
            if (int.TryParse(trimmedName, out var filterId))
            {
                rule = await db.AccountsReportFilters.AsNoTracking()
                    .Include(x => x.Categories)
                    .Include(x => x.Accounts)
                    .Include(x => x.SubAccounts)
                    .FirstOrDefaultAsync(x => (tenantId == 0 || x.TenantId == tenantId) && (x.Id == filterId || x.Name == trimmedName), ct);
            }
            else
            {
                rule = await db.AccountsReportFilters.AsNoTracking()
                    .Include(x => x.Categories)
                    .Include(x => x.Accounts)
                    .Include(x => x.SubAccounts)
                    .FirstOrDefaultAsync(x => (tenantId == 0 || x.TenantId == tenantId) && x.Name == trimmedName, ct);
            }
        }

        var effectiveCategoryIds = categoryIds?.ToList() ?? new List<int>();
        var effectiveAccountIds = accountIds?.ToList() ?? new List<int>();
        var effectiveTypeId = typeId;

        if (rule != null)
        {
            var ruleCatIds = rule.Categories.Select(c => c.CategoryId).ToList();
            if (rule.CategoryId.HasValue && !ruleCatIds.Contains(rule.CategoryId.Value))
                ruleCatIds.Add(rule.CategoryId.Value);

            var ruleAccIds = rule.Accounts.Select(a => a.AccountId).ToList();
            if (rule.AccountId.HasValue && !ruleAccIds.Contains(rule.AccountId.Value))
                ruleAccIds.Add(rule.AccountId.Value);

            if (rule.SubAccounts != null)
            {
                foreach (var sub in rule.SubAccounts)
                {
                    if (!ruleAccIds.Contains(sub.SubAccountId))
                        ruleAccIds.Add(sub.SubAccountId);
                }
            }

            if (effectiveCategoryIds.Count == 0 && ruleCatIds.Count > 0)
                effectiveCategoryIds = ruleCatIds;

            if (effectiveAccountIds.Count == 0 && ruleAccIds.Count > 0)
                effectiveAccountIds = ruleAccIds;

            if ((!effectiveTypeId.HasValue || effectiveTypeId.Value == 0) && rule.TypeId.HasValue && rule.TypeId.Value > 0)
                effectiveTypeId = rule.TypeId.Value;
        }

        // Bug Fix 3: Stored Procedure & Query Logic Parity
        // Filter TransDate >= from AND TransDate <= to
        // Exclude internal technical adjustments: TransTypeId != 8
        // Exclude hidden entries: IsShow == true && !IsDeleted
        // Filter by Project when projectId is passed
        var query = db.RoznamchaEntries.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsShow && x.TransDate >= from && x.TransDate <= to);

        if (tenantId > 0)
            query = query.Where(x => x.TenantId == tenantId);

        // Exclude internal technical adjustments: TransTypeId != 8
        query = query.Where(x => !x.TransTypeId.HasValue || x.TransTypeId.Value != 8);

        if (projectId.HasValue && projectId.Value > 0)
            query = query.Where(x => x.ProjectId == projectId.Value);

        // Mode Handling:
        // typeId == 1 (Payment / Expense): RoznamchaTypeId == 1
        // typeId == 2 (Receipt / Income): RoznamchaTypeId == 2
        // typeId == 3 (Summary): Matches both Payments and Receipts
        if (effectiveTypeId.HasValue && effectiveTypeId.Value > 0)
        {
            if (effectiveTypeId.Value == 1)
                query = query.Where(x => x.RoznamchaTypeId == 1);
            else if (effectiveTypeId.Value == 2)
                query = query.Where(x => x.RoznamchaTypeId == 2);
            // If effectiveTypeId.Value == 3 (Summary), both Payment and Receipt are included
        }

        // Category & Account Mode Handling:
        // typeId == 1 (Payment / Expense): Target account matches ToAccountId
        // typeId == 2 (Receipt / Income): Source account matches FromAccountId
        // typeId == 3 (Summary): Matches both Payments and Receipts involving the selected account(s)
        var categoryAccountIds = new HashSet<int>();
        if (effectiveCategoryIds.Count > 0)
        {
            var accQuery = db.AccountsChartAccounts.AsNoTracking()
                .Where(a => a.CategoryId.HasValue && effectiveCategoryIds.Contains(a.CategoryId.Value));
            if (tenantId > 0) accQuery = accQuery.Where(a => a.TenantId == tenantId);
            categoryAccountIds = (await accQuery.Select(a => a.Id).ToListAsync(ct)).ToHashSet();
        }

        if (effectiveAccountIds.Count > 0)
        {
            if (effectiveTypeId.HasValue && effectiveTypeId.Value == 1) // Payment -> Target account matches ToAccountId
            {
                query = query.Where(x => x.ToAccountId.HasValue && effectiveAccountIds.Contains(x.ToAccountId.Value));
            }
            else if (effectiveTypeId.HasValue && effectiveTypeId.Value == 2) // Receipt -> Source account matches FromAccountId
            {
                query = query.Where(x => x.FromAccountId.HasValue && effectiveAccountIds.Contains(x.FromAccountId.Value));
            }
            else // Summary -> Matches either account
            {
                query = query.Where(x =>
                    (x.ToAccountId.HasValue && effectiveAccountIds.Contains(x.ToAccountId.Value)) ||
                    (x.FromAccountId.HasValue && effectiveAccountIds.Contains(x.FromAccountId.Value)));
            }
        }
        else if (effectiveCategoryIds.Count > 0)
        {
            if (effectiveTypeId.HasValue && effectiveTypeId.Value == 1) // Payment -> ToAccountId or CategoryId
            {
                query = query.Where(x =>
                    (x.ToAccountId.HasValue && categoryAccountIds.Contains(x.ToAccountId.Value)) ||
                    (x.CategoryId.HasValue && effectiveCategoryIds.Contains(x.CategoryId.Value)));
            }
            else if (effectiveTypeId.HasValue && effectiveTypeId.Value == 2) // Receipt -> FromAccountId or CategoryId
            {
                query = query.Where(x =>
                    (x.FromAccountId.HasValue && categoryAccountIds.Contains(x.FromAccountId.Value)) ||
                    (x.CategoryId.HasValue && effectiveCategoryIds.Contains(x.CategoryId.Value)));
            }
            else // Summary -> ToAccountId, FromAccountId, or CategoryId
            {
                query = query.Where(x =>
                    (x.ToAccountId.HasValue && categoryAccountIds.Contains(x.ToAccountId.Value)) ||
                    (x.FromAccountId.HasValue && categoryAccountIds.Contains(x.FromAccountId.Value)) ||
                    (x.CategoryId.HasValue && effectiveCategoryIds.Contains(x.CategoryId.Value)));
            }
        }

        // If rule was null and user typed a manual text search, filter by text
        if (rule == null && !string.IsNullOrWhiteSpace(name))
        {
            var term = name.Trim();
            query = query.Where(x =>
                (x.Ref != null && x.Ref.Contains(term)) ||
                (x.Descriptions != null && x.Descriptions.Contains(term)) ||
                (x.OldRef != null && x.OldRef.Contains(term)) ||
                (x.InstrumentNo != null && x.InstrumentNo.Contains(term)));
        }

        // Opening balance calculation for continuous financial ledger parity
        decimal openingBalance = 0;
        decimal openingUsdBalance = 0;

        if (effectiveAccountIds.Count > 0 || effectiveCategoryIds.Count > 0)
        {
            var priorQuery = db.RoznamchaEntries.AsNoTracking()
                .Where(x => !x.IsDeleted && x.IsShow && x.TransDate < from);

            if (tenantId > 0)
                priorQuery = priorQuery.Where(x => x.TenantId == tenantId);

            priorQuery = priorQuery.Where(x => !x.TransTypeId.HasValue || x.TransTypeId.Value != 8);

            if (projectId.HasValue && projectId.Value > 0)
                priorQuery = priorQuery.Where(x => x.ProjectId == projectId.Value);

            if (effectiveTypeId.HasValue && effectiveTypeId.Value > 0)
            {
                if (effectiveTypeId.Value == 1)
                    priorQuery = priorQuery.Where(x => x.RoznamchaTypeId == 1);
                else if (effectiveTypeId.Value == 2)
                    priorQuery = priorQuery.Where(x => x.RoznamchaTypeId == 2);
            }

            if (effectiveAccountIds.Count > 0)
            {
                if (effectiveTypeId.HasValue && effectiveTypeId.Value == 1)
                    priorQuery = priorQuery.Where(x => x.ToAccountId.HasValue && effectiveAccountIds.Contains(x.ToAccountId.Value));
                else if (effectiveTypeId.HasValue && effectiveTypeId.Value == 2)
                    priorQuery = priorQuery.Where(x => x.FromAccountId.HasValue && effectiveAccountIds.Contains(x.FromAccountId.Value));
                else
                    priorQuery = priorQuery.Where(x =>
                        (x.ToAccountId.HasValue && effectiveAccountIds.Contains(x.ToAccountId.Value)) ||
                        (x.FromAccountId.HasValue && effectiveAccountIds.Contains(x.FromAccountId.Value)));
            }
            else if (effectiveCategoryIds.Count > 0)
            {
                if (effectiveTypeId.HasValue && effectiveTypeId.Value == 1)
                    priorQuery = priorQuery.Where(x =>
                        (x.ToAccountId.HasValue && categoryAccountIds.Contains(x.ToAccountId.Value)) ||
                        (x.CategoryId.HasValue && effectiveCategoryIds.Contains(x.CategoryId.Value)));
                else if (effectiveTypeId.HasValue && effectiveTypeId.Value == 2)
                    priorQuery = priorQuery.Where(x =>
                        (x.FromAccountId.HasValue && categoryAccountIds.Contains(x.FromAccountId.Value)) ||
                        (x.CategoryId.HasValue && effectiveCategoryIds.Contains(x.CategoryId.Value)));
                else
                    priorQuery = priorQuery.Where(x =>
                        (x.ToAccountId.HasValue && categoryAccountIds.Contains(x.ToAccountId.Value)) ||
                        (x.FromAccountId.HasValue && categoryAccountIds.Contains(x.FromAccountId.Value)) ||
                        (x.CategoryId.HasValue && effectiveCategoryIds.Contains(x.CategoryId.Value)));
            }

            var priorTotals = await priorQuery
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    NetPkr = g.Sum(x => x.RoznamchaTypeId == 2
                        ? ((x.Amount ?? 0) + (x.Adjustment ?? 0))
                        : -((x.Amount ?? 0) + (x.Adjustment ?? 0))),
                    NetUsd = g.Sum(x => x.RoznamchaTypeId == 2
                        ? (x.UsdAmount ?? 0)
                        : -(x.UsdAmount ?? 0))
                })
                .FirstOrDefaultAsync(ct);

            if (priorTotals != null)
            {
                openingBalance = priorTotals.NetPkr;
                openingUsdBalance = priorTotals.NetUsd;
            }
        }

        var entries = await query.OrderBy(x => x.TransDate).ThenBy(x => x.Id).Select(x => new
        {
            x.Id, x.TransDate, x.RoznamchaTypeId, x.FromAccountId, x.ToAccountId, x.CategoryId,
            x.ProjectId,
            PkrAmount = (x.Amount ?? 0) + (x.Adjustment ?? 0), UsdAmount = x.UsdAmount ?? 0,
            x.Ref, x.Descriptions, x.SNo, x.OldRef, x.TransTypeId, x.TransModeId,
            x.CreatedOnUtc, x.CreatedByUserId, x.InstrumentNo, x.TaxTypeId, x.TaxRate, x.TaxAmt,
            x.Adjustment, x.Qty, x.Rate, x.EnterStatusId, x.Attachment, x.Remarks,
            x.IsDeleted
        }).ToListAsync(ct);

        var referencedAccountIds = entries
            .SelectMany(x => new[] { x.FromAccountId, x.ToAccountId })
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();

        var accounts = await (from account in db.AccountsChartAccounts.AsNoTracking()
                              join category in db.AccountsCategories.AsNoTracking() on account.CategoryId equals category.Id into categoryRows
                              from category in categoryRows.DefaultIfEmpty()
                              where referencedAccountIds.Contains(account.Id)
                              select new { account.Id, account.AccountName, account.AccountNumber, account.CategoryId, Category = category == null ? null : category.Name })
            .ToDictionaryAsync(x => x.Id, ct);

        var referencedCategoryIds = entries.Where(x => x.CategoryId.HasValue).Select(x => x.CategoryId!.Value).Distinct().ToArray();
        var categories = await db.AccountsCategories.AsNoTracking()
            .Where(c => referencedCategoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var referencedProjectIds = entries.Where(x => x.ProjectId.HasValue).Select(x => x.ProjectId!.Value).Distinct().ToArray();
        var projects = await db.AccountsProjects.AsNoTracking()
            .Where(p => referencedProjectIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var createdByUserIds = entries.Where(x => !string.IsNullOrEmpty(x.CreatedByUserId)).Select(x => x.CreatedByUserId!).Distinct().ToArray();
        var userMap = await db.Users.AsNoTracking()
            .Where(u => createdByUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UserName ?? u.Email ?? "", ct);

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

        // Bug Fix 2: Single Row per Transaction
        var rows = new List<ReportRowDto>(entries.Count);
        decimal runningBalance = openingBalance;
        decimal runningUsdBalance = openingUsdBalance;

        foreach (var entry in entries)
        {
            var fromAcct = entry.FromAccountId.HasValue ? accounts.GetValueOrDefault(entry.FromAccountId.Value) : null;
            var toAcct = entry.ToAccountId.HasValue ? accounts.GetValueOrDefault(entry.ToAccountId.Value) : null;

            var typeName = entry.RoznamchaTypeId.HasValue && types.TryGetValue(entry.RoznamchaTypeId.Value, out var resolvedType) ? resolvedType : null;
            var typeCode = entry.RoznamchaTypeId.HasValue ? typeCodes.GetValueOrDefault(entry.RoznamchaTypeId.Value)?.ToUpperInvariant() : null;
            var processName = typeCode is "PAYMENT" or "RECEIPT" ? typeCode : typeName?.ToUpperInvariant();
            var style = entry.EnterStatusId.HasValue && processName != null ? statusStyles.GetValueOrDefault((processName, entry.EnterStatusId.Value)) : null;

            var catName = entry.CategoryId.HasValue && categories.TryGetValue(entry.CategoryId.Value, out var cName)
                ? cName
                : (entry.RoznamchaTypeId == 1 ? toAcct?.Category ?? fromAcct?.Category : fromAcct?.Category ?? toAcct?.Category);
            var toCatName = toAcct?.Category;

            // Debit: entry.Amount if RoznamchaTypeId == 1 (Payment)
            // Credit: entry.Amount if RoznamchaTypeId == 2 (Receipt)
            // UsdDebit: entry.UsdAmount if Payment
            // UsdCredit: entry.UsdAmount if Receipt
            decimal debit = 0, credit = 0, usdDebit = 0, usdCredit = 0;
            if (entry.RoznamchaTypeId == 1)
            {
                debit = entry.PkrAmount;
                usdDebit = entry.UsdAmount;
            }
            else if (entry.RoznamchaTypeId == 2)
            {
                credit = entry.PkrAmount;
                usdCredit = entry.UsdAmount;
            }
            else
            {
                debit = entry.PkrAmount;
                usdDebit = entry.UsdAmount;
            }

            // Running balance: Balance += Credit - Debit
            runningBalance += (credit - debit);
            runningUsdBalance += (usdCredit - usdDebit);

            var row = new ReportRowDto
            {
                Id = entry.Id,
                SNo = entry.SNo,
                OldRef = entry.OldRef,
                ReferenceNo = entry.Ref,
                Date = entry.TransDate,
                CreatedOnUtc = entry.CreatedOnUtc,
                CreatedBy = entry.CreatedByUserId != null && userMap.TryGetValue(entry.CreatedByUserId, out var creator) ? creator : null,
                InstrumentNo = entry.InstrumentNo,
                TransType = entry.TransTypeId.HasValue ? transTypes.GetValueOrDefault(entry.TransTypeId.Value) : null,
                TransMode = entry.TransModeId.HasValue ? transModes.GetValueOrDefault(entry.TransModeId.Value) : null,
                TaxType = entry.TaxTypeId.HasValue ? taxTypes.GetValueOrDefault(entry.TaxTypeId.Value) : null,
                TaxRate = entry.TaxRate,
                TaxAmount = entry.TaxAmt,
                Adjustment = entry.Adjustment,
                Qty = entry.Qty,
                Rate = entry.Rate,
                Attachment = entry.Attachment,
                Remarks = entry.Remarks,
                Description = entry.Descriptions,
                Status = entry.EnterStatusId.HasValue ? statuses.GetValueOrDefault(entry.EnterStatusId.Value) : null,
                StatusId = entry.EnterStatusId,
                EnterStatusId = entry.EnterStatusId,
                StatusColorCode = style?.ColorCode,
                StatusFontColor = style?.FontColor,

                FromAccountId = entry.FromAccountId,
                FromAccount = fromAcct?.AccountName,
                FromAccountNumber = fromAcct?.AccountNumber,
                ToAccountId = entry.ToAccountId,
                ToAccount = toAcct?.AccountName,
                ToAccountNumber = toAcct?.AccountNumber,
                Category = catName,
                ToCategory = toCatName,

                CategoryId = entry.CategoryId ?? (entry.RoznamchaTypeId == 1 ? toAcct?.CategoryId : fromAcct?.CategoryId),
                AccountId = entry.RoznamchaTypeId == 1 ? entry.ToAccountId : entry.FromAccountId,
                Account = entry.RoznamchaTypeId == 1 ? toAcct?.AccountName : fromAcct?.AccountName,

                ProjectId = entry.ProjectId,
                ProjectName = entry.ProjectId.HasValue && projects.TryGetValue(entry.ProjectId.Value, out var pName) ? pName : null,
                IsDeleted = entry.IsDeleted,

                EntryTypeId = entry.RoznamchaTypeId ?? 0,
                EntryType = typeName,

                Debit = debit,
                Credit = credit,
                Balance = runningBalance,
                UsdDebit = usdDebit,
                UsdCredit = usdCredit,
                UsdBalance = runningUsdBalance
            };

            rows.Add(row);
        }

        return rows;
    }

    public Task<IReadOnlyList<ReportRowDto>> DailyAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        MonthlyAsync(null, from, to, null, null, null, null, ct);

    public async Task<IReadOnlyList<AccountsDailyUpdateDto>> DailyUpdatesAsync(
        DateOnly from, DateOnly to, bool includeDeleted = true, bool byTransactionDate = false, CancellationToken ct = default)
    {
        if (to < from) throw new InvalidOperationException("toDate must be on or after todayDate.");

        var startUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var tenantId = tenantService.TenantId ?? current.TenantId;

        var baseQuery = db.RoznamchaEntries.AsNoTracking()
            .Where(e => (tenantId == 0 || e.TenantId == tenantId) && e.IsShow);

        if (!includeDeleted)
        {
            baseQuery = baseQuery.Where(e => !e.IsDeleted);
        }

        if (byTransactionDate)
        {
            baseQuery = baseQuery.Where(e => e.TransDate >= from && e.TransDate <= to);
        }
        else
        {
            baseQuery = baseQuery.Where(e => e.CreatedOnUtc >= startUtc && e.CreatedOnUtc < endUtc);
        }

        var query = from e in baseQuery
                    join rt in db.AccountsRoznamchaTypes.AsNoTracking() on e.RoznamchaTypeId equals rt.Id into rtRows
                    from rt in rtRows.DefaultIfEmpty()
                    join cat in db.AccountsCategories.AsNoTracking() on e.CategoryId equals cat.Id into catRows
                    from cat in catRows.DefaultIfEmpty()
                    join fromAcct in db.AccountsChartAccounts.AsNoTracking() on e.FromAccountId equals fromAcct.Id into fromRows
                    from fromAcct in fromRows.DefaultIfEmpty()
                    join toAcct in db.AccountsChartAccounts.AsNoTracking() on e.ToAccountId equals toAcct.Id into toRows
                    from toAcct in toRows.DefaultIfEmpty()
                    join toCat in db.AccountsCategories.AsNoTracking() on toAcct.CategoryId equals toCat.Id into toCatRows
                    from toCat in toCatRows.DefaultIfEmpty()
                    join st in db.AccountsEntryStatuses.AsNoTracking() on e.EnterStatusId equals st.Id into stRows
                    from st in stRows.DefaultIfEmpty()
                    join u in db.Users.AsNoTracking() on e.CreatedByUserId equals u.Id into uRows
                    from u in uRows.DefaultIfEmpty()
                    join uu in db.Users.AsNoTracking() on e.UpdatedByUserId equals uu.Id into uuRows
                    from uu in uuRows.DefaultIfEmpty()
                    orderby e.CreatedOnUtc descending, e.Id descending
                    select new AccountsDailyUpdateDto
                    {
                        Id = e.Id,
                        SNo = e.SNo,
                        ReferenceNo = e.Ref,
                        OldRef = e.OldRef,
                        RoznamchaTypeId = e.RoznamchaTypeId,
                        EntryType = rt != null ? rt.Name : null,
                        TransactionDate = e.TransDate,
                        CreatedOnUtc = e.CreatedOnUtc,
                        CreatedBy = u != null ? (u.UserName ?? u.Email ?? e.CreatedByUserId) : e.CreatedByUserId,
                        CategoryId = e.CategoryId,
                        Category = cat != null ? cat.Name : null,
                        ToCategoryId = toAcct != null ? toAcct.CategoryId : null,
                        ToCategory = toCat != null ? toCat.Name : null,
                        FromAccountId = e.FromAccountId,
                        FromAccount = fromAcct != null ? fromAcct.AccountName : null,
                        FromAccountNumber = fromAcct != null ? fromAcct.AccountNumber : null,
                        ToAccountId = e.ToAccountId,
                        ToAccount = toAcct != null ? toAcct.AccountName : null,
                        ToAccountNumber = toAcct != null ? toAcct.AccountNumber : null,
                        Debit = (e.RoznamchaTypeId == 1) ? ((e.Amount ?? 0) + (e.Adjustment ?? 0)) : null,
                        Credit = (e.RoznamchaTypeId == 2) ? ((e.Amount ?? 0) + (e.Adjustment ?? 0)) : null,
                        UsdDebit = (e.RoznamchaTypeId == 1) ? e.UsdAmount : null,
                        UsdCredit = (e.RoznamchaTypeId == 2) ? e.UsdAmount : null,
                        Balance = e.BalanceAmount,
                        Adjustment = e.Adjustment,
                        InstrumentNo = e.InstrumentNo,
                        Description = e.Descriptions,
                        Remarks = e.Remarks,
                        EnterStatusId = e.EnterStatusId,
                        Status = st != null ? st.Name : null,
                        StatusColorCode = st != null ? st.ColorCode : null,
                        StatusFontColor = st != null ? st.FontColor : null,
                        Attachment = e.Attachment ?? e.ImagePath,
                        IsDeleted = e.IsDeleted,
                        UpdatedBy = uu != null ? (uu.UserName ?? uu.Email ?? e.UpdatedByUserId) : e.UpdatedByUserId,
                        UpdatedOnUtc = e.UpdatedOnUtc
                    };

        return await query.ToListAsync(ct);
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
        var tenantId = tenantService.TenantId ?? current.TenantId;
        IQueryable<AccountsReportFilter> query = db.AccountsReportFilters.AsNoTracking()
            .Include(x => x.Categories).Include(x => x.Accounts).Include(x => x.SubAccounts);
        if (tenantId > 0)
            query = query.Where(x => x.TenantId == tenantId);
        var rows = await query.OrderBy(x => x.Name).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ReportFilterDetailRowDto>> ListFilterDetailsAsync(CancellationToken ct = default)
    {
        var tenantId = tenantService.TenantId ?? current.TenantId;
        IQueryable<AccountsReportFilter> query = db.AccountsReportFilters.AsNoTracking()
            .Include(x => x.Categories)
            .Include(x => x.Accounts)
            .Include(x => x.SubAccounts);
        if (tenantId > 0)
            query = query.Where(x => x.TenantId == tenantId);

        var filters = await query.OrderBy(x => x.Name).ToListAsync(ct);
        if (filters.Count == 0) return [];

        var allAccountIds = filters
            .SelectMany(f => f.Accounts.Select(a => a.AccountId)
                .Concat(f.SubAccounts.Select(s => s.SubAccountId))
                .AppendIf(f.AccountId))
            .Distinct()
            .ToList();

        var accounts = await db.AccountsChartAccounts.AsNoTracking()
            .Where(a => (tenantId == 0 || a.TenantId == tenantId) && allAccountIds.Contains(a.Id))
            .ToListAsync(ct);
        var accountDict = accounts.ToDictionary(a => a.Id);

        var allCategoryIds = accounts.Select(a => a.CategoryId).Where(c => c.HasValue).Select(c => c!.Value)
            .Concat(filters.SelectMany(f => f.Categories.Select(c => c.CategoryId).AppendIf(f.CategoryId)))
            .Distinct()
            .ToList();

        var categories = await db.AccountsCategories.AsNoTracking()
            .Where(c => (tenantId == 0 || c.TenantId == tenantId) && allCategoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var result = new List<ReportFilterDetailRowDto>();

        foreach (var filter in filters)
        {
            var filterAccountIds = filter.Accounts.Select(a => a.AccountId)
                .Concat(filter.SubAccounts.Select(s => s.SubAccountId))
                .AppendIf(filter.AccountId)
                .Distinct()
                .ToList();

            string typeName = filter.TypeId switch
            {
                1 => "Expense",
                2 => "Income",
                3 => "Summary",
                _ => "All"
            };

            foreach (var accId in filterAccountIds)
            {
                if (accountDict.TryGetValue(accId, out var acc))
                {
                    int catId = acc.CategoryId ?? filter.CategoryId ?? 0;
                    string catName = catId > 0 && categories.TryGetValue(catId, out var cName) ? cName : "-";

                    result.Add(new ReportFilterDetailRowDto
                    {
                        FilterId = filter.Id,
                        AccountMappingId = (filter.Id * 100_000) + acc.Id,
                        RuleName = filter.Name,
                        CategoryId = catId,
                        CategoryName = catName,
                        AccountId = acc.Id,
                        AccountName = acc.AccountName,
                        AccountNumber = acc.AccountNumber,
                        TypeId = filter.TypeId,
                        TypeName = typeName,
                        CreatedDate = filter.CreatedOnUtc,
                        CreatedBy = filter.CreatedByUserId
                    });
                }
            }
        }

        return result.OrderBy(x => x.RuleName).ThenBy(x => x.CategoryName).ThenBy(x => x.AccountName).ToList();
    }

    public async Task<IReadOnlyList<ReportFilterNameLookupDto>> ListFilterNamesAsync(CancellationToken ct = default)
    {
        var tenantId = tenantService.TenantId ?? current.TenantId;
        var filters = await db.AccountsReportFilters.AsNoTracking()
            .Where(x => (tenantId == 0 || x.TenantId == tenantId) && !string.IsNullOrWhiteSpace(x.Name))
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

        return filters
            .GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(f => new ReportFilterNameLookupDto
            {
                Id = f.Id,
                Name = f.Name,
                TypeId = f.TypeId,
                TypeName = f.TypeId switch
                {
                    1 => "Expense",
                    2 => "Income",
                    3 => "Summary",
                    _ => "All"
                }
            })
            .ToList();
    }

    public async Task AddAccountsToFilterAsync(int? filterId, AddAccountsToFilterRequest request, CancellationToken ct = default)
    {
        var tenantId = tenantService.TenantId ?? current.TenantId;
        AccountsReportFilter? filter = null;

        var targetFilterId = filterId ?? request.FilterId;
        var ruleName = (request.RuleName ?? request.Name)?.Trim();

        if (targetFilterId.HasValue && targetFilterId.Value > 0)
        {
            filter = await db.AccountsReportFilters
                .Include(x => x.Categories)
                .Include(x => x.Accounts)
                .Include(x => x.SubAccounts)
                .FirstOrDefaultAsync(x => x.Id == targetFilterId.Value && (tenantId == 0 || x.TenantId == tenantId), ct);
        }
        else if (!string.IsNullOrWhiteSpace(ruleName))
        {
            filter = await db.AccountsReportFilters
                .Include(x => x.Categories)
                .Include(x => x.Accounts)
                .Include(x => x.SubAccounts)
                .FirstOrDefaultAsync(x => x.Name == ruleName && (tenantId == 0 || x.TenantId == tenantId), ct);
        }

        if (filter == null)
            throw new KeyNotFoundException("Report filter was not found.");

        var categoryIds = request.CategoryIds ?? [];
        var targetAccountIds = new HashSet<int>(request.AccountIds ?? []);
        var targetSubAccountIds = new HashSet<int>(request.SubAccountIds ?? []);

        if (request.IsAllCategoryAccounts && categoryIds.Count > 0)
        {
            var categoryAccountIds = await db.AccountsChartAccounts.AsNoTracking()
                .Where(a => (tenantId == 0 || a.TenantId == tenantId) && a.CategoryId.HasValue && categoryIds.Contains(a.CategoryId.Value))
                .Select(a => a.Id)
                .ToListAsync(ct);

            foreach (var id in categoryAccountIds)
                targetAccountIds.Add(id);
        }

        var existingAccountIds = filter.Accounts.Select(a => a.AccountId).ToHashSet();
        foreach (var accId in targetAccountIds)
        {
            if (existingAccountIds.Add(accId))
            {
                filter.Accounts.Add(new AccountsReportFilterAccount
                {
                    ReportFilterId = filter.Id,
                    AccountId = accId
                });
            }
        }

        var existingSubAccountIds = filter.SubAccounts.Select(s => s.SubAccountId).ToHashSet();
        foreach (var subId in targetSubAccountIds)
        {
            if (existingSubAccountIds.Add(subId))
            {
                filter.SubAccounts.Add(new AccountsReportFilterSubAccount
                {
                    ReportFilterId = filter.Id,
                    SubAccountId = subId
                });
            }
        }

        var existingCategoryIds = filter.Categories.Select(c => c.CategoryId).ToHashSet();
        var resolvedCatIds = new HashSet<int>(categoryIds);
        if (targetAccountIds.Count > 0)
        {
            var accountCatIds = await db.AccountsChartAccounts.AsNoTracking()
                .Where(a => targetAccountIds.Contains(a.Id) && a.CategoryId.HasValue)
                .Select(a => a.CategoryId!.Value)
                .ToListAsync(ct);
            foreach (var cid in accountCatIds) resolvedCatIds.Add(cid);
        }

        foreach (var catId in resolvedCatIds)
        {
            if (existingCategoryIds.Add(catId))
            {
                filter.Categories.Add(new AccountsReportFilterCategory
                {
                    ReportFilterId = filter.Id,
                    CategoryId = catId
                });
            }
        }

        filter.UpdatedOnUtc = DateTime.UtcNow;
        filter.UpdatedByUserId = current.UserId;

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteFilterMappingAsync(int mappingId, CancellationToken ct = default)
    {
        var tenantId = tenantService.TenantId ?? current.TenantId;
        int filterId;
        int accountId;

        if (mappingId >= 100_000)
        {
            filterId = mappingId / 100_000;
            accountId = mappingId % 100_000;
        }
        else
        {
            var mapping = await db.AccountsReportFilterAccounts.AsNoTracking()
                .FirstOrDefaultAsync(m => m.ReportFilterId == mappingId, ct);
            if (mapping != null)
            {
                filterId = mapping.ReportFilterId;
                accountId = mapping.AccountId;
            }
            else
            {
                throw new KeyNotFoundException($"Mapping with ID {mappingId} was not found.");
            }
        }

        await RemoveAccountFromFilterAsync(filterId, accountId, ct);
    }

    public async Task RemoveAccountFromFilterAsync(int filterId, int accountId, CancellationToken ct = default)
    {
        var tenantId = tenantService.TenantId ?? current.TenantId;
        var filter = await db.AccountsReportFilters
            .Include(x => x.Categories)
            .Include(x => x.Accounts)
            .Include(x => x.SubAccounts)
            .FirstOrDefaultAsync(x => x.Id == filterId && (tenantId == 0 || x.TenantId == tenantId), ct)
            ?? throw new KeyNotFoundException("Report filter was not found.");

        var toRemove = filter.Accounts.Where(a => a.AccountId == accountId).ToList();
        foreach (var item in toRemove) db.AccountsReportFilterAccounts.Remove(item);

        var subToRemove = filter.SubAccounts.Where(s => s.SubAccountId == accountId).ToList();
        foreach (var item in subToRemove) db.AccountsReportFilterSubAccounts.Remove(item);

        if (filter.AccountId == accountId)
        {
            filter.AccountId = filter.Accounts.FirstOrDefault(a => a.AccountId != accountId)?.AccountId;
        }

        var account = await db.AccountsChartAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account?.CategoryId.HasValue == true)
        {
            int catId = account.CategoryId.Value;
            var remainingAccountIds = filter.Accounts
                .Where(a => a.AccountId != accountId)
                .Select(a => a.AccountId)
                .Concat(filter.SubAccounts.Where(s => s.SubAccountId != accountId).Select(s => s.SubAccountId))
                .Distinct()
                .ToList();

            bool categoryStillUsed = await db.AccountsChartAccounts.AsNoTracking()
                .AnyAsync(a => remainingAccountIds.Contains(a.Id) && a.CategoryId == catId, ct);

            if (!categoryStillUsed)
            {
                var catToRemove = filter.Categories.Where(c => c.CategoryId == catId).ToList();
                foreach (var c in catToRemove) db.AccountsReportFilterCategories.Remove(c);

                if (filter.CategoryId == catId)
                {
                    filter.CategoryId = filter.Categories.FirstOrDefault(c => c.CategoryId != catId)?.CategoryId;
                }
            }
        }

        filter.UpdatedOnUtc = DateTime.UtcNow;
        filter.UpdatedByUserId = current.UserId;

        await db.SaveChangesAsync(ct);
    }

    public async Task<ReportFilterDto> SaveFilterAsync(int? id, ReportFilterRequest request, CancellationToken ct = default)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Filter name is required.");
        if (name.Length > 160) throw new InvalidOperationException("Filter name must not exceed 160 characters.");
        if (request.TypeId.HasValue && request.TypeId.Value != 3 && !await db.AccountsRoznamchaTypes.AsNoTracking().AnyAsync(x => x.Id == request.TypeId.Value && x.IsActive, ct))
            throw new InvalidOperationException("Select a valid active report type.");
        var tenantId = tenantService.TenantId ?? current.TenantId;
        if (await db.AccountsReportFilters.AsNoTracking().AnyAsync(x => (tenantId == 0 || x.TenantId == tenantId) && x.Name == name && (!id.HasValue || x.Id != id.Value), ct))
            throw new InvalidOperationException("A report filter with this name already exists.");

        var categoryIds = request.CategoryIds.AppendIf(request.CategoryId).Distinct().ToList();
        var accountIds = request.AccountIds.AppendIf(request.AccountId).Distinct().ToList();
        var subAccountIds = request.SubAccountIds.Distinct().ToList();

        // Parity Fix: "All Accounts in Category" shortcut
        if (request.IsAllCategoryAccounts && categoryIds.Count > 0)
        {
            var catAccountIds = await db.AccountsChartAccounts.AsNoTracking()
                .Where(a => (tenantId == 0 || a.TenantId == tenantId) && a.CategoryId.HasValue && categoryIds.Contains(a.CategoryId.Value))
                .Select(a => a.Id)
                .ToListAsync(ct);
            foreach (var cid in catAccountIds)
            {
                if (!accountIds.Contains(cid)) accountIds.Add(cid);
            }
        }

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
            row = new AccountsReportFilter { TenantId = tenantId > 0 ? tenantId : current.TenantId, CreatedByUserId = current.UserId };
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

    public async Task<ReportRowDto> UpdateReportInlineAsync(long id, UpdateReportInlineRequest request, CancellationToken ct = default)
    {
        var tenantId = tenantService.TenantId ?? current.TenantId;
        var entry = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id && (tenantId == 0 || x.TenantId == tenantId), ct);
        if (entry == null) throw new KeyNotFoundException($"Voucher with ID {id} was not found.");

        if (request.Description != null)
            entry.Descriptions = request.Description.Trim();

        if (request.Remarks != null)
            entry.Remarks = request.Remarks.Trim();

        if (request.StatusId.HasValue && request.StatusId.Value > 0)
            entry.EnterStatusId = request.StatusId.Value;

        entry.UpdatedByUserId = current.UserId;
        entry.UpdatedOnUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        var rows = await MonthlyAsync(null, entry.TransDate, entry.TransDate, null, null, null, null, ct);
        return rows.FirstOrDefault(x => x.Id == id) ?? new ReportRowDto
        {
            Id = entry.Id,
            ReferenceNo = entry.Ref,
            Description = entry.Descriptions,
            Remarks = entry.Remarks,
            Date = entry.TransDate
        };
    }

    public async Task<AccountsDailyUpdateDto> UpdateDailyReportInlineAsync(long id, UpdateReportInlineRequest request, CancellationToken ct = default)
    {
        var tenantId = tenantService.TenantId ?? current.TenantId;
        var entry = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id && (tenantId == 0 || x.TenantId == tenantId), ct);
        if (entry == null) throw new KeyNotFoundException($"Voucher with ID {id} was not found.");

        var changes = new List<string>();
        if (request.Debit.HasValue && entry.RoznamchaTypeId == 1)
        {
            var adjustment = entry.Adjustment ?? 0;
            var oldAmt = (entry.Amount ?? 0) + adjustment;
            changes.Add($"Debit: {oldAmt} -> {request.Debit.Value}");
            entry.Amount = request.Debit.Value - adjustment;
        }
        if (request.Credit.HasValue && entry.RoznamchaTypeId == 2)
        {
            var adjustment = entry.Adjustment ?? 0;
            var oldAmt = (entry.Amount ?? 0) + adjustment;
            changes.Add($"Credit: {oldAmt} -> {request.Credit.Value}");
            entry.Amount = request.Credit.Value - adjustment;
        }
        if (request.Description != null && entry.Descriptions != request.Description.Trim())
        {
            changes.Add($"Description: '{entry.Descriptions}' -> '{request.Description.Trim()}'");
            entry.Descriptions = request.Description.Trim();
        }
        if (request.Remarks != null && entry.Remarks != request.Remarks.Trim())
        {
            changes.Add($"Remarks: '{entry.Remarks}' -> '{request.Remarks.Trim()}'");
            entry.Remarks = request.Remarks.Trim();
        }
        if (request.StatusId.HasValue && request.StatusId.Value > 0 && entry.EnterStatusId != request.StatusId.Value)
        {
            changes.Add($"StatusId: {entry.EnterStatusId} -> {request.StatusId.Value}");
            entry.EnterStatusId = request.StatusId.Value;
        }

        entry.UpdatedByUserId = current.UserId;
        entry.UpdatedOnUtc = DateTime.UtcNow;

        if (changes.Count > 0)
        {
            db.RoznamchaEntryProcessLogs.Add(new RoznamchaEntryProcessLog
            {
                TenantId = entry.TenantId,
                RoznamchaEntryId = entry.Id,
                Action = "InlineEdit",
                Notes = string.Join("; ", changes),
                ProcessedByUserId = current.UserId,
                ProcessedOnUtc = DateTime.UtcNow,
                StatusId = entry.EnterStatusId
            });
        }

        await db.SaveChangesAsync(ct);

        var list = await DailyUpdatesAsync(entry.TransDate, entry.TransDate, includeDeleted: true, byTransactionDate: true, ct: ct);
        var updated = list.FirstOrDefault(x => x.Id == id);
        if (updated != null) return updated;

        return new AccountsDailyUpdateDto
        {
            Id = entry.Id,
            ReferenceNo = entry.Ref,
            TransactionDate = entry.TransDate,
            Description = entry.Descriptions,
            Remarks = entry.Remarks,
            Debit = (entry.RoznamchaTypeId == 1) ? ((entry.Amount ?? 0) + (entry.Adjustment ?? 0)) : null,
            Credit = (entry.RoznamchaTypeId == 2) ? ((entry.Amount ?? 0) + (entry.Adjustment ?? 0)) : null,
            Attachment = entry.Attachment ?? entry.ImagePath,
            IsDeleted = entry.IsDeleted,
            UpdatedOnUtc = entry.UpdatedOnUtc
        };
    }

    public async Task<IReadOnlyList<AccountsVoucherChangeLogDto>> GetDailyReportChangesAsync(long entryId, CancellationToken ct = default)
    {
        var tenantId = tenantService.TenantId ?? current.TenantId;
        var entry = await db.RoznamchaEntries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == entryId && (tenantId == 0 || x.TenantId == tenantId), ct);
        if (entry == null) throw new KeyNotFoundException($"Voucher with ID {entryId} was not found.");

        var logs = await (from log in db.RoznamchaEntryProcessLogs.AsNoTracking()
                          join u in db.Users.AsNoTracking() on log.ProcessedByUserId equals u.Id into uRows
                          from u in uRows.DefaultIfEmpty()
                          where (tenantId == 0 || log.TenantId == tenantId) && log.RoznamchaEntryId == entryId
                          orderby log.ProcessedOnUtc descending
                          select new AccountsVoucherChangeLogDto
                          {
                              Id = log.Id,
                              EntryId = log.RoznamchaEntryId,
                              Action = log.Action,
                              Notes = log.Notes,
                              OldCheckNo = log.OldCheckNo,
                              NewCheckNo = log.NewCheckNo,
                              RemindDate = log.RemindDate,
                              StatusId = log.StatusId,
                              ProcessedBy = u != null ? (u.UserName ?? u.Email ?? log.ProcessedByUserId) : log.ProcessedByUserId,
                              ChangeDate = log.ProcessedOnUtc,
                              ProcessedOnUtc = log.ProcessedOnUtc
                          }).ToListAsync(ct);

        if (logs.Count == 0)
        {
            var creator = !string.IsNullOrWhiteSpace(entry.CreatedByUserId)
                ? await db.Users.AsNoTracking().Where(u => u.Id == entry.CreatedByUserId).Select(u => u.UserName ?? u.Email).FirstOrDefaultAsync(ct)
                : null;

            return new List<AccountsVoucherChangeLogDto>
            {
                new AccountsVoucherChangeLogDto
                {
                    Id = 0,
                    EntryId = entry.Id,
                    Action = "Created",
                    Notes = $"Voucher {entry.Ref} created",
                    ProcessedBy = creator ?? entry.CreatedByUserId ?? "System",
                    ChangeDate = entry.CreatedOnUtc,
                    ProcessedOnUtc = entry.CreatedOnUtc
                }
            };
        }

        return logs;
    }

    public async Task<IReadOnlyList<EntryAttachmentDto>> GetAttachmentsByRefAsync(string reference, CancellationToken ct = default)
    {
        var tenantId = tenantService.TenantId ?? current.TenantId;
        if (string.IsNullOrWhiteSpace(reference)) return Array.Empty<EntryAttachmentDto>();

        var trimmedRef = reference.Trim();
        var docs = await (from doc in db.AccountsEntryDocuments.AsNoTracking()
                          join entry in db.RoznamchaEntries.AsNoTracking() on doc.RoznamchaEntryId equals entry.Id
                          where (tenantId == 0 || doc.TenantId == tenantId) && entry.Ref == trimmedRef
                          orderby doc.UploadedOnUtc descending
                          select doc).ToListAsync(ct);

        return docs.Select(x => new EntryAttachmentDto
        {
            Id = x.Id,
            EntryId = x.RoznamchaEntryId,
            FileName = x.FileName,
            ContentType = x.ContentType,
            FileSizeBytes = x.FileSizeBytes,
            UploadedOnUtc = x.UploadedOnUtc,
            DocumentReference = x.DocumentReference,
            Remarks = x.Remarks
        }).ToList();
    }

    public async Task<IReadOnlyList<EntryAttachmentDto>> GetAttachmentsByEntryIdAsync(long entryId, CancellationToken ct = default)
    {
        var tenantId = tenantService.TenantId ?? current.TenantId;
        var docs = await db.AccountsEntryDocuments.AsNoTracking()
            .Where(x => (tenantId == 0 || x.TenantId == tenantId) && x.RoznamchaEntryId == entryId)
            .OrderByDescending(x => x.UploadedOnUtc)
            .ToListAsync(ct);

        return docs.Select(x => new EntryAttachmentDto
        {
            Id = x.Id,
            EntryId = x.RoznamchaEntryId,
            FileName = x.FileName,
            ContentType = x.ContentType,
            FileSizeBytes = x.FileSizeBytes,
            UploadedOnUtc = x.UploadedOnUtc,
            DocumentReference = x.DocumentReference,
            Remarks = x.Remarks
        }).ToList();
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
