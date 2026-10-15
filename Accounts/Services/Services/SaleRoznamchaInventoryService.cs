using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Security.Cryptography;

namespace Accounts.Services.Services;

public sealed class SaleRoznamchaInventoryService(
    ApplicationDbContext db,
    ICurrentUserService current,
    IWebHostEnvironment environment) : ISaleRoznamchaInventoryService
{
    private static readonly HashSet<string> AllowedAttachmentExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".doc", ".docx", ".xls", ".xlsx" };

    public async Task<IReadOnlyList<SaleRoznamchaAccountLookupRow>> ListAccountsAsync(CancellationToken ct = default) =>
        await db.AccountsChartAccounts.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.AccountName)
            .ThenBy(x => x.Id)
            .Select(x => new SaleRoznamchaAccountLookupRow
            {
                Id = x.Id,
                Code = x.AccountCode ?? x.AccountNumber,
                Name = x.AccountName,
            })
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SaleRoznamchaCurrencyRow>> ListCurrenciesAsync(CancellationToken ct = default)
    {
        var configuredDefaultId = await db.AccountsModuleSettings.AsNoTracking()
            .Where(x => x.TenantId == current.TenantId)
            .Select(x => x.DefaultCurrencyId)
            .SingleOrDefaultAsync(ct);
        var inventoryDefaultId = await db.SaleRoznamchaProducts.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .GroupBy(x => x.CurrencyId)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => (int?)group.Key)
            .FirstOrDefaultAsync(ct);

        var currencies = await db.AccountsCurrencies.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.Id, x.Code, x.Name, x.TenantId })
            .ToListAsync(ct);

        var selected = currencies.OrderBy(x => x.Code).ThenBy(x => x.Id).ToList();

        var preferredDefaultId = configuredDefaultId ?? inventoryDefaultId;
        var defaultCurrencyId = selected.Any(x => x.Id == preferredDefaultId)
            ? preferredDefaultId
            : selected.FirstOrDefault()?.Id;

        return selected
            .OrderByDescending(x => x.Id == defaultCurrencyId)
            .ThenBy(x => x.Code)
            .Select(x => new SaleRoznamchaCurrencyRow
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                IsDefault = x.Id == defaultCurrencyId,
            })
            .ToList();
    }

    public async Task<IReadOnlyList<SaleRoznamchaInventoryRow>> ListAsync(
        int? companyId,
        int? platformId,
        int? productCategoryId,
        bool activeOnly,
        CancellationToken ct = default) =>
        await SpListQuery.ExecAsync<SaleRoznamchaInventoryRow>(
            db,
            "EXEC dbo.usp_SaleRoznamcha_InventoryList @TenantId, @CompanyId, @PlatformId, @ProductCategoryId, @ActiveOnly",
            ct,
            SpListQuery.TenantId(current.TenantId),
            SpListQuery.IntNullable("@CompanyId", companyId),
            SpListQuery.IntNullable("@PlatformId", platformId),
            SpListQuery.IntNullable("@ProductCategoryId", productCategoryId),
            SpListQuery.Bit("@ActiveOnly", activeOnly));

    public async Task<IReadOnlyList<SaleRoznamchaInventoryPurchaseRow>> ListPurchasesAsync(
        int? companyId, int? platformId, int? productCategoryId,
        CancellationToken ct = default) =>
        await SpListQuery.ExecAsync<SaleRoznamchaInventoryPurchaseRow>(
            db,
            "EXEC dbo.usp_SaleRoznamcha_InventoryPurchaseList @TenantId, @CompanyId, @PlatformId, @ProductCategoryId",
            ct,
            SpListQuery.TenantId(current.TenantId),
            SpListQuery.IntNullable("@CompanyId", companyId),
            SpListQuery.IntNullable("@PlatformId", platformId),
            SpListQuery.IntNullable("@ProductCategoryId", productCategoryId));

    public async Task<SaleRoznamchaInventoryRow> SaveProductAsync(
        long? id,
        SaveSaleRoznamchaProductRequest request,
        CancellationToken ct = default)
    {
        var productName = request.ProductName?.Trim();
        if (string.IsNullOrWhiteSpace(productName))
            throw new InvalidOperationException("Product name is required.");
        if (productName.Length > 200)
            throw new InvalidOperationException("Product name cannot exceed 200 characters.");
        await ValidateHierarchyAsync(request, ct);
        var productCode = NormalizeProductCode(request.ProductCode, id.HasValue);

        try
        {
            IReadOnlyList<SaleRoznamchaCreatedId> created;
            if (id.HasValue)
            {
                created = await SpListQuery.ExecAsync<SaleRoznamchaCreatedId>(
                    db,
                    "EXEC dbo.usp_SaleRoznamcha_InventoryProductUpdate @TenantId, @ProductId, @ProductCategoryId, @ProductCode, @ProductName, @Description, @IsActive, @UserId",
                    ct,
                    SpListQuery.TenantId(current.TenantId),
                    SpListQuery.BigInt("@ProductId", id.Value),
                    SpListQuery.Int("@ProductCategoryId", request.ProductCategoryId),
                    SpListQuery.NVarChar("@ProductCode", productCode),
                    SpListQuery.NVarChar("@ProductName", productName),
                    SpListQuery.NVarChar("@Description", CleanDescription(request.Description)),
                    SpListQuery.Bit("@IsActive", request.IsActive),
                    SpListQuery.NVarChar("@UserId", current.UserId));
            }
            else
            {
                created = await SpListQuery.ExecAsync<SaleRoznamchaCreatedId>(
                    db,
                    "EXEC dbo.usp_SaleRoznamcha_ProductCreate @TenantId, @ProductCategoryId, @ProductCode, @ProductName, @Description, @IsActive, @UserId",
                    ct,
                    SpListQuery.TenantId(current.TenantId),
                    SpListQuery.Int("@ProductCategoryId", request.ProductCategoryId),
                    SpListQuery.NVarChar("@ProductCode", productCode),
                    SpListQuery.NVarChar("@ProductName", productName),
                    SpListQuery.NVarChar("@Description", CleanDescription(request.Description)),
                    SpListQuery.Bit("@IsActive", request.IsActive),
                    SpListQuery.NVarChar("@UserId", current.UserId));
            }

            var savedId = created.SingleOrDefault()?.Id
                ?? throw new InvalidOperationException("Inventory product could not be saved.");
            return await ReloadAsync(savedId, ct);
        }
        catch (SqlException ex) when (ex.Number is 51000 or 51001 or 2601 or 2627)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    public async Task<SaleRoznamchaInventoryRow> AddStockAsync(
        long id,
        AddSaleRoznamchaStockRequest request,
        CancellationToken ct = default)
    {
        var totalPurchaseCost = ResolveTotalPurchaseCost(request);
        var unitCost = ResolvePurchaseUnitCost(request.Quantity, totalPurchaseCost);
        ValidatePurchase(request.Quantity, unitCost, totalPurchaseCost, request.CurrencyId);
        var description = CleanDescription(request.Description);
        var reference = CleanReference(request.Reference);
        ValidateOptionalAmount(request.UsdAmount, "USD amount", 2, allowNegative: true);
        ValidateOptionalAmount(request.ExchangeRate, "Exchange rate", 6);
        await ValidateCurrencyAsync(request.CurrencyId!.Value, ct);
        await ValidateAccountsAsync(request.FromAccountId, request.ToAccountId, requireFromAccount: true, ct);
        var purchasedDate = request.PurchasedDate ?? BusinessClock.Today(BusinessClock.DefaultTimeZoneId);
        try
        {
            var result = await SpListQuery.ExecAsync<SaleRoznamchaCreatedId>(
                db,
                "EXEC dbo.usp_SaleRoznamcha_InventoryStockAdd @TenantId, @ProductId, @Reference, @Quantity, @UnitCost, @TotalPurchaseCost, @UsdAmount, @ExchangeRate, @CurrencyId, @FromAccountId, @ToAccountId, @PurchasedDate, @Description, @UserId",
                ct,
                SpListQuery.TenantId(current.TenantId),
                SpListQuery.BigInt("@ProductId", id),
                SpListQuery.NVarChar("@Reference", reference),
                SpListQuery.Decimal("@Quantity", request.Quantity),
                SpListQuery.Decimal("@UnitCost", unitCost),
                SpListQuery.Decimal("@TotalPurchaseCost", totalPurchaseCost),
                SpListQuery.DecimalNullable("@UsdAmount", request.UsdAmount, 2),
                SpListQuery.DecimalNullable("@ExchangeRate", request.ExchangeRate, 6),
                SpListQuery.Int("@CurrencyId", request.CurrencyId!.Value),
                SpListQuery.IntNullable("@FromAccountId", request.FromAccountId),
                SpListQuery.IntNullable("@ToAccountId", request.ToAccountId),
                SpListQuery.Date("@PurchasedDate", purchasedDate),
                SpListQuery.NVarChar("@Description", description),
                SpListQuery.NVarChar("@UserId", current.UserId));
            var savedId = result.SingleOrDefault()?.Id
                ?? throw new InvalidOperationException("Stock purchase could not be saved.");
            return await ReloadAsync(savedId, ct);
        }
        catch (SqlException ex) when (ex.Number is 51000 or 51001 or 2601 or 2627)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    public async Task<SaleRoznamchaPurchaseImportResult> ImportPurchasesAsync(
        IFormFile excelFile,
        CancellationToken ct = default)
    {
        if (excelFile == null || excelFile.Length == 0)
            throw new InvalidOperationException("Excel purchase file is required.");
        if (excelFile.Length > 1_000_000)
            throw new InvalidOperationException("Excel purchase file cannot exceed 1 MB.");
        var originalFileName = Path.GetFileName(excelFile.FileName);
        if (string.IsNullOrWhiteSpace(originalFileName) || originalFileName.Length > 260)
            throw new InvalidOperationException("The purchase file name is invalid or exceeds 260 characters.");

        var extension = Path.GetExtension(excelFile.FileName).ToLowerInvariant();
        if (extension is not (".xlsx" or ".xls" or ".csv"))
            throw new InvalidOperationException("Please upload a valid .xlsx, .xls, or .csv file.");

        await using var uploadStream = excelFile.OpenReadStream();
        await using var bufferedFile = new MemoryStream();
        await uploadStream.CopyToAsync(bufferedFile, ct);
        var fileBytes = bufferedFile.ToArray();
        var fileHash = Convert.ToHexString(SHA256.HashData(fileBytes));
        if (await db.SaleRoznamchaPurchaseImports.AsNoTracking()
            .AnyAsync(x => x.TenantId == current.TenantId && x.FileHash == fileHash, ct))
            throw new InvalidOperationException("This purchase file has already been imported. Duplicate stock was not posted.");

        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        List<ImportedPurchase> parsed;
        try
        {
            await using var stream = new MemoryStream(fileBytes, writable: false);
            using var reader = extension == ".csv"
                ? ExcelReaderFactory.CreateCsvReader(stream)
                : ExcelReaderFactory.CreateReader(stream);
            var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration
            {
                ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
            });
            if (dataSet.Tables.Count == 0)
                throw new InvalidOperationException("The uploaded file has no worksheet.");
            parsed = ParsePurchaseRows(dataSet.Tables[0]);
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("The uploaded file could not be read as a valid Excel or CSV purchase file.", ex);
        }

        if (parsed.Count == 0)
            throw new InvalidOperationException("The uploaded file has no purchase rows.");
        if (parsed.Count > 1000)
            throw new InvalidOperationException("A maximum of 1,000 purchase rows can be imported at once.");

        var categoryValues = parsed.Where(x => !string.IsNullOrWhiteSpace(x.FromCategoryValue))
            .Select(x => x.FromCategoryValue!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var accountValues = parsed.Select(x => x.FromAccountCode)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var products = await ListAsync(null, null, null, true, ct);
        var productsByExactCode = products
            .ToDictionary(x => x.ProductCode, x => x, StringComparer.OrdinalIgnoreCase);
        var productsByCanonicalCode = products
            .GroupBy(x => CanonicalProductCode(x.ProductCode), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
        var productsByName = products
            .GroupBy(x => NormalizeMatchText(x.ProductName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var currencies = (await db.AccountsCurrencies.AsNoTracking()
                .Where(x => x.IsActive)
                .Select(x => new { x.Id, x.Code })
                .ToListAsync(ct))
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Id, StringComparer.OrdinalIgnoreCase);
        var accountCandidates = (await db.AccountsChartAccounts.AsNoTracking()
                .Where(x => x.IsActive &&
                            (accountValues.Contains((x.AccountCode ?? string.Empty).Trim()) ||
                             accountValues.Contains(x.AccountNumber.Trim()) ||
                             accountValues.Contains(x.AccountName.Trim())))
                .Select(x => new { x.Id, x.CategoryId, x.AccountCode, x.AccountNumber, x.AccountName })
                .ToListAsync(ct))
            .SelectMany(x => new[] { x.AccountCode, x.AccountNumber, x.AccountName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => new { x.Id, x.CategoryId, Value = NormalizeMatchText(value!) }))
            .GroupBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.GroupBy(candidate => candidate.Id).Select(group => group.First()).ToArray(), StringComparer.OrdinalIgnoreCase);
        var categoryCandidates = (await db.AccountsCategories.AsNoTracking()
                .Where(x => x.IsActive && (categoryValues.Contains(x.Code) || categoryValues.Contains(x.Name)))
                .Select(x => new { x.Id, x.Code, x.Name })
                .ToListAsync(ct))
            .SelectMany(x => new[] { x.Code, x.Name }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => new { x.Id, Value = value }))
            .GroupBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Select(candidate => candidate.Id).Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);
        var resolvedCategoryIds = categoryCandidates.Values.SelectMany(ids => ids).Distinct().ToList();
        var accountsByCategory = (await db.AccountsChartAccounts.AsNoTracking()
                .Where(x => x.IsActive && x.CategoryId.HasValue && resolvedCategoryIds.Contains(x.CategoryId.Value))
                .Select(x => new { x.Id, CategoryId = x.CategoryId!.Value })
                .ToListAsync(ct))
            .GroupBy(x => x.CategoryId)
            .ToDictionary(group => group.Key, group => group.Select(x => x.Id).Distinct().ToArray());

        var errors = new List<string>();
        foreach (var row in parsed)
        {
            SaleRoznamchaInventoryRow? product = null;
            var foundProduct = false;
            if (!string.IsNullOrWhiteSpace(row.ProductCode))
            {
                foundProduct = productsByExactCode.TryGetValue(row.ProductCode, out product)
                    || productsByCanonicalCode.TryGetValue(CanonicalProductCode(row.ProductCode), out product);
            }
            if (!foundProduct && !string.IsNullOrWhiteSpace(row.ProductName))
            {
                var nameMatches = productsByName.TryGetValue(NormalizeMatchText(row.ProductName), out var sameNameProducts)
                    ? sameNameProducts.Where(candidate =>
                        (row.CompanyName == null || NormalizeMatchText(candidate.CompanyName) == NormalizeMatchText(row.CompanyName)) &&
                        (row.PlatformName == null || NormalizeMatchText(candidate.PlatformName) == NormalizeMatchText(row.PlatformName)) &&
                        (row.ProductCategoryName == null || NormalizeMatchText(candidate.ProductCategoryName) == NormalizeMatchText(row.ProductCategoryName)))
                        .ToList()
                    : [];
                if (nameMatches.Count == 1)
                {
                    product = nameMatches[0];
                    row.ProductCode = product.ProductCode;
                    foundProduct = true;
                }
                else if (nameMatches.Count > 1)
                {
                    errors.Add($"Row {row.RowNumber}: product name '{row.ProductName}' is ambiguous. Add Product Code or complete hierarchy columns.");
                    continue;
                }
            }
            if (foundProduct && product != null)
            {
                row.ProductId = product.Id;
                row.ProductCode = product.ProductCode;
                if (string.IsNullOrWhiteSpace(row.CurrencyCode) && !string.IsNullOrWhiteSpace(product.CurrencyCode))
                    row.CurrencyCode = product.CurrencyCode;
            }
            else
            {
                var productReference = string.IsNullOrWhiteSpace(row.ProductCode) ? row.ProductName : row.ProductCode;
                errors.Add($"Row {row.RowNumber}: product '{productReference}' was not found. Create it in E-Marketing > Product before importing inventory.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(row.CurrencyCode))
                errors.Add($"Row {row.RowNumber}: Currency Code is required.");
            else if (!currencies.TryGetValue(row.CurrencyCode, out var currencyId))
                errors.Add($"Row {row.RowNumber}: active currency code '{row.CurrencyCode}' was not found.");
            else row.CurrencyId = currencyId;
            if (row.FromCategoryValue != null)
            {
                if (!categoryCandidates.TryGetValue(row.FromCategoryValue, out var categoryMatches) || categoryMatches.Length == 0)
                    errors.Add($"Row {row.RowNumber}: From Category '{row.FromCategoryValue}' was not found.");
                else if (categoryMatches.Length > 1)
                    errors.Add($"Row {row.RowNumber}: From Category '{row.FromCategoryValue}' is ambiguous. Use its unique code.");
                else
                    row.MatchedFromCategoryId = categoryMatches[0];
            }
            if (row.FromAccountCode == null)
            {
                var uniqueCategoryAccounts = row.MatchedFromCategoryId.HasValue &&
                                             accountsByCategory.TryGetValue(row.MatchedFromCategoryId.Value, out var categoryAccounts)
                    ? categoryAccounts
                    : [];
                if (uniqueCategoryAccounts.Length == 1)
                    row.FromAccountId = uniqueCategoryAccounts[0];
                else
                    errors.Add($"Row {row.RowNumber}: AcctName / From Account is required because the selected category does not contain exactly one active account.");
            }
            else
            {
                var accountKey = NormalizeMatchText(row.FromAccountCode);
                var matches = accountCandidates.TryGetValue(accountKey, out var candidates)
                    ? candidates.Where(candidate => !row.MatchedFromCategoryId.HasValue || candidate.CategoryId == row.MatchedFromCategoryId).ToArray()
                    : [];
                if (matches.Length == 1)
                    row.FromAccountId = matches[0].Id;
                else if (matches.Length == 0)
                    errors.Add($"Row {row.RowNumber}: From Account '{row.FromAccountCode}' was not found{(row.FromCategoryValue == null ? "." : $" in category '{row.FromCategoryValue}'.")}");
                else
                    errors.Add($"Row {row.RowNumber}: From Account '{row.FromAccountCode}' is ambiguous. Use its unique account code or number.");
            }
        }

        if (errors.Count > 0)
            throw new InvalidOperationException($"Excel import was not posted.{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");

        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    foreach (var row in parsed)
                    {
                        try
                        {
                            await SpListQuery.ExecAsync<SaleRoznamchaCreatedId>(
                                db,
                                "EXEC dbo.usp_SaleRoznamcha_InventoryStockAdd @TenantId, @ProductId, @Reference, @Quantity, @UnitCost, @TotalPurchaseCost, @UsdAmount, @ExchangeRate, @CurrencyId, @FromAccountId, @ToAccountId, @PurchasedDate, @Description, @UserId",
                                ct,
                                SpListQuery.TenantId(current.TenantId),
                                SpListQuery.BigInt("@ProductId", row.ProductId),
                                SpListQuery.NVarChar("@Reference", row.Reference),
                                SpListQuery.Decimal("@Quantity", row.Quantity),
                                SpListQuery.Decimal("@UnitCost", row.UnitCost),
                                SpListQuery.Decimal("@TotalPurchaseCost", row.TotalPurchaseCost),
                                SpListQuery.DecimalNullable("@UsdAmount", row.UsdAmount, 2),
                                SpListQuery.DecimalNullable("@ExchangeRate", row.ExchangeRate, 6),
                                SpListQuery.Int("@CurrencyId", row.CurrencyId),
                                SpListQuery.IntNullable("@FromAccountId", row.FromAccountId),
                                SpListQuery.IntNullable("@ToAccountId", row.ToAccountId),
                                SpListQuery.Date("@PurchasedDate", row.PurchasedDate ?? BusinessClock.Today(BusinessClock.DefaultTimeZoneId)),
                                SpListQuery.NVarChar("@Description", row.Description),
                                SpListQuery.NVarChar("@UserId", current.UserId));
                        }
                        catch (SqlException ex) when (ex.Number is 51000 or 51001 or 2601 or 2627)
                        {
                            var productReference = string.IsNullOrWhiteSpace(row.ProductCode) ? row.ProductName : row.ProductCode;
                            throw new InvalidOperationException(
                                $"Row {row.RowNumber} (Ref: {row.Reference ?? "—"}, Product: {productReference}): {ex.Message}",
                                ex);
                        }
                    }
                    db.SaleRoznamchaPurchaseImports.Add(new SaleRoznamchaPurchaseImport
                    {
                        TenantId = current.TenantId,
                        FileHash = fileHash,
                        OriginalFileName = originalFileName,
                        FileSize = excelFile.Length,
                        ImportedRows = parsed.Count,
                        ProductCount = parsed.Select(x => x.ProductId).Distinct().Count(),
                        CreatedByUserId = current.UserId,
                        CreatedOnUtc = DateTime.UtcNow
                    });
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                }
                catch
                {
                    try { await transaction.RollbackAsync(CancellationToken.None); }
                    catch (InvalidOperationException) { /* A nested SQL procedure may already have rolled back the transaction. */ }
                    db.ChangeTracker.Clear();
                    throw;
                }
            });
        }
        catch (SqlException ex) when (ex.Number is 51000 or 51001 or 2601 or 2627)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new InvalidOperationException("This purchase file has already been imported. Duplicate stock was not posted.", ex);
        }

        return new SaleRoznamchaPurchaseImportResult
        {
            ImportedRows = parsed.Count,
            ProductCount = parsed.Select(x => x.ProductId).Distinct().Count()
        };
    }

    public async Task<string> UploadPurchaseAttachmentAsync(
        long purchaseId,
        IFormFile file,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            throw new InvalidOperationException("Select an attachment to upload.");
        if (file.Length > 10 * 1024 * 1024)
            throw new InvalidOperationException("Purchase attachments cannot exceed 10 MB.");

        var originalName = Path.GetFileName(file.FileName)?.Trim();
        if (string.IsNullOrWhiteSpace(originalName) || originalName.Length > 260)
            throw new InvalidOperationException("The attachment file name is invalid or exceeds 260 characters.");
        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        if (!AllowedAttachmentExtensions.Contains(extension))
            throw new InvalidOperationException("Allowed attachment types are PDF, image, Word, and Excel files.");

        var receipt = await db.SaleRoznamchaStockReceipts
            .SingleOrDefaultAsync(x => x.Id == purchaseId, ct)
            ?? throw new KeyNotFoundException("Purchase entry was not found.");

        var directory = PurchaseAttachmentDirectory(purchaseId);
        Directory.CreateDirectory(directory);
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(directory, storedName);
        var oldStoredName = receipt.AttachmentStoredName;

        try
        {
            await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await file.CopyToAsync(output, ct);

            receipt.AttachmentOriginalName = originalName;
            receipt.AttachmentStoredName = storedName;
            receipt.AttachmentContentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType[..Math.Min(file.ContentType.Length, 100)];
            receipt.AttachmentSize = file.Length;
            if (receipt.RoznamchaEntryId is long paymentId)
            {
                var payment = await db.RoznamchaEntries.FirstOrDefaultAsync(x => x.Id == paymentId, ct);
                if (payment != null) payment.Attachment = originalName;
            }
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
            throw;
        }

        if (!string.IsNullOrWhiteSpace(oldStoredName) && !string.Equals(oldStoredName, storedName, StringComparison.OrdinalIgnoreCase))
        {
            var oldPath = ResolvePurchaseAttachmentPath(purchaseId, oldStoredName);
            if (File.Exists(oldPath)) File.Delete(oldPath);
        }

        return originalName;
    }

    public async Task<SaleRoznamchaPurchaseAttachmentContent> GetPurchaseAttachmentAsync(
        long purchaseId,
        CancellationToken ct = default)
    {
        var receipt = await db.SaleRoznamchaStockReceipts.AsNoTracking()
            .Where(x => x.Id == purchaseId)
            .Select(x => new { x.AttachmentOriginalName, x.AttachmentStoredName, x.AttachmentContentType })
            .SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Purchase entry was not found.");
        if (string.IsNullOrWhiteSpace(receipt.AttachmentStoredName))
            throw new KeyNotFoundException("This purchase entry has no attachment.");

        var fullPath = ResolvePurchaseAttachmentPath(purchaseId, receipt.AttachmentStoredName);
        if (!File.Exists(fullPath))
            throw new KeyNotFoundException("The purchase attachment file was not found.");
        var content = await File.ReadAllBytesAsync(fullPath, ct);
        return new SaleRoznamchaPurchaseAttachmentContent(
            receipt.AttachmentOriginalName ?? "attachment",
            receipt.AttachmentContentType ?? "application/octet-stream",
            content);
    }

    public async Task DeleteProductAsync(long id, CancellationToken ct = default)
    {
        var row = await db.SaleRoznamchaProducts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Inventory product was not found.");
        if (row.QuantityOnHand != 0)
            throw new InvalidOperationException("A product with available stock cannot be removed. Set its quantity to zero first.");

        row.IsDeleted = true;
        row.IsActive = false;
        row.UpdatedByUserId = current.UserId;
        row.UpdatedOnUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task ValidateHierarchyAsync(SaveSaleRoznamchaProductRequest request, CancellationToken ct)
    {
        if (request.CompanyId <= 0 || request.PlatformId <= 0 || request.ProductCategoryId <= 0)
            throw new InvalidOperationException("Company, platform and product category are required.");

        var valid = await (
            from productCategory in db.SaleRoznamchaProductCategories.AsNoTracking()
            join platform in db.SaleRoznamchaPlatforms.AsNoTracking()
                on productCategory.PlatformId equals platform.Id
            join company in db.SaleRoznamchaCompanies.AsNoTracking()
                on platform.CompanyId equals company.Id
            where productCategory.Id == request.ProductCategoryId &&
                  platform.Id == request.PlatformId &&
                  company.Id == request.CompanyId &&
                  productCategory.IsActive && platform.IsActive && company.IsActive
            select productCategory.Id).AnyAsync(ct);

        if (!valid)
            throw new InvalidOperationException("The selected hierarchy is invalid or contains an inactive item.");
    }

    private async Task<SaleRoznamchaInventoryRow> ReloadAsync(long id, CancellationToken ct)
    {
        var row = (await ListAsync(null, null, null, false, ct)).FirstOrDefault(x => x.Id == id);
        return row ?? throw new InvalidOperationException("Saved inventory product could not be reloaded.");
    }

    private async Task ValidateCurrencyAsync(int currencyId, CancellationToken ct)
    {
        if (!await db.AccountsCurrencies.AsNoTracking().AnyAsync(x => x.Id == currencyId && x.IsActive, ct))
            throw new InvalidOperationException("Purchase currency was not found or is inactive.");
    }

    private async Task ValidateAccountsAsync(int? fromAccountId, int? toAccountId, bool requireFromAccount, CancellationToken ct)
    {
        if (requireFromAccount && !fromAccountId.HasValue)
            throw new InvalidOperationException("From Account is required.");
        if (fromAccountId.HasValue && toAccountId.HasValue && fromAccountId == toAccountId)
            throw new InvalidOperationException("From Account and To Account must be different.");

        var accountIds = new[] { fromAccountId, toAccountId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        if (accountIds.Count == 0) return;

        var activeCount = await db.AccountsChartAccounts.AsNoTracking()
            .CountAsync(x => accountIds.Contains(x.Id) && x.IsActive, ct);
        if (activeCount != accountIds.Count)
            throw new InvalidOperationException("A selected inventory account was not found or is inactive.");

    }

    private static void ValidatePurchase(
        decimal quantity,
        decimal? purchasePrice,
        decimal totalPurchaseCost,
        int? currencyId)
    {
        if (quantity < 0)
            throw new InvalidOperationException("Quantity cannot be negative.");
        if (quantity > 99999999999999.9999m || decimal.Round(quantity, 4) != quantity)
            throw new InvalidOperationException("Quantity is outside the supported range or has more than four decimal places.");
        if (!purchasePrice.HasValue)
            throw new InvalidOperationException("Purchase price is required.");
        if (purchasePrice.Value is < -9999999999999999.99m or > 9999999999999999.99m)
            throw new InvalidOperationException("Purchase price is outside the supported range.");
        if (decimal.Round(purchasePrice.Value, 2) != purchasePrice.Value)
            throw new InvalidOperationException("Purchase price cannot have more than two decimal places.");
        if (totalPurchaseCost is < -9999999999999999.99m or > 9999999999999999.99m || decimal.Round(totalPurchaseCost, 2) != totalPurchaseCost)
            throw new InvalidOperationException("Debit is outside the supported range or has more than two decimal places.");
        if (!currencyId.HasValue || currencyId.Value <= 0)
            throw new InvalidOperationException("Purchase currency is required.");
    }

    private static decimal ResolveTotalPurchaseCost(AddSaleRoznamchaStockRequest request)
    {
        var total = request.TotalPurchaseCost
            ?? (request.PurchasePrice.HasValue && request.Quantity > 0
                ? decimal.Round(request.PurchasePrice.Value * request.Quantity, 2, MidpointRounding.AwayFromZero)
                : throw new InvalidOperationException("Debit is required."));
        if (total is < -9999999999999999.99m or > 9999999999999999.99m || decimal.Round(total, 2) != total)
            throw new InvalidOperationException("Debit is outside the supported range or has more than two decimal places.");

        return total;
    }

    private static decimal ResolvePurchaseUnitCost(decimal quantity, decimal totalPurchaseCost)
    {
        if (quantity == 0) return 0;
        if (quantity < 0) throw new InvalidOperationException("Quantity cannot be negative.");

        // Debit can be negative because purchases are exported as an accounting
        // cash outflow. Inventory valuation must still use a positive unit cost.
        return decimal.Round(decimal.Abs(totalPurchaseCost) / quantity, 2, MidpointRounding.AwayFromZero);
    }

    private static void ValidateOptionalAmount(decimal? value, string label, int scale, bool allowNegative = false)
    {
        if (!value.HasValue) return;
        if ((!allowNegative && value.Value < 0) ||
            value.Value is < -9999999999999999.99m or > 9999999999999999.99m ||
            decimal.Round(value.Value, scale) != value.Value)
            throw new InvalidOperationException($"{label} is outside the supported range or has more than {scale} decimal places.");
    }

    private static string? CleanReference(string? value)
    {
        var reference = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (reference?.Length > 100)
            throw new InvalidOperationException("Purchase reference cannot exceed 100 characters.");
        return reference;
    }

    private static string? NormalizeProductCode(string? value, bool required)
    {
        var code = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
        {
            if (required) throw new InvalidOperationException("Product code is required.");
            return null;
        }
        if (code.Length > 80)
            throw new InvalidOperationException("Product code cannot exceed 80 characters.");
        if (code.Any(character => !char.IsLetterOrDigit(character) && character != '-'))
            throw new InvalidOperationException("Product code may contain only letters, numbers and hyphens.");
        return code;
    }

    private static string? CleanDescription(string? value)
    {
        var description = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (description?.Length > 1000)
            throw new InvalidOperationException("Purchase description cannot exceed 1000 characters.");
        return description;
    }

    private string PurchaseAttachmentDirectory(long purchaseId) =>
        Path.Combine(environment.ContentRootPath, "App_Data", "sale-roznamcha-purchase-attachments", current.TenantId.ToString(CultureInfo.InvariantCulture), purchaseId.ToString(CultureInfo.InvariantCulture));

    private string ResolvePurchaseAttachmentPath(long purchaseId, string storedName)
    {
        var directory = Path.GetFullPath(PurchaseAttachmentDirectory(purchaseId));
        var fullPath = Path.GetFullPath(Path.Combine(directory, Path.GetFileName(storedName)));
        if (!fullPath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The attachment path is invalid.");
        return fullPath;
    }

    private static List<ImportedPurchase> ParsePurchaseRows(DataTable table)
    {
        var headers = table.Columns.Cast<DataColumn>()
            .GroupBy(column => NormalizeHeader(column.ColumnName))
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(group => group.Key, group => group.First().Ordinal);
        var productColumn = OptionalColumn(headers, "productcode");
        var productNameColumns = OptionalColumns(table, "productname", "toacctname", "toaccountname", "product");
        if (productColumn < 0 && productNameColumns.Count == 0)
            throw new InvalidOperationException("Required Excel column 'Product Code', 'Product Name', or 'ToAcctName' was not found.");
        var referenceColumn = OptionalColumn(headers, "ref", "reference", "purchasereference", "productreference");
        var categoryColumn = OptionalColumn(headers, "inventorycategory", "inventorycategoryname");
        var companyColumn = OptionalColumn(headers, "company", "companyname");
        var platformColumn = OptionalColumn(headers, "platform", "platformname");
        var productCategoryColumn = OptionalColumn(headers, "productcategory", "productcategoryname");
        var quantityColumn = RequiredColumn(
            headers,
            "quantity", "qty", "purchasedquantity", "purchasequantity", "purchasedqty");
        var totalCostColumn = OptionalColumn(
            headers,
            "debit", "totalpurchasecost", "purchasetotal", "totalcost", "purchaseamount", "totalamount");
        var unitCostColumn = OptionalColumn(
            headers,
            "unitcost", "purchaseprice", "peritempurchasecost", "latestunitcost");
        var usdAmountColumn = OptionalColumn(headers, "usdamt", "usdamount", "totalusd", "purchaseusd");
        var exchangeRateColumn = OptionalColumn(headers, "rate", "exchangerate", "usdrate");
        if (totalCostColumn < 0 && unitCostColumn < 0)
            throw new InvalidOperationException("Required Excel column 'Debit' or 'Unit Cost' was not found.");
        var currencyColumn = OptionalColumn(headers, "currencycode", "currency");
        var fromCategoryColumn = OptionalColumn(headers, "fromcategorycode", "fromcategory", "fromcategoryname", "category", "categoryname");
        var fromAccountColumn = OptionalColumn(headers, "fromaccountcode", "fromaccount", "fromaccountname", "acctname", "accountname");
        var purchasedDateColumn = OptionalColumn(headers, "transdateadjustment", "purchaseddate", "purchasedon", "purchasedondate", "purchasedat");
        var createdDateColumn = OptionalColumn(headers, "createddate", "createdon", "createdatutc");
        var descriptionColumns = OptionalColumns(
            table,
            "discriptions", "descriptions",
            "rdiscriptions", "rdescriptions",
            "purchasedescriptions", "purchasedescription",
            "remarks",
            "discription", "description",
            "rdiscription", "rdescription");

        var result = new List<ImportedPurchase>();
        var parsingErrors = new List<string>();
        for (var index = 0; index < table.Rows.Count; index++)
        {
            var source = table.Rows[index];
            var rowNumber = index + 2;
            var reference = Cell(source, referenceColumn);
            var productCode = Cell(source, productColumn).ToUpperInvariant();
            var productName = Cell(source, productNameColumns);
            var categoryName = Cell(source, categoryColumn);
            var companyName = Cell(source, companyColumn);
            var platformName = Cell(source, platformColumn);
            var productCategoryName = Cell(source, productCategoryColumn);
            var quantityText = Cell(source, quantityColumn);
            var totalCostText = Cell(source, totalCostColumn);
            var unitCostText = Cell(source, unitCostColumn);
            var usdAmountText = Cell(source, usdAmountColumn);
            var exchangeRateText = Cell(source, exchangeRateColumn);
            var currencyCode = Cell(source, currencyColumn).ToUpperInvariant();
            var fromCategoryValue = Cell(source, fromCategoryColumn);
            var fromAccountCode = Cell(source, fromAccountColumn);
            var description = Cell(source, descriptionColumns);
            if (string.IsNullOrWhiteSpace(productCode) && string.IsNullOrWhiteSpace(productName) && string.IsNullOrWhiteSpace(quantityText) &&
                string.IsNullOrWhiteSpace(totalCostText) && string.IsNullOrWhiteSpace(unitCostText) && string.IsNullOrWhiteSpace(usdAmountText))
                continue;

            if (string.IsNullOrWhiteSpace(productCode) && string.IsNullOrWhiteSpace(productName))
                parsingErrors.Add($"Row {rowNumber}: Product Code or Product Name is required.");
            var quantity = 0m;
            if ((!string.IsNullOrWhiteSpace(quantityText) && !TryDecimal(source[quantityColumn], out quantity)) ||
                quantity < 0 || quantity > 99999999999999.9999m || decimal.Round(quantity, 4) != quantity)
                parsingErrors.Add($"Row {rowNumber}: Quantity must be zero or greater and have no more than four decimal places.");
            decimal totalPurchaseCost = 0;
            decimal unitCost = 0;
            if (!string.IsNullOrWhiteSpace(totalCostText))
            {
                if (!TryDecimal(source[totalCostColumn], out totalPurchaseCost) || totalPurchaseCost is < -9999999999999999.99m or > 9999999999999999.99m || decimal.Round(totalPurchaseCost, 2) != totalPurchaseCost)
                    parsingErrors.Add($"Row {rowNumber}: Debit must be a signed amount with up to two decimals.");
                else if (quantity > 0)
                {
                    unitCost = decimal.Round(decimal.Abs(totalPurchaseCost) / quantity, 2, MidpointRounding.AwayFromZero);
                    if (unitCost > 9999999999999999.99m)
                        parsingErrors.Add($"Row {rowNumber}: Calculated Unit Cost is outside the supported range.");
                }
                if (string.IsNullOrWhiteSpace(currencyCode)) currencyCode = "PKR";
            }
            else if (!string.IsNullOrWhiteSpace(unitCostText))
            {
                if (!TryDecimal(source[unitCostColumn], out unitCost) || unitCost is < -9999999999999999.99m or > 9999999999999999.99m || decimal.Round(unitCost, 2) != unitCost)
                    parsingErrors.Add($"Row {rowNumber}: Unit Cost must be a signed amount with up to two decimals.");
                else if (quantity > 0)
                {
                    totalPurchaseCost = decimal.Round(unitCost * quantity, 2, MidpointRounding.AwayFromZero);
                    unitCost = decimal.Abs(unitCost);
                }
            }
            decimal? usdAmount = null;
            if (!string.IsNullOrWhiteSpace(usdAmountText))
            {
                if (!TryDecimal(source[usdAmountColumn], out var parsedUsdAmount) || parsedUsdAmount is < -9999999999999999.99m or > 9999999999999999.99m || decimal.Round(parsedUsdAmount, 2) != parsedUsdAmount)
                    parsingErrors.Add($"Row {rowNumber}: UsdAmt must be a signed amount with up to two decimals.");
                else usdAmount = parsedUsdAmount;
            }
            if (string.IsNullOrWhiteSpace(totalCostText) && string.IsNullOrWhiteSpace(unitCostText))
                parsingErrors.Add($"Row {rowNumber}: Debit or Unit Cost is required. UsdAmt is stored separately and cannot replace Debit.");
            decimal? exchangeRate = null;
            if (!string.IsNullOrWhiteSpace(exchangeRateText))
            {
                if (!TryDecimal(source[exchangeRateColumn], out var parsedRate) || parsedRate < 0 || parsedRate > 999999999999.999999m || decimal.Round(parsedRate, 6) != parsedRate)
                    parsingErrors.Add($"Row {rowNumber}: Rate must be a non-negative amount with up to six decimals.");
                else exchangeRate = parsedRate;
            }
            DateOnly? purchasedDate = null;
            var adjustmentDateText = Cell(source, purchasedDateColumn);
            var createdDateText = Cell(source, createdDateColumn);
            if (!string.IsNullOrWhiteSpace(adjustmentDateText) &&
                TryDateOnly(source[purchasedDateColumn], out var adjustmentDate))
            {
                purchasedDate = adjustmentDate;
            }
            else if (!string.IsNullOrWhiteSpace(createdDateText) &&
                     TryDateOnly(source[createdDateColumn], out var createdDate))
            {
                purchasedDate = createdDate;
            }
            else if (!string.IsNullOrWhiteSpace(adjustmentDateText) || !string.IsNullOrWhiteSpace(createdDateText))
            {
                parsingErrors.Add($"Row {rowNumber}: TransDateAdjustment and Created Date are invalid.");
            }
            if (description.Length > 1000)
                parsingErrors.Add($"Row {rowNumber}: Description cannot exceed 1,000 characters.");
            if (reference.Length > 100)
                parsingErrors.Add($"Row {rowNumber}: Ref cannot exceed 100 characters.");

            result.Add(new ImportedPurchase
            {
                RowNumber = rowNumber,
                Reference = string.IsNullOrWhiteSpace(reference) ? null : reference,
                ProductCode = productCode,
                ProductName = productName,
                CategoryName = string.IsNullOrWhiteSpace(categoryName) ? null : categoryName,
                CompanyName = string.IsNullOrWhiteSpace(companyName) ? null : companyName,
                PlatformName = string.IsNullOrWhiteSpace(platformName) ? null : platformName,
                ProductCategoryName = string.IsNullOrWhiteSpace(productCategoryName) ? null : productCategoryName,
                Quantity = quantity,
                UnitCost = unitCost,
                TotalPurchaseCost = totalPurchaseCost,
                UsdAmount = usdAmount,
                ExchangeRate = exchangeRate,
                CurrencyCode = currencyCode,
                FromCategoryValue = string.IsNullOrWhiteSpace(fromCategoryValue) ? null : fromCategoryValue,
                FromAccountCode = string.IsNullOrWhiteSpace(fromAccountCode) ? null : fromAccountCode,
                PurchasedDate = purchasedDate,
                Description = string.IsNullOrWhiteSpace(description) ? null : description
            });
        }

        if (parsingErrors.Count > 0)
            throw new InvalidOperationException($"Excel import was not posted.{Environment.NewLine}{string.Join(Environment.NewLine, parsingErrors)}");
        return result;
    }

    private static int RequiredColumn(IReadOnlyDictionary<string, int> headers, params string[] aliases)
    {
        var index = OptionalColumn(headers, aliases);
        return index >= 0
            ? index
            : throw new InvalidOperationException($"Required Excel column '{aliases[0]}' was not found.");
    }

    private static int OptionalColumn(IReadOnlyDictionary<string, int> headers, params string[] aliases)
    {
        foreach (var alias in aliases)
            if (headers.TryGetValue(alias, out var index)) return index;
        return -1;
    }

    private static IReadOnlyList<int> OptionalColumns(DataTable table, params string[] aliases)
    {
        var columns = table.Columns.Cast<DataColumn>()
            .Select(column => new { column.Ordinal, Header = NormalizeHeader(column.ColumnName) })
            .ToList();

        return aliases
            .SelectMany(alias => columns.Where(column =>
                column.Header.Equals(alias, StringComparison.Ordinal) ||
                column.Header.StartsWith(alias, StringComparison.Ordinal)))
            .Select(column => column.Ordinal)
            .Distinct()
            .ToList();
    }

    private static string Cell(DataRow row, int column) =>
        column < 0 || row.IsNull(column)
            ? string.Empty
            : Convert.ToString(row[column], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;

    private static string Cell(DataRow row, IReadOnlyList<int> columns)
    {
        foreach (var column in columns)
        {
            var value = Cell(row, column);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return string.Empty;
    }

    private static bool TryDecimal(object value, out decimal result)
    {
        if (value is decimal decimalValue) { result = decimalValue; return true; }
        if (value is double doubleValue) { result = Convert.ToDecimal(doubleValue); return true; }
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[^\d.,()\-]", string.Empty);
        if (text.StartsWith('(') && text.EndsWith(')'))
            text = $"-{text[1..^1]}";
        return decimal.TryParse(
                   text,
                   NumberStyles.Number,
                   CultureInfo.InvariantCulture,
                   out result)
               || decimal.TryParse(
                   text,
                   NumberStyles.Number,
                   CultureInfo.CurrentCulture,
                   out result);
    }

    private static bool TryDateOnly(object value, out DateOnly result)
    {
        if (value is DateTime dateTime)
        {
            result = DateOnly.FromDateTime(dateTime);
            return true;
        }
        if (value is double serialDate)
        {
            try
            {
                result = DateOnly.FromDateTime(DateTime.FromOADate(serialDate));
                return true;
            }
            catch (ArgumentException) { }
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out result)
            || DateOnly.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out result))
            return true;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedDateTime)
            || DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out parsedDateTime))
        {
            result = DateOnly.FromDateTime(parsedDateTime);
            return true;
        }
        return false;
    }

    private static string NormalizeHeader(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string CanonicalProductCode(string value)
    {
        var parts = value.Trim().ToUpperInvariant().Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return string.Empty;
        if (long.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
            parts[^1] = sequence.ToString(CultureInfo.InvariantCulture);
        return string.Join('-', parts);
    }

    private static string NormalizeMatchText(string value) =>
        string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private sealed class ImportedPurchase
    {
        public int RowNumber { get; init; }
        public string? Reference { get; init; }
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; init; } = string.Empty;
        public string? CategoryName { get; init; }
        public string? CompanyName { get; init; }
        public string? PlatformName { get; init; }
        public string? ProductCategoryName { get; init; }
        public decimal Quantity { get; init; }
        public decimal UnitCost { get; init; }
        public decimal TotalPurchaseCost { get; init; }
        public decimal? UsdAmount { get; init; }
        public decimal? ExchangeRate { get; init; }
        public string CurrencyCode { get; set; } = string.Empty;
        public string? FromCategoryValue { get; init; }
        public string? FromAccountCode { get; init; }
        public DateOnly? PurchasedDate { get; init; }
        public string? Description { get; init; }
        public long ProductId { get; set; }
        public int CurrencyId { get; set; }
        public int? MatchedFromCategoryId { get; set; }
        public int? FromAccountId { get; set; }
        public int? ToAccountId { get; set; }
    }
}
