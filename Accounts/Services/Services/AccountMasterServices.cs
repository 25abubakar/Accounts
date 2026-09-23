using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class AccountCategoryService(ApplicationDbContext db, ICurrentUserService current) : IAccountCategoryService
{
    public async Task<IReadOnlyList<AccountCategoryTypeDto>> ListTypesAsync(CancellationToken ct = default) =>
        await db.AccountsCategoryTypes.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new AccountCategoryTypeDto { Id = x.Id, CategoryType = x.Name, IsActive = x.IsActive })
            .ToListAsync(ct);

    public async Task<AccountCategoryTypeDto> SaveTypeAsync(int? id, SaveAccountCategoryTypeRequest request, CancellationToken ct = default)
    {
        var name = Required(request.CategoryType, "Category type", 120);
        var duplicate = await db.AccountsCategoryTypes.AsNoTracking()
            .AnyAsync(x => x.Name == name && (!id.HasValue || x.Id != id.Value), ct);
        if (duplicate) throw new InvalidOperationException("Category type already exists.");

        AccountsCategoryType row;
        if (id.HasValue)
        {
            row = await db.AccountsCategoryTypes.FirstOrDefaultAsync(x => x.Id == id.Value, ct)
                ?? throw new KeyNotFoundException("Category type was not found.");
            row.Name = name;
            row.IsActive = request.IsActive;
            row.UpdatedByUserId = current.UserId;
            row.UpdatedOnUtc = DateTime.UtcNow;
        }
        else
        {
            row = new AccountsCategoryType
            {
                TenantId = current.TenantId,
                Name = name,
                IsActive = request.IsActive,
                CreatedByUserId = current.UserId
            };
            db.AccountsCategoryTypes.Add(row);
        }
        await db.SaveChangesAsync(ct);
        return new AccountCategoryTypeDto { Id = row.Id, CategoryType = row.Name, IsActive = row.IsActive };
    }

    public async Task DeleteTypeAsync(int id, CancellationToken ct = default)
    {
        var row = await db.AccountsCategoryTypes.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Category type was not found.");
        if (await db.AccountsCategories.AnyAsync(x => x.CategoryTypeId == id, ct))
            throw new InvalidOperationException("Category type cannot be deleted because categories use it.");
        db.AccountsCategoryTypes.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AccountCategoryDto>> ListAsync(CancellationToken ct = default) =>
        await SpListQuery.ExecAsync<AccountCategoryDto>(
            db,
            "EXEC dbo.usp_Accounts_CategoryList @TenantId",
            ct,
            SpListQuery.TenantId(current.TenantId));

    public Task<AccountCategoryDto?> GetAsync(int id, CancellationToken ct = default) =>
        CategoryQuery().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<AccountCategoryDto> SaveAsync(int? id, SaveAccountCategoryRequest request, CancellationToken ct = default)
    {
        var name = Required(request.Name, "Category name", 120);
        var code = Required(request.Code, "Category code", 40).ToUpperInvariant();
        if (request.BudgetAmount < 0) throw new InvalidOperationException("Budget amount cannot be negative.");
        if (request.CategoryTypeId.HasValue && !await db.AccountsCategoryTypes.AnyAsync(x => x.Id == request.CategoryTypeId && x.IsActive, ct))
            throw new InvalidOperationException("Category type is invalid or inactive.");
        if (await db.AccountsCategories.AsNoTracking().AnyAsync(x => (x.Name == name || x.Code == code) && (!id.HasValue || x.Id != id.Value), ct))
            throw new InvalidOperationException("Category name or code already exists.");

        AccountsCategory row;
        if (id.HasValue)
        {
            row = await db.AccountsCategories.FirstOrDefaultAsync(x => x.Id == id.Value, ct)
                ?? throw new KeyNotFoundException("Account category was not found.");
            row.UpdatedByUserId = current.UserId;
            row.UpdatedOnUtc = DateTime.UtcNow;
        }
        else
        {
            row = new AccountsCategory { TenantId = current.TenantId, CreatedByUserId = current.UserId };
            db.AccountsCategories.Add(row);
        }
        row.CategoryTypeId = request.CategoryTypeId;
        row.ReferenceNumber = Clean(request.ReferenceNo, 80);
        row.Name = name;
        row.Number = Clean(request.Number, 80);
        row.Code = code;
        row.BudgetAmount = decimal.Round(request.BudgetAmount, 2);
        if (request.UsedAmount < 0) throw new InvalidOperationException("Used amount cannot be negative.");
        row.UsedAmount = decimal.Round(request.UsedAmount, 2);
        row.BalanceAmount = decimal.Round(row.BudgetAmount - row.UsedAmount, 2);
        row.IsNotInReport = request.IsNotInReport;
        row.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);

        // Legacy-style Ref: LT-Cat-{Id} when not supplied.
        if (string.IsNullOrWhiteSpace(row.ReferenceNumber))
        {
            row.ReferenceNumber = $"LT-Cat-{row.Id}";
            await db.SaveChangesAsync(ct);
        }

        return await GetAsync(row.Id, ct) ?? throw new InvalidOperationException("Saved category could not be reloaded.");
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var row = await db.AccountsCategories.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Account category was not found.");
        if (await db.AccountsChartAccounts.AnyAsync(x => x.CategoryId == id, ct) ||
            await db.RoznamchaEntries.AnyAsync(x => x.CategoryId == id, ct))
            throw new InvalidOperationException("Category cannot be deleted because accounts or transactions use it.");
        db.AccountsCategories.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    private IQueryable<AccountCategoryDto> CategoryQuery() =>
        from category in db.AccountsCategories.AsNoTracking()
        join type in db.AccountsCategoryTypes.AsNoTracking() on category.CategoryTypeId equals type.Id into typeRows
        from type in typeRows.DefaultIfEmpty()
        select new AccountCategoryDto
        {
            Id = category.Id,
            CategoryTypeId = category.CategoryTypeId,
            CategoryType = type == null ? null : type.Name,
            ReferenceNo = category.ReferenceNumber,
            Name = category.Name,
            Number = category.Number,
            Code = category.Code,
            BudgetAmount = category.BudgetAmount,
            UsedAmount = category.UsedAmount,
            BalanceAmount = category.BalanceAmount,
            IsNotInReport = category.IsNotInReport,
            IsActive = category.IsActive
        };

    private static string Required(string? value, string label, int max)
    {
        var result = value?.Trim();
        if (string.IsNullOrWhiteSpace(result)) throw new InvalidOperationException($"{label} is required.");
        return result.Length <= max ? result : result[..max];
    }

    private static string? Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}

public sealed class AccountService(
    ApplicationDbContext db,
    ICurrentUserService current,
    IReferenceGeneratorService references) : IAccountService
{
    public Task<IReadOnlyList<AccountDto>> ListAsync(int? categoryId, int? parentId, bool activeOnly, CancellationToken ct = default) =>
        ExecChartListAsync(categoryId, parentId, activeOnly, mainOnly: false, ct);

    public Task<IReadOnlyList<AccountDto>> ListMainAsync(int? categoryId, CancellationToken ct = default) =>
        ExecChartListAsync(categoryId, parentId: null, activeOnly: true, mainOnly: true, ct);

    public Task<AccountDto?> GetAsync(int id, CancellationToken ct = default) =>
        AccountQuery().FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<AccountDto>> ListSubAccountsAsync(int id, CancellationToken ct = default)
    {
        if (!await db.AccountsChartAccounts.AsNoTracking().AnyAsync(x => x.Id == id, ct))
            throw new KeyNotFoundException("Account was not found.");
        return await ExecChartListAsync(categoryId: null, parentId: id, activeOnly: false, mainOnly: false, ct);
    }

    private async Task<IReadOnlyList<AccountDto>> ExecChartListAsync(
        int? categoryId,
        int? parentId,
        bool activeOnly,
        bool mainOnly,
        CancellationToken ct) =>
        await SpListQuery.ExecAsync<AccountDto>(
            db,
            "EXEC dbo.usp_Accounts_ChartAccountList @TenantId, @CategoryId, @ParentId, @ActiveOnly, @MainOnly",
            ct,
            SpListQuery.TenantId(current.TenantId),
            SpListQuery.IntNullable("@CategoryId", categoryId),
            SpListQuery.IntNullable("@ParentId", parentId),
            SpListQuery.Bit("@ActiveOnly", activeOnly),
            SpListQuery.Bit("@MainOnly", mainOnly));

    public async Task<AccountDto> CreateAsync(SaveAccountRequest request, CancellationToken ct = default)
    {
        var name = RequiredName(request.AccountName);
        AccountsChartAccount? parent = null;
        AccountsCategory category;
        if (request.ParentId.HasValue)
        {
            parent = await db.AccountsChartAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.ParentId.Value, ct)
                ?? throw new InvalidOperationException("Parent account was not found.");
            if (!parent.CategoryId.HasValue) throw new InvalidOperationException("Parent account has no category.");
            category = await db.AccountsCategories.AsNoTracking().FirstAsync(x => x.Id == parent.CategoryId.Value, ct);
        }
        else
        {
            if (!request.CategoryId.HasValue) throw new InvalidOperationException("Category is required for a main account.");
            category = await db.AccountsCategories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.CategoryId.Value && x.IsActive, ct)
                ?? throw new InvalidOperationException("Account category was not found or is inactive.");
        }

        if (await db.AccountsChartAccounts.AsNoTracking().AnyAsync(x => x.AccountName == name && x.ParentId == request.ParentId, ct))
            throw new InvalidOperationException("An account with this name already exists at the selected level.");

        var code = parent == null
            ? await references.MainAccountCodeAsync(current.TenantId, category.Code, ct)
            : await references.SubAccountCodeAsync(current.TenantId, parent.Id, parent.AccountCode ?? parent.AccountNumber, ct);
        var row = new AccountsChartAccount
        {
            TenantId = current.TenantId,
            CategoryId = category.Id,
            ParentId = parent?.Id,
            AccountName = name,
            AccountCode = code,
            AccountNumber = await references.AccountNumberAsync(current.TenantId, code, ct),
            AccountReference = await references.AccountReferenceAsync(current.TenantId, ct),
            CreatedByUserId = current.UserId,
            CreatedOnUtc = DateTime.UtcNow
        };
        ApplyMutable(row, request);
        db.AccountsChartAccounts.Add(row);
        await db.SaveChangesAsync(ct);
        return await GetAsync(row.Id, ct) ?? throw new InvalidOperationException("Saved account could not be reloaded.");
    }

    public async Task<AccountDto> UpdateAsync(int id, SaveAccountRequest request, CancellationToken ct = default)
    {
        var row = await db.AccountsChartAccounts.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Account was not found.");
        var name = RequiredName(request.AccountName);
        if (await db.AccountsChartAccounts.AsNoTracking().AnyAsync(x => x.Id != id && x.ParentId == row.ParentId && x.AccountName == name, ct))
            throw new InvalidOperationException("An account with this name already exists at the selected level.");
        row.AccountName = name;
        ApplyMutable(row, request);
        row.UpdatedByUserId = current.UserId;
        row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(row.Id, ct) ?? throw new InvalidOperationException("Updated account could not be reloaded.");
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var row = await db.AccountsChartAccounts.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Account was not found.");
        if (await db.RoznamchaEntries.AnyAsync(x => x.FromAccountId == id || x.ToAccountId == id, ct))
            throw new InvalidOperationException("Account cannot be deleted because transactions already exist.");
        if (await db.AccountsChartAccounts.AnyAsync(x => x.ParentId == id, ct))
            throw new InvalidOperationException("Account cannot be deleted while it has subaccounts.");
        db.AccountsChartAccounts.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AccountDto> UpdateBudgetAsync(int id, UpdateAccountBudgetRequest request, CancellationToken ct = default)
    {
        if (request.BudgetAmount < 0 || request.AccountLimit < 0)
            throw new InvalidOperationException("Budget and account limit cannot be negative.");
        var row = await db.AccountsChartAccounts.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Account was not found.");
        row.BudgetAmount = decimal.Round(request.BudgetAmount, 2);
        row.AccountLimit = decimal.Round(request.AccountLimit, 2);
        row.BalanceAmount = row.BudgetAmount - row.UsedAmount;
        row.UpdatedByUserId = current.UserId;
        row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct) ?? throw new InvalidOperationException("Updated account could not be reloaded.");
    }

    public async Task<IReadOnlyList<AccountLedgerDto>> LedgerAsync(int id, DateOnly? from, DateOnly? to, bool hidden, CancellationToken ct = default)
    {
        if (!await db.AccountsChartAccounts.AsNoTracking().AnyAsync(x => x.Id == id, ct))
            throw new KeyNotFoundException("Account was not found.");
        var query = db.RoznamchaEntries.AsNoTracking()
            .Where(x => !x.IsDeleted && (x.FromAccountId == id || x.ToAccountId == id));
        query = hidden ? query.Where(x => !x.IsShow) : query.Where(x => x.IsShow && !x.IsLedger);
        if (from.HasValue) query = query.Where(x => x.TransDate >= from.Value);
        if (to.HasValue) query = query.Where(x => x.TransDate <= to.Value);

        var rows = await (
            from e in query
            join roz in db.AccountsRoznamchaTypes.AsNoTracking() on e.RoznamchaTypeId equals roz.Id into rozRows
            from roz in rozRows.DefaultIfEmpty()
            join cat in db.AccountsCategories.AsNoTracking() on e.CategoryId equals cat.Id into catRows
            from cat in catRows.DefaultIfEmpty()
            join fromAcc in db.AccountsChartAccounts.AsNoTracking() on e.FromAccountId equals fromAcc.Id into fromRows
            from fromAcc in fromRows.DefaultIfEmpty()
            join toAcc in db.AccountsChartAccounts.AsNoTracking() on e.ToAccountId equals toAcc.Id into toRows
            from toAcc in toRows.DefaultIfEmpty()
            join tt in db.AccountsTransTypes.AsNoTracking() on e.TransTypeId equals tt.Id into ttRows
            from tt in ttRows.DefaultIfEmpty()
            join tm in db.AccountsTransModes.AsNoTracking() on e.TransModeId equals tm.Id into tmRows
            from tm in tmRows.DefaultIfEmpty()
            orderby e.TransDate, e.Id
            select new
            {
                e.Id,
                e.SNo,
                e.Ref,
                e.OldRef,
                Type = roz == null ? null : roz.Name,
                e.TransDate,
                Category = cat == null ? null : cat.Name,
                FromAcc = fromAcc == null ? null : fromAcc.AccountName,
                FromAccNo = fromAcc == null ? null : fromAcc.AccountNumber,
                ToAcct = toAcc == null ? null : toAcc.AccountName,
                ToAcctNo = toAcc == null ? null : toAcc.AccountNumber,
                TransType = tt == null ? null : tt.Name,
                TransMode = tm == null ? null : tm.Name,
                e.InstrumentDate,
                e.InstrumentNo,
                e.Descriptions,
                e.Amount,
                e.Adjustment,
                e.FromAccountId,
                e.ToAccountId,
                e.IsShow
            }).ToListAsync(ct);

        decimal running = 0;
        return rows.Select(x =>
        {
            var amount = (x.Amount ?? 0) + (x.Adjustment ?? 0);
            var debit = x.ToAccountId == id ? amount : 0;
            var credit = x.FromAccountId == id ? amount : 0;
            running += debit - credit;
            return new AccountLedgerDto
            {
                EntryId = x.Id,
                SNo = x.SNo,
                ReferenceNo = x.Ref,
                OldRef = x.OldRef,
                Type = x.Type,
                TransactionDate = x.TransDate,
                Category = x.Category,
                FromAcc = x.FromAcc,
                FromAccNo = x.FromAccNo,
                ToAcct = x.ToAcct,
                ToAcctNo = x.ToAcctNo,
                TransType = x.TransType,
                TransMode = x.TransMode,
                InstrumentDate = x.InstrumentDate,
                InstrumentNo = x.InstrumentNo,
                Descriptions = x.Descriptions,
                OtherAccount = x.FromAccountId == id ? x.ToAcct : x.FromAcc,
                Debit = debit,
                Credit = credit,
                RunningBalance = running,
                IsHidden = !x.IsShow
            };
        }).ToList();
    }

    public async Task TransferLedgerAsync(LedgerTransferRequest request, CancellationToken ct = default)
    {
        var entry = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == request.EntryId && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Ledger entry was not found.");
        if (!request.IncludeHidden && !entry.IsShow)
            throw new InvalidOperationException("Hidden ledger entry transfer requires includeHidden=true.");
        if (!await db.AccountsChartAccounts.AsNoTracking().AnyAsync(x => x.Id == request.TransferAccountId && x.IsActive, ct))
            throw new InvalidOperationException("Transfer account was not found or is inactive.");
        var side = request.TransferSide.Trim().ToLowerInvariant();
        if (side == "from") entry.FromAccountId = request.TransferAccountId;
        else if (side == "to") entry.ToAccountId = request.TransferAccountId;
        else throw new InvalidOperationException("transferSide must be 'from' or 'to'.");
        if (entry.FromAccountId == entry.ToAccountId)
            throw new InvalidOperationException("Cannot transfer both sides of a transaction to the same account.");
        entry.UpdatedByUserId = current.UserId;
        entry.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private IQueryable<AccountDto> AccountQuery() =>
        from account in db.AccountsChartAccounts.AsNoTracking()
        join category in db.AccountsCategories.AsNoTracking() on account.CategoryId equals category.Id into categoryRows
        from category in categoryRows.DefaultIfEmpty()
        select new AccountDto
        {
            Id = account.Id,
            CategoryId = account.CategoryId,
            CategoryName = category == null ? null : category.Name,
            ParentId = account.ParentId,
            AccountName = account.AccountName,
            AccountNo = account.AccountNumber,
            AccountCode = account.AccountCode,
            AccountReference = account.AccountReference,
            BankAccountNo = account.BankAccountNumber,
            CnicNtn = account.CnicNtn,
            Address = account.Address,
            FullName = account.FullName,
            Email = account.Email,
            Phone = account.Phone,
            DesignationId = account.DesignationId,
            PersonId = account.PersonId,
            Description = account.Description,
            Attachment = account.Attachment,
            BudgetAmount = account.BudgetAmount,
            UsedAmount = account.UsedAmount,
            BalanceAmount = account.BalanceAmount,
            AccountLimit = account.AccountLimit,
            Credit = account.Credit,
            Debit = account.Debit,
            StatusId = account.StatusId,
            IsStatement = account.IsStatement,
            IsInventory = account.IsInventory,
            IsStaff = account.IsStaff,
            IsActive = account.IsActive
        };

    private static void ApplyMutable(AccountsChartAccount row, SaveAccountRequest request)
    {
        row.BankAccountNumber = Clean(request.BankAccountNo, 100);
        row.CnicNtn = Clean(request.CnicNtn, 100);
        row.Address = Clean(request.Address, 500);
        row.FullName = Clean(request.FullName, 200);
        row.Email = Clean(request.Email, 200);
        row.Phone = Clean(request.Phone, 50);
        row.DesignationId = request.DesignationId;
        row.PersonId = request.PersonId;
        row.Description = Clean(request.Description, 2000);
        row.Attachment = Clean(request.Attachment, 500);
        if (!string.IsNullOrWhiteSpace(request.OldAccountNo))
            row.AccountReference = Clean(request.OldAccountNo, 80);
        row.BudgetAmount = decimal.Round(request.BudgetAmount, 2);
        row.AccountLimit = decimal.Round(request.AccountLimit, 2);
        row.Credit = decimal.Round(request.Credit, 2);
        row.Debit = decimal.Round(request.Debit, 2);
        row.BalanceAmount = row.BudgetAmount - row.UsedAmount;
        row.StatusId = request.StatusId <= 0 ? 11 : request.StatusId;
        row.IsStatement = request.IsStatement;
        row.IsInventory = request.IsInventory;
        row.IsStaff = request.IsStaff;
        row.IsActive = request.IsActive;
    }

    private static string RequiredName(string? value)
    {
        var result = value?.Trim();
        if (string.IsNullOrWhiteSpace(result)) throw new InvalidOperationException("Account name is required.");
        return result.Length <= 200 ? result : result[..200];
    }

    private static string? Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
