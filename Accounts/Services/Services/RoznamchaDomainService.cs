using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using ExcelDataReader;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;

namespace Accounts.Services.Services;

public sealed class RoznamchaService(
    ApplicationDbContext db,
    ICurrentUserService current,
    IReferenceGeneratorService references,
    IFileStorageService files) : IRoznamchaService
{
    public async Task<PaymentRozExcelImportResult> ImportPaymentsAsync(IFormFile excelFile, CancellationToken ct = default)
    {
        if (excelFile == null || excelFile.Length == 0)
            throw new InvalidOperationException("Excel payment file is required.");

        var extension = Path.GetExtension(excelFile.FileName).ToLowerInvariant();
        if (extension is not (".xlsx" or ".xls" or ".csv"))
            throw new InvalidOperationException("Please upload a valid .xlsx, .xls, or .csv file.");

        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        DataTable table;
        try
        {
            await using var upload = excelFile.OpenReadStream();
            await using var source = new MemoryStream();
            await upload.CopyToAsync(source, ct);
            source.Position = 0;
            using var reader = extension == ".csv"
                ? ExcelReaderFactory.CreateCsvReader(source)
                : ExcelReaderFactory.CreateReader(source);
            var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
            {
                ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
            });
            table = dataSet.Tables.Cast<DataTable>().FirstOrDefault(x => x.Rows.Count > 0)
                ?? throw new InvalidOperationException("The uploaded file has no payment rows.");
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "The uploaded file could not be read as a valid Excel or CSV file. " + (ex.InnerException?.Message ?? ex.Message));
        }

        var headers = table.Columns.Cast<DataColumn>()
            .GroupBy(x => NormalizeExcelHeader(x.ColumnName))
            .Where(x => x.Key.Length > 0)
            .ToDictionary(x => x.Key, x => x.First().Ordinal, StringComparer.OrdinalIgnoreCase);
        if (!HasAnyExcelHeader(headers, "ref", "oldref", "reference", "acctname", "fromaccount", "toacctname", "debit", "amount", "transdate"))
        {
            var found = table.Columns.Cast<DataColumn>()
                .Select(x => x.ColumnName?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x));
            throw new InvalidOperationException(
                "The Excel header row was not recognized as Payment (ROZ) columns. Expected headers such as Ref, AcctName, ToAcctName, Debit, and TransDate. Found: "
                + string.Join(", ", found)
                + ".");
        }
        var accounts = await db.AccountsChartAccounts.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        var categories = await db.AccountsCategories.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        var currencies = await db.AccountsCurrencies.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        var transTypes = await db.AccountsTransTypes.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        var transModes = await db.AccountsTransModes.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
        var statuses = await RoznamchaStatusCatalog.Query(db, "Payment").ToListAsync(ct);
        var projects = await db.AccountsProjects.Where(x => x.IsActive).ToListAsync(ct);
        var products = await db.SaleRoznamchaProducts.AsNoTracking().Where(x => x.IsActive && !x.IsDeleted).ToListAsync(ct);
        var stockReceipts = await db.SaleRoznamchaStockReceipts
            .Where(x => x.Reference != null)
            .ToListAsync(ct);
        var stockByReference = stockReceipts
            .GroupBy(x => NormalizeMatchText(x.Reference))
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        var existingOldRefs = (await db.RoznamchaEntries.AsNoTracking()
                .Where(x => x.OldRef != null)
                .Select(x => x.OldRef!)
                .ToListAsync(ct))
            .Select(NormalizeMatchText)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new PaymentRozExcelImportResult { TotalRows = table.Rows.Count };
        var fileReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaultPaymentType = transTypes.FirstOrDefault(x => EqualsLookup(x.Code, "PAYMENT"))
            ?? transTypes.FirstOrDefault(x => EqualsLookup(x.Name, "PAYMENT"));
        var defaultStatus = statuses.FirstOrDefault(x => EqualsLookup(x.Code, "ENTERED"))
            ?? statuses.FirstOrDefault(x => EqualsLookup(x.Code, "DRAFT"));
        var pkr = FindConfiguredCurrency(currencies, "PKR", "RS", "RUPEE", "RUPEES", "PRS");
        var usd = FindConfiguredCurrency(currencies, "USD");

        for (var index = 0; index < table.Rows.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var excelRow = index + 2;
            var row = table.Rows[index];
            if (row.ItemArray.All(x => string.IsNullOrWhiteSpace(Convert.ToString(x, CultureInfo.InvariantCulture))))
            {
                result.SkippedRows++;
                continue;
            }

            try
            {
                if (ReadBool(row, headers, "isdeleted", "deleted"))
                {
                    result.SkippedRows++;
                    continue;
                }

                var oldRef = ReadText(row, headers, "oldref", "oldreference", "oldreferenceno", "ref", "reference", "referenceno");
                var normalizedRef = NormalizeMatchText(oldRef);
                if (string.IsNullOrEmpty(normalizedRef))
                    throw new InvalidOperationException("Ref / Old_Ref is required so the same payment is not imported twice.");
                if (existingOldRefs.Contains(normalizedRef) || !fileReferences.Add(normalizedRef))
                {
                    result.SkippedRows++;
                    result.DuplicateRows++;
                    continue;
                }

                stockByReference.TryGetValue(normalizedRef, out var matchedStocks);
                var matchedStock = matchedStocks?.Count == 1 ? matchedStocks[0] : null;
                var fromAccount = ResolveAccount(accounts,
                    ReadInt(row, headers, "fromaccountid", "fromacctid"),
                    ReadText(row, headers, "acctname", "accountname", "fromaccount", "fromaccountname", "fromacct", "fromaccountcode", "bankname", "bank"),
                    ReadText(row, headers, "fromaccno", "fromaccountno", "fromaccountnumber", "fromacctno"));
                if (fromAccount == null && matchedStock != null)
                    fromAccount = accounts.FirstOrDefault(x => x.Id == matchedStock.FromAccountId);

                var toAccount = ResolveAccount(accounts,
                    ReadInt(row, headers, "toacctid", "toaccountid"),
                    ReadText(row, headers, "toacctname", "toaccountname", "toaccount", "toacct", "tobankname", "tobank"),
                    ReadText(row, headers, "toacctno", "toaccountno", "toaccountnumber"));
                if (toAccount == null && matchedStock?.ToAccountId is int matchedToId)
                    toAccount = accounts.FirstOrDefault(x => x.Id == matchedToId);

                var product = ResolveProduct(products,
                    ReadLong(row, headers, "productid"),
                    ReadText(row, headers, "productcode", "itemcode"),
                    ReadText(row, headers, "productname", "product", "itemname"));
                if (toAccount == null && product != null)
                    throw new InvalidOperationException($"To Account was not matched. Product '{product.ProductCode}' was found — add ToAcctName / To Account.");

                if (fromAccount == null)
                    throw new InvalidOperationException("From Account was not matched by AcctName / from Acc No.");
                if (toAccount == null)
                    throw new InvalidOperationException("To Account was not matched by ToAcctName / To Acct No.");
                if (fromAccount.Id == toAccount.Id) throw new InvalidOperationException("From and To accounts cannot be the same.");

                var categoryText = ReadText(row, headers, "category", "fromcategory");
                var category = ResolveLookup(categories, categoryText, x => x.Code, x => x.Name)
                    ?? categories.FirstOrDefault(x => x.Id == fromAccount.CategoryId);
                var usdAmount = ReadDecimal(row, headers, "usdamt", "usdamount", "usddebit");
                var pkrAmount = ReadDecimal(row, headers, "totalpurchasecost", "amount", "debit", "pkramount", "credit");
                var quantity = ReadDecimal(row, headers, "qty", "quantity", "purchasedquantity");
                var rate = ReadDecimal(row, headers, "rate", "unitcost", "purchaseprice");
                var isUsd = usdAmount != 0 && pkrAmount == 0;
                var amount = isUsd ? usdAmount : pkrAmount;
                if (amount == 0 && quantity > 0 && rate != 0)
                    amount = decimal.Round(quantity * rate, 2, MidpointRounding.AwayFromZero);
                if (amount == 0) throw new InvalidOperationException("Debit / Amount is missing or zero.");
                var currencyText = ReadText(row, headers, "currency", "currencycode");
                var currency = ResolveLookup(currencies, currencyText, x => x.Code, x => x.Name);
                if (currency == null && IsLocalCurrencyAlias(currencyText))
                    currency = pkr;
                currency ??= isUsd ? usd : pkr;
                if (currency == null)
                    throw new InvalidOperationException(isUsd
                        ? "USD currency is not configured. Add it on Create Accounts → Account Type → Currency."
                        : "PKR currency is not configured. Add PKR, RS, or Rupee on Create Accounts → Account Type → Currency.");

                var transType = ResolveLookup(transTypes,
                    ReadText(row, headers, "transtype", "transactiontype", "rtype", "type"), x => x.Code, x => x.Name)
                    ?? defaultPaymentType;
                var transMode = ResolveLookup(transModes,
                    ReadText(row, headers, "transmode", "transactionmode", "mode"), x => x.Code, x => x.Name);
                var statusText = ReadText(row, headers, "status", "entrystatus");
                var explicitStatusId = ReadInt(row, headers, "statusid", "entrystatusid");
                var status = explicitStatusId.HasValue
                    ? statuses.FirstOrDefault(x => x.Id == explicitStatusId.Value)
                    : ResolveLookup(statuses, statusText, x => x.Code, x => x.Name);
                if ((explicitStatusId.HasValue || !string.IsNullOrWhiteSpace(statusText)) && status == null)
                    throw new InvalidOperationException($"Status '{statusText ?? explicitStatusId?.ToString()}' is not configured for Payment in Settings / Configuration.");
                if (explicitStatusId.HasValue && !string.IsNullOrWhiteSpace(statusText) &&
                    ResolveLookup(statuses, statusText, x => x.Code, x => x.Name)?.Id != explicitStatusId.Value)
                    throw new InvalidOperationException("Status ID and status name refer to different configured statuses.");
                status ??= defaultStatus;
                var project = await EnsureProjectAsync(
                    projects,
                    ReadInt(row, headers, "projectid", "project_id"),
                    ReadText(row, headers, "project", "projectname"),
                    ct);
                var transactionDate = ReadDate(row, headers, "transdate", "transactiondate", "purchaseddate", "transdateadjustment", "date")
                    ?? throw new InvalidOperationException("TransDate is missing or invalid.");

                var request = new SaveRoznamchaEntryRequest
                {
                    OldReferenceNo = oldRef,
                    CategoryId = category?.Id,
                    FromAccountId = fromAccount.Id,
                    ToAccountId = toAccount.Id,
                    TransactionTypeId = transType?.Id,
                    TransactionModeId = transMode?.Id,
                    InstrumentNo = ReadText(row, headers, "instrumentno", "instrumentnumber", "chequeno"),
                    Description = ReadText(row, headers, "discriptions", "descriptions", "description", "rdiscriptions"),
                    CurrencyId = currency.Id,
                    Adjustment = ReadDecimal(row, headers, "adjustment"),
                    Amount = amount,
                    Quantity = quantity,
                    Rate = rate,
                    EntryStatusId = status?.Id,
                    Remarks = ReadText(row, headers, "remarks", "remark"),
                    BankReference = ReadText(row, headers, "bankref", "bankreference"),
                    BankLTReference = ReadText(row, headers, "libref", "ltref", "bankltref"),
                    TransactionDate = transactionDate,
                    IsSettled = EqualsLookup(status?.Code, "SETTLED") || EqualsLookup(status?.Name, "SETTLED"),
                    IsManual = true,
                    ProjectId = project?.Id,
                    ProductId = matchedStock == null ? product?.Id : null,
                    LibraryDocumentIds = []
                };

                // Historical Excel already represents individual ledger sides. Do not
                // synthesize receipts when importing legacy payment rows.
                var created = await CreateCoreAsync(1, request, false, ct);
                var createdEntity = await db.RoznamchaEntries.FirstAsync(x => x.Id == created.Id, ct);
                var createdOn = ReadDateTime(row, headers, "createddate", "createdon", "createdatutc");
                if (createdOn.HasValue) createdEntity.CreatedOnUtc = createdOn.Value;
                createdEntity.IsApproved = EqualsLookup(status?.Code, "APPROVED") || EqualsLookup(status?.Name, "APPROVED");
                createdEntity.Attachment = Clean(ReadText(row, headers, "attachment", "attachmentname"), 500);
                createdEntity.ImagePath = Clean(ReadText(row, headers, "image", "imagename", "imagepath"), 500);

                if (matchedStock != null && matchedStock.RoznamchaEntryId == null)
                {
                    // Successful previous rows are detached to keep large imports bounded.
                    if (db.Entry(matchedStock).State == EntityState.Detached)
                        db.SaleRoznamchaStockReceipts.Attach(matchedStock);
                    matchedStock.RoznamchaEntryId = created.Id;
                    createdEntity.IsLocked = true;
                    result.InventoryLinksRestored++;
                }
                await db.SaveChangesAsync(ct);
                if (!string.IsNullOrEmpty(normalizedRef)) existingOldRefs.Add(normalizedRef);
                result.ImportedRows++;
                db.ChangeTracker.Clear();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear();
                result.Errors.Add($"Row {excelRow}: {FormatImportError(ex)}");
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<RoznamchaEntryDto>> ListAsync(int entryTypeId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        EnsureEntryType(entryTypeId);
        var query = EntryQuery().Where(x => x.EntryTypeId == entryTypeId && !x.IsDeleted);
        if (from.HasValue) query = query.Where(x => x.TransactionDate >= from.Value);
        if (to.HasValue) query = query.Where(x => x.TransactionDate <= to.Value);
        return await query.OrderByDescending(x => x.TransactionDate).ThenByDescending(x => x.Id).ToListAsync(ct);
    }

    public Task<RoznamchaEntryDto?> GetAsync(long id, CancellationToken ct = default) =>
        EntryQuery().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<RoznamchaEntryDto> CreateAsync(int entryTypeId, SaveRoznamchaEntryRequest request, CancellationToken ct = default) =>
        CreateCoreAsync(entryTypeId, request, true, ct);

    private async Task<RoznamchaEntryDto> CreateCoreAsync(int entryTypeId, SaveRoznamchaEntryRequest request, bool pairInternalTransfer, CancellationToken ct)
    {
        EnsureEntryType(entryTypeId);
        await ValidateAsync(entryTypeId, request, ct);
        await ValidateLibraryDocumentsAsync(request.LibraryDocumentIds ?? [], ct);
        var isTransfer = pairInternalTransfer && await IsInternalTransferAsync(request.TransactionTypeId, ct);
        if (isTransfer && request.ProductId.HasValue)
            throw new InvalidOperationException("An internal account transfer cannot also post an inventory purchase.");
        var gross = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        if (isTransfer) gross = entryTypeId == 1 ? -decimal.Abs(gross) : decimal.Abs(gross);
        var taxRate = request.TaxRate.GetValueOrDefault();
        if (taxRate < 0 || taxRate > 100) throw new InvalidOperationException("Tax rate must be between 0 and 100.");
        var tax = request.TaxTypeId.HasValue || taxRate > 0
            ? decimal.Round(gross * taxRate / 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;
        var net = gross - tax;
        var isUsd = await IsUsdCurrencyAsync(request.CurrencyId, ct);

        var row = new RoznamchaEntry
        {
            TenantId = current.TenantId,
            SNo = await db.RoznamchaEntries.CountAsync(ct) + 1,
            RoznamchaTypeId = await ResolveRoznamchaTypeIdAsync(entryTypeId, ct)
                ?? throw new InvalidOperationException("The requested Payment/Receipt type is not configured."),
            TransTypeId = request.TransactionTypeId ?? entryTypeId,
            CreatedByUserId = current.UserId,
            CreatedOnUtc = DateTime.UtcNow
        };
        if (isTransfer) row.InternalTransferGroupId = Guid.NewGuid();
        Apply(row, request, net, tax, isUsd);
        await ExecuteInTransactionAsync(async () =>
        {
            await AcquireJournalReferenceLockAsync(ct);
            row.Ref = entryTypeId == 1
                ? await references.PaymentReferenceAsync(current.TenantId, ct)
                : await references.ReceiptReferenceAsync(current.TenantId, ct);
            var counterpart = isTransfer ? await NewTransferCounterpartAsync(row, entryTypeId, ct) : null;
            db.RoznamchaEntries.Add(row);
            if (counterpart != null)
            {
                counterpart.SNo = row.SNo + 1;
                db.RoznamchaEntries.Add(counterpart);
            }
            await db.SaveChangesAsync(ct);
            await ReconcileLibraryDocumentsAsync(row, request.LibraryDocumentIds ?? [], ct);
            await db.SaveChangesAsync(ct);
            await ApplyInventoryPurchaseFromPaymentAsync(entryTypeId, row, request, isUsd, ct);
            if (counterpart != null)
            {
                await ReconcileLibraryDocumentsAsync(counterpart, request.LibraryDocumentIds ?? [], ct);
                await AddProcessLogAsync(counterpart.Id, "Created", $"Internal transfer linked to {row.Ref} (entry {row.Id}).", ct);
            }
            await AddProcessLogAsync(row.Id, "Created", isTransfer ? "Linked internal transfer Payment/Receipt pair." : null, ct);
        }, ct);
        return await GetAsync(row.Id, ct) ?? throw new InvalidOperationException("Saved transaction could not be reloaded.");
    }

    public async Task<RoznamchaEntryDto> UpdateAsync(long id, int entryTypeId, SaveRoznamchaEntryRequest request, CancellationToken ct = default)
    {
        EnsureEntryType(entryTypeId);
        var row = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Transaction was not found.");
        if (row.RoznamchaTypeId != await ResolveRoznamchaTypeIdAsync(entryTypeId, ct))
            throw new KeyNotFoundException("Transaction was not found for the requested Payment/Receipt type.");
        var inventoryPurchase = entryTypeId == 1
            ? await db.SaleRoznamchaStockReceipts.FirstOrDefaultAsync(x => x.RoznamchaEntryId == id, ct)
            : null;
        if (row.IsLocked && inventoryPurchase == null)
            throw new InvalidOperationException("Locked transaction cannot be edited.");
        var counterpart = await FindTransferCounterpartAsync(row, ct);
        if (counterpart?.IsLocked == true)
            throw new InvalidOperationException("The linked internal transfer entry is locked; neither side can be edited.");
        await ValidateAsync(entryTypeId, request, ct);
        if (request.LibraryDocumentIds != null)
            await ValidateLibraryDocumentsAsync(request.LibraryDocumentIds, ct);
        var isTransfer = await IsInternalTransferAsync(request.TransactionTypeId, ct);
        if (row.InternalTransferGroupId.HasValue && !isTransfer)
            throw new InvalidOperationException("A linked internal transfer must remain Internal Transfer. Delete the pair before creating a different transaction type.");
        if (isTransfer && (request.ProductId.HasValue || inventoryPurchase != null))
            throw new InvalidOperationException("An internal account transfer cannot also post an inventory purchase.");
        var gross = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        if (isTransfer) gross = entryTypeId == 1 ? -decimal.Abs(gross) : decimal.Abs(gross);
        var taxRate = request.TaxRate.GetValueOrDefault();
        if (taxRate < 0 || taxRate > 100) throw new InvalidOperationException("Tax rate must be between 0 and 100.");
        var tax = request.TaxTypeId.HasValue || taxRate > 0 ? decimal.Round(gross * taxRate / 100m, 2) : 0m;
        var net = gross - tax;
        var isUsd = await IsUsdCurrencyAsync(request.CurrencyId, ct);
        row.RoznamchaTypeId = await ResolveRoznamchaTypeIdAsync(entryTypeId, ct);
        row.TransTypeId = request.TransactionTypeId ?? entryTypeId;
        row.UpdatedByUserId = current.UserId;
        row.UpdatedOnUtc = DateTime.UtcNow;
        Apply(row, request, net, tax, isUsd);
        await ExecuteInTransactionAsync(async () =>
        {
            if (isTransfer)
            {
                row.InternalTransferGroupId ??= Guid.NewGuid();
                if (counterpart == null)
                {
                    counterpart = await NewTransferCounterpartAsync(row, entryTypeId, ct);
                    db.RoznamchaEntries.Add(counterpart);
                }
                else await CopyTransferCounterpartAsync(row, counterpart, entryTypeId, ct);
            }
            if (inventoryPurchase != null)
                await SynchronizeInventoryPurchaseAsync(inventoryPurchase, request, isUsd, ct);
            if (request.LibraryDocumentIds != null)
                await ReconcileLibraryDocumentsAsync(row, request.LibraryDocumentIds, ct);
            else
                row.IsFromLibrary = !string.IsNullOrWhiteSpace(row.LibRef) ||
                    await db.RoznamchaLibraryDocuments.AnyAsync(x => x.RoznamchaEntryId == row.Id, ct);
            await db.SaveChangesAsync(ct);
            if (counterpart != null)
            {
                if (request.LibraryDocumentIds != null)
                    await ReconcileLibraryDocumentsAsync(counterpart, request.LibraryDocumentIds, ct);
                await AddProcessLogAsync(counterpart.Id, "Updated", $"Internal transfer linked to {row.Ref} (entry {row.Id}).", ct);
            }
            await AddProcessLogAsync(row.Id, "Updated", null, ct);
        }, ct);
        return await GetAsync(row.Id, ct) ?? throw new InvalidOperationException("Updated transaction could not be reloaded.");
    }

    public async Task SoftDeleteAsync(long id, string? remarks = null, CancellationToken ct = default)
    {
        var row = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Transaction was not found.");
        var counterpart = await FindTransferCounterpartAsync(row, ct);
        var entries = counterpart == null ? new[] { row } : new[] { row, counterpart };
        foreach (var entry in entries)
        {
            if (entry.IsLocked) throw new InvalidOperationException("An internal transfer side or transaction is locked and cannot be deleted.");
            await EnsureNotInventoryPostedAsync(entry.Id, "deleted", ct);
        }
        await ExecuteInTransactionAsync(async () =>
        {
            foreach (var entry in entries)
            {
                entry.IsDeleted = true;
                entry.UpdatedByUserId = current.UserId;
                entry.UpdatedOnUtc = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(remarks)) entry.Remarks = Clean(remarks, 2000);
            }
            await db.SaveChangesAsync(ct);
            foreach (var entry in entries) await AddProcessLogAsync(entry.Id, "SoftDeleted", remarks, ct);
        }, ct);
    }

    public async Task<RoznamchaEntryDto> UpdateStatusAsync(long id, UpdateRoznamchaStatusRequest request, CancellationToken ct = default)
    {
        var row = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Transaction was not found.");
        if (request.EntryStatusId.HasValue)
        {
            var processName = await RoznamchaStatusCatalog.ProcessForEntryAsync(db, row.RoznamchaTypeId, ct);
            if (!await RoznamchaStatusCatalog.Query(db, processName).AnyAsync(x => x.Id == request.EntryStatusId, ct))
                throw new InvalidOperationException("Status is not configured for this process in Settings / Configuration.");
            row.EnterStatusId = request.EntryStatusId;
        }
        if (request.IsSettled.HasValue) row.IsSettled = request.IsSettled.Value;
        if (request.IsShownHidden.HasValue) row.IsShow = !request.IsShownHidden.Value;
        if (request.IsLocked == false)
            await EnsureNotInventoryPostedAsync(id, "unlocked", ct);
        if (request.IsLocked.HasValue) row.IsLocked = request.IsLocked.Value;
        if (!string.IsNullOrWhiteSpace(request.Remarks)) row.Remarks = Clean(request.Remarks, 2000);
        row.UpdatedByUserId = current.UserId;
        row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await AddProcessLogAsync(row.Id, "StatusUpdated", request.Remarks, ct);
        return await GetAsync(row.Id, ct) ?? throw new InvalidOperationException("Updated transaction could not be reloaded.");
    }

    public async Task<RoznamchaEntryDto> ApproveAsync(long id, CancellationToken ct = default)
    {
        var row = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Transaction was not found.");
        row.IsApproved = true;
        row.IsLocked = true;
        row.UpdatedByUserId = current.UserId;
        row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await AddProcessLogAsync(row.Id, "Approved", null, ct);
        return await GetAsync(row.Id, ct) ?? throw new InvalidOperationException("Approved transaction could not be reloaded.");
    }

    public async Task<EntryAttachmentDto> AddAttachmentAsync(long id, IFormFile file, string? remarks, CancellationToken ct = default)
    {
        var entry = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Transaction was not found.");
        var stored = await files.SaveAccountsFileAsync(current.TenantId, file, ct);
        var row = new AccountsEntryDocument
        {
            TenantId = current.TenantId,
            RoznamchaEntryId = entry.Id,
            FileName = stored.FileName,
            StoredPath = stored.StoredPath,
            ContentType = stored.ContentType,
            FileSizeBytes = stored.FileSizeBytes,
            Remarks = Clean(remarks, 2000),
            DocumentReference = await references.DocumentReferenceAsync(current.TenantId, ct),
            UploadedByUserId = current.UserId
        };
        db.AccountsEntryDocuments.Add(row);
        entry.Attachment = stored.StoredPath;
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<IReadOnlyList<EntryAttachmentDto>> ListAttachmentsAsync(long id, CancellationToken ct = default)
    {
        if (!await db.RoznamchaEntries.AsNoTracking().AnyAsync(x => x.Id == id, ct))
            throw new KeyNotFoundException("Transaction was not found.");
        return await db.AccountsEntryDocuments.AsNoTracking().Where(x => x.RoznamchaEntryId == id)
            .OrderByDescending(x => x.UploadedOnUtc)
            .Select(x => new EntryAttachmentDto
            {
                Id = x.Id, EntryId = x.RoznamchaEntryId, FileName = x.FileName,
                ContentType = x.ContentType, FileSizeBytes = x.FileSizeBytes, UploadedOnUtc = x.UploadedOnUtc,
                DocumentReference = x.DocumentReference, Remarks = x.Remarks
            }).ToListAsync(ct);
    }

    public async Task DeleteAttachmentAsync(long attachmentId, CancellationToken ct = default)
    {
        var row = await db.AccountsEntryDocuments.FirstOrDefaultAsync(x => x.Id == attachmentId, ct)
            ?? throw new KeyNotFoundException("Attachment was not found.");
        db.AccountsEntryDocuments.Remove(row);
        await db.SaveChangesAsync(ct);
        await files.DeleteAccountsFileAsync(row.StoredPath, ct);
    }

    public async Task<(Stream Stream, string ContentType, string FileName)?> OpenAttachmentAsync(long attachmentId, CancellationToken ct = default)
    {
        var row = await db.AccountsEntryDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attachmentId, ct);
        if (row == null) return null;
        var stored = await files.OpenAccountsFileAsync(row.StoredPath, ct);
        return stored == null ? null : (stored.Value.Stream, row.ContentType ?? stored.Value.ContentType, row.FileName);
    }

    public async Task<IReadOnlyList<RoznamchaProcessLogDto>> ListProcessLogsAsync(long id, CancellationToken ct = default)
    {
        if (!await db.RoznamchaEntries.AsNoTracking().AnyAsync(x => x.Id == id, ct))
            throw new KeyNotFoundException("Transaction was not found.");
        return await db.RoznamchaEntryProcessLogs.AsNoTracking()
            .Where(x => x.RoznamchaEntryId == id)
            .OrderByDescending(x => x.ProcessedOnUtc)
            .Select(x => new RoznamchaProcessLogDto
            {
                Id = x.Id, EntryId = x.RoznamchaEntryId, Action = x.Action,
                Notes = x.Notes, ProcessedByUserId = x.ProcessedByUserId,
                ProcessedOnUtc = x.ProcessedOnUtc
            }).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LibraryDocumentLinkDto>> ListLibraryDocumentsAsync(long id, CancellationToken ct = default)
    {
        if (!await db.RoznamchaEntries.AsNoTracking().AnyAsync(x => x.Id == id && !x.IsDeleted, ct))
            throw new KeyNotFoundException("Transaction was not found.");
        return await LinkedLibraryDocumentsQuery(id).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LibraryDocumentLinkDto>> LinkLibraryDocumentsAsync(
        long id, IReadOnlyCollection<long> documentIds, CancellationToken ct = default)
    {
        var entry = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Transaction was not found.");
        var requested = documentIds.Where(x => x > 0).Distinct().ToArray();
        await ValidateLibraryDocumentsAsync(requested, ct);
        await ExecuteInTransactionAsync(async () =>
        {
            var existing = await db.RoznamchaLibraryDocuments.Where(x => x.RoznamchaEntryId == id)
                .Select(x => x.LibraryDocumentId).ToListAsync(ct);
            foreach (var documentId in requested.Except(existing))
                db.RoznamchaLibraryDocuments.Add(NewLibraryLink(id, documentId));
            entry.IsFromLibrary = existing.Count > 0 || requested.Length > 0 || !string.IsNullOrWhiteSpace(entry.LibRef);
            await db.SaveChangesAsync(ct);
        }, ct);
        return await LinkedLibraryDocumentsQuery(id).ToListAsync(ct);
    }

    public async Task UnlinkLibraryDocumentAsync(long id, long documentId, CancellationToken ct = default)
    {
        var entry = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Transaction was not found.");
        var link = await db.RoznamchaLibraryDocuments.FirstOrDefaultAsync(
            x => x.RoznamchaEntryId == id && x.LibraryDocumentId == documentId, ct)
            ?? throw new KeyNotFoundException("Linked library document was not found.");
        await ExecuteInTransactionAsync(async () =>
        {
            db.RoznamchaLibraryDocuments.Remove(link);
            entry.IsFromLibrary = await db.RoznamchaLibraryDocuments.AnyAsync(
                    x => x.RoznamchaEntryId == id && x.LibraryDocumentId != documentId, ct)
                || !string.IsNullOrWhiteSpace(entry.LibRef);
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    private Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return work();

        return db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await work();
            await transaction.CommitAsync(ct);
        });
    }

    private async Task ValidateAsync(int entryTypeId, SaveRoznamchaEntryRequest request, CancellationToken ct)
    {
        if (request.FromAccountId <= 0 || request.ToAccountId <= 0)
            throw new InvalidOperationException("From and To accounts are required.");
        if (request.FromAccountId == request.ToAccountId)
            throw new InvalidOperationException("Cannot create transaction in the same account.");
        if (request.Amount == 0) throw new InvalidOperationException("Amount cannot be zero.");
        if (request.TransactionDate == default) throw new InvalidOperationException("Transaction date is required.");
        var count = await db.AccountsChartAccounts.AsNoTracking()
            .CountAsync(x => (x.Id == request.FromAccountId || x.Id == request.ToAccountId) && x.IsActive, ct);
        if (count != 2) throw new InvalidOperationException("From or To account was not found or is inactive.");
        if (request.CategoryId.HasValue && !await db.AccountsCategories.AsNoTracking().AnyAsync(x => x.Id == request.CategoryId && x.IsActive, ct))
            throw new InvalidOperationException("Category was not found or is inactive.");
        if (request.TransactionTypeId.HasValue && !await db.AccountsTransTypes.AsNoTracking().AnyAsync(x => x.Id == request.TransactionTypeId && x.IsActive, ct))
            throw new InvalidOperationException("Transaction type was not found or is inactive.");
        if (request.TransactionModeId.HasValue && !await db.AccountsTransModes.AsNoTracking().AnyAsync(x => x.Id == request.TransactionModeId && x.IsActive, ct))
            throw new InvalidOperationException("Transaction mode was not found or is inactive.");
        if (request.CurrencyId.HasValue && !await db.AccountsCurrencies.AsNoTracking().AnyAsync(x => x.Id == request.CurrencyId && x.IsActive, ct))
            throw new InvalidOperationException("Currency was not found or is inactive.");
        if (request.TaxTypeId.HasValue && !await db.AccountsTaxTypes.AsNoTracking().AnyAsync(x => x.Id == request.TaxTypeId && x.IsActive, ct))
            throw new InvalidOperationException("Tax type was not found or is inactive.");
        if (request.EntryStatusId.HasValue && !await RoznamchaStatusCatalog.Query(db, entryTypeId == 1 ? "Payment" : "Receipt")
                .AnyAsync(x => x.Id == request.EntryStatusId, ct))
            throw new InvalidOperationException("Status is not configured for this process in Settings / Configuration.");
        if (request.ProjectId.HasValue && !await db.AccountsProjects.AsNoTracking().AnyAsync(x => x.Id == request.ProjectId && x.IsActive, ct))
            throw new InvalidOperationException("Project was not found or is inactive.");
        if (entryTypeId == 1 && request.ProductId.HasValue)
        {
            if (request.ProductId.Value <= 0)
                throw new InvalidOperationException("Selected product is invalid.");
            if (request.Quantity < 0)
                throw new InvalidOperationException("Purchase quantity cannot be negative.");
            if (!request.CurrencyId.HasValue)
                throw new InvalidOperationException("Currency is required when purchasing a product.");
            if (!await db.SaleRoznamchaProducts.AsNoTracking()
                    .AnyAsync(x => x.Id == request.ProductId.Value && x.IsActive && !x.IsDeleted, ct))
                throw new InvalidOperationException("Selected inventory product was not found or is inactive.");
        }
    }

    private async Task ValidateLibraryDocumentsAsync(IEnumerable<long> documentIds, CancellationToken ct)
    {
        var ids = documentIds.Where(x => x > 0).Distinct().ToArray();
        if (ids.Length == 0) return;
        var count = await db.LibraryDocuments.AsNoTracking()
            .CountAsync(x => ids.Contains(x.Id) && x.IsActive && x.AssetKind == "Document", ct);
        if (count != ids.Length)
            throw new InvalidOperationException("One or more library documents were not found, inactive, or outside this tenant.");
    }

    private async Task ReconcileLibraryDocumentsAsync(
        RoznamchaEntry entry, IEnumerable<long> documentIds, CancellationToken ct)
    {
        var requested = documentIds.Where(x => x > 0).Distinct().ToHashSet();
        var existing = await db.RoznamchaLibraryDocuments
            .Where(x => x.RoznamchaEntryId == entry.Id).ToListAsync(ct);
        db.RoznamchaLibraryDocuments.RemoveRange(existing.Where(x => !requested.Contains(x.LibraryDocumentId)));
        foreach (var documentId in requested.Except(existing.Select(x => x.LibraryDocumentId)))
            db.RoznamchaLibraryDocuments.Add(NewLibraryLink(entry.Id, documentId));
        entry.IsFromLibrary = requested.Count > 0 || !string.IsNullOrWhiteSpace(entry.LibRef);
    }

    private RoznamchaLibraryDocument NewLibraryLink(long entryId, long documentId) => new()
    {
        TenantId = current.TenantId,
        RoznamchaEntryId = entryId,
        LibraryDocumentId = documentId,
        CreatedByUserId = current.UserId,
        CreatedOnUtc = DateTime.UtcNow
    };

    private IQueryable<LibraryDocumentLinkDto> LinkedLibraryDocumentsQuery(long entryId) =>
        from link in db.RoznamchaLibraryDocuments.AsNoTracking()
        join document in db.LibraryDocuments.AsNoTracking() on link.LibraryDocumentId equals document.Id
        where link.RoznamchaEntryId == entryId
        orderby document.Id
        select new LibraryDocumentLinkDto
        {
            Id = document.Id,
            DocumentReference = document.DocumentReference ?? ("Doc-" + document.Id),
            Title = document.Title,
            OriginalFileName = document.OriginalFileName,
            ContentType = document.ContentType,
            FileSizeBytes = document.FileSizeBytes,
            DownloadUrl = $"/api/accounts/roznamcha/entry/{entryId}/library-documents/{document.Id}/download"
        };

    private Task<bool> IsInternalTransferAsync(int? transactionTypeId, CancellationToken ct) =>
        db.AccountsTransTypes.AsNoTracking().AnyAsync(x =>
            x.Id == transactionTypeId && x.IsActive && x.Code == "INTERNAL_TRANSFER", ct);

    private async Task<RoznamchaEntry?> FindTransferCounterpartAsync(RoznamchaEntry row, CancellationToken ct)
    {
        if (!row.InternalTransferGroupId.HasValue) return null;
        var linked = await db.RoznamchaEntries.Where(x => x.TenantId == row.TenantId &&
                x.InternalTransferGroupId == row.InternalTransferGroupId && x.Id != row.Id)
            .ToListAsync(ct);
        if (linked.Count != 1 || linked[0].IsDeleted)
            throw new InvalidOperationException("The linked internal transfer pair is incomplete. No changes were saved.");
        return linked[0];
    }

    private async Task<RoznamchaEntry> NewTransferCounterpartAsync(RoznamchaEntry row, int entryTypeId, CancellationToken ct)
    {
        await AcquireJournalReferenceLockAsync(ct);
        var counterpart = new RoznamchaEntry
        {
            TenantId = row.TenantId,
            SNo = await db.RoznamchaEntries.CountAsync(ct) + 1,
            Ref = entryTypeId == 1
                ? await references.ReceiptReferenceAsync(current.TenantId, ct)
                : await references.PaymentReferenceAsync(current.TenantId, ct),
            CreatedByUserId = current.UserId,
            CreatedOnUtc = DateTime.UtcNow
        };
        await CopyTransferCounterpartAsync(row, counterpart, entryTypeId, ct);
        return counterpart;
    }

    private async Task AcquireJournalReferenceLockAsync(CancellationToken ct)
    {
        if (!db.Database.IsSqlServer()) return;
        if (db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("Journal reference allocation requires an active transaction.");
        var resource = $"Accounts:RoznamchaReference:{current.TenantId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource={resource},
                @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
            IF @result < 0 THROW 51000, 'Could not allocate a journal reference. Please retry.', 1;
            """, ct);
    }

    private async Task CopyTransferCounterpartAsync(RoznamchaEntry source, RoznamchaEntry target, int sourceType, CancellationToken ct)
    {
        var targetType = sourceType == 1 ? 2 : 1;
        var typeId = await ResolveRoznamchaTypeIdAsync(targetType, ct)
            ?? throw new InvalidOperationException("Both Payment and Receipt types must be configured before saving an internal transfer.");
        if (source.EnterStatusId.HasValue && !await RoznamchaStatusCatalog.Query(db, targetType == 1 ? "Payment" : "Receipt")
                .AnyAsync(x => x.Id == source.EnterStatusId, ct))
            throw new InvalidOperationException("The status is not configured for the linked internal transfer process in Settings / Configuration.");
        target.InternalTransferGroupId = source.InternalTransferGroupId;
        target.RoznamchaTypeId = typeId;
        target.OldRef = source.OldRef;
        target.FromAccountId = source.ToAccountId;
        target.ToAccountId = source.FromAccountId;
        target.CategoryId = await db.AccountsChartAccounts.AsNoTracking()
            .Where(x => x.Id == source.ToAccountId).Select(x => x.CategoryId).SingleAsync(ct);
        target.ProjectId = source.ProjectId;
        target.CustomerId = source.CustomerId;
        target.StaffId = source.StaffId;
        target.TransTypeId = source.TransTypeId;
        target.TransModeId = source.TransModeId;
        target.InstrumentDate = source.InstrumentDate;
        target.InstrumentNo = source.InstrumentNo;
        target.Descriptions = source.Descriptions;
        target.CurrencyId = source.CurrencyId;
        target.TaxTypeId = source.TaxTypeId;
        target.TaxRate = source.TaxRate;
        target.TaxAmt = -source.TaxAmt;
        target.Adjustment = -source.Adjustment;
        target.Amount = -source.Amount;
        target.UsdAmount = -source.UsdAmount;
        target.BalanceAmount = -source.BalanceAmount;
        target.Qty = source.Qty;
        target.Rate = source.Rate;
        target.EnterStatusId = source.EnterStatusId;
        target.Remarks = source.Remarks;
        target.BankRef = source.BankRef;
        target.BankLtRef = source.BankLtRef;
        target.TransDate = source.TransDate;
        target.IsShow = source.IsShow;
        target.IsSettled = source.IsSettled;
        target.LibRef = source.LibRef;
        target.IsLedger = source.IsLedger;
        target.IsManual = source.IsManual;
        if (target.Id != 0)
        {
            target.UpdatedByUserId = current.UserId;
            target.UpdatedOnUtc = DateTime.UtcNow;
        }
    }

    private async Task<bool> IsUsdCurrencyAsync(int? currencyId, CancellationToken ct) =>
        currencyId.HasValue && await db.AccountsCurrencies.AsNoTracking()
            .AnyAsync(x => x.Id == currencyId.Value && x.IsActive && x.Code == "USD", ct);

    private static void Apply(RoznamchaEntry row, SaveRoznamchaEntryRequest request, decimal net, decimal tax, bool isUsd)
    {
        row.OldRef = Clean(request.OldReferenceNo, 80);
        row.CategoryId = request.CategoryId;
        row.FromAccountId = request.FromAccountId;
        row.ToAccountId = request.ToAccountId;
        row.CustomerId = request.CustomerId;
        row.StaffId = request.StaffId;
        row.TransModeId = request.TransactionModeId;
        row.InstrumentDate = request.InstrumentDate;
        row.InstrumentNo = Clean(request.InstrumentNo, 100);
        row.Descriptions = Clean(request.Description, 2000);
        row.CurrencyId = request.CurrencyId;
        row.TaxTypeId = request.TaxTypeId;
        row.TaxRate = request.TaxRate;
        row.TaxAmt = tax;
        row.Adjustment = decimal.Round(request.Adjustment, 2);
        row.Amount = isUsd ? 0 : net;
        row.UsdAmount = isUsd ? net : 0;
        row.BalanceAmount = net + request.Adjustment;
        row.Qty = request.Quantity;
        row.Rate = request.Rate;
        row.EnterStatusId = request.EntryStatusId;
        row.Remarks = Clean(request.Remarks, 2000);
        row.BankRef = Clean(request.BankReference, 100);
        row.BankLtRef = Clean(request.BankLTReference, 100);
        row.TransDate = request.TransactionDate;
        row.IsShow = !request.IsShownHidden;
        row.IsSettled = request.IsSettled;
        row.LibRef = Clean(request.LibraryReference, 80);
        row.IsLedger = request.IsLedger;
        row.IsManual = request.IsManual;
        row.ProjectId = request.ProjectId;
    }

    private IQueryable<RoznamchaEntryDto> EntryQuery() =>
        from entry in db.RoznamchaEntries.AsNoTracking()
        join kind in db.AccountsRoznamchaTypes.AsNoTracking() on entry.RoznamchaTypeId equals kind.Id into kindRows
        from kind in kindRows.DefaultIfEmpty()
        join fromAccount in db.AccountsChartAccounts.AsNoTracking() on entry.FromAccountId equals fromAccount.Id into fromRows
        from fromAccount in fromRows.DefaultIfEmpty()
        join toAccount in db.AccountsChartAccounts.AsNoTracking() on entry.ToAccountId equals toAccount.Id into toRows
        from toAccount in toRows.DefaultIfEmpty()
        select new RoznamchaEntryDto
        {
            Id = entry.Id,
            InternalTransferGroupId = entry.InternalTransferGroupId,
            SerialNo = entry.SNo,
            ReferenceNo = entry.Ref,
            OldReferenceNo = entry.OldRef,
            EntryTypeId = kind == null ? 0 : kind.Code == "PAYMENT" ? 1 : kind.Code == "RECEIPT" ? 2 : 0,
            CategoryId = entry.CategoryId,
            FromAccountId = entry.FromAccountId,
            FromAccountName = fromAccount == null ? null : fromAccount.AccountName,
            ToAccountId = entry.ToAccountId,
            ToAccountName = toAccount == null ? null : toAccount.AccountName,
            TransactionTypeId = entry.TransTypeId,
            TransactionModeId = entry.TransModeId,
            InstrumentDate = entry.InstrumentDate,
            InstrumentNo = entry.InstrumentNo,
            CurrencyId = entry.CurrencyId,
            TaxTypeId = entry.TaxTypeId,
            TaxRate = entry.TaxRate,
            EntryStatusId = entry.EnterStatusId,
            ProjectId = entry.ProjectId,
            Amount = entry.Amount ?? 0,
            UsdAmount = entry.UsdAmount ?? 0,
            GrossAmount = (entry.Amount ?? 0) + (entry.UsdAmount ?? 0) + (entry.TaxAmt ?? 0),
            TaxAmount = entry.TaxAmt ?? 0,
            Adjustment = entry.Adjustment ?? 0,
            Quantity = entry.Qty ?? 0,
            Rate = entry.Rate ?? 0,
            TransactionDate = entry.TransDate,
            Description = entry.Descriptions,
            Remarks = entry.Remarks,
            BankReference = entry.BankRef,
            BankLTReference = entry.BankLtRef,
            IsFromLibrary = entry.IsFromLibrary,
            LibraryReference = entry.LibRef,
            LibraryDocuments = (from link in db.RoznamchaLibraryDocuments
                                join document in db.LibraryDocuments on link.LibraryDocumentId equals document.Id
                                where link.RoznamchaEntryId == entry.Id
                                orderby document.Id
                                select new LibraryDocumentLinkDto
                                {
                                    Id = document.Id,
                                    DocumentReference = document.DocumentReference ?? ("Doc-" + document.Id),
                                    Title = document.Title,
                                    OriginalFileName = document.OriginalFileName,
                                    ContentType = document.ContentType,
                                    FileSizeBytes = document.FileSizeBytes,
                                    DownloadUrl = "/api/accounts/roznamcha/entry/" + entry.Id + "/library-documents/" + document.Id + "/download"
                                }).ToList(),
            IsApproved = entry.IsApproved,
            IsSettled = entry.IsSettled,
            IsShownHidden = !entry.IsShow,
            IsDeleted = entry.IsDeleted,
            IsLocked = entry.IsLocked,
            ProductId = db.SaleRoznamchaStockReceipts
                .Where(purchase => purchase.RoznamchaEntryId == entry.Id)
                .Select(purchase => (long?)purchase.ProductId)
                .FirstOrDefault()
        };

    private async Task AddProcessLogAsync(long id, string action, string? notes, CancellationToken ct)
    {
        db.RoznamchaEntryProcessLogs.Add(new RoznamchaEntryProcessLog
        {
            TenantId = current.TenantId,
            RoznamchaEntryId = id,
            Action = action,
            Notes = Clean(notes, 2000),
            ProcessedByUserId = current.UserId
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task<int?> ResolveRoznamchaTypeIdAsync(int entryTypeId, CancellationToken ct)
    {
        var code = entryTypeId == 1 ? "PAYMENT" : "RECEIPT";
        return await db.AccountsRoznamchaTypes.AsNoTracking()
            .Where(x => x.IsActive && x.Code == code)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct);
    }

    private static EntryAttachmentDto Map(AccountsEntryDocument row) => new()
    {
        Id = row.Id, EntryId = row.RoznamchaEntryId, FileName = row.FileName,
        ContentType = row.ContentType, FileSizeBytes = row.FileSizeBytes, UploadedOnUtc = row.UploadedOnUtc,
        DocumentReference = row.DocumentReference, Remarks = row.Remarks
    };

    private async Task ApplyInventoryPurchaseFromPaymentAsync(
        int entryTypeId,
        RoznamchaEntry entry,
        SaveRoznamchaEntryRequest request,
        bool isUsd,
        CancellationToken ct)
    {
        if (entryTypeId != 1 || request.ProductId is not > 0 || !db.Database.IsRelational())
            return;
        if (!request.CurrencyId.HasValue)
            throw new InvalidOperationException("Currency is required when purchasing a product.");

        var quantity = decimal.Round(request.Quantity, 4, MidpointRounding.AwayFromZero);
        var total = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        var unitCost = request.Rate != 0
            ? decimal.Round(request.Rate, 2, MidpointRounding.AwayFromZero)
            : quantity > 0
                ? decimal.Round(total / quantity, 2, MidpointRounding.AwayFromZero)
                : total;
        try
        {
            await SpListQuery.ExecAsync<SaleRoznamchaCreatedId>(
                db,
                "EXEC dbo.usp_SaleRoznamcha_InventoryStockAdd @TenantId, @ProductId, @Reference, @Quantity, @UnitCost, @TotalPurchaseCost, @UsdAmount, @ExchangeRate, @CurrencyId, @FromAccountId, @ToAccountId, @PurchasedDate, @Description, @UserId, @RoznamchaEntryId",
                ct,
                SpListQuery.TenantId(current.TenantId),
                SpListQuery.BigInt("@ProductId", request.ProductId.Value),
                SpListQuery.NVarChar("@Reference", entry.Ref),
                SpListQuery.Decimal("@Quantity", quantity),
                SpListQuery.Decimal("@UnitCost", unitCost),
                SpListQuery.Decimal("@TotalPurchaseCost", total),
                SpListQuery.DecimalNullable("@UsdAmount", isUsd ? total : null, 2),
                SpListQuery.DecimalNullable("@ExchangeRate", null, 6),
                SpListQuery.Int("@CurrencyId", request.CurrencyId.Value),
                SpListQuery.Int("@FromAccountId", request.FromAccountId),
                SpListQuery.IntNullable("@ToAccountId", request.ToAccountId),
                SpListQuery.Date("@PurchasedDate", request.TransactionDate),
                SpListQuery.NVarChar("@Description", request.Description?.Trim()),
                SpListQuery.NVarChar("@UserId", current.UserId),
                SpListQuery.BigIntNullable("@RoznamchaEntryId", entry.Id));
            entry.IsLocked = true;
            await db.SaveChangesAsync(ct);
        }
        catch (SqlException ex) when (ex.Number is 51000 or 51001 or 2601 or 2627)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    private async Task EnsureNotInventoryPostedAsync(long entryId, string action, CancellationToken ct)
    {
        if (await db.SaleRoznamchaStockReceipts.AsNoTracking()
            .AnyAsync(x => x.RoznamchaEntryId == entryId, ct))
            throw new InvalidOperationException(
                $"This payment was posted from an inventory purchase and cannot be {action} here.");
    }

    private async Task SynchronizeInventoryPurchaseAsync(
        SaleRoznamchaStockReceipt purchase,
        SaveRoznamchaEntryRequest request,
        bool isUsd,
        CancellationToken ct)
    {
        if (request.ProductId.HasValue && request.ProductId.Value != purchase.ProductId)
            throw new InvalidOperationException("The product of an existing inventory purchase cannot be changed.");
        if (!request.CurrencyId.HasValue)
            throw new InvalidOperationException("Currency is required when updating an inventory purchase.");

        var quantity = decimal.Round(request.Quantity, 4, MidpointRounding.AwayFromZero);
        var soldQuantity = purchase.PurchasedQuantity - purchase.RemainingQuantity;
        if (quantity < soldQuantity)
            throw new InvalidOperationException(
                $"Purchased quantity cannot be less than the already sold quantity ({soldQuantity:#,##0.####}).");

        var nextRemaining = quantity - soldQuantity;
        var quantityDelta = nextRemaining - purchase.RemainingQuantity;
        var total = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        var unitCost = request.Rate != 0
            ? decimal.Round(request.Rate, 2, MidpointRounding.AwayFromZero)
            : quantity != 0
                ? decimal.Round(total / quantity, 2, MidpointRounding.AwayFromZero)
                : total;

        purchase.PurchasedQuantity = quantity;
        purchase.RemainingQuantity = nextRemaining;
        purchase.UnitCost = unitCost;
        purchase.TotalPurchaseCost = total;
        purchase.UsdAmount = isUsd ? total : null;
        purchase.CurrencyId = request.CurrencyId.Value;
        purchase.FromAccountId = request.FromAccountId;
        purchase.ToAccountId = request.ToAccountId;
        purchase.PurchasedDate = request.TransactionDate;
        purchase.Description = Clean(request.Description, 1000);

        if (quantityDelta == 0) return;

        var currentBalance = await db.SaleRoznamchaStockReceipts.AsNoTracking()
            .Where(x => x.ProductId == purchase.ProductId)
            .SumAsync(x => x.RemainingQuantity, ct);
        db.SaleRoznamchaInventoryMovements.Add(new SaleRoznamchaInventoryMovement
        {
            TenantId = current.TenantId,
            ProductId = purchase.ProductId,
            StockReceiptId = purchase.Id,
            MovementType = "PURCHASE_ADJUSTMENT",
            QuantityDelta = quantityDelta,
            BalanceAfter = currentBalance + quantityDelta,
            CreatedByUserId = current.UserId,
            CreatedOnUtc = DateTime.UtcNow
        });
    }

    private static AccountsChartAccount? ResolveAccount(
        IReadOnlyCollection<AccountsChartAccount> accounts,
        int? accountId,
        string? name,
        string? number)
    {
        if (accountId is > 0)
        {
            var byId = accounts.FirstOrDefault(account => account.Id == accountId.Value);
            if (byId != null) return byId;
        }

        var values = new[] { name, number }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizeMatchText)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (values.Length == 0)
        {
            if (accountId is > 0)
                throw new InvalidOperationException($"Account Id {accountId.Value} was not found or is inactive.");
            return null;
        }

        var numberValue = NormalizeMatchText(number);
        if (numberValue.Length > 0)
        {
            var numberMatches = accounts.Where(account => new[] { account.AccountNumber, account.AccountCode, account.BankAccountNumber }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeMatchText)
                .Any(candidate => candidate.Equals(numberValue, StringComparison.OrdinalIgnoreCase))).ToList();
            if (numberMatches.Count == 1) return numberMatches[0];
            if (numberMatches.Count > 1)
                throw new InvalidOperationException($"Account number '{number}' matched more than one chart account.");
        }

        var matches = accounts.Where(account =>
        {
            var candidates = new[] { account.AccountName, account.FullName, account.AccountNumber, account.AccountCode, account.BankAccountNumber }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeMatchText)
                .ToArray();
            return values.Any(value => candidates.Any(candidate =>
                candidate.Equals(value, StringComparison.OrdinalIgnoreCase)));
        }).ToList();
        if (matches.Count == 1) return matches[0];
        if (matches.Count > 1)
        {
            var banks = matches.Where(account => account.IsStatement).ToList();
            if (banks.Count == 1) return banks[0];
            throw new InvalidOperationException($"Account '{name ?? number}' matched more than one chart account. Use the account number.");
        }

        matches = accounts.Where(account => values.Any(value =>
            {
                var accountName = NormalizeMatchText(account.AccountName);
                var fullName = NormalizeMatchText(account.FullName);
                return value.EndsWith(accountName, StringComparison.OrdinalIgnoreCase) ||
                       accountName.EndsWith(value, StringComparison.OrdinalIgnoreCase) ||
                       (fullName.Length > 0 && (value.EndsWith(fullName, StringComparison.OrdinalIgnoreCase) ||
                                                fullName.EndsWith(value, StringComparison.OrdinalIgnoreCase))) ||
                       (!string.IsNullOrWhiteSpace(account.AccountNumber) &&
                        value.StartsWith(NormalizeMatchText(account.AccountNumber), StringComparison.OrdinalIgnoreCase));
            }))
            .ToList();
        if (matches.Count > 1)
        {
            var banks = matches.Where(account => account.IsStatement).ToList();
            if (banks.Count == 1) return banks[0];
            throw new InvalidOperationException($"Account '{name ?? number}' matched more than one chart account. Use the account number.");
        }
        if (matches.Count == 1) return matches[0];

        matches = accounts.Where(account =>
        {
            var names = new[] { account.AccountName, account.FullName }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeMatchText)
                .Where(x => x.Length >= 4)
                .ToArray();
            return values.Where(value => value.Length >= 4).Any(value => names.Any(accountName =>
                accountName.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                value.Contains(accountName, StringComparison.OrdinalIgnoreCase)));
        }).ToList();
        if (matches.Count > 1)
        {
            var banks = matches.Where(account => account.IsStatement).ToList();
            if (banks.Count == 1) return banks[0];
            throw new InvalidOperationException($"Account '{name ?? number}' matched more than one chart account. Use the account number.");
        }
        if (matches.Count == 1) return matches[0];

        if (accountId is > 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(name)
                ? $"Account Id {accountId.Value} was not found or is inactive."
                : $"Account Id {accountId.Value} was not found. Bank/account name '{name}' was not matched in the chart of accounts.");
        }
        return null;
    }

    private static SaleRoznamchaProduct? ResolveProduct(
        IReadOnlyCollection<SaleRoznamchaProduct> products,
        long? productId,
        string? productCode,
        string? productName)
    {
        if (productId is > 0)
            return products.FirstOrDefault(x => x.Id == productId.Value)
                ?? throw new InvalidOperationException($"Product Id {productId.Value} was not found or is inactive.");

        var code = NormalizeMatchText(productCode);
        if (code.Length > 0)
        {
            var matches = products.Where(x => NormalizeMatchText(x.ProductCode)
                .Equals(code, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1)
                throw new InvalidOperationException($"Product code '{productCode}' matched more than one inventory product.");
        }

        var name = NormalizeMatchText(productName);
        if (name.Length == 0) return null;
        var nameMatches = products.Where(x => NormalizeMatchText(x.Name)
            .Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (nameMatches.Count == 1) return nameMatches[0];
        if (nameMatches.Count > 1)
            throw new InvalidOperationException($"Product name '{productName}' matched more than one inventory product. Use Product Code.");
        return null;
    }

    private static bool HasAnyExcelHeader(IReadOnlyDictionary<string, int> headers, params string[] aliases) =>
        aliases.Any(alias => headers.ContainsKey(NormalizeExcelHeader(alias)));

    private async Task<AccountsProject?> EnsureProjectAsync(
        List<AccountsProject> projects,
        int? projectId,
        string? projectName,
        CancellationToken ct)
    {
        var name = Clean(projectName, 160);
        if (projectId is not > 0 && string.IsNullOrWhiteSpace(name))
            return null;

        if (projectId is > 0)
        {
            var byId = projects.FirstOrDefault(x => x.Id == projectId.Value);
            if (byId != null)
            {
                if (!string.IsNullOrWhiteSpace(name) &&
                    !string.Equals(byId.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    if (db.Entry(byId).State == EntityState.Detached)
                        db.AccountsProjects.Attach(byId);
                    byId.Name = name;
                    await db.SaveChangesAsync(ct);
                }
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            var byName = projects.FirstOrDefault(x =>
                string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (byName != null)
                return byName;
        }

        if (current.TenantId <= 0)
            throw new InvalidOperationException("A tenant is required before Project / ProjectId can be imported.");

        var created = new AccountsProject
        {
            TenantId = current.TenantId,
            Name = string.IsNullOrWhiteSpace(name) ? $"Project {projectId}" : name,
            IsActive = true
        };

        if (projectId is > 0)
        {
            var taken = await db.AccountsProjects.IgnoreQueryFilters()
                .AnyAsync(x => x.Id == projectId.Value, ct);
            if (taken)
                throw new InvalidOperationException($"Project Id {projectId.Value} already exists.");

            created.Id = projectId.Value;
            await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT dbo.AccountsProjects ON", cancellationToken: ct);
            try
            {
                db.AccountsProjects.Add(created);
                await db.SaveChangesAsync(ct);
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT dbo.AccountsProjects OFF", cancellationToken: ct);
            }
        }
        else
        {
            db.AccountsProjects.Add(created);
            await db.SaveChangesAsync(ct);
        }

        projects.Add(created);
        return created;
    }

    private static int? ReadInt(DataRow row, IReadOnlyDictionary<string, int> headers, params string[] aliases)
    {
        var text = ReadText(row, headers, aliases);
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;
        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var decimalValue)
            && decimalValue == decimal.Truncate(decimalValue)
            && decimalValue > 0
            && decimalValue <= int.MaxValue)
            return (int)decimalValue;
        return null;
    }

    private static long? ReadLong(DataRow row, IReadOnlyDictionary<string, int> headers, params string[] aliases)
    {
        var text = ReadText(row, headers, aliases);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string FormatImportError(Exception ex)
    {
        var messages = new List<string>();
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (string.IsNullOrWhiteSpace(current.Message)) continue;
            if (!messages.Contains(current.Message, StringComparer.OrdinalIgnoreCase))
                messages.Add(current.Message);
        }
        return messages.Count == 0 ? "This row could not be imported." : string.Join(" ", messages);
    }

    private static T? ResolveLookup<T>(
        IEnumerable<T> source,
        string? value,
        Func<T, string?> code,
        Func<T, string?> name) where T : class
    {
        var normalized = NormalizeMatchText(value);
        if (normalized.Length == 0) return null;
        var matches = source.Where(x =>
            NormalizeMatchText(code(x)).Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            NormalizeMatchText(name(x)).Equals(normalized, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string? ReadText(DataRow row, IReadOnlyDictionary<string, int> headers, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (!headers.TryGetValue(NormalizeExcelHeader(alias), out var ordinal)) continue;
            var value = row[ordinal];
            if (value == DBNull.Value) continue;
            var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return null;
    }

    private static decimal ReadDecimal(DataRow row, IReadOnlyDictionary<string, int> headers, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (!headers.TryGetValue(NormalizeExcelHeader(alias), out var ordinal)) continue;
            var value = row[ordinal];
            if (value == DBNull.Value || value == null) continue;
            if (value is decimal decimalValue) return decimalValue;
            if (value is double doubleValue) return Convert.ToDecimal(doubleValue);
            if (value is float floatValue) return Convert.ToDecimal(floatValue);
            if (value is int intValue) return intValue;
            if (value is long longValue) return longValue;
            var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            var negativeParentheses = text.StartsWith('(') && text.EndsWith(')');
            text = text.Trim('(', ')').Replace(",", string.Empty).Replace("PKR", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("USD", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return negativeParentheses ? -Math.Abs(parsed) : parsed;
        }
        return 0;
    }

    private static bool ReadBool(DataRow row, IReadOnlyDictionary<string, int> headers, params string[] aliases)
    {
        var value = ReadText(row, headers, aliases);
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("y", StringComparison.OrdinalIgnoreCase) || value == "1";
    }

    private static DateOnly? ReadDate(DataRow row, IReadOnlyDictionary<string, int> headers, params string[] aliases)
    {
        var dateTime = ReadDateTime(row, headers, aliases);
        return dateTime.HasValue ? DateOnly.FromDateTime(dateTime.Value) : null;
    }

    private static DateTime? ReadDateTime(DataRow row, IReadOnlyDictionary<string, int> headers, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (!headers.TryGetValue(NormalizeExcelHeader(alias), out var ordinal)) continue;
            var value = row[ordinal];
            if (value == DBNull.Value || value == null) continue;
            if (value is DateTime dateTime) return dateTime;
            if (value is double serial && serial > 0) return DateTime.FromOADate(serial);
            var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            var formats = new[]
            {
                "yyyy-MM-ddTHH:mm:ss.fffffff", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd",
                "dd/MM/yyyy", "MM/dd/yyyy", "dd-MM-yyyy", "MM-dd-yyyy",
                "dd MMM yyyy", "d MMM yyyy", "dd-MMM-yyyy", "d-MMM-yyyy", "dd MMM yyyy HH:mm:ss"
            };
            if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var exact)) return exact;
            if (DateTime.TryParse(text, CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.AllowWhiteSpaces, out var british)) return british;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)) return parsed;
        }
        return null;
    }

    private static readonly string[] LocalCurrencyAliases = ["PKR", "RS", "RUPEE", "RUPEES", "PRS"];

    private static AccountsCurrency? FindConfiguredCurrency(IEnumerable<AccountsCurrency> currencies, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            var match = currencies.FirstOrDefault(x => EqualsLookup(x.Code, alias) || EqualsLookup(x.Name, alias));
            if (match != null) return match;
        }
        return null;
    }

    private static bool IsLocalCurrencyAlias(string? value) =>
        LocalCurrencyAliases.Any(alias => EqualsLookup(value, alias));

    private static bool EqualsLookup(string? value, string expected) =>
        NormalizeMatchText(value).Equals(NormalizeMatchText(expected), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeExcelHeader(string? value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string NormalizeMatchText(string? value) =>
        string.Join(' ', (value ?? string.Empty).Trim().Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static void EnsureEntryType(int entryTypeId)
    {
        if (entryTypeId is not (1 or 2)) throw new InvalidOperationException("Entry type must be Payment (1) or Receipt (2).");
    }

    private static string? Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
