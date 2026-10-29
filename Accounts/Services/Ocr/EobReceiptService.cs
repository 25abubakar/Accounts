using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Ocr.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Accounts.Services.Ocr;

public sealed class EobReceiptService : IEobReceiptService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<EobReceiptService> _logger;

    public EobReceiptService(ApplicationDbContext db, ILogger<EobReceiptService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task WriteReceiptAsync(
        string checkNumber,
        string insuranceName,
        decimal checkAmount,
        DateTime statementDate,
        string attachmentFileName,
        int orgId,
        string? userId,
        CancellationToken ct = default)
    {
        if (checkAmount <= 0 || string.IsNullOrWhiteSpace(checkNumber))
        {
            return;
        }

        // 1. Check existing roznamcha entry by InstrumentNo
        var existing = await _db.RoznamchaEntries
            .FirstOrDefaultAsync(x => x.TenantId == orgId && x.InstrumentNo == checkNumber && !x.IsDeleted, ct);

        if (existing != null)
        {
            existing.EobCheckNo = checkNumber;
            await _db.SaveChangesAsync(ct);
            return;
        }

        // 2. Resolve or create Chart Account for Insurance
        var accountName = string.IsNullOrWhiteSpace(insuranceName) ? "Unknown Insurance" : insuranceName.Trim();
        var chartAccount = await _db.AccountsChartAccounts
            .FirstOrDefaultAsync(x => x.TenantId == orgId && x.CategoryId == 37 && x.AccountName == accountName && x.IsActive, ct);

        if (chartAccount == null)
        {
            var count = await _db.AccountsChartAccounts.CountAsync(x => x.TenantId == orgId && x.CategoryId == 37, ct);
            var nextSeq = count + 1;
            chartAccount = new AccountsChartAccount
            {
                TenantId = orgId,
                AccountNumber = $"37-{nextSeq:D4}",
                AccountName = accountName,
                CategoryId = 37,
                StatusId = 11,
                AccountCode = $"37-{nextSeq}",
                IsActive = true,
                CreatedByUserId = userId,
                CreatedOnUtc = DateTime.UtcNow
            };
            _db.AccountsChartAccounts.Add(chartAccount);
            await _db.SaveChangesAsync(ct);
        }

        // 3. Create Roznamcha receipt entry
        var transDate = DateOnly.FromDateTime(statementDate);
        var entry = new RoznamchaEntry
        {
            TenantId = orgId,
            RoznamchaTypeId = 2, // Receipt
            CategoryId = 37,     // Insurance
            FromAccountId = chartAccount.Id,
            ToAccountId = 1527,  // Bank / EOB collection account
            TransDate = transDate,
            InstrumentDate = transDate,
            InstrumentNo = checkNumber,
            EobCheckNo = checkNumber,
            Descriptions = $"EOB Payment - {accountName} - Check #{checkNumber}",
            CurrencyId = 2,      // USD
            UsdAmount = checkAmount,
            Amount = 0,          // Currency is USD, so Amount = 0
            EnterStatusId = 14,
            Attachment = attachmentFileName,
            CreatedByUserId = userId,
            CreatedOnUtc = DateTime.UtcNow
        };

        _db.RoznamchaEntries.Add(entry);
        await _db.SaveChangesAsync(ct);

        // 4. Record Document Process row if applicable
        if (!string.IsNullOrWhiteSpace(attachmentFileName))
        {
            var docProc = new BillingDocumentProcess
            {
                EntryRef = checkNumber,
                Attachment = attachmentFileName,
                CreatedDate = DateTime.UtcNow
            };
            _db.BillingDocumentProcesses.Add(docProc);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task WriteHealthNetReceiptAsync(
        string checkNumber,
        string? insuranceName,
        decimal checkAmount,
        DateTime statementDate,
        string attachmentFileName,
        int orgId,
        string? userId,
        CancellationToken ct = default)
    {
        if (checkAmount <= 0 || string.IsNullOrWhiteSpace(checkNumber))
        {
            return;
        }

        var effectiveName = string.IsNullOrWhiteSpace(insuranceName) ? "Health Net" : insuranceName.Trim();

        // Idempotency: match on TenantId + RoznamchaTypeId=2 + (InstrumentNo or EobCheckNo)
        var existing = await _db.RoznamchaEntries
            .FirstOrDefaultAsync(x => x.TenantId == orgId &&
                                      x.RoznamchaTypeId == 2 &&
                                      (x.InstrumentNo == checkNumber || x.EobCheckNo == checkNumber) &&
                                      !x.IsDeleted, ct);

        if (existing != null)
        {
            existing.UsdAmount = checkAmount;
            existing.TransDate = DateOnly.FromDateTime(statementDate);
            if (!string.IsNullOrWhiteSpace(attachmentFileName))
            {
                existing.Attachment = attachmentFileName;
            }
            await _db.SaveChangesAsync(ct);
            return;
        }

        await WriteReceiptAsync(checkNumber, effectiveName, checkAmount, statementDate, attachmentFileName, orgId, userId, ct);
    }
}
