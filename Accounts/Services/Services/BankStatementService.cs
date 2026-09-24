using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using ExcelDataReader;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class BankStatementService(
    ApplicationDbContext db,
    ICurrentUserService current,
    IRoznamchaService roznamcha,
    IFileStorageService files) : IBankStatementService
{
    static BankStatementService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<IReadOnlyList<BankStatementDto>> ListAsync(
        int? accountId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken ct = default) =>
        await SpListQuery.ExecAsync<BankStatementDto>(
            db,
            "EXEC dbo.usp_Accounts_BankStatementList @TenantId, @AccountId, @DateFrom, @DateTo",
            ct,
            SpListQuery.TenantId(current.TenantId),
            SpListQuery.IntNullable("@AccountId", accountId),
            SpListQuery.DateNullable("@DateFrom", dateFrom),
            SpListQuery.DateNullable("@DateTo", dateTo));

    public Task<BankStatementDto?> GetAsync(long id, CancellationToken ct = default) =>
        Map(db.BankStatements.AsNoTracking()).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<BankStatementDto> SaveAsync(long? id, SaveBankStatementRequest request, CancellationToken ct = default)
    {
        var account = await ResolveAccountAsync(request.AccountId, ct);
        ValidateAmounts(request.Debit, request.Credit);

        BankStatement row;
        if (id.HasValue)
        {
            row = await db.BankStatements.FirstOrDefaultAsync(x => x.Id == id.Value, ct)
                ?? throw new KeyNotFoundException("Bank statement was not found.");
            row.UpdatedByUserId = current.UserId;
            row.UpdatedOnUtc = DateTime.UtcNow;
            row.IsManual = request.IsManual;
        }
        else
        {
            row = new BankStatement
            {
                TenantId = current.TenantId,
                CreatedByUserId = current.UserId,
                IsManual = true
            };
            db.BankStatements.Add(row);
        }

        ApplyRow(row, request, account.AccountReference ?? account.AccountNumber);
        await db.SaveChangesAsync(ct);

        if (string.IsNullOrWhiteSpace(row.ReferenceNumber))
        {
            row.ReferenceNumber = $"BS-{row.Id}";
            await db.SaveChangesAsync(ct);
        }

        return await GetAsync(row.Id, ct) ?? throw new InvalidOperationException("Saved bank statement could not be reloaded.");
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var row = await db.BankStatements.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Bank statement was not found.");
        if (row.IsMatched || row.MatchedRoznamchaEntryId.HasValue)
            throw new InvalidOperationException("Matched statement lines cannot be deleted. Reverse the Roznamcha entry first.");
        db.BankStatements.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<BankStatementPreviewResultDto> PreviewExcelAsync(
        int accountId,
        string dateFormat,
        int? year,
        IFormFile excelFile,
        CancellationToken ct = default)
    {
        await ResolveAccountAsync(accountId, ct);
        var format = NormalizeDateFormat(dateFormat);
        if (excelFile == null || excelFile.Length == 0)
            throw new InvalidOperationException("Excel statement file is required.");

        var ext = Path.GetExtension(excelFile.FileName).ToLowerInvariant();
        if (ext is not (".xlsx" or ".xls" or ".csv"))
            throw new InvalidOperationException("Please upload a valid file: .xlsx, .xls, or .csv.");

        await using var stream = excelFile.OpenReadStream();
        using var reader = ext == ".csv"
            ? ExcelReaderFactory.CreateCsvReader(stream)
            : ExcelReaderFactory.CreateReader(stream);
        var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
        });

        if (dataSet.Tables.Count == 0)
            throw new InvalidOperationException("The uploaded file has no data rows.");

        var table = dataSet.Tables[0];
        var map = BuildHeaderMap(table);
        var rows = new List<BankStatementPreviewRowDto>();
        for (var i = 0; i < table.Rows.Count; i++)
        {
            var dataRow = table.Rows[i];
            if (IsBlankStatementRow(dataRow, map))
                continue;
            var preview = ParsePreviewRow(i + 2, dataRow, map, format, year);
            rows.Add(preview);
        }

        if (rows.Count == 0)
            throw new InvalidOperationException("The uploaded file has no statement data rows.");

        return new BankStatementPreviewResultDto
        {
            TotalRows = rows.Count,
            ValidRows = rows.Count(x => x.IsValid),
            InvalidRows = rows.Count(x => !x.IsValid),
            Rows = rows
        };
    }

    public async Task<IReadOnlyList<BankStatementDto>> SaveUploadAsync(
        BankStatementUploadSaveRequest request,
        IFormFile? attachment,
        CancellationToken ct = default)
    {
        var account = await ResolveAccountAsync(request.AccountId, ct);
        var format = NormalizeDateFormat(request.DateFormat);
        var sourceRows = request.Rows ?? [];
        var valid = sourceRows
            .Where(x => x.IsValid)
            .Where(x => x.Debit > 0 || x.Credit > 0)
            .ToList();
        if (valid.Count == 0)
            throw new InvalidOperationException("No valid statement rows to save.");
        if (sourceRows.Any(x => !x.IsValid))
            throw new InvalidOperationException("The preview contains invalid rows. Fix the Excel file and preview again.");

        string? attachmentPath = null;
        if (attachment is { Length: > 0 })
        {
            var stored = await files.SaveAccountsFileAsync(current.TenantId, attachment, ct);
            attachmentPath = stored.StoredPath;
        }

        var createdIds = new List<long>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var item in valid)
        {
            var row = new BankStatement
            {
                TenantId = current.TenantId,
                ChartAccountId = account.Id,
                AccountNumber = Clean(account.AccountReference ?? account.AccountNumber, 50),
                ValueDate = item.ValueDate,
                PostingDate = item.PostingDate,
                StatementDate = item.PostingDate ?? item.ValueDate,
                InstrumentNo = Clean(item.InstrumentNo, 100),
                Description = Clean(item.TransactionDetails, 1000),
                TransactionReferenceNumber = Clean(item.TransactionReferenceNo, 100),
                Debit = decimal.Round(item.Debit, 2),
                Credit = decimal.Round(item.Credit, 2),
                Balance = decimal.Round(item.Balance, 2),
                Remarks = Clean(item.Remarks, 2000),
                DateFormat = format,
                YearId = request.YearId,
                Attachment = attachmentPath,
                IsManual = false,
                CreatedByUserId = current.UserId
            };
            db.BankStatements.Add(row);
            await db.SaveChangesAsync(ct);
            row.ReferenceNumber = $"BS-{row.Id}";
            createdIds.Add(row.Id);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await Map(db.BankStatements.AsNoTracking())
            .Where(x => createdIds.Contains(x.Id))
            .OrderByDescending(x => x.Id)
            .ToListAsync(ct);
    }

    public async Task<BankStatementTransferResultDto> TransferToRoznamchaAsync(
        BankStatementTransferRequest request,
        CancellationToken ct = default)
    {
        if (request.AccountId <= 0) throw new InvalidOperationException("Bank account is required.");
        if (request.DateTo < request.DateFrom) throw new InvalidOperationException("From Date cannot be later than To Date.");
        await ResolveAccountAsync(request.AccountId, ct);

        var settings = await db.AccountsModuleSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == current.TenantId, ct);
        if (settings?.DefaultFromAccountId is null || settings.DefaultToAccountId is null)
            throw new InvalidOperationException(
                "Configure Default From/To accounts in Accounts module settings before transferring statements to Roznamcha.");

        var counterFrom = settings.DefaultFromAccountId.Value;
        var counterTo = settings.DefaultToAccountId.Value;
        if (counterFrom == request.AccountId || counterTo == request.AccountId)
            throw new InvalidOperationException("Default From/To accounts must be different from the selected bank statement account.");

        var lines = await db.BankStatements
            .Where(x => x.ChartAccountId == request.AccountId
                        && !x.IsMatched
                        && x.MatchedRoznamchaEntryId == null
                        && (x.PostingDate ?? x.ValueDate ?? x.StatementDate) >= request.DateFrom
                        && (x.PostingDate ?? x.ValueDate ?? x.StatementDate) <= request.DateTo)
            .OrderBy(x => x.PostingDate ?? x.ValueDate ?? x.StatementDate)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        var transferred = 0;
        var skipped = 0;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var line in lines)
        {
            var debit = line.Debit ?? 0;
            var credit = line.Credit ?? 0;
            var amount = debit > 0 ? debit : credit;
            var transDate = line.PostingDate ?? line.ValueDate ?? line.StatementDate;
            if (amount <= 0 || !transDate.HasValue)
            {
                skipped++;
                continue;
            }

            // Debit on bank statement = money out → Payment (from bank → counter To)
            // Credit on bank statement = money in → Receipt (from counter From → bank)
            var isPayment = debit > 0;
            var entryTypeId = isPayment ? 1 : 2;
            var created = await roznamcha.CreateAsync(entryTypeId, new SaveRoznamchaEntryRequest
            {
                CategoryId = settings.BillingCategoryId,
                FromAccountId = isPayment ? request.AccountId : counterFrom,
                ToAccountId = isPayment ? counterTo : request.AccountId,
                TransactionTypeId = entryTypeId,
                InstrumentNo = line.InstrumentNo,
                Description = line.Description ?? $"Bank statement {line.ReferenceNumber}",
                Amount = amount,
                TransactionDate = transDate.Value,
                Remarks = line.Remarks,
                BankReference = line.TransactionReferenceNumber,
                BankLTReference = line.ReferenceNumber,
                IsManual = false,
                IsSettled = false
            }, ct);

            line.IsMatched = true;
            line.MatchedRoznamchaEntryId = created.Id;
            line.IsSettled = true;
            line.UpdatedByUserId = current.UserId;
            line.UpdatedOnUtc = DateTime.UtcNow;
            transferred++;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new BankStatementTransferResultDto
        {
            TransferredCount = transferred,
            SkippedCount = skipped,
            Message = transferred == 0
                ? "No unmatched statement lines found for the selected account and date range."
                : $"Transferred {transferred} statement line(s) to Roznamcha. Skipped {skipped}."
        };
    }

    private async Task<(int Id, string AccountNumber, string? AccountReference)> ResolveAccountAsync(int accountId, CancellationToken ct)
    {
        var account = await db.AccountsChartAccounts.AsNoTracking()
            .Where(x => x.Id == accountId && x.IsActive)
            .Select(x => new { x.Id, x.AccountNumber, x.AccountReference })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Account was not found or is inactive.");
        return (account.Id, account.AccountNumber, account.AccountReference);
    }

    private static void ValidateAmounts(decimal debit, decimal credit)
    {
        if (debit < 0 || credit < 0) throw new InvalidOperationException("Debit and credit cannot be negative.");
        if (debit > 0 && credit > 0) throw new InvalidOperationException("A statement row cannot contain both debit and credit.");
    }

    private static void ApplyRow(BankStatement row, SaveBankStatementRequest request, string? accountRef)
    {
        row.ChartAccountId = request.AccountId;
        row.AccountNumber = Clean(accountRef, 50);
        row.ValueDate = request.ValueDate;
        row.PostingDate = request.PostingDate;
        row.StatementDate = request.PostingDate ?? request.ValueDate;
        row.InstrumentNo = Clean(request.InstrumentNo, 100);
        row.Description = Clean(request.TransactionDetails, 1000);
        row.TransactionReferenceNumber = Clean(request.TransactionReferenceNo, 100);
        row.Debit = decimal.Round(request.Debit, 2);
        row.Credit = decimal.Round(request.Credit, 2);
        row.Balance = decimal.Round(request.Balance, 2);
        row.Remarks = Clean(request.Remarks, 2000);
        row.ReferenceNumber = Clean(request.ReferenceNo, 80);
        row.Attachment = Clean(request.Attachment, 500);
        row.IsSettled = request.IsSettled;
        row.IsReversal = request.IsReversal;
        row.StatusId = request.StatusId;
        row.DateFormat = Clean(request.DateFormat, 40);
    }

    private IQueryable<BankStatementDto> Map(IQueryable<BankStatement> query) =>
        from s in query
        join a in db.AccountsChartAccounts.AsNoTracking() on s.ChartAccountId equals a.Id into accounts
        from a in accounts.DefaultIfEmpty()
        select new BankStatementDto
        {
            Id = s.Id,
            AccountId = s.ChartAccountId,
            AccountName = a == null ? null : a.AccountName,
            AccountReference = s.AccountNumber,
            ValueDate = s.ValueDate,
            PostingDate = s.PostingDate,
            InstrumentNo = s.InstrumentNo,
            TransactionDetails = s.Description,
            TransactionReferenceNo = s.TransactionReferenceNumber,
            Debit = s.Debit ?? 0,
            Credit = s.Credit ?? 0,
            Balance = s.Balance ?? 0,
            Remarks = s.Remarks,
            ReferenceNo = s.ReferenceNumber,
            Attachment = s.Attachment,
            IsSettled = s.IsSettled,
            IsReversal = s.IsReversal,
            IsManual = s.IsManual,
            IsMatched = s.IsMatched,
            StatusId = s.StatusId,
            DateFormat = s.DateFormat
        };

    private static string NormalizeDateFormat(string? dateFormat)
    {
        var value = (dateFormat ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Please select an Excel Date Format.");
        return value is "MDY" or "DMY"
            ? value
            : throw new InvalidOperationException("Date format must be MDY or DMY.");
    }

    private static Dictionary<string, int> BuildHeaderMap(DataTable table)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var name = NormalizeHeader(table.Columns[c].ColumnName);
            if (!string.IsNullOrWhiteSpace(name) && !map.ContainsKey(name))
                map[name] = c;
        }
        return map;
    }

    private static string NormalizeHeader(string? header)
    {
        var raw = (header ?? string.Empty).Trim().ToLowerInvariant();
        raw = Regex.Replace(raw, @"[^a-z0-9]+", "");
        return raw switch
        {
            "valuedate" or "value" or "valdate" => "valuedate",
            "postingdate" or "postdate" or "date" or "transdate" or "transactiondate" => "postingdate",
            "instrumentno" or "instrument" or "chequeno" or "checkno" => "instrumentno",
            "transactiondetails" or "details" or "description" or "narration" or "particulars" => "transactiondetails",
            "transactionrefno" or "transactionref" or "refno" or "reference" or "bankref" => "transactionrefno",
            "debit" or "debitamount" or "withdrawal" or "dr" => "debit",
            "credit" or "creditamount" or "deposit" or "cr" => "credit",
            "balance" or "runningbalance" => "balance",
            "remarks" or "remark" or "note" or "notes" => "remarks",
            _ => raw
        };
    }

    private static bool IsBlankStatementRow(DataRow dataRow, IReadOnlyDictionary<string, int> map)
    {
        static bool HasText(DataRow row, IReadOnlyDictionary<string, int> headers, string key)
        {
            if (!headers.TryGetValue(key, out var index)) return false;
            var value = row[index];
            if (value == null || value == DBNull.Value) return false;
            if (value is DateTime) return true;
            if (value is double or float or decimal or int or long)
                return Convert.ToDecimal(value, CultureInfo.InvariantCulture) != 0;
            return !string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        return !(
            HasText(dataRow, map, "valuedate")
            || HasText(dataRow, map, "postingdate")
            || HasText(dataRow, map, "debit")
            || HasText(dataRow, map, "credit")
            || HasText(dataRow, map, "balance")
            || HasText(dataRow, map, "instrumentno")
            || HasText(dataRow, map, "transactiondetails")
            || HasText(dataRow, map, "transactionrefno")
            || HasText(dataRow, map, "remarks"));
    }

    private static BankStatementPreviewRowDto ParsePreviewRow(
        int rowNumber,
        DataRow dataRow,
        IReadOnlyDictionary<string, int> map,
        string dateFormat,
        int? year)
    {
        var errors = new List<string>();
        var valueDate = ReadDate(dataRow, map, "valuedate", dateFormat, year, errors, "Value Date");
        var postingDate = ReadDate(dataRow, map, "postingdate", dateFormat, year, errors, "Posting Date") ?? valueDate;
        var debit = ReadDecimal(dataRow, map, "debit", errors, "Debit");
        var credit = ReadDecimal(dataRow, map, "credit", errors, "Credit");
        var balance = ReadDecimal(dataRow, map, "balance", errors, "Balance");
        var details = ReadString(dataRow, map, "transactiondetails");

        // Opening Balance lines often carry amount only in Balance (Debit/Credit blank).
        if (debit <= 0 && credit <= 0 && balance != 0 && IsOpeningBalanceDetails(details))
        {
            if (balance > 0) credit = Math.Abs(balance);
            else debit = Math.Abs(balance);
        }

        if (debit > 0 && credit > 0) errors.Add("Both debit and credit are present.");
        if (debit <= 0 && credit <= 0) errors.Add("Debit or Credit amount is required.");
        if (!valueDate.HasValue && !postingDate.HasValue) errors.Add("Value Date or Posting Date is required.");

        return new BankStatementPreviewRowDto
        {
            RowNumber = rowNumber,
            ValueDate = valueDate,
            PostingDate = postingDate,
            InstrumentNo = ReadString(dataRow, map, "instrumentno"),
            TransactionDetails = details,
            TransactionReferenceNo = ReadString(dataRow, map, "transactionrefno"),
            Debit = debit,
            Credit = credit,
            Balance = balance,
            Remarks = ReadString(dataRow, map, "remarks"),
            IsValid = errors.Count == 0,
            ErrorMessage = errors.Count == 0 ? null : string.Join(" ", errors)
        };
    }

    private static bool IsOpeningBalanceDetails(string? details)
    {
        if (string.IsNullOrWhiteSpace(details)) return false;
        var text = details.Trim().ToLowerInvariant();
        return text.Contains("opening balance")
            || text.Contains("opening bal")
            || text is "opening" or "op. balance" or "op balance";
    }

    private static string? ReadString(DataRow row, IReadOnlyDictionary<string, int> map, string key)
    {
        if (!map.TryGetValue(key, out var index)) return null;
        var value = row[index];
        if (value == null || value == DBNull.Value) return null;
        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static decimal ReadDecimal(DataRow row, IReadOnlyDictionary<string, int> map, string key, List<string> errors, string label)
    {
        if (!map.TryGetValue(key, out var index)) return 0;
        var value = row[index];
        if (value == null || value == DBNull.Value) return 0;
        if (value is double or float or decimal or int or long)
            return Math.Round(Convert.ToDecimal(value, CultureInfo.InvariantCulture), 2);
        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text)) return 0;
        text = text.Replace(",", "").Replace("(", "-").Replace(")", "");
        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
            return Math.Round(amount, 2);
        errors.Add($"{label} is invalid.");
        return 0;
    }

    private static DateOnly? ReadDate(
        DataRow row,
        IReadOnlyDictionary<string, int> map,
        string key,
        string dateFormat,
        int? year,
        List<string> errors,
        string label)
    {
        if (!map.TryGetValue(key, out var index)) return null;
        var value = row[index];
        if (value == null || value == DBNull.Value) return null;
        if (value is DateTime dt) return DateOnly.FromDateTime(dt);
        if (value is double oa)
        {
            try { return DateOnly.FromDateTime(DateTime.FromOADate(oa)); }
            catch { errors.Add($"{label} is invalid."); return null; }
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return null;

        // When Excel has only day/month, apply selected Year.
        if (year is >= 1900 and <= 2100
            && Regex.IsMatch(text, @"^\d{1,2}([/\-.])\d{1,2}$"))
            text = $"{text}{text[text.IndexOfAny(['/', '-', '.'])]!}{year.Value}";

        var formats = dateFormat == "MDY"
            ? new[] { "M/d/yyyy", "MM/dd/yyyy", "M-d-yyyy", "MM-dd-yyyy", "yyyy-MM-dd" }
            : new[] { "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "yyyy-MM-dd" };

        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            return DateOnly.FromDateTime(parsed);

        errors.Add($"{label} is invalid for {dateFormat}.");
        return null;
    }

    private static string? Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
