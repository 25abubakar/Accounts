using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace Accounts.Services.Services;

public sealed class SaleRoznamchaInventoryService(
    ApplicationDbContext db,
    ICurrentUserService current) : ISaleRoznamchaInventoryService
{
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
        int? categoryId,
        int? companyId,
        int? platformId,
        int? productCategoryId,
        bool activeOnly,
        CancellationToken ct = default) =>
        await SpListQuery.ExecAsync<SaleRoznamchaInventoryRow>(
            db,
            "EXEC dbo.usp_SaleRoznamcha_InventoryList @TenantId, @CategoryId, @CompanyId, @PlatformId, @ProductCategoryId, @ActiveOnly",
            ct,
            SpListQuery.TenantId(current.TenantId),
            SpListQuery.IntNullable("@CategoryId", categoryId),
            SpListQuery.IntNullable("@CompanyId", companyId),
            SpListQuery.IntNullable("@PlatformId", platformId),
            SpListQuery.IntNullable("@ProductCategoryId", productCategoryId),
            SpListQuery.Bit("@ActiveOnly", activeOnly));

    public async Task<SaleRoznamchaInventoryRow> SaveAsync(
        long? id,
        SaveSaleRoznamchaInventoryRequest request,
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
                    "EXEC dbo.usp_SaleRoznamcha_InventoryProductUpdate @TenantId, @ProductId, @ProductCategoryId, @ProductCode, @ProductName, @IsActive, @UserId",
                    ct,
                    SpListQuery.TenantId(current.TenantId),
                    SpListQuery.BigInt("@ProductId", id.Value),
                    SpListQuery.Int("@ProductCategoryId", request.ProductCategoryId),
                    SpListQuery.NVarChar("@ProductCode", productCode),
                    SpListQuery.NVarChar("@ProductName", productName),
                    SpListQuery.Bit("@IsActive", request.IsActive),
                    SpListQuery.NVarChar("@UserId", current.UserId));
            }
            else
            {
                ValidatePurchase(request.Quantity, request.PurchasePrice, request.CurrencyId);
                await ValidateCurrencyAsync(request.CurrencyId!.Value, ct);
                created = await SpListQuery.ExecAsync<SaleRoznamchaCreatedId>(
                    db,
                    "EXEC dbo.usp_SaleRoznamcha_InventoryProductCreate @TenantId, @ProductCategoryId, @ProductCode, @ProductName, @Quantity, @UnitCost, @CurrencyId, @IsActive, @UserId",
                    ct,
                    SpListQuery.TenantId(current.TenantId),
                    SpListQuery.Int("@ProductCategoryId", request.ProductCategoryId),
                    SpListQuery.NVarChar("@ProductCode", productCode),
                    SpListQuery.NVarChar("@ProductName", productName),
                    SpListQuery.Decimal("@Quantity", request.Quantity),
                    SpListQuery.Decimal("@UnitCost", request.PurchasePrice!.Value),
                    SpListQuery.Int("@CurrencyId", request.CurrencyId!.Value),
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
        ValidatePurchase(request.Quantity, request.PurchasePrice, request.CurrencyId, quantityMustBePositive: true);
        await ValidateCurrencyAsync(request.CurrencyId!.Value, ct);
        try
        {
            var result = await SpListQuery.ExecAsync<SaleRoznamchaCreatedId>(
                db,
                "EXEC dbo.usp_SaleRoznamcha_InventoryStockAdd @TenantId, @ProductId, @Quantity, @UnitCost, @CurrencyId, @UserId",
                ct,
                SpListQuery.TenantId(current.TenantId),
                SpListQuery.BigInt("@ProductId", id),
                SpListQuery.Decimal("@Quantity", request.Quantity),
                SpListQuery.Decimal("@UnitCost", request.PurchasePrice!.Value),
                SpListQuery.Int("@CurrencyId", request.CurrencyId!.Value),
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

    public async Task DeleteAsync(long id, CancellationToken ct = default)
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

    private async Task ValidateHierarchyAsync(SaveSaleRoznamchaInventoryRequest request, CancellationToken ct)
    {
        if (request.CategoryId <= 0 || request.CompanyId <= 0 || request.PlatformId <= 0 || request.ProductCategoryId <= 0)
            throw new InvalidOperationException("Category, company, platform and product category are required.");

        var valid = await (
            from productCategory in db.SaleRoznamchaProductCategories.AsNoTracking()
            join platform in db.SaleRoznamchaPlatforms.AsNoTracking()
                on productCategory.PlatformId equals platform.Id
            join company in db.SaleRoznamchaCompanies.AsNoTracking()
                on platform.CompanyId equals company.Id
            join category in db.SaleRoznamchaCategories.AsNoTracking()
                on company.CategoryId equals category.Id
            where productCategory.Id == request.ProductCategoryId &&
                  platform.Id == request.PlatformId &&
                  company.Id == request.CompanyId &&
                  category.Id == request.CategoryId &&
                  productCategory.IsActive && platform.IsActive && company.IsActive && category.IsActive
            select productCategory.Id).AnyAsync(ct);

        if (!valid)
            throw new InvalidOperationException("The selected hierarchy is invalid or contains an inactive item.");
    }

    private async Task<SaleRoznamchaInventoryRow> ReloadAsync(long id, CancellationToken ct)
    {
        var row = (await ListAsync(null, null, null, null, false, ct)).FirstOrDefault(x => x.Id == id);
        return row ?? throw new InvalidOperationException("Saved inventory product could not be reloaded.");
    }

    private async Task ValidateCurrencyAsync(int currencyId, CancellationToken ct)
    {
        if (!await db.AccountsCurrencies.AsNoTracking().AnyAsync(x => x.Id == currencyId && x.IsActive, ct))
            throw new InvalidOperationException("Purchase currency was not found or is inactive.");
    }

    private static void ValidatePurchase(
        decimal quantity,
        decimal? purchasePrice,
        int? currencyId,
        bool quantityMustBePositive = false)
    {
        if (quantityMustBePositive ? quantity <= 0 : quantity < 0)
            throw new InvalidOperationException(quantityMustBePositive
                ? "Stock quantity must be greater than zero."
                : "Quantity cannot be negative.");
        if (!purchasePrice.HasValue)
            throw new InvalidOperationException("Purchase price is required.");
        if (purchasePrice.Value < 0 || purchasePrice.Value > 9999999999999999.99m)
            throw new InvalidOperationException("Purchase price is outside the supported range.");
        if (decimal.Round(purchasePrice.Value, 2) != purchasePrice.Value)
            throw new InvalidOperationException("Purchase price cannot have more than two decimal places.");
        if (!currencyId.HasValue || currencyId.Value <= 0)
            throw new InvalidOperationException("Purchase currency is required.");
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
}
