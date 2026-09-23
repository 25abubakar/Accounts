using Accounts.Data;
using Accounts.Models.SpListRows;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class AccountsRoznamchaService(ApplicationDbContext db) : IAccountsRoznamchaService
{
    public Task<IReadOnlyList<PaymentRozListRow>> ListPaymentRozAsync(
        int tenantId,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken = default) =>
        ListRoznamchaAsync(tenantId, dateFrom, dateTo, "PAYMENT", true, cancellationToken);

    public Task<IReadOnlyList<PaymentRozListRow>> ListReceiptRozAsync(
        int tenantId,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken = default) =>
        ListRoznamchaAsync(tenantId, dateFrom, dateTo, "RECEIPT", false, cancellationToken);

    private async Task<IReadOnlyList<PaymentRozListRow>> ListRoznamchaAsync(
        int tenantId,
        DateOnly dateFrom,
        DateOnly dateTo,
        string typeCode,
        bool isPayment,
        CancellationToken cancellationToken)
    {
        var settings = await db.AccountsModuleSettings.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => new { x.BillingCategoryId, x.PaymentRozTypeId, x.ReceiptRozTypeId })
            .FirstOrDefaultAsync(cancellationToken);

        var configuredTypeId = isPayment ? settings?.PaymentRozTypeId : settings?.ReceiptRozTypeId;
        var roznamchaTypeId = configuredTypeId ?? await db.AccountsRoznamchaTypes.AsNoTracking()
            .Where(x => x.IsActive && x.Code == typeCode)
            .OrderByDescending(x => x.TenantId.HasValue)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!roznamchaTypeId.HasValue)
            return Array.Empty<PaymentRozListRow>();

        var query =
            from entry in db.RoznamchaEntries.AsNoTracking()
            where entry.TenantId == tenantId
                  && !entry.IsDeleted
                  && entry.TransDate >= dateFrom
                  && entry.TransDate <= dateTo
                  && (settings == null || !settings.BillingCategoryId.HasValue || entry.CategoryId == settings.BillingCategoryId)
                  && entry.RoznamchaTypeId == roznamchaTypeId.Value
            join rozType in db.AccountsRoznamchaTypes.AsNoTracking() on entry.RoznamchaTypeId equals rozType.Id into rozTypes
            from rozType in rozTypes.DefaultIfEmpty()
            join project in db.AccountsProjects.AsNoTracking() on entry.ProjectId equals project.Id into projects
            from project in projects.DefaultIfEmpty()
            join category in db.AccountsCategories.AsNoTracking() on entry.CategoryId equals category.Id into categories
            from category in categories.DefaultIfEmpty()
            join fromAccount in db.AccountsChartAccounts.AsNoTracking() on entry.FromAccountId equals fromAccount.Id into fromAccounts
            from fromAccount in fromAccounts.DefaultIfEmpty()
            join toAccount in db.AccountsChartAccounts.AsNoTracking() on entry.ToAccountId equals toAccount.Id into toAccounts
            from toAccount in toAccounts.DefaultIfEmpty()
            join toCategory in db.AccountsCategories.AsNoTracking() on toAccount.CategoryId equals toCategory.Id into toCategories
            from toCategory in toCategories.DefaultIfEmpty()
            join transType in db.AccountsTransTypes.AsNoTracking() on entry.TransTypeId equals transType.Id into transTypes
            from transType in transTypes.DefaultIfEmpty()
            join transMode in db.AccountsTransModes.AsNoTracking() on entry.TransModeId equals transMode.Id into transModes
            from transMode in transModes.DefaultIfEmpty()
            join status in db.AccountsEntryStatuses.AsNoTracking() on entry.EnterStatusId equals status.Id into statuses
            from status in statuses.DefaultIfEmpty()
            orderby entry.TransDate descending, entry.Id descending
            select new PaymentRozListRow
            {
                Id = entry.Id,
                SNo = entry.SNo,
                Ref = entry.Ref,
                OldRef = entry.OldRef,
                TypeName = rozType == null ? null : rozType.Name,
                Project = project == null ? null : project.Name,
                ProjectId = entry.ProjectId,
                ToCategoryId = toCategory == null ? null : toCategory.Id,
                CategoryName = category == null ? null : category.Name,
                AccountName = fromAccount == null ? null : fromAccount.AccountName,
                ToAccountId = entry.ToAccountId,
                FromAccountNumber = fromAccount == null ? null : fromAccount.AccountNumber,
                ToCategoryName = toCategory == null ? null : toCategory.Name,
                ToAccountName = toAccount == null ? null : toAccount.AccountName,
                ToAccountNumber = toAccount == null ? null : toAccount.AccountNumber,
                TransTypeName = transType == null ? null : transType.Name,
                TransModeName = transMode == null ? null : transMode.Name,
                CreatedDate = entry.CreatedOnUtc,
                BankRef = entry.BankRef,
                InstrumentNo = entry.InstrumentNo,
                Descriptions = entry.Descriptions,
                TransDate = entry.TransDate,
                Adjustment = entry.Adjustment,
                Debit = entry.Amount,
                UsdDebit = entry.UsdAmount,
                Qty = entry.Qty,
                Rate = entry.Rate,
                StatusName = status == null ? null : status.Name,
                IsDeleted = entry.IsDeleted,
                IsApproved = entry.IsApproved,
                IsLocked = entry.IsLocked,
                Attachment = entry.Attachment,
                Image = entry.ImagePath,
                LibRef = entry.LibRef,
                Remarks = entry.Remarks
            };

        return await query.ToListAsync(cancellationToken);
    }
}
