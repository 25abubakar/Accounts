using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Accounts.Services.Services;

public sealed class CopayService(
    ApplicationDbContext db,
    ICurrentUserService current,
    ILogger<CopayService> logger) : ICopayService
{
    private const int CopayCategoryId = 54;
    private const int CopayHoldingAccountId = 1567;
    private const int ReceiptRoznamchaTypeId = 2;

    private const int StatusOpen = 14;
    private const int StatusSettled = 20;
    private const int StatusMerchantFee = 1060;
    private const int StatusSettledManually = 1066;

    static CopayService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // ── Child Module 1: Merchant To Bank ───────────────────────────────────────

    public async Task<PagedResult<MerchantToBankItemDto>> GetMerchantToBankListAsync(
        CopayFilterDto filter, CancellationToken ct = default)
    {
        var tenantId = current.TenantId;
        var query = db.RoznamchaEntries.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.CategoryId == CopayCategoryId && !x.IsShow);

        if (filter.DateFrom.HasValue)
            query = query.Where(x => x.TransDate >= filter.DateFrom.Value);

        if (filter.DateTo.HasValue)
            query = query.Where(x => x.TransDate <= filter.DateTo.Value);

        if (filter.IsSettled.HasValue)
            query = query.Where(x => x.IsSettled == filter.IsSettled.Value);

        if (filter.StatusId.HasValue)
            query = query.Where(x => x.EnterStatusId == filter.StatusId.Value);

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            query = query.Where(x =>
                (x.Ref != null && x.Ref.Contains(term)) ||
                (x.BankLtRef != null && x.BankLtRef.Contains(term)) ||
                (x.BankRef != null && x.BankRef.Contains(term)) ||
                (x.InstrumentNo != null && x.InstrumentNo.Contains(term)) ||
                (x.Remarks != null && x.Remarks.Contains(term)));
        }

        var totalCount = await query.CountAsync(ct);
        var page = Math.Max(1, filter.PageNumber);
        var pageSize = Math.Clamp(filter.PageSize, 1, 200);

        var entries = await query
            .OrderByDescending(x => x.TransDate)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Pre-fetch matched bank statements for items that have BankLtRef or Ref
        var refs = entries.Where(x => x.IsSettled && x.Ref != null).Select(x => x.Ref!).ToHashSet();
        var bankMatches = refs.Count > 0
            ? await db.BankStatements.AsNoTracking()
                .Where(b => b.TenantId == tenantId && b.ReferenceNumber != null && refs.Contains(b.ReferenceNumber))
                .ToDictionaryAsync(b => b.ReferenceNumber!, ct)
            : new Dictionary<string, BankStatement>();

        var items = entries.Select(e =>
        {
            bankMatches.TryGetValue(e.Ref ?? string.Empty, out var matchedBank);
            return new MerchantToBankItemDto
            {
                Id = e.Id,
                Ref = e.Ref,
                TransDate = e.TransDate,
                UsdAmount = e.UsdAmount,
                Amount = e.Amount,
                BankLtRef = e.BankLtRef,
                BankRef = e.BankRef,
                EnterStatusId = e.EnterStatusId,
                EnterStatusName = GetStatusName(e.EnterStatusId),
                IsSettled = e.IsSettled,
                Remarks = e.Remarks,
                MatchedBankStatementId = matchedBank?.Id,
                BankPostingDate = matchedBank?.PostingDate,
                BankCredit = matchedBank?.Credit
            };
        }).ToList();

        return new PagedResult<MerchantToBankItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };
    }

    public async Task<SettlementResultDto> SettleMerchantToBankAutoAsync(
        DateRangeDto dateRange, CancellationToken ct = default)
    {
        var tenantId = current.TenantId;
        var tolerance = Math.Clamp(dateRange.ToleranceDays ?? 7, 0, 30);

        // Fetch unsettled batch copay entries in the specified date range
        var entries = await db.RoznamchaEntries
            .Where(x => x.TenantId == tenantId
                     && x.CategoryId == CopayCategoryId
                     && !x.IsShow
                     && !x.IsSettled
                     && x.TransDate >= dateRange.DateFrom
                     && x.TransDate <= dateRange.DateTo)
            .OrderBy(x => x.TransDate)
            .ToListAsync(ct);

        if (entries.Count == 0)
        {
            return new SettlementResultDto
            {
                MatchedCount = 0,
                FailedCount = 0,
                TotalSettledAmount = 0,
                Messages = ["No unsettled merchant batch entries found in the specified date range."]
            };
        }

        // Fetch candidate unsettled bank statements
        var maxBankDate = dateRange.DateTo.AddDays(tolerance);
        var bankStatements = await db.BankStatements
            .Where(b => b.TenantId == tenantId
                     && !b.IsSettled
                     && b.PostingDate != null
                     && b.PostingDate >= dateRange.DateFrom
                     && b.PostingDate <= maxBankDate)
            .ToListAsync(ct);

        var matchedCount = 0;
        decimal totalSettled = 0;
        var messages = new List<string>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var entry in entries)
        {
            var entryAmount = entry.UsdAmount ?? entry.Amount ?? 0;
            BankStatement? matchedBank = null;

            if (entryAmount >= 0)
            {
                // Positive batch deposit -> matches bank statement Credit
                // 1. Same-day match
                matchedBank = bankStatements.FirstOrDefault(b =>
                    !b.IsSettled && b.PostingDate == entry.TransDate && b.Credit == entryAmount);

                // 2. Tolerance window check (1 to tolerance days ahead)
                if (matchedBank == null)
                {
                    for (var i = 1; i <= tolerance; i++)
                    {
                        var targetDate = entry.TransDate.AddDays(i);
                        matchedBank = bankStatements.FirstOrDefault(b =>
                            !b.IsSettled && b.PostingDate == targetDate && b.Credit == entryAmount);
                        if (matchedBank != null) break;
                    }
                }
            }
            else
            {
                // Negative amount (chargeback / refund) -> matches bank statement Debit
                var absAmount = Math.Abs(entryAmount);
                matchedBank = bankStatements.FirstOrDefault(b =>
                    !b.IsSettled && b.PostingDate == entry.TransDate && b.Debit == absAmount);

                if (matchedBank == null)
                {
                    for (var i = 1; i <= tolerance; i++)
                    {
                        var targetDate = entry.TransDate.AddDays(i);
                        matchedBank = bankStatements.FirstOrDefault(b =>
                            !b.IsSettled && b.PostingDate == targetDate && b.Debit == absAmount);
                        if (matchedBank != null) break;
                    }
                }
            }

            if (matchedBank != null)
            {
                // Update bank statement
                matchedBank.IsSettled = true;
                matchedBank.ReferenceNumber = entry.Ref;
                matchedBank.StatusId = StatusSettled;
                matchedBank.MatchedRoznamchaEntryId = entry.Id;
                matchedBank.UpdatedByUserId = current.UserId;
                matchedBank.UpdatedOnUtc = DateTime.UtcNow;

                // Update roznamcha entry
                entry.IsSettled = true;
                entry.BankLtRef = !string.IsNullOrWhiteSpace(matchedBank.BankRef)
                    ? matchedBank.BankRef
                    : (!string.IsNullOrWhiteSpace(matchedBank.TransactionReferenceNumber)
                        ? matchedBank.TransactionReferenceNumber
                        : $"BANK-{matchedBank.Id}");
                entry.EnterStatusId = StatusSettled;
                entry.ToAccountId = matchedBank.ChartAccountId;
                entry.UpdatedByUserId = current.UserId;
                entry.UpdatedOnUtc = DateTime.UtcNow;

                db.RoznamchaEntryProcessLogs.Add(new RoznamchaEntryProcessLog
                {
                    TenantId = tenantId,
                    RoznamchaEntryId = entry.Id,
                    Action = "SettledMerchantToBank",
                    Notes = $"Auto-matched with Bank Statement #{matchedBank.Id} (Posting: {matchedBank.PostingDate:yyyy-MM-dd}, Ref: {matchedBank.BankRef})",
                    ProcessedByUserId = current.UserId,
                    ProcessedOnUtc = DateTime.UtcNow
                });

                matchedCount++;
                totalSettled += Math.Abs(entryAmount);
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        messages.Add($"Settlement completed: {matchedCount} batch entries matched with bank statements totaling ${totalSettled:N2}.");
        return new SettlementResultDto
        {
            MatchedCount = matchedCount,
            FailedCount = entries.Count - matchedCount,
            TotalSettledAmount = totalSettled,
            Messages = messages
        };
    }

    // ── Child Module 2: Copay IMS To Merchant Statement ────────────────────────

    public async Task<PagedResult<MerchantStatementItemDto>> GetMerchantStatementsAsync(
        CopayFilterDto filter, CancellationToken ct = default)
    {
        var tenantId = current.TenantId;
        var query = db.CopayStatements.AsNoTracking().Where(x => x.TenantId == tenantId);

        if (filter.DateFrom.HasValue)
            query = query.Where(x => x.PaymentDate >= filter.DateFrom.Value);

        if (filter.DateTo.HasValue)
            query = query.Where(x => x.PaymentDate <= filter.DateTo.Value);

        if (filter.IsSettled.HasValue)
            query = query.Where(x => x.IsSettled == filter.IsSettled.Value);

        if (filter.StatusId.HasValue)
            query = query.Where(x => x.StatusId == filter.StatusId.Value);

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            query = query.Where(x =>
                (x.Ref != null && x.Ref.Contains(term)) ||
                (x.CopayRef != null && x.CopayRef.Contains(term)) ||
                (x.CardNumber != null && x.CardNumber.Contains(term)) ||
                (x.PaymentNo != null && x.PaymentNo.Contains(term)) ||
                (x.GBOK_BatchID != null && x.GBOK_BatchID.Contains(term)) ||
                (x.ARNNumber != null && x.ARNNumber.Contains(term)) ||
                (x.BusinessName != null && x.BusinessName.Contains(term)));
        }

        var totalCount = await query.CountAsync(ct);
        var page = Math.Max(1, filter.PageNumber);
        var pageSize = Math.Clamp(filter.PageSize, 1, 200);

        var rows = await query
            .OrderByDescending(x => x.PaymentDate)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = rows.Select(r => new MerchantStatementItemDto
        {
            Id = r.Id,
            Ref = r.Ref,
            AcctId = r.AcctId,
            CardNumber = r.CardNumber,
            Currency = r.Currency,
            PaymentNo = r.PaymentNo,
            PaymentDate = r.PaymentDate,
            TransactionAmount = r.TransactionAmount,
            FundSource = r.FundSource,
            BusinessName = r.BusinessName,
            MerchantID = r.MerchantID,
            PaymentType = r.PaymentType,
            AdjustmentDescription = r.AdjustmentDescription,
            GBOK_BatchID = r.GBOK_BatchID,
            InterchangeDescription = r.InterchangeDescription,
            ARNNumber = r.ARNNumber,
            CopayRef = r.CopayRef,
            Attachment = r.Attachment,
            Remarks = r.Remarks,
            IsSettled = r.IsSettled,
            StatusId = r.StatusId,
            StatusName = GetStatusName(r.StatusId),
            IsManual = r.IsManual,
            MatchedRoznamchaEntryId = r.MatchedRoznamchaEntryId
        }).ToList();

        return new PagedResult<MerchantStatementItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };
    }

    public async Task<int> ImportMerchantStatementExcelAsync(
        UploadMerchantStatementDto dto, CancellationToken ct = default)
    {
        if (dto.File == null || dto.File.Length == 0)
            throw new InvalidOperationException("Please select an Excel or CSV statement file to upload.");

        var ext = Path.GetExtension(dto.File.FileName).ToLowerInvariant();
        if (ext is not (".xlsx" or ".xls" or ".csv"))
            throw new InvalidOperationException("Supported file types are .xlsx, .xls, and .csv.");

        var tenantId = current.TenantId;
        var table = ReadFirstWorksheet(dto.File);
        if (table == null || table.Rows.Count == 0)
            throw new InvalidOperationException("The uploaded statement contains no rows.");

        var headers = BuildHeaderIndexMap(table);
        var tenantPrefix = await ResolveTenantPrefixAsync(tenantId, ct);

        // Find the current highest numeric reference for statement rows
        var maxExistingSeq = await GetMaxStatementSequenceAsync(tenantId, tenantPrefix, ct);

        // Pre-fetch active statement fingerprints to avoid duplicates
        var existingFingerprints = await db.CopayStatements.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.PaymentDate != null && x.TransactionAmount != null)
            .Select(x => new
            {
                x.PaymentDate,
                x.TransactionAmount,
                Card = x.CardNumber ?? "",
                PNo = x.PaymentNo ?? "",
                Batch = x.GBOK_BatchID ?? "",
                Arn = x.ARNNumber ?? ""
            })
            .ToListAsync(ct);

        var fingerprintSet = existingFingerprints
            .Select(x => $"{x.PaymentDate:yyyyMMdd}|{x.TransactionAmount:F2}|{x.Card}|{x.PNo}|{x.Batch}|{x.Arn}")
            .ToHashSet();

        var importedStatements = new List<CopayStatement>();

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            var amountStr = ReadString(row, headers, "transactionamount", "amount", "total", "amt", "net");
            var amount = ParseDecimalWithBrackets(amountStr);
            if (!amount.HasValue || amount == 0) continue;

            var date = ReadDateOnly(row, headers, dto.YearId, "paymentdate", "date", "transdate", "datetime");
            var card = ReadString(row, headers, "cardnumber", "card", "account", "last4");
            var paymentNo = ReadString(row, headers, "paymentno", "paymentid", "transno", "transactionid", "receiptno", "authcode");
            var batchId = ReadString(row, headers, "gbok_batchid", "batchid", "batch");
            var arn = ReadString(row, headers, "arnnumber", "arn", "referencenumber");
            var paymentType = ReadString(row, headers, "paymenttype", "type", "transtype");

            // Check duplicate
            var fp = $"{date:yyyyMMdd}|{amount:F2}|{card ?? ""}|{paymentNo ?? ""}|{batchId ?? ""}|{arn ?? ""}";
            if (fingerprintSet.Contains(fp))
                continue;

            fingerprintSet.Add(fp);
            maxExistingSeq++;

            var isFee = paymentType?.Contains("fee", StringComparison.OrdinalIgnoreCase) == true;

            var statement = new CopayStatement
            {
                TenantId = tenantId,
                Ref = $"{tenantPrefix}-CO-{maxExistingSeq}",
                AcctId = dto.AccountId,
                YearId = dto.YearId,
                CardNumber = card,
                Currency = ReadString(row, headers, "currency") ?? "USD",
                PaymentNo = paymentNo,
                PaymentDate = date,
                TransactionAmount = amount,
                FundSource = ReadString(row, headers, "fundsource", "fund", "source", "cardtype"),
                BusinessName = ReadString(row, headers, "businessname", "business", "location"),
                MerchantID = ReadString(row, headers, "merchantid", "mid", "terminalid", "tid"),
                PaymentType = paymentType,
                AdjustmentDescription = ReadString(row, headers, "adjustmentdescription", "adjustment", "description"),
                GBOK_BatchID = batchId,
                InterchangeDescription = ReadString(row, headers, "interchangedescription"),
                ARNNumber = arn,
                Remarks = ReadString(row, headers, "remarks", "note"),
                IsSettled = false,
                StatusId = isFee ? StatusMerchantFee : StatusOpen,
                CreatedByUserId = current.UserId,
                CreatedOnUtc = DateTime.UtcNow
            };

            importedStatements.Add(statement);
        }

        if (importedStatements.Count > 0)
        {
            await db.CopayStatements.AddRangeAsync(importedStatements, ct);
            await db.SaveChangesAsync(ct);
        }

        return importedStatements.Count;
    }

    // ── Child Module 3: Copay IMS - Excel ──────────────────────────────────────

    public async Task<PagedResult<ImsReceiptItemDto>> GetImsReceiptsAsync(
        CopayFilterDto filter, CancellationToken ct = default)
    {
        var tenantId = current.TenantId;
        var query = db.RoznamchaEntries.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.CategoryId == CopayCategoryId && x.IsShow);

        if (filter.DateFrom.HasValue)
            query = query.Where(x => x.TransDate >= filter.DateFrom.Value);

        if (filter.DateTo.HasValue)
            query = query.Where(x => x.TransDate <= filter.DateTo.Value);

        if (filter.IsSettled.HasValue)
            query = query.Where(x => x.IsSettled == filter.IsSettled.Value);

        if (filter.StatusId.HasValue)
            query = query.Where(x => x.EnterStatusId == filter.StatusId.Value);

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            query = query.Where(x =>
                (x.Ref != null && x.Ref.Contains(term)) ||
                (x.PatientInfo != null && x.PatientInfo.Contains(term)) ||
                (x.InstrumentNo != null && x.InstrumentNo.Contains(term)) ||
                (x.Reference != null && x.Reference.Contains(term)) ||
                (x.BankLtRef != null && x.BankLtRef.Contains(term)) ||
                (x.Remarks != null && x.Remarks.Contains(term)));
        }

        var totalCount = await query.CountAsync(ct);
        var page = Math.Max(1, filter.PageNumber);
        var pageSize = Math.Clamp(filter.PageSize, 1, 200);

        var rows = await query
            .OrderByDescending(x => x.TransDate)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = rows.Select(r => new ImsReceiptItemDto
        {
            Id = r.Id,
            Ref = r.Ref,
            TransDate = r.TransDate,
            UsdAmount = r.UsdAmount,
            Amount = r.Amount,
            PatientInfo = r.PatientInfo,
            TransModeId = r.TransModeId,
            TransModeName = GetTransModeName(r.TransModeId),
            InstrumentNo = r.InstrumentNo,
            Reference = r.Reference,
            BankLtRef = r.BankLtRef,
            IsSettled = r.IsSettled,
            EnterStatusId = r.EnterStatusId,
            EnterStatusName = GetStatusName(r.EnterStatusId),
            Remarks = r.Remarks
        }).ToList();

        return new PagedResult<ImsReceiptItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };
    }

    public async Task<int> ImportImsDailySheetAsync(UploadImsSheetDto dto, CancellationToken ct = default)
    {
        if (dto.File == null || dto.File.Length == 0)
            throw new InvalidOperationException("Please select an IMS collection sheet file to upload.");

        var ext = Path.GetExtension(dto.File.FileName).ToLowerInvariant();
        if (ext is not (".xlsx" or ".xls" or ".csv"))
            throw new InvalidOperationException("Supported file types are .xlsx, .xls, and .csv.");

        var tenantId = current.TenantId;
        var table = ReadFirstWorksheet(dto.File);
        if (table == null || table.Rows.Count == 0)
            throw new InvalidOperationException("The uploaded sheet has no data rows.");

        var headers = BuildHeaderIndexMap(table);
        var tenantPrefix = await ResolveTenantPrefixAsync(tenantId, ct);
        var maxSeq = await GetMaxImsReceiptSequenceAsync(tenantId, tenantPrefix, ct);

        var entries = new List<RoznamchaEntry>();

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            var amountStr = ReadString(row, headers, "crdbamt", "cr_dbamt", "crdbeamt", "usdamount", "amount", "copay", "amt", "paid");
            var amount = ParseDecimalWithBrackets(amountStr);
            if (!amount.HasValue || amount == 0) continue;

            var date = ReadDateOnly(row, headers, dto.YearId, "transdate", "date", "receiptdate") ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var modeRaw = ReadString(row, headers, "trancode", "tran_code", "transmode", "mode", "paymentmode", "type");
            var transModeId = DetectTransactionModeId(modeRaw);

            var patientInfo = ReadString(row, headers, "rdiscriptions", "rdescriptions", "patientinfo", "patientname", "patient", "mrn", "name");
            var instrumentNo = ReadString(row, headers, "instrumentno", "instrument_no", "checkno", "check_no", "cardlast4", "auth");
            var remarks = ReadString(row, headers, "remarks", "rdiscriptions", "rdescriptions", "description", "note");

            maxSeq++;
            var entry = new RoznamchaEntry
            {
                TenantId = tenantId,
                Ref = $"{tenantPrefix}CR-{maxSeq}",
                TransDate = date,
                Amount = amount,
                UsdAmount = amount,
                CategoryId = CopayCategoryId,
                FromAccountId = CopayHoldingAccountId,
                RoznamchaTypeId = ReceiptRoznamchaTypeId,
                TransModeId = transModeId,
                IsShow = true,
                IsSettled = false,
                EnterStatusId = StatusOpen,
                PatientInfo = patientInfo,
                InstrumentNo = instrumentNo,
                Remarks = remarks,
                CreatedByUserId = current.UserId,
                CreatedOnUtc = DateTime.UtcNow
            };

            entries.Add(entry);
        }

        if (entries.Count > 0)
        {
            await db.RoznamchaEntries.AddRangeAsync(entries, ct);
            await db.SaveChangesAsync(ct);
        }

        return entries.Count;
    }

    public async Task<SettlementResultDto> SettleImsAutoAsync(
        DateRangeDto dateRange, CancellationToken ct = default)
    {
        var tenantId = current.TenantId;
        var tolerance = Math.Clamp(dateRange.ToleranceDays ?? 15, 0, 60);

        // Fetch unsettled patient copay collection entries (excluding Cash (CA), which never appears on a card terminal statement)
        var entries = await db.RoznamchaEntries
            .Where(x => x.TenantId == tenantId
                     && x.CategoryId == CopayCategoryId
                     && x.IsShow
                     && !x.IsSettled
                     && x.TransModeId != 1 // Cash is excluded from merchant terminal reconciliation
                     && x.TransDate >= dateRange.DateFrom
                     && x.TransDate <= dateRange.DateTo)
            .OrderBy(x => x.TransDate)
            .ToListAsync(ct);

        if (entries.Count == 0)
        {
            return new SettlementResultDto
            {
                MatchedCount = 0,
                FailedCount = 0,
                TotalSettledAmount = 0,
                Messages = ["No unsettled non-cash IMS collection entries found in the given date range."]
            };
        }

        // Fetch candidate unsettled merchant statements
        var maxStatementDate = dateRange.DateTo.AddDays(tolerance);
        var statements = await db.CopayStatements
            .Where(s => s.TenantId == tenantId
                     && !s.IsSettled
                     && s.StatusId != StatusMerchantFee
                     && s.PaymentDate != null
                     && s.PaymentDate >= dateRange.DateFrom
                     && s.PaymentDate <= maxStatementDate)
            .ToListAsync(ct);

        // Fetch candidate unsettled bank statements for direct -ST- instrument matches
        var bankStatements = await db.BankStatements
            .Where(b => b.TenantId == tenantId
                     && !b.IsSettled
                     && b.PostingDate != null
                     && b.PostingDate >= dateRange.DateFrom
                     && b.PostingDate <= maxStatementDate)
            .ToListAsync(ct);

        var matchedCount = 0;
        decimal totalSettled = 0;
        var messages = new List<string>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var entry in entries)
        {
            var amount = entry.UsdAmount ?? entry.Amount ?? 0;

            // Direct check for -ST- (Statement/Direct Bank deposit instruments)
            if (entry.InstrumentNo != null && entry.InstrumentNo.Contains("-ST-", StringComparison.OrdinalIgnoreCase))
            {
                var bankMatch = bankStatements.FirstOrDefault(b =>
                    !b.IsSettled && b.InstrumentNo == entry.InstrumentNo);

                if (bankMatch != null)
                {
                    bankMatch.IsSettled = true;
                    bankMatch.ReferenceNumber = entry.Ref;
                    bankMatch.StatusId = StatusSettled;
                    bankMatch.MatchedRoznamchaEntryId = entry.Id;
                    bankMatch.UpdatedByUserId = current.UserId;
                    bankMatch.UpdatedOnUtc = DateTime.UtcNow;

                    entry.IsSettled = true;
                    entry.EnterStatusId = StatusSettled;
                    entry.BankLtRef = bankMatch.BankRef ?? $"BANK-{bankMatch.Id}";
                    entry.Reference = bankMatch.BankRef ?? $"BANK-{bankMatch.Id}";
                    entry.ToAccountId = bankMatch.ChartAccountId;
                    entry.UpdatedByUserId = current.UserId;
                    entry.UpdatedOnUtc = DateTime.UtcNow;

                    db.RoznamchaEntryProcessLogs.Add(new RoznamchaEntryProcessLog
                    {
                        TenantId = tenantId,
                        RoznamchaEntryId = entry.Id,
                        Action = "SettledImsWithBank",
                        Notes = $"Auto-matched via InstrumentNo {entry.InstrumentNo} with Bank Statement #{bankMatch.Id}",
                        ProcessedByUserId = current.UserId,
                        ProcessedOnUtc = DateTime.UtcNow
                    });

                    matchedCount++;
                    totalSettled += amount;
                    continue;
                }
            }

            CopayStatement? match = null;

            // 1. Exact amount and exact date
            match = statements.FirstOrDefault(s =>
                !s.IsSettled && s.PaymentDate == entry.TransDate && s.TransactionAmount == amount);

            // 2. Tolerance window
            if (match == null)
            {
                for (var i = 1; i <= tolerance; i++)
                {
                    var targetDate = entry.TransDate.AddDays(i);
                    match = statements.FirstOrDefault(s =>
                        !s.IsSettled && s.PaymentDate == targetDate && s.TransactionAmount == amount);
                    if (match != null) break;
                }
            }

            if (match != null)
            {
                // Link merchant statement
                match.IsSettled = true;
                match.CopayRef = entry.Ref;
                match.StatusId = StatusSettled;
                match.MatchedRoznamchaEntryId = entry.Id;
                match.UpdatedByUserId = current.UserId;
                match.UpdatedOnUtc = DateTime.UtcNow;

                // Link roznamcha entry
                entry.IsSettled = true;
                entry.EnterStatusId = StatusSettled;
                entry.Reference = match.Ref;
                entry.BankLtRef = match.Ref;
                entry.UpdatedByUserId = current.UserId;
                entry.UpdatedOnUtc = DateTime.UtcNow;

                db.RoznamchaEntryProcessLogs.Add(new RoznamchaEntryProcessLog
                {
                    TenantId = tenantId,
                    RoznamchaEntryId = entry.Id,
                    Action = "SettledImsAuto",
                    Notes = $"Auto-matched with Merchant Statement {match.Ref} (Date: {match.PaymentDate:yyyy-MM-dd}, Amount: ${match.TransactionAmount:N2})",
                    ProcessedByUserId = current.UserId,
                    ProcessedOnUtc = DateTime.UtcNow
                });

                matchedCount++;
                totalSettled += amount;
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        messages.Add($"Auto settlement completed: {matchedCount} collection entries matched with merchant statements totaling ${totalSettled:N2}.");
        return new SettlementResultDto
        {
            MatchedCount = matchedCount,
            FailedCount = entries.Count - matchedCount,
            TotalSettledAmount = totalSettled,
            Messages = messages
        };
    }

    public async Task<SettlementResultDto> SettleImsWithBankAutoAsync(
        DateRangeDto dateRange, CancellationToken ct = default)
    {
        var tenantId = current.TenantId;
        var tolerance = Math.Clamp(dateRange.ToleranceDays ?? 7, 0, 30);

        // Fetch unsettled collection entries (Cheques, direct payments, etc.)
        var entries = await db.RoznamchaEntries
            .Where(x => x.TenantId == tenantId
                     && x.CategoryId == CopayCategoryId
                     && x.IsShow
                     && !x.IsSettled
                     && x.TransDate >= dateRange.DateFrom
                     && x.TransDate <= dateRange.DateTo)
            .OrderBy(x => x.TransDate)
            .ToListAsync(ct);

        if (entries.Count == 0)
        {
            return new SettlementResultDto
            {
                MatchedCount = 0,
                FailedCount = 0,
                TotalSettledAmount = 0,
                Messages = ["No unsettled IMS collection entries found in the given date range."]
            };
        }

        var maxBankDate = dateRange.DateTo.AddDays(tolerance);
        var bankStatements = await db.BankStatements
            .Where(b => b.TenantId == tenantId
                     && !b.IsSettled
                     && b.PostingDate != null
                     && b.PostingDate >= dateRange.DateFrom
                     && b.PostingDate <= maxBankDate)
            .ToListAsync(ct);

        var matchedCount = 0;
        decimal totalSettled = 0;
        var messages = new List<string>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var entry in entries)
        {
            var amount = entry.UsdAmount ?? entry.Amount ?? 0;
            if (amount <= 0) continue;

            // 1. Exact amount and exact date
            var matchedBank = bankStatements.FirstOrDefault(b =>
                !b.IsSettled && b.PostingDate == entry.TransDate && b.Credit == amount);

            // 2. Tolerance window
            if (matchedBank == null)
            {
                for (var i = 1; i <= tolerance; i++)
                {
                    var targetDate = entry.TransDate.AddDays(i);
                    matchedBank = bankStatements.FirstOrDefault(b =>
                        !b.IsSettled && b.PostingDate == targetDate && b.Credit == amount);
                    if (matchedBank != null) break;
                }
            }

            if (matchedBank != null)
            {
                matchedBank.IsSettled = true;
                matchedBank.ReferenceNumber = entry.Ref;
                matchedBank.StatusId = 1082; // Settled with Bank
                matchedBank.MatchedRoznamchaEntryId = entry.Id;
                matchedBank.UpdatedByUserId = current.UserId;
                matchedBank.UpdatedOnUtc = DateTime.UtcNow;

                entry.IsSettled = true;
                entry.EnterStatusId = 1081; // Settled with Bank
                entry.BankLtRef = matchedBank.BankRef ?? $"BANK-{matchedBank.Id}";
                entry.Reference = matchedBank.BankRef ?? $"BANK-{matchedBank.Id}";
                entry.ToAccountId = matchedBank.ChartAccountId;
                entry.UpdatedByUserId = current.UserId;
                entry.UpdatedOnUtc = DateTime.UtcNow;

                db.RoznamchaEntryProcessLogs.Add(new RoznamchaEntryProcessLog
                {
                    TenantId = tenantId,
                    RoznamchaEntryId = entry.Id,
                    Action = "SettledImsWithBank",
                    Notes = $"Directly matched with Bank Statement #{matchedBank.Id} (Posting: {matchedBank.PostingDate:yyyy-MM-dd}, Amount: ${matchedBank.Credit:N2})",
                    ProcessedByUserId = current.UserId,
                    ProcessedOnUtc = DateTime.UtcNow
                });

                matchedCount++;
                totalSettled += amount;
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        messages.Add($"Direct bank settlement completed: {matchedCount} collection entries matched with bank statements totaling ${totalSettled:N2}.");
        return new SettlementResultDto
        {
            MatchedCount = matchedCount,
            FailedCount = entries.Count - matchedCount,
            TotalSettledAmount = totalSettled,
            Messages = messages
        };
    }

    // ── Manual Reconciliation & Un-settle ───────────────────────────────────────

    public async Task<SettlementResultDto> SettleImsManualAsync(
        ManualSettleRequestDto request, CancellationToken ct = default)
    {
        if (request.EntryIds == null || request.EntryIds.Count == 0)
            throw new InvalidOperationException("Please select at least one collection row to settle.");

        var tenantId = current.TenantId;
        var entries = await db.RoznamchaEntries
            .Where(x => x.TenantId == tenantId && request.EntryIds.Contains(x.Id))
            .ToListAsync(ct);

        if (entries.Count == 0)
            throw new InvalidOperationException("None of the selected collection entries were found.");

        var calAmount = entries.Sum(e => e.UsdAmount ?? e.Amount ?? 0);
        var tenantPrefix = await ResolveTenantPrefixAsync(tenantId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        RoznamchaEntry? summaryBatch = null;

        if (request.CreateSummaryBatch)
        {
            // Create Consolidated Summary Batch entry (isSHow = false) ready for Child 1 Merchant-to-Bank matching!
            var maxBatchSeq = await GetMaxBatchSequenceAsync(tenantId, tenantPrefix, ct);
            var batchRef = $"{tenantPrefix}-CO-BATCH-{maxBatchSeq + 1}";
            var maxDate = entries.Max(e => e.TransDate);

            summaryBatch = new RoznamchaEntry
            {
                TenantId = tenantId,
                Ref = batchRef,
                TransDate = maxDate,
                UsdAmount = calAmount,
                Amount = calAmount,
                CategoryId = CopayCategoryId,
                FromAccountId = CopayHoldingAccountId,
                RoznamchaTypeId = ReceiptRoznamchaTypeId,
                IsShow = false, // batch entry for Merchant To Bank
                IsSettled = false,
                EnterStatusId = StatusOpen,
                Remarks = $"Consolidated Copay Batch for {entries.Count} collection rows. {request.Remarks}".Trim(),
                Reference = request.TargetRef,
                CreatedByUserId = current.UserId,
                CreatedOnUtc = DateTime.UtcNow
            };

            await db.RoznamchaEntries.AddAsync(summaryBatch, ct);
            await db.SaveChangesAsync(ct);
        }

        var linkedTargetRef = summaryBatch?.Ref ?? request.TargetRef;

        foreach (var entry in entries)
        {
            entry.IsSettled = true;
            entry.EnterStatusId = StatusSettledManually;
            entry.Reference = linkedTargetRef;
            if (!string.IsNullOrWhiteSpace(request.Remarks))
                entry.Remarks = $"{entry.Remarks} {request.Remarks}".Trim();
            entry.UpdatedByUserId = current.UserId;
            entry.UpdatedOnUtc = DateTime.UtcNow;

            db.RoznamchaEntryProcessLogs.Add(new RoznamchaEntryProcessLog
            {
                TenantId = tenantId,
                RoznamchaEntryId = entry.Id,
                Action = "SettledImsManual",
                Notes = $"Manually settled with {linkedTargetRef} (Sum: ${calAmount:N2})",
                ProcessedByUserId = current.UserId,
                ProcessedOnUtc = DateTime.UtcNow
            });
        }

        // If target reference is a merchant statement or bank statement, update it
        if (!string.IsNullOrWhiteSpace(request.TargetRef))
        {
            var statement = await db.CopayStatements
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Ref == request.TargetRef, ct);

            if (statement != null)
            {
                statement.IsSettled = true;
                statement.StatusId = StatusSettledManually;
                statement.CopayRef = linkedTargetRef;
                statement.UpdatedByUserId = current.UserId;
                statement.UpdatedOnUtc = DateTime.UtcNow;
            }
            else
            {
                var bank = await db.BankStatements
                    .FirstOrDefaultAsync(b => b.TenantId == tenantId &&
                        (b.BankRef == request.TargetRef || b.ReferenceNumber == request.TargetRef ||
                         b.TransactionReferenceNumber == request.TargetRef || b.Id.ToString() == request.TargetRef), ct);

                if (bank != null)
                {
                    bank.IsSettled = true;
                    bank.StatusId = 1082; // Settled with Bank Manually
                    bank.ReferenceNumber = linkedTargetRef;
                    bank.UpdatedByUserId = current.UserId;
                    bank.UpdatedOnUtc = DateTime.UtcNow;

                    if (summaryBatch != null)
                    {
                        summaryBatch.ToAccountId = bank.ChartAccountId;
                        summaryBatch.BankLtRef = bank.BankRef ?? $"BANK-{bank.Id}";
                    }
                }
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new SettlementResultDto
        {
            MatchedCount = entries.Count,
            FailedCount = 0,
            TotalSettledAmount = calAmount,
            Messages = [$"Successfully manually settled {entries.Count} entries with summary batch {linkedTargetRef} totaling ${calAmount:N2}."]
        };
    }

    public async Task<bool> UnsettleEntryAsync(UnsettleRequestDto request, CancellationToken ct = default)
    {
        var tenantId = current.TenantId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (request.RoznamchaEntryId.HasValue)
        {
            var entry = await db.RoznamchaEntries
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.RoznamchaEntryId.Value, ct);

            if (entry != null)
            {
                // 1. If this was a summary batch (isSHow == false), rollback all child items linked to it
                if (!entry.IsShow && !string.IsNullOrEmpty(entry.Ref))
                {
                    var childEntries = await db.RoznamchaEntries
                        .Where(x => x.TenantId == tenantId && x.Reference == entry.Ref)
                        .ToListAsync(ct);

                    foreach (var child in childEntries)
                    {
                        child.IsSettled = false;
                        child.EnterStatusId = StatusOpen;
                        child.Reference = null;
                        child.UpdatedByUserId = current.UserId;
                        child.UpdatedOnUtc = DateTime.UtcNow;

                        db.RoznamchaEntryProcessLogs.Add(new RoznamchaEntryProcessLog
                        {
                            TenantId = tenantId,
                            RoznamchaEntryId = child.Id,
                            Action = "Unsettled",
                            Notes = $"Unsettled because batch {entry.Ref} was un-settled. {request.Remarks}".Trim(),
                            ProcessedByUserId = current.UserId,
                            ProcessedOnUtc = DateTime.UtcNow
                        });
                    }
                }

                // 2. Clear any linked CopayStatement
                if (!string.IsNullOrEmpty(entry.Reference) || !string.IsNullOrEmpty(entry.Ref))
                {
                    var statements = await db.CopayStatements
                        .Where(s => s.TenantId == tenantId &&
                                    (s.Ref == entry.Reference || s.CopayRef == entry.Ref || s.MatchedRoznamchaEntryId == entry.Id))
                        .ToListAsync(ct);

                    foreach (var stmt in statements)
                    {
                        stmt.IsSettled = false;
                        stmt.CopayRef = null;
                        stmt.StatusId = StatusOpen;
                        stmt.MatchedRoznamchaEntryId = null;
                        stmt.UpdatedByUserId = current.UserId;
                        stmt.UpdatedOnUtc = DateTime.UtcNow;
                    }
                }

                // 3. Clear any linked BankStatement
                if (!string.IsNullOrEmpty(entry.Ref))
                {
                    var bankStatements = await db.BankStatements
                        .Where(b => b.TenantId == tenantId &&
                                    (b.ReferenceNumber == entry.Ref || b.MatchedRoznamchaEntryId == entry.Id))
                        .ToListAsync(ct);

                    foreach (var bank in bankStatements)
                    {
                        bank.IsSettled = false;
                        bank.ReferenceNumber = null;
                        bank.StatusId = StatusOpen;
                        bank.MatchedRoznamchaEntryId = null;
                        bank.UpdatedByUserId = current.UserId;
                        bank.UpdatedOnUtc = DateTime.UtcNow;
                    }
                }

                // 4. Reset entry itself
                entry.IsSettled = false;
                entry.EnterStatusId = StatusOpen;
                entry.BankLtRef = null;
                entry.Reference = null;
                entry.UpdatedByUserId = current.UserId;
                entry.UpdatedOnUtc = DateTime.UtcNow;

                db.RoznamchaEntryProcessLogs.Add(new RoznamchaEntryProcessLog
                {
                    TenantId = tenantId,
                    RoznamchaEntryId = entry.Id,
                    Action = "Unsettled",
                    Notes = $"Entry un-settled. {request.Remarks}".Trim(),
                    ProcessedByUserId = current.UserId,
                    ProcessedOnUtc = DateTime.UtcNow
                });
            }
        }

        if (request.StatementId.HasValue)
        {
            var statement = await db.CopayStatements
                .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == request.StatementId.Value, ct);

            if (statement != null)
            {
                // Revert linked roznamcha entry if present
                if (!string.IsNullOrEmpty(statement.CopayRef) || statement.MatchedRoznamchaEntryId.HasValue)
                {
                    var linkedEntries = await db.RoznamchaEntries
                        .Where(x => x.TenantId == tenantId &&
                                    (x.Ref == statement.CopayRef || x.Id == statement.MatchedRoznamchaEntryId))
                        .ToListAsync(ct);

                    foreach (var le in linkedEntries)
                    {
                        le.IsSettled = false;
                        le.EnterStatusId = StatusOpen;
                        le.Reference = null;
                        le.UpdatedByUserId = current.UserId;
                        le.UpdatedOnUtc = DateTime.UtcNow;
                    }
                }

                statement.IsSettled = false;
                statement.CopayRef = null;
                statement.StatusId = StatusOpen;
                statement.MatchedRoznamchaEntryId = null;
                statement.UpdatedByUserId = current.UserId;
                statement.UpdatedOnUtc = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    // ── Lookups & Dropdowns for React UI ───────────────────────────────────────

    public async Task<CopayLookupsDto> GetLookupsAsync(CancellationToken ct = default)
    {
        var tenantId = current.TenantId;
        var accounts = await db.AccountsChartAccounts.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.IsActive)
            .OrderBy(a => a.AccountName)
            .Select(a => new LookupItemDto
            {
                Id = a.Id,
                DisplayText = $"{a.AccountNumber} - {a.AccountName}"
            })
            .ToListAsync(ct);

        var statuses = new List<LookupItemDto>
        {
            new() { Id = StatusOpen, DisplayText = "Open / Pending" },
            new() { Id = StatusSettled, DisplayText = "Settled" },
            new() { Id = StatusMerchantFee, DisplayText = "Merchant Fee" },
            new() { Id = StatusSettledManually, DisplayText = "Settled Manually" },
            new() { Id = 1081, DisplayText = "Settled with Bank" }
        };

        var modes = new List<LookupItemDto>
        {
            new() { Id = 1, DisplayText = "Cash (CA)" },
            new() { Id = 2, DisplayText = "Cheque (CK)" },
            new() { Id = 7, DisplayText = "Debit Card (DT)" },
            new() { Id = 8, DisplayText = "Credit Card (CT)" }
        };

        var currentYear = DateTime.UtcNow.Year;
        var years = Enumerable.Range(currentYear - 4, 6).OrderByDescending(y => y).ToList();

        return new CopayLookupsDto
        {
            Accounts = accounts,
            Statuses = statuses,
            TransactionModes = modes,
            AvailableYears = years
        };
    }

    // ── Helper Utilities ───────────────────────────────────────────────────────

    private static string GetStatusName(int? statusId) => statusId switch
    {
        StatusOpen => "Open / Pending",
        StatusSettled => "Settled",
        StatusMerchantFee => "Merchant Fee",
        StatusSettledManually => "Settled Manually",
        1081 or 1082 => "Settled with Bank",
        22 => "Deleted",
        _ => "Open"
    };

    private static string GetTransModeName(int? modeId) => modeId switch
    {
        1 => "Cash (CA)",
        2 => "Cheque (CK)",
        7 => "Debit (DT)",
        8 => "Credit Card (CT)",
        _ => "Credit Card (CT)"
    };

    private static int DetectTransactionModeId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 8; // Default Credit Card
        var lower = text.ToLowerInvariant();
        if (lower.Contains("(ca)") || lower.Contains("cash")) return 1;
        if (lower.Contains("(ck)") || lower.Contains("cheque") || lower.Contains("check")) return 2;
        if (lower.Contains("(dt)") || lower.Contains("debit")) return 7;
        if (lower.Contains("(ct)") || lower.Contains("credit") || lower.Contains("card")) return 8;
        return 8;
    }

    private static decimal? ParseDecimalWithBrackets(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var clean = text.Trim().Replace("$", "").Replace(",", "");
        var isNegative = false;

        if (clean.StartsWith('(') && clean.EndsWith(')'))
        {
            isNegative = true;
            clean = clean[1..^1].Trim();
        }
        else if (clean.StartsWith('-'))
        {
            isNegative = true;
            clean = clean[1..].Trim();
        }

        if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            return isNegative ? -val : val;

        return null;
    }

    private static DataTable? ReadFirstWorksheet(Microsoft.AspNetCore.Http.IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var mem = new MemoryStream();
        stream.CopyTo(mem);
        mem.Position = 0;

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        using var reader = ext == ".csv"
            ? ExcelReaderFactory.CreateCsvReader(mem)
            : ExcelReaderFactory.CreateReader(mem);

        var ds = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
        });

        return ds.Tables.Cast<DataTable>().FirstOrDefault(t => t.Rows.Count > 0);
    }

    private static Dictionary<string, int> BuildHeaderIndexMap(DataTable table)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < table.Columns.Count; i++)
        {
            var raw = table.Columns[i].ColumnName;
            var clean = Regex.Replace(raw ?? "", @"[^a-zA-Z0-9]", "").ToLowerInvariant();
            if (!string.IsNullOrEmpty(clean) && !map.ContainsKey(clean))
                map[clean] = i;
        }
        return map;
    }

    private static string? ReadString(DataRow row, Dictionary<string, int> headers, params string[] candidates)
    {
        foreach (var c in candidates)
        {
            var clean = Regex.Replace(c, @"[^a-zA-Z0-9]", "").ToLowerInvariant();
            if (headers.TryGetValue(clean, out var idx) && idx < row.ItemArray.Length)
            {
                var val = row[idx]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(val)) return val;
            }
        }
        return null;
    }

    private static DateOnly? ReadDateOnly(DataRow row, Dictionary<string, int> headers, params string[] candidates)
        => ReadDateOnly(row, headers, null, candidates);

    private static DateOnly? ReadDateOnly(DataRow row, Dictionary<string, int> headers, int? defaultYear, params string[] candidates)
    {
        var raw = ReadString(row, headers, candidates);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var trimmed = raw.Trim();

        // Support "MM/dd" or "M/d" where year is omitted (common in merchant statements)
        if (trimmed.Contains('/') && trimmed.Split('/').Length == 2)
        {
            var parts = trimmed.Split('/');
            if (int.TryParse(parts[0].Trim(), out var m) && int.TryParse(parts[1].Trim(), out var d))
            {
                var y = defaultYear ?? DateTime.UtcNow.Year;
                if (m >= 1 && m <= 12 && d >= 1 && d <= DateTime.DaysInMonth(y, m))
                    return new DateOnly(y, m, d);
            }
        }

        if (trimmed.Contains('-') && trimmed.Split('-').Length == 2)
        {
            var parts = trimmed.Split('-');
            if (int.TryParse(parts[0].Trim(), out var m) && int.TryParse(parts[1].Trim(), out var d))
            {
                var y = defaultYear ?? DateTime.UtcNow.Year;
                if (m >= 1 && m <= 12 && d >= 1 && d <= DateTime.DaysInMonth(y, m))
                    return new DateOnly(y, m, d);
            }
        }

        string[] formats = ["MM/dd/yyyy", "M/d/yyyy", "MM-dd-yyyy", "M-d-yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "yyyy-MM-dd", "dd-MMM-yyyy", "MM/dd/yy", "dd/MM/yy"];
        if (DateTime.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtExact))
            return DateOnly.FromDateTime(dtExact);

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return DateOnly.FromDateTime(dt);

        if (DateTime.TryParse(trimmed, out dt))
            return DateOnly.FromDateTime(dt);

        return null;
    }

    private async Task<string> ResolveTenantPrefixAsync(int tenantId, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .Where(x => x.Id == tenantId)
            .Select(x => new { x.TenantCode, x.TenantName })
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrWhiteSpace(tenant?.TenantCode))
            return Regex.Replace(tenant.TenantCode.Trim().ToUpperInvariant(), @"[^A-Z0-9]", "");

        var name = tenant?.TenantName?.Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var initials = string.Concat(words.Select(w => char.ToUpperInvariant(w[0])));
            return initials.Length >= 2 ? initials : name[..Math.Min(3, name.Length)].ToUpperInvariant();
        }

        return "CO";
    }

    private async Task<int> GetMaxStatementSequenceAsync(int tenantId, string prefix, CancellationToken ct)
    {
        var pattern = $"{prefix}-CO-";
        var refs = await db.CopayStatements.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Ref != null && x.Ref.StartsWith(pattern))
            .Select(x => x.Ref!)
            .ToListAsync(ct);

        var max = 0;
        foreach (var r in refs)
        {
            var numPart = r[pattern.Length..];
            if (int.TryParse(numPart, out var num) && num > max)
                max = num;
        }
        return max;
    }

    private async Task<int> GetMaxImsReceiptSequenceAsync(int tenantId, string prefix, CancellationToken ct)
    {
        var pattern = $"{prefix}CR-";
        var refs = await db.RoznamchaEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Ref != null && x.Ref.StartsWith(pattern))
            .Select(x => x.Ref!)
            .ToListAsync(ct);

        var max = 0;
        foreach (var r in refs)
        {
            var numPart = r[pattern.Length..];
            if (int.TryParse(numPart, out var num) && num > max)
                max = num;
        }
        return max;
    }

    private async Task<int> GetMaxBatchSequenceAsync(int tenantId, string prefix, CancellationToken ct)
    {
        var pattern = $"{prefix}-CO-BATCH-";
        var refs = await db.RoznamchaEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Ref != null && x.Ref.StartsWith(pattern))
            .Select(x => x.Ref!)
            .ToListAsync(ct);

        var max = 0;
        foreach (var r in refs)
        {
            var numPart = r[pattern.Length..];
            if (int.TryParse(numPart, out var num) && num > max)
                max = num;
        }
        return max;
    }
}
