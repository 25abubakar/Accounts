using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using ExcelDataReader;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class CopayService(
    ApplicationDbContext db,
    ICurrentUserService current,
    IWebHostEnvironment environment) : ICopayService
{
    private const int LegacyCopayCategoryId = 54;
    private const int LegacyCopayHoldingAccountId = 1567;
    private const int LegacyCopayMerchantClearingAccountId = 1568;
    private const int LegacyCopayMerchantAccountId = 3608;
    private const int LegacyReceiptRoznamchaTypeId = 2;

    private const int StatusOpen = 14;
    private const int StatusSettled = 20;
    private const int StatusMerchantFee = 1060;
    private const int StatusSettledManually = 1066;
    private const int StatusCashCopay = 1058;
    private const int StatusImsWithBank = 1081;
    private const int StatusTargetSettled = 1082;
    private const int StatusWithBilling = 1083;
    private const int StatusWithMachine = 1084;
    private const int StatusWithCopayExcel = 1087;

    static CopayService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // ── Child Module 1: Merchant To Bank ───────────────────────────────────────

    public async Task<PagedResult<MerchantToBankItemDto>> GetMerchantToBankListAsync(
        CopayFilterDto filter, CancellationToken ct = default)
    {
        var tenantId = current.TenantId;
        var config = await ResolveConfigAsync(tenantId, ct);
        var query = db.RoznamchaEntries.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.CategoryId == config.CategoryId && !x.IsShow);

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
        var statusIds = entries.Where(x => x.EnterStatusId.HasValue).Select(x => x.EnterStatusId!.Value).Distinct().ToList();
        var rawStatuses = await db.AccountsEntryStatuses.AsNoTracking()
            .Where(x => statusIds.Contains(x.Id) && (x.TenantId == null || x.TenantId == tenantId))
            .ToListAsync(ct);
        var statuses = rawStatuses
            .GroupBy(x => x.Id)
            .Select(x => x.OrderByDescending(s => s.TenantId == tenantId).First())
            .ToDictionary(x => x.Id);

        var items = entries.Select(e =>
        {
            bankMatches.TryGetValue(e.Ref ?? string.Empty, out var matchedBank);
            statuses.TryGetValue(e.EnterStatusId ?? 0, out var status);
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
                EnterStatusName = status?.Name ?? GetStatusName(e.EnterStatusId),
                StatusColor = status?.ColorCode,
                StatusFontColor = status?.FontColor,
                IsSettled = e.IsSettled,
                Remarks = e.Remarks,
                MatchedBankStatementId = matchedBank?.Id,
                BankPostingDate = matchedBank?.PostingDate,
                BankCredit = matchedBank?.Credit,
                BankDebit = matchedBank?.Debit
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
        var config = await ResolveConfigAsync(tenantId, ct);
        var tolerance = Math.Clamp(dateRange.ToleranceDays ?? config.BankMatchWindowDays, 0, 30);

        // Fetch unsettled batch copay entries in the specified date range
        var entries = await db.RoznamchaEntries
            .Where(x => x.TenantId == tenantId
                     && x.CategoryId == config.CategoryId
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
            var matchedBank = FindUniqueBankMatch(bankStatements, entry.TransDate, entryAmount, tolerance);

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
                db.CopaySettlementLinks.Add(NewSettlementLink(
                    tenantId, "MERCHANT_TO_BANK", entry.Ref, matchedBank.BankRef ?? matchedBank.TransactionReferenceNumber,
                    sourceRoznamchaEntryId: entry.Id, targetBankStatementId: matchedBank.Id,
                    statusId: StatusSettled, isManual: false));

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

    public async Task<CopayImportResultDto> ImportMerchantPayoutExcelAsync(
        UploadMerchantPayoutDto dto, CancellationToken ct = default)
    {
        ValidateSpreadsheet(dto.File, "merchant payout");
        var tenantId = current.TenantId;
        var config = await ResolveConfigAsync(tenantId, ct);
        var batch = await StartImportBatchAsync(tenantId, "MERCHANT_TO_BANK", dto.File, ct);
        if (batch.Status == "Duplicate") return ToImportResult(batch);

        var table = ReadFirstWorksheet(dto.File);
        if (table == null || table.Rows.Count == 0)
            throw new InvalidOperationException("The uploaded payout sheet contains no data rows.");

        var headers = BuildHeaderIndexMap(table);
        var tenantPrefix = await ResolveTenantPrefixAsync(tenantId, ct);
        var maxSeq = await GetMaxImsReceiptSequenceAsync(tenantId, tenantPrefix, ct);
        var attachmentPath = dto.Attachment == null ? null : await SaveUploadAsync(tenantId, dto.Attachment, ct);
        var existing = (await db.CopayImportRows.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Result == "Imported")
            .Select(x => x.Fingerprint).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var entries = new List<RoznamchaEntry>();
        var results = new List<CopayImportRow>();
        var errors = new List<string>();

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var sourceRow = r + 2;
            var row = table.Rows[r];
            batch.TotalRows++;
            var amount = ParseDecimalWithBrackets(ReadString(row, headers,
                "crdbamt", "cr_dbamt", "usdamount", "transactionamount", "amount", "payout", "net"));
            var date = ReadDateOnly(row, headers, dto.YearId, "transdate", "paymentdate", "date", "postingdate");
            if (!amount.HasValue || !date.HasValue)
            {
                AddImportError(results, batch, tenantId, sourceRow, $"invalid-payout|{sourceRow}",
                    "A valid payout date and amount are required.", errors);
                continue;
            }

            var instrument = ReadString(row, headers, "instrumentno", "batchid", "paymentno", "reference", "authcode");
            var description = ReadString(row, headers, "rdiscriptions", "rdescriptions", "description", "memo", "remarks");
            var mode = DetectTransactionModeId(ReadString(row, headers, "trancode", "tran_code", "transmode", "mode"));
            var fp = HashText($"{tenantId}|PAYOUT|{date:yyyyMMdd}|{amount:F2}|{mode}|{Normalize(instrument)}|{Normalize(description)}");
            if (existing.Contains(fp))
            {
                batch.DuplicateRows++;
                results.Add(NewImportRow(batch, tenantId, sourceRow, fp, "Duplicate", "The merchant payout row already exists."));
                continue;
            }
            existing.Add(fp);
            maxSeq++;
            var entry = new RoznamchaEntry
            {
                TenantId = tenantId,
                Ref = $"{tenantPrefix}CR-{maxSeq}",
                TransDate = date.Value,
                Amount = 0,
                UsdAmount = amount,
                CategoryId = config.CategoryId,
                FromAccountId = config.MerchantClearingAccountId ?? config.HoldingAccountId,
                RoznamchaTypeId = config.ReceiptTypeId,
                CurrencyId = config.CurrencyId,
                TransModeId = mode,
                InstrumentNo = instrument,
                Descriptions = description,
                Remarks = description,
                Attachment = attachmentPath,
                IsShow = false,
                IsSettled = false,
                EnterStatusId = StatusOpen,
                CreatedByUserId = current.UserId,
                CreatedOnUtc = DateTime.UtcNow
            };
            entries.Add(entry);
            results.Add(NewImportRow(batch, tenantId, sourceRow, fp, "Imported", null, entry: entry));
        }

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            if (entries.Count > 0) await db.RoznamchaEntries.AddRangeAsync(entries, ct);
            if (results.Count > 0) await db.CopayImportRows.AddRangeAsync(results, ct);
            batch.ImportedRows = entries.Count;
            CompleteBatch(batch, errors);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        var response = ToImportResult(batch, errors);
        if (dto.AutoSettle && entries.Count > 0)
        {
            var settlement = await SettleMerchantToBankAutoAsync(new DateRangeDto
            {
                DateFrom = entries.Min(x => x.TransDate),
                DateTo = entries.Max(x => x.TransDate),
                ToleranceDays = config.BankMatchWindowDays
            }, ct);
            response.AutoSettledRows = settlement.MatchedCount;
        }
        return response;
    }

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

        var accountIds = rows.Where(x => x.AcctId.HasValue).Select(x => x.AcctId!.Value).Distinct().ToList();
        var accountNames = await db.AccountsChartAccounts.AsNoTracking()
            .Where(x => x.TenantId == tenantId && accountIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.AccountName, ct);
        var statusIds = rows.Where(x => x.StatusId.HasValue).Select(x => x.StatusId!.Value).Distinct().ToList();
        var rawStatuses = await db.AccountsEntryStatuses.AsNoTracking()
            .Where(x => statusIds.Contains(x.Id) && (x.TenantId == null || x.TenantId == tenantId))
            .ToListAsync(ct);
        var statuses = rawStatuses
            .GroupBy(x => x.Id)
            .Select(x => x.OrderByDescending(s => s.TenantId == tenantId).First())
            .ToDictionary(x => x.Id);

        var items = rows.Select(r =>
        {
            statuses.TryGetValue(r.StatusId ?? 0, out var status);
            return new MerchantStatementItemDto
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
            StatusName = status?.Name ?? GetStatusName(r.StatusId),
            StatusColor = status?.ColorCode,
            StatusFontColor = status?.FontColor,
            AccountName = r.AcctId.HasValue && accountNames.TryGetValue(r.AcctId.Value, out var accountName) ? accountName : null,
            IsManual = r.IsManual,
            MatchedRoznamchaEntryId = r.MatchedRoznamchaEntryId
            };
        }).ToList();

        return new PagedResult<MerchantStatementItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };
    }

    public async Task<CopayImportResultDto> ImportMerchantStatementExcelAsync(
        UploadMerchantStatementDto dto, CancellationToken ct = default)
    {
        if (dto.File == null || dto.File.Length == 0)
            throw new InvalidOperationException("Please select an Excel or CSV statement file to upload.");

        var ext = Path.GetExtension(dto.File.FileName).ToLowerInvariant();
        if (ext is not (".xlsx" or ".xls" or ".csv"))
            throw new InvalidOperationException("Supported file types are .xlsx, .xls, and .csv.");

        var tenantId = current.TenantId;
        var config = await ResolveConfigAsync(tenantId, ct);
        var accountId = dto.AccountId ?? config.MerchantAccountId;
        if (!accountId.HasValue)
            throw new InvalidOperationException("Select a merchant account or configure the tenant Copay merchant account.");
        var accountExists = await db.AccountsChartAccounts.AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.Id == accountId.Value && x.IsActive, ct);
        if (!accountExists)
            throw new InvalidOperationException("The selected merchant account is not available for this tenant.");

        var batch = await StartImportBatchAsync(tenantId, "IMS_TO_MERCHANT", dto.File, ct);
        if (batch.Status == "Duplicate")
            return ToImportResult(batch);
        var table = ReadFirstWorksheet(dto.File);
        if (table == null || table.Rows.Count == 0)
            throw new InvalidOperationException("The uploaded statement contains no rows.");

        var headers = BuildHeaderIndexMap(table);
        var tenantPrefix = await ResolveTenantPrefixAsync(tenantId, ct);

        // Find the current highest numeric reference for statement rows
        var maxExistingSeq = await GetMaxStatementSequenceAsync(tenantId, tenantPrefix, ct);

        // Pre-fetch active statement fingerprints to avoid duplicates
        var fingerprintSet = (await db.CopayStatements.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.SourceFingerprint != null)
            .Select(x => x.SourceFingerprint!)
            .ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

        var importedStatements = new List<CopayStatement>();
        var rowResults = new List<CopayImportRow>();
        var errors = new List<string>();
        var attachmentPath = dto.Attachment == null ? null : await SaveUploadAsync(tenantId, dto.Attachment, ct);

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            var sourceRow = r + 2;
            batch.TotalRows++;
            var amountStr = ReadString(row, headers, "transactionamount", "amount", "total", "amt", "net");
            var amount = ParseDecimalWithBrackets(amountStr);
            if (!amount.HasValue)
            {
                AddImportError(rowResults, batch, tenantId, sourceRow, $"invalid|{sourceRow}", "Transaction amount is missing or invalid.", errors);
                continue;
            }

            var date = ReadDateOnly(row, headers, dto.YearId, "paymentdate", "date", "transdate", "datetime");
            var card = ReadString(row, headers, "cardnumber", "card", "account", "last4");
            var paymentNo = ReadString(row, headers, "paymentno", "paymentid", "transno", "transactionid", "receiptno", "authcode");
            var batchId = ReadString(row, headers, "gbok_batchid", "batchid", "batch");
            var arn = ReadString(row, headers, "arnnumber", "arn", "referencenumber");
            var paymentType = ReadString(row, headers, "paymenttype", "type", "transtype");
            var interchange = ReadString(row, headers, "interchangedescription");
            if (!date.HasValue)
            {
                AddImportError(rowResults, batch, tenantId, sourceRow, $"invalid-date|{sourceRow}", "Payment date is missing or invalid.", errors);
                continue;
            }

            // Check duplicate
            var fp = HashText($"{tenantId}|{accountId}|{date:yyyyMMdd}|{amount:F2}|{Normalize(card)}|{Normalize(paymentNo)}|{Normalize(batchId)}|{Normalize(interchange)}|{Normalize(arn)}");
            if (fingerprintSet.Contains(fp))
            {
                batch.DuplicateRows++;
                rowResults.Add(NewImportRow(batch, tenantId, sourceRow, fp, "Duplicate", "The merchant statement row already exists."));
                continue;
            }

            fingerprintSet.Add(fp);
            maxExistingSeq++;

            var isFee = paymentType?.Contains("fee", StringComparison.OrdinalIgnoreCase) == true;

            var statement = new CopayStatement
            {
                TenantId = tenantId,
                Ref = $"{tenantPrefix}-CO-{maxExistingSeq}",
                AcctId = accountId,
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
                InterchangeDescription = interchange,
                ARNNumber = arn,
                Remarks = ReadString(row, headers, "remarks", "note"),
                IsSettled = false,
                StatusId = isFee ? StatusMerchantFee : StatusOpen,
                Attachment = attachmentPath,
                ImportBatchId = batch.Id,
                SourceRowNumber = sourceRow,
                SourceFingerprint = fp,
                CreatedByUserId = current.UserId,
                CreatedOnUtc = DateTime.UtcNow
            };

            importedStatements.Add(statement);
            rowResults.Add(NewImportRow(batch, tenantId, sourceRow, fp, "Imported", null, statement: statement));
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (importedStatements.Count > 0)
        {
            await db.CopayStatements.AddRangeAsync(importedStatements, ct);
            await db.SaveChangesAsync(ct);
        }
        if (rowResults.Count > 0) await db.CopayImportRows.AddRangeAsync(rowResults, ct);
        batch.ImportedRows = importedStatements.Count;
        CompleteBatch(batch, errors);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToImportResult(batch, errors);
    }

    // ── Child Module 3: Copay IMS - Excel ──────────────────────────────────────

    public async Task<PagedResult<ImsReceiptItemDto>> GetImsReceiptsAsync(
        CopayFilterDto filter, CancellationToken ct = default)
    {
        var tenantId = current.TenantId;
        var config = await ResolveConfigAsync(tenantId, ct);
        var query = db.RoznamchaEntries.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.CategoryId == config.CategoryId && x.IsShow);

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

        var statusIds = rows.Where(x => x.EnterStatusId.HasValue).Select(x => x.EnterStatusId!.Value).Distinct().ToList();
        var rawStatuses = await db.AccountsEntryStatuses.AsNoTracking()
            .Where(x => statusIds.Contains(x.Id) && (x.TenantId == null || x.TenantId == tenantId))
            .ToListAsync(ct);
        var statuses = rawStatuses
            .GroupBy(x => x.Id)
            .Select(x => x.OrderByDescending(s => s.TenantId == tenantId).First())
            .ToDictionary(x => x.Id);
        var modeIds = rows.Where(x => x.TransModeId.HasValue).Select(x => x.TransModeId!.Value).Distinct().ToList();
        var modes = await db.AccountsTransModes.AsNoTracking().Where(x => modeIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var typeIds = rows.Where(x => x.TransTypeId.HasValue).Select(x => x.TransTypeId!.Value).Distinct().ToList();
        var types = await db.AccountsTransTypes.AsNoTracking().Where(x => typeIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var accountIds = rows.SelectMany(x => new int?[] { x.FromAccountId, x.ToAccountId }).Where(x => x.HasValue)
            .Select(x => x!.Value).Distinct().ToList();
        var accounts = await db.AccountsChartAccounts.AsNoTracking()
            .Where(x => x.TenantId == tenantId && accountIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var rtypeIds = rows.Where(x => x.RoznamchaTypeId.HasValue).Select(x => x.RoznamchaTypeId!.Value).Distinct().ToList();
        var rozTypes = await db.AccountsRoznamchaTypes.AsNoTracking()
            .Where(x => rtypeIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var catIds = accounts.Values.Where(a => a.CategoryId.HasValue).Select(a => a.CategoryId!.Value)
            .Concat(rows.Where(r => r.CategoryId.HasValue).Select(r => r.CategoryId!.Value)).Distinct().ToList();
        var categories = await db.AccountsCategories.AsNoTracking()
            .Where(x => x.TenantId == tenantId && catIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var currencyIds = rows.Where(x => x.CurrencyId.HasValue).Select(x => x.CurrencyId!.Value).Distinct().ToList();
        var currencies = await db.AccountsCurrencies.AsNoTracking().Where(x => currencyIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Code, ct);

        var items = rows.Select(r =>
        {
            statuses.TryGetValue(r.EnterStatusId ?? 0, out var status);
            accounts.TryGetValue(r.FromAccountId ?? 0, out var fromAcc);
            accounts.TryGetValue(r.ToAccountId ?? 0, out var toAcc);

            var catName = r.CategoryId.HasValue && categories.TryGetValue(r.CategoryId.Value, out var cn) ? cn : "Copay";
            var toCatName = toAcc?.CategoryId.HasValue == true && categories.TryGetValue(toAcc.CategoryId.Value, out var tcn) ? tcn : catName;
            var rtypeName = r.RoznamchaTypeId.HasValue && rozTypes.TryGetValue(r.RoznamchaTypeId.Value, out var rtn) ? rtn : "Receipt";

            return new ImsReceiptItemDto
            {
                Id = r.Id,
                SNo = r.SNo,
                Ref = r.Ref,
                RoznamchaTypeName = rtypeName,
                CategoryName = catName,
                FromAccountId = r.FromAccountId,
                FromAccountName = fromAcc?.AccountName,
                FromAccountNumber = fromAcc?.AccountNumber,
                ToCategoryName = toCatName,
                ToAccountId = r.ToAccountId,
                ToAccountName = toAcc?.AccountName,
                ToAccountNumber = toAcc?.AccountNumber,
                TransTypeId = r.TransTypeId,
                TransTypeName = r.TransTypeId.HasValue && types.TryGetValue(r.TransTypeId.Value, out var typeName) ? typeName : "Not Specified",
                TransModeId = r.TransModeId,
                TransModeName = r.TransModeId.HasValue && modes.TryGetValue(r.TransModeId.Value, out var modeName) ? modeName : GetTransModeName(r.TransModeId),
                CreatedDate = r.CreatedOnUtc,
                InstrumentNo = r.InstrumentNo,
                Description = r.Descriptions,
                BankLtRef = r.BankLtRef,
                TransDate = r.TransDate,
                Adjustment = r.Adjustment,
                Amount = r.Amount,
                UsdAmount = r.UsdAmount,
                PaidCopayAmount = r.PaidCopayAmount,
                PatientInfo = r.PatientInfo,
                Reference = r.Reference,
                ReferralSource = r.ReferralSource,
                Qty = r.Qty,
                Rate = r.Rate,
                EnterStatusId = r.EnterStatusId,
                EnterStatusName = status?.Name ?? GetStatusName(r.EnterStatusId),
                IsDeleted = r.IsDeleted,
                Attachment = r.Attachment,
                Image = r.Attachment,
                LibRef = r.LibRef,
                Remarks = r.Remarks,
                IsSettled = r.IsSettled,
                StatusColor = status?.ColorCode,
                StatusFontColor = status?.FontColor,
                CurrencyId = r.CurrencyId,
                CurrencyCode = r.CurrencyId.HasValue && currencies.TryGetValue(r.CurrencyId.Value, out var code) ? code : null,
                CreatedOnUtc = r.CreatedOnUtc,
                IsManual = r.IsManual
            };
        }).ToList();

        return new PagedResult<ImsReceiptItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };
    }

    public async Task<IReadOnlyList<ImsReceiptItemDto>> GetImsVerificationAsync(
        DateRangeDto dateRange, CancellationToken ct = default)
    {
        if (dateRange.DateTo < dateRange.DateFrom)
            throw new InvalidOperationException("Date To must be on or after Date From.");

        var result = await GetImsReceiptsAsync(new CopayFilterDto
        {
            DateFrom = dateRange.DateFrom,
            DateTo = dateRange.DateTo,
            PageNumber = 1,
            PageSize = 200
        }, ct);
        return result.Items;
    }

    public async Task<IReadOnlyList<CopayProcessLogDto>> GetProcessLogsAsync(
        long entryId, CancellationToken ct = default)
    {
        var tenantId = current.TenantId;
        var entryExists = await db.RoznamchaEntries.AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.Id == entryId, ct);
        var statementExists = await db.CopayStatements.AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.Id == entryId, ct);
        if (!entryExists && !statementExists) throw new KeyNotFoundException("Copay entry was not found.");

        var logs = await db.RoznamchaEntryProcessLogs.AsNoTracking()
            .Where(x => entryExists && x.TenantId == tenantId && x.RoznamchaEntryId == entryId)
            .OrderByDescending(x => x.ProcessedOnUtc)
            .ToListAsync(ct);
        var links = await db.CopaySettlementLinks.AsNoTracking()
            .Where(x => x.TenantId == tenantId &&
                ((entryExists && (x.SourceRoznamchaEntryId == entryId || x.TargetRoznamchaEntryId == entryId)) ||
                 (statementExists && (x.SourceCopayStatementId == entryId || x.TargetCopayStatementId == entryId))))
            .OrderByDescending(x => x.CreatedOnUtc)
            .ToListAsync(ct);
        var statusIds = logs.Where(x => x.StatusId.HasValue).Select(x => x.StatusId!.Value)
            .Concat(links.Where(x => x.StatusId.HasValue).Select(x => x.StatusId!.Value)).Distinct().ToList();
        var rawStatuses = await db.AccountsEntryStatuses.AsNoTracking()
            .Where(x => statusIds.Contains(x.Id) && (x.TenantId == null || x.TenantId == tenantId))
            .ToListAsync(ct);
        var statusNames = rawStatuses
            .GroupBy(x => x.Id)
            .Select(x => x.OrderByDescending(s => s.TenantId == tenantId).First())
            .ToDictionary(x => x.Id, x => x.Name);

        var result = logs.Select(x => new CopayProcessLogDto
        {
            Id = x.Id,
            RoznamchaEntryId = x.RoznamchaEntryId,
            Action = x.Action,
            Notes = x.Notes,
            StatusId = x.StatusId,
            StatusName = x.StatusId.HasValue && statusNames.TryGetValue(x.StatusId.Value, out var name) ? name : GetStatusName(x.StatusId),
            ProcessedByUserId = x.ProcessedByUserId,
            ProcessedOnUtc = x.ProcessedOnUtc
        }).ToList();
        result.AddRange(links.Select(x => new CopayProcessLogDto
        {
            Id = x.Id,
            RoznamchaEntryId = x.SourceRoznamchaEntryId,
            CopayStatementId = x.SourceCopayStatementId,
            SourceReference = x.SourceReference,
            TargetReference = x.TargetReference,
            LinkType = x.LinkType,
            IsActive = x.IsActive,
            Action = x.IsActive ? x.LinkType : $"{x.LinkType} (Un-set)",
            Notes = x.Remarks ?? $"{x.SourceReference} → {x.TargetReference}",
            StatusId = x.StatusId,
            StatusName = x.StatusId.HasValue && statusNames.TryGetValue(x.StatusId.Value, out var name) ? name : GetStatusName(x.StatusId),
            ProcessedByUserId = x.IsActive ? x.CreatedByUserId : x.VoidedByUserId,
            ProcessedOnUtc = x.IsActive ? x.CreatedOnUtc : x.VoidedOnUtc ?? x.CreatedOnUtc
        }));
        return result.OrderByDescending(x => x.ProcessedOnUtc).ToList();
    }

    public async Task<CopayImportResultDto> ImportImsDailySheetAsync(UploadImsSheetDto dto, CancellationToken ct = default)
    {
        if (dto.File == null || dto.File.Length == 0)
            throw new InvalidOperationException("Please select an IMS collection sheet file to upload.");

        var ext = Path.GetExtension(dto.File.FileName).ToLowerInvariant();
        if (ext is not (".xlsx" or ".xls" or ".csv"))
            throw new InvalidOperationException("Supported file types are .xlsx, .xls, and .csv.");

        var tenantId = current.TenantId;
        var config = await ResolveConfigAsync(tenantId, ct);
        var batch = await StartImportBatchAsync(tenantId, "IMS_EXCEL", dto.File, ct);
        if (batch.Status == "Duplicate") return ToImportResult(batch);
        var table = ReadFirstWorksheet(dto.File);
        if (table == null || table.Rows.Count == 0)
            throw new InvalidOperationException("The uploaded sheet has no data rows.");

        var headers = BuildHeaderIndexMap(table);
        var tenantPrefix = await ResolveTenantPrefixAsync(tenantId, ct);
        var maxSeq = await GetMaxImsReceiptSequenceAsync(tenantId, tenantPrefix, ct);

        var entries = new List<RoznamchaEntry>();
        var rowResults = new List<CopayImportRow>();
        var errors = new List<string>();
        var fingerprints = (await db.CopayImportRows.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Result == "Imported")
            .Select(x => x.Fingerprint).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var attachmentPath = dto.Attachment == null ? null : await SaveUploadAsync(tenantId, dto.Attachment, ct);

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            var sourceRow = r + 2;
            batch.TotalRows++;
            var amountStr = ReadString(row, headers, "crdbamt", "cr_dbamt", "crdbeamt", "usdamount", "amount", "copay", "amt", "paid");
            var amount = ParseDecimalWithBrackets(amountStr);
            if (!amount.HasValue)
            {
                AddImportError(rowResults, batch, tenantId, sourceRow, $"invalid|{sourceRow}", "Copay amount is missing or invalid.", errors);
                continue;
            }

            var date = ReadDateOnly(row, headers, dto.YearId, "transdate", "date", "receiptdate");
            if (!date.HasValue)
            {
                AddImportError(rowResults, batch, tenantId, sourceRow, $"invalid-date|{sourceRow}", "Transaction date is missing or invalid.", errors);
                continue;
            }
            var modeRaw = ReadString(row, headers, "trancode", "tran_code", "transmode", "mode", "paymentmode", "type");
            var transModeId = DetectTransactionModeId(modeRaw);

            var patientInfo = ReadString(row, headers, "rdiscriptions", "rdescriptions", "patientinfo", "patientname", "patient", "mrn", "name");
            var instrumentNo = ReadString(row, headers, "instrumentno", "instrument_no", "checkno", "check_no", "cardlast4", "auth");
            var remarks = ReadString(row, headers, "remarks", "rdiscriptions", "rdescriptions", "description", "note");
            var reference = ReadString(row, headers, "refrence", "reference", "receiptno", "transactionreference");
            var referralSource = ReadString(row, headers, "referralsource", "referral", "source");
            var paidCopay = ParseDecimalWithBrackets(ReadString(row, headers, "paidcopamt", "paidcopayamount", "paid"));
            var fp = HashText($"{tenantId}|{date:yyyyMMdd}|{amount:F2}|{transModeId}|{Normalize(instrumentNo)}|{Normalize(patientInfo)}|{Normalize(reference)}");
            if (fingerprints.Contains(fp))
            {
                batch.DuplicateRows++;
                rowResults.Add(NewImportRow(batch, tenantId, sourceRow, fp, "Duplicate", "The IMS Copay row already exists."));
                continue;
            }
            fingerprints.Add(fp);

            maxSeq++;
            var entry = new RoznamchaEntry
            {
                TenantId = tenantId,
                Ref = $"{tenantPrefix}CR-{maxSeq}",
                TransDate = date.Value,
                Amount = 0,
                UsdAmount = amount,
                CategoryId = config.CategoryId,
                FromAccountId = config.HoldingAccountId,
                ToAccountId = config.MerchantClearingAccountId,
                RoznamchaTypeId = config.ReceiptTypeId,
                CurrencyId = config.CurrencyId,
                TransModeId = transModeId,
                IsShow = true,
                IsSettled = false,
                EnterStatusId = transModeId == 1 ? StatusCashCopay : StatusOpen,
                PatientInfo = patientInfo,
                InstrumentNo = instrumentNo,
                Descriptions = remarks,
                Remarks = remarks,
                Reference = reference,
                ReferralSource = referralSource,
                PaidCopayAmount = paidCopay,
                Attachment = attachmentPath,
                CreatedByUserId = current.UserId,
                CreatedOnUtc = DateTime.UtcNow
            };

            entries.Add(entry);
            rowResults.Add(NewImportRow(batch, tenantId, sourceRow, fp, "Imported", null, entry: entry));
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (entries.Count > 0)
        {
            await db.RoznamchaEntries.AddRangeAsync(entries, ct);
            await db.SaveChangesAsync(ct);
        }
        if (rowResults.Count > 0) await db.CopayImportRows.AddRangeAsync(rowResults, ct);
        batch.ImportedRows = entries.Count;
        CompleteBatch(batch, errors);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToImportResult(batch, errors);
    }

    public async Task<SettlementResultDto> SettleImsAutoAsync(
        DateRangeDto dateRange, CancellationToken ct = default)
    {
        var tenantId = current.TenantId;
        var config = await ResolveConfigAsync(tenantId, ct);
        var tolerance = Math.Clamp(dateRange.ToleranceDays ?? config.MerchantMatchWindowDays, 0, 60);

        // Fetch unsettled patient copay collection entries (excluding Cash (CA), which never appears on a card terminal statement)
        var entries = await db.RoznamchaEntries
            .Where(x => x.TenantId == tenantId
                     && x.CategoryId == config.CategoryId
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
                    db.CopaySettlementLinks.Add(NewSettlementLink(
                        tenantId, "IMS_TO_BANK", entry.Ref, bankMatch.BankRef ?? bankMatch.TransactionReferenceNumber,
                        sourceRoznamchaEntryId: entry.Id, targetBankStatementId: bankMatch.Id,
                        statusId: StatusSettled, isManual: false));

                    matchedCount++;
                    totalSettled += amount;
                    continue;
                }
            }

            var match = FindUniqueMerchantMatch(statements, entry.TransDate, amount, tolerance);

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
                db.CopaySettlementLinks.Add(NewSettlementLink(
                    tenantId, "IMS_TO_MERCHANT", entry.Ref, match.Ref,
                    sourceRoznamchaEntryId: entry.Id, targetCopayStatementId: match.Id,
                    statusId: StatusSettled, isManual: false));

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
        var config = await ResolveConfigAsync(tenantId, ct);
        var tolerance = Math.Clamp(dateRange.ToleranceDays ?? config.BankMatchWindowDays, 0, 30);

        // Fetch unsettled collection entries (Cheques, direct payments, etc.)
        var entries = await db.RoznamchaEntries
            .Where(x => x.TenantId == tenantId
                     && x.CategoryId == config.CategoryId
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

            var matchedBank = FindUniqueBankMatch(bankStatements, entry.TransDate, amount, tolerance);

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
                db.CopaySettlementLinks.Add(NewSettlementLink(
                    tenantId, "IMS_TO_BANK", entry.Ref, matchedBank.BankRef ?? matchedBank.TransactionReferenceNumber,
                    sourceRoznamchaEntryId: entry.Id, targetBankStatementId: matchedBank.Id,
                    statusId: StatusImsWithBank, isManual: false));

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
        var config = await ResolveConfigAsync(tenantId, ct);
        var entries = await db.RoznamchaEntries
            .Where(x => x.TenantId == tenantId && request.EntryIds.Contains(x.Id))
            .ToListAsync(ct);

        if (entries.Count == 0)
            throw new InvalidOperationException("None of the selected collection entries were found.");

        var calAmount = entries.Sum(e => e.UsdAmount ?? e.Amount ?? 0);
        var tenantPrefix = await ResolveTenantPrefixAsync(tenantId, ct);
        var targetType = request.TargetType?.Trim().ToUpperInvariant();
        var manualStatus = targetType switch
        {
            "BANK" => StatusImsWithBank,
            "BILLING" => StatusWithBilling,
            "MACHINE" or "MERCHANT" => StatusWithMachine,
            "COPAY_EXCEL" => StatusWithCopayExcel,
            _ => StatusSettledManually
        };

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
                Amount = 0,
                CategoryId = config.CategoryId,
                FromAccountId = config.HoldingAccountId,
                ToAccountId = config.MerchantClearingAccountId,
                RoznamchaTypeId = config.ReceiptTypeId,
                CurrencyId = config.CurrencyId,
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
        var manualLinks = new List<CopaySettlementLink>();

        foreach (var entry in entries)
        {
            entry.IsSettled = true;
            entry.EnterStatusId = manualStatus;
            entry.Reference = linkedTargetRef;
            entry.BankLtRef = linkedTargetRef;
            entry.IsManual = true;
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
                StatusId = manualStatus,
                ProcessedByUserId = current.UserId,
                ProcessedOnUtc = DateTime.UtcNow
            });
            manualLinks.Add(NewSettlementLink(
                tenantId, $"IMS_TO_{targetType ?? "BATCH"}", entry.Ref, linkedTargetRef,
                sourceRoznamchaEntryId: entry.Id,
                targetRoznamchaEntryId: summaryBatch?.Id,
                statusId: manualStatus, isManual: true, remarks: request.Remarks));
        }

        // If target reference is a merchant statement or bank statement, update it
        CopayStatement? statementTarget = null;
        BankStatement? bankTarget = null;
        RoznamchaEntry? rozTarget = null;
        if (!string.IsNullOrWhiteSpace(request.TargetRef))
        {
            if (targetType is "MACHINE" or "MERCHANT")
            {
                statementTarget = await db.CopayStatements
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Ref == request.TargetRef, ct)
                    ?? throw new InvalidOperationException("The target merchant statement was not found for this tenant.");
                statementTarget.IsSettled = true;
                statementTarget.StatusId = StatusTargetSettled;
                statementTarget.IsManual = true;
                statementTarget.CopayRef = AppendReferences(statementTarget.CopayRef, entries.Select(x => x.Ref));
                statementTarget.UpdatedByUserId = current.UserId;
                statementTarget.UpdatedOnUtc = DateTime.UtcNow;
            }
            else if (targetType == "BANK")
            {
                bankTarget = await db.BankStatements
                    .FirstOrDefaultAsync(b => b.TenantId == tenantId &&
                        (b.BankRef == request.TargetRef || b.ReferenceNumber == request.TargetRef ||
                         b.TransactionReferenceNumber == request.TargetRef || b.Id.ToString() == request.TargetRef), ct)
                    ?? throw new InvalidOperationException("The target bank statement was not found for this tenant.");

                bankTarget.IsSettled = true;
                bankTarget.IsManual = true;
                bankTarget.StatusId = StatusTargetSettled;
                bankTarget.ReferenceNumber = AppendReferences(bankTarget.ReferenceNumber, entries.Select(x => x.Ref));
                bankTarget.UpdatedByUserId = current.UserId;
                bankTarget.UpdatedOnUtc = DateTime.UtcNow;

                if (summaryBatch != null)
                {
                    summaryBatch.ToAccountId = bankTarget.ChartAccountId;
                    summaryBatch.BankLtRef = bankTarget.BankRef ?? $"BANK-{bankTarget.Id}";
                }
            }
            else if (targetType == "BILLING")
            {
                rozTarget = await db.RoznamchaEntries
                    .FirstOrDefaultAsync(x => x.TenantId == tenantId && !x.IsDeleted && x.Ref == request.TargetRef, ct)
                    ?? throw new InvalidOperationException("The target Billing Roznamcha entry was not found for this tenant.");
                rozTarget.BankLtRef = AppendReferences(rozTarget.BankLtRef, entries.Select(x => x.Ref));
                rozTarget.IsSettled = true;
                rozTarget.IsManual = true;
                rozTarget.IsClosed = true;
                rozTarget.EnterStatusId = StatusTargetSettled;
                rozTarget.UpdatedByUserId = current.UserId;
                rozTarget.UpdatedOnUtc = DateTime.UtcNow;
            }
            else if (targetType != "COPAY_EXCEL")
            {
                throw new InvalidOperationException("Target type must be BANK, COPAY_EXCEL, BILLING, or MACHINE.");
            }
        }
        else if (summaryBatch == null)
        {
            throw new InvalidOperationException("Target reference is required when a summary batch is not created.");
        }

        foreach (var link in manualLinks)
        {
            if (summaryBatch == null)
            {
                link.TargetCopayStatementId = statementTarget?.Id;
                link.TargetBankStatementId = bankTarget?.Id;
                link.TargetRoznamchaEntryId = rozTarget?.Id;
            }
            db.CopaySettlementLinks.Add(link);
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

    public async Task<SettlementResultDto> SettleMerchantStatementsManualAsync(
        ManualSettleRequestDto request, CancellationToken ct = default)
    {
        if (request.EntryIds == null || request.EntryIds.Count == 0)
            throw new InvalidOperationException("Please select at least one merchant statement row.");
        if (string.IsNullOrWhiteSpace(request.TargetType) || string.IsNullOrWhiteSpace(request.TargetRef))
            throw new InvalidOperationException("Target type and target reference are required.");

        var tenantId = current.TenantId;
        var entries = await db.CopayStatements
            .Where(x => x.TenantId == tenantId && request.EntryIds.Contains(x.Id))
            .ToListAsync(ct);
        if (entries.Count != request.EntryIds.Distinct().Count())
            throw new InvalidOperationException("One or more selected merchant statement rows are unavailable for this tenant.");

        var targetType = request.TargetType.Trim().ToUpperInvariant();
        var targetRef = request.TargetRef.Trim();
        var now = DateTime.UtcNow;
        BankStatement? bankTarget = null;
        CopayStatement? statementTarget = null;
        RoznamchaEntry? rozTarget = null;

        if (targetType == "BANK")
        {
            bankTarget = await db.BankStatements.FirstOrDefaultAsync(x => x.TenantId == tenantId &&
                (x.BankRef == targetRef || x.ReferenceNumber == targetRef || x.TransactionReferenceNumber == targetRef || x.Id.ToString() == targetRef), ct)
                ?? throw new InvalidOperationException("The selected bank statement was not found for this tenant.");
        }
        else if (targetType is "MACHINE" or "MERCHANT")
        {
            statementTarget = await db.CopayStatements.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Ref == targetRef, ct)
                ?? throw new InvalidOperationException("The target merchant statement was not found for this tenant.");
            if (entries.Any(x => x.Id == statementTarget.Id))
                throw new InvalidOperationException("The target merchant statement cannot also be a selected source row.");
        }
        else if (targetType == "BILLING")
        {
            rozTarget = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.TenantId == tenantId && !x.IsDeleted && x.Ref == targetRef, ct)
                ?? throw new InvalidOperationException("The target Billing Roznamcha entry was not found for this tenant.");
        }
        else if (targetType != "COPAY_EXCEL")
        {
            throw new InvalidOperationException("Target type must be BANK, COPAY_EXCEL, BILLING, or MACHINE.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var sourceStatus = targetType switch
        {
            "BANK" => StatusImsWithBank,
            "COPAY_EXCEL" => StatusWithCopayExcel,
            _ => StatusWithBilling
        };

        foreach (var entry in entries)
        {
            entry.CopayRef = targetRef;
            entry.IsSettled = true;
            entry.IsManual = true;
            entry.StatusId = sourceStatus;
            entry.UpdatedByUserId = current.UserId;
            entry.UpdatedOnUtc = now;
            db.CopaySettlementLinks.Add(NewSettlementLink(
                tenantId, $"STATEMENT_TO_{targetType}", entry.Ref, targetRef,
                sourceCopayStatementId: entry.Id,
                targetRoznamchaEntryId: rozTarget?.Id,
                targetCopayStatementId: statementTarget?.Id,
                targetBankStatementId: bankTarget?.Id,
                statusId: sourceStatus, isManual: true, remarks: request.Remarks));
        }

        if (bankTarget != null)
        {
            bankTarget.IsSettled = true;
            bankTarget.IsManual = true;
            bankTarget.StatusId = StatusTargetSettled;
            bankTarget.ReferenceNumber = AppendReferences(bankTarget.ReferenceNumber, entries.Select(x => x.Ref));
            bankTarget.UpdatedByUserId = current.UserId;
            bankTarget.UpdatedOnUtc = now;
        }
        if (statementTarget != null)
        {
            statementTarget.CopayRef = AppendReferences(statementTarget.CopayRef, entries.Select(x => x.Ref));
            statementTarget.IsSettled = true;
            statementTarget.IsManual = true;
            statementTarget.StatusId = StatusTargetSettled;
            statementTarget.UpdatedByUserId = current.UserId;
            statementTarget.UpdatedOnUtc = now;
        }
        if (rozTarget != null)
        {
            rozTarget.BankLtRef = AppendReferences(rozTarget.BankLtRef, entries.Select(x => x.Ref));
            rozTarget.IsSettled = true;
            rozTarget.IsManual = true;
            rozTarget.IsClosed = true;
            rozTarget.EnterStatusId = StatusWithMachine;
            rozTarget.UpdatedByUserId = current.UserId;
            rozTarget.UpdatedOnUtc = now;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new SettlementResultDto
        {
            MatchedCount = entries.Count,
            TotalSettledAmount = entries.Sum(x => Math.Abs(x.TransactionAmount ?? 0)),
            Messages = [$"{entries.Count} merchant statement row(s) linked to {targetType} {targetRef}."]
        };
    }

    public async Task<bool> UnsettleEntryAsync(UnsettleRequestDto request, CancellationToken ct = default)
    {
        var tenantId = current.TenantId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var hasNormalizedLinks = await DeactivateSettlementLinksAsync(
            tenantId, request.RoznamchaEntryId, request.StatementId, request.Remarks, ct);

        if (request.RoznamchaEntryId.HasValue)
        {
            var entry = await db.RoznamchaEntries
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.RoznamchaEntryId.Value, ct);

            if (entry != null)
            {
                // 1. If this was a summary batch (isSHow == false), rollback all child items linked to it
                if (!hasNormalizedLinks && !entry.IsShow && !string.IsNullOrEmpty(entry.Ref))
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
                if (!hasNormalizedLinks && (!string.IsNullOrEmpty(entry.Reference) || !string.IsNullOrEmpty(entry.Ref)))
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
                if (!hasNormalizedLinks && !string.IsNullOrEmpty(entry.Ref))
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
                if (!hasNormalizedLinks && (!string.IsNullOrEmpty(statement.CopayRef) || statement.MatchedRoznamchaEntryId.HasValue))
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

        var rawStatuses = await db.AccountsEntryStatuses.AsNoTracking()
            .Where(x => x.IsActive && (x.TenantId == null || x.TenantId == tenantId))
            .ToListAsync(ct);
        var statuses = rawStatuses
            .GroupBy(x => x.Id)
            .Select(x => x.OrderByDescending(s => s.TenantId == tenantId).First())
            .OrderBy(x => x.Name)
            .Select(x => new LookupItemDto { Id = x.Id, DisplayText = x.Name })
            .ToList();

        var modes = await db.AccountsTransModes.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new LookupItemDto { Id = x.Id, DisplayText = x.Name })
            .ToListAsync(ct);

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

    private async Task<bool> DeactivateSettlementLinksAsync(
        int tenantId, long? sourceRoznamchaId, long? sourceStatementId, string? remarks, CancellationToken ct)
    {
        var links = await db.CopaySettlementLinks
            .Where(x => x.TenantId == tenantId && x.IsActive &&
                ((sourceRoznamchaId.HasValue && x.SourceRoznamchaEntryId == sourceRoznamchaId) ||
                 (sourceStatementId.HasValue && x.SourceCopayStatementId == sourceStatementId)))
            .ToListAsync(ct);
        if (links.Count == 0) return false;

        var now = DateTime.UtcNow;
        foreach (var link in links)
        {
            link.IsActive = false;
            link.VoidedByUserId = current.UserId;
            link.VoidedOnUtc = now;
            if (!string.IsNullOrWhiteSpace(remarks))
                link.Remarks = $"{link.Remarks} {remarks}".Trim();
        }

        var linkIds = links.Select(x => x.Id).ToList();
        foreach (var targetId in links.Where(x => x.TargetBankStatementId.HasValue).Select(x => x.TargetBankStatementId!.Value).Distinct())
        {
            var stillLinked = await db.CopaySettlementLinks.AnyAsync(x => x.TenantId == tenantId && x.IsActive &&
                x.TargetBankStatementId == targetId && !linkIds.Contains(x.Id), ct);
            if (!stillLinked)
            {
                var target = await db.BankStatements.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == targetId, ct);
                if (target != null)
                {
                    target.IsSettled = false; target.IsManual = false; target.StatusId = null;
                    target.ReferenceNumber = null; target.MatchedRoznamchaEntryId = null;
                    target.UpdatedByUserId = current.UserId; target.UpdatedOnUtc = now;
                }
            }
        }
        foreach (var targetId in links.Where(x => x.TargetCopayStatementId.HasValue).Select(x => x.TargetCopayStatementId!.Value).Distinct())
        {
            var stillLinked = await db.CopaySettlementLinks.AnyAsync(x => x.TenantId == tenantId && x.IsActive &&
                x.TargetCopayStatementId == targetId && !linkIds.Contains(x.Id), ct);
            if (!stillLinked)
            {
                var target = await db.CopayStatements.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == targetId, ct);
                if (target != null)
                {
                    target.IsSettled = false; target.IsManual = false; target.StatusId = StatusOpen;
                    target.CopayRef = null; target.MatchedRoznamchaEntryId = null;
                    target.UpdatedByUserId = current.UserId; target.UpdatedOnUtc = now;
                }
            }
        }
        foreach (var targetId in links.Where(x => x.TargetRoznamchaEntryId.HasValue).Select(x => x.TargetRoznamchaEntryId!.Value).Distinct())
        {
            var stillLinked = await db.CopaySettlementLinks.AnyAsync(x => x.TenantId == tenantId && x.IsActive &&
                x.TargetRoznamchaEntryId == targetId && !linkIds.Contains(x.Id), ct);
            if (!stillLinked && targetId != sourceRoznamchaId)
            {
                var target = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == targetId, ct);
                if (target != null)
                {
                    target.IsSettled = false; target.IsManual = false; target.IsClosed = false;
                    target.EnterStatusId = StatusOpen; target.BankLtRef = null; target.Reference = null;
                    target.UpdatedByUserId = current.UserId; target.UpdatedOnUtc = now;
                }
            }
        }
        return true;
    }

    private CopaySettlementLink NewSettlementLink(
        int tenantId,
        string linkType,
        string? sourceReference,
        string? targetReference,
        long? sourceRoznamchaEntryId = null,
        long? sourceCopayStatementId = null,
        long? targetRoznamchaEntryId = null,
        long? targetCopayStatementId = null,
        long? targetBankStatementId = null,
        int? statusId = null,
        bool isManual = false,
        string? remarks = null) => new()
    {
        TenantId = tenantId,
        LinkType = linkType,
        SourceReference = sourceReference,
        TargetReference = targetReference,
        SourceRoznamchaEntryId = sourceRoznamchaEntryId,
        SourceCopayStatementId = sourceCopayStatementId,
        TargetRoznamchaEntryId = targetRoznamchaEntryId,
        TargetCopayStatementId = targetCopayStatementId,
        TargetBankStatementId = targetBankStatementId,
        StatusId = statusId,
        IsManual = isManual,
        Remarks = remarks,
        CreatedByUserId = current.UserId,
        CreatedOnUtc = DateTime.UtcNow
    };

    private static string? AppendReferences(string? existing, IEnumerable<string?> values)
    {
        var refs = (existing ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return refs.Count == 0 ? null : string.Join(',', refs);
    }

    private static BankStatement? FindUniqueBankMatch(
        IEnumerable<BankStatement> candidates, DateOnly sourceDate, decimal amount, int toleranceDays)
    {
        for (var offset = 0; offset <= toleranceDays; offset++)
        {
            var date = sourceDate.AddDays(offset);
            var matches = candidates.Where(x => !x.IsSettled && x.PostingDate == date &&
                (amount >= 0 ? x.Credit == amount : x.Debit == Math.Abs(amount))).Take(2).ToList();
            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1) return null;
        }
        return null;
    }

    private static CopayStatement? FindUniqueMerchantMatch(
        IEnumerable<CopayStatement> candidates, DateOnly sourceDate, decimal amount, int toleranceDays)
    {
        for (var offset = 0; offset <= toleranceDays; offset++)
        {
            var date = sourceDate.AddDays(offset);
            var matches = candidates.Where(x => !x.IsSettled && x.PaymentDate == date && x.TransactionAmount == amount)
                .Take(2).ToList();
            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1) return null;
        }
        return null;
    }

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

    private sealed record CopayConfig(
        int CategoryId,
        int HoldingAccountId,
        int? MerchantClearingAccountId,
        int? MerchantAccountId,
        int CurrencyId,
        int ReceiptTypeId,
        int MerchantMatchWindowDays,
        int BankMatchWindowDays);

    private async Task<CopayConfig> ResolveConfigAsync(int tenantId, CancellationToken ct)
    {
        var settings = await db.AccountsModuleSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);

        var categoryId = settings?.CopayCategoryId
            ?? await db.AccountsCategories.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.IsActive && (x.Code == "COPAY" || x.Name == "Copay"))
                .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct)
            ?? await db.AccountsCategories.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Id == LegacyCopayCategoryId)
                .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);

        var holdingId = settings?.CopayHoldingAccountId
            ?? await db.AccountsChartAccounts.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.IsActive &&
                    (x.AccountCode == "COPAY_HOLDING" || x.AccountName.Contains("Copay Patient")))
                .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct)
            ?? await db.AccountsChartAccounts.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Id == LegacyCopayHoldingAccountId)
                .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);

        var clearingId = settings?.CopayMerchantClearingAccountId
            ?? await db.AccountsChartAccounts.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.IsActive &&
                    (x.AccountCode == "COPAY_MERCHANT_CLEARING" || x.AccountName.Contains("Copay Merchant")))
                .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct)
            ?? await db.AccountsChartAccounts.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Id == LegacyCopayMerchantClearingAccountId)
                .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);

        var currencyId = settings?.CopayCurrencyId
            ?? await db.AccountsCurrencies.AsNoTracking().Where(x => x.Code == "USD" && x.IsActive)
                .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        var receiptTypeId = settings?.CopayReceiptTypeId ?? settings?.ReceiptRozTypeId
            ?? await db.AccountsRoznamchaTypes.AsNoTracking()
                .Where(x => x.IsActive && (x.Code == "RECEIPT" || x.Id == LegacyReceiptRoznamchaTypeId))
                .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);

        if (!categoryId.HasValue || !holdingId.HasValue || !currencyId.HasValue || !receiptTypeId.HasValue)
            throw new InvalidOperationException("Copay tenant configuration is incomplete. Configure category, holding account, USD currency, and receipt type.");

        return new CopayConfig(
            categoryId.Value,
            holdingId.Value,
            clearingId,
            settings?.CopayMerchantAccountId
                ?? await db.AccountsChartAccounts.AsNoTracking()
                    .Where(x => x.TenantId == tenantId && x.Id == LegacyCopayMerchantAccountId && x.IsActive)
                    .Select(x => (int?)x.Id).FirstOrDefaultAsync(ct),
            currencyId.Value,
            receiptTypeId.Value,
            Math.Clamp(settings?.CopayMerchantMatchWindowDays ?? 14, 0, 60),
            Math.Clamp(settings?.CopayBankMatchWindowDays ?? 6, 0, 30));
    }

    private static void ValidateSpreadsheet(IFormFile? file, string label)
    {
        if (file == null || file.Length == 0)
            throw new InvalidOperationException($"Please select a {label} Excel or CSV file.");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".xlsx" or ".xls" or ".csv"))
            throw new InvalidOperationException("Supported file types are .xlsx, .xls, and .csv.");
    }

    private async Task<CopayImportBatch> StartImportBatchAsync(int tenantId, string stage, IFormFile file, CancellationToken ct)
    {
        var fileHash = await HashFileAsync(file, ct);
        var existing = await db.CopayImportBatches.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Stage == stage && x.FileHash == fileHash, ct);
        if (existing != null)
        {
            existing.Status = "Duplicate";
            return existing;
        }

        var batch = new CopayImportBatch
        {
            TenantId = tenantId,
            Stage = stage,
            FileName = Path.GetFileName(file.FileName),
            FileHash = fileHash,
            Status = "Processing",
            CreatedByUserId = current.UserId,
            CreatedOnUtc = DateTime.UtcNow
        };
        db.CopayImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        return batch;
    }

    private static async Task<string> HashFileAsync(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    private static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private async Task<string> SaveUploadAsync(int tenantId, IFormFile file, CancellationToken ct)
    {
        var safeName = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(safeName).ToLowerInvariant();
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".xlsx", ".xls", ".csv", ".pdf", ".png", ".jpg", ".jpeg" };
        if (!allowed.Contains(extension)) throw new InvalidOperationException("Unsupported Copay attachment type.");
        if (file.Length > 20 * 1024 * 1024) throw new InvalidOperationException("Copay attachment cannot exceed 20 MB.");
        var folder = Path.Combine(environment.ContentRootPath, "App_Data", "accounts", tenantId.ToString(CultureInfo.InvariantCulture), "copay");
        Directory.CreateDirectory(folder);
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(folder, storedName);
        await using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await file.CopyToAsync(output, ct);
        return Path.Combine("App_Data", "accounts", tenantId.ToString(CultureInfo.InvariantCulture), "copay", storedName).Replace('\\', '/');
    }

    private static CopayImportRow NewImportRow(CopayImportBatch batch, int tenantId, int rowNumber,
        string fingerprint, string result, string? message, RoznamchaEntry? entry = null, CopayStatement? statement = null) => new()
    {
        TenantId = tenantId,
        ImportBatchId = batch.Id,
        SourceRowNumber = rowNumber,
        Fingerprint = fingerprint,
        Result = result,
        Message = message,
        RoznamchaEntry = entry,
        CopayStatement = statement
    };

    private static void AddImportError(List<CopayImportRow> rows, CopayImportBatch batch, int tenantId,
        int sourceRow, string fingerprint, string message, List<string> errors)
    {
        batch.InvalidRows++;
        if (errors.Count < 100) errors.Add($"Row {sourceRow}: {message}");
        rows.Add(NewImportRow(batch, tenantId, sourceRow, HashText(fingerprint), "Invalid", message));
    }

    private static void CompleteBatch(CopayImportBatch batch, IReadOnlyCollection<string> errors)
    {
        batch.Status = errors.Count == 0 ? "Completed" : batch.ImportedRows > 0 ? "CompletedWithErrors" : "Failed";
        batch.CompletedOnUtc = DateTime.UtcNow;
        batch.ErrorSummary = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors.Take(20));
    }

    private static CopayImportResultDto ToImportResult(CopayImportBatch batch, List<string>? errors = null) => new()
    {
        BatchId = batch.Id,
        TotalRows = batch.TotalRows,
        ImportedRows = batch.ImportedRows,
        DuplicateRows = batch.DuplicateRows,
        InvalidRows = batch.InvalidRows,
        Errors = errors ?? (batch.Status == "Duplicate" ? ["This exact file was already imported."] : [])
    };

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
