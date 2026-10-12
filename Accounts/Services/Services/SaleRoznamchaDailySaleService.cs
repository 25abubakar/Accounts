using Accounts.Data;
using Accounts.DTOs;
using Accounts.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Accounts.Services.Services;

public sealed class SaleRoznamchaDailySaleService(
    ApplicationDbContext db,
    ICurrentUserService current) : ISaleRoznamchaDailySaleService
{
    public async Task<IReadOnlyList<SaleRoznamchaDailySaleRow>> ListAsync(
        int? categoryId,
        int? companyId,
        int? platformId,
        int? productCategoryId,
        CancellationToken ct = default) =>
        await SpListQuery.ExecAsync<SaleRoznamchaDailySaleRow>(
            db,
            "EXEC dbo.usp_SaleRoznamcha_DailySaleList @TenantId, @CategoryId, @CompanyId, @PlatformId, @ProductCategoryId",
            ct,
            SpListQuery.TenantId(current.TenantId),
            SpListQuery.IntNullable("@CategoryId", categoryId),
            SpListQuery.IntNullable("@CompanyId", companyId),
            SpListQuery.IntNullable("@PlatformId", platformId),
            SpListQuery.IntNullable("@ProductCategoryId", productCategoryId));

    public async Task<IReadOnlyList<SaleRoznamchaSaleStatusRow>> StatusesAsync(CancellationToken ct = default) =>
        await db.SaleRoznamchaSaleStatuses.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new SaleRoznamchaSaleStatusRow
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                ColorCode = x.ColorCode,
                InventoryEffect = x.InventoryEffect,
            })
            .ToListAsync(ct);

    public async Task<SaleRoznamchaDailySaleLookups> LookupsAsync(CancellationToken ct = default) =>
        new()
        {
            TransactionTypes = await db.AccountsTransTypes.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new SaleRoznamchaLookupRow { Id = x.Id, Code = x.Code, Name = x.Name })
                .ToListAsync(ct),
            PaidStatuses = await db.SaleRoznamchaPaidStatuses.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new SaleRoznamchaLookupRow { Id = x.Id, Code = x.Code, Name = x.Name, ColorCode = x.ColorCode })
                .ToListAsync(ct),
            Accounts = await db.AccountsChartAccounts.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.AccountName)
                .Select(x => new SaleRoznamchaAccountLookupRow
                {
                    Id = x.Id,
                    Code = x.AccountCode ?? x.AccountNumber,
                    Name = x.AccountName,
                })
                .ToListAsync(ct),
            ChargeLabels = await db.SaleRoznamchaChargeLabels.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
                .Select(x => new SaleRoznamchaLookupRow { Id = x.Id, Code = x.Code, Name = x.Name })
                .ToListAsync(ct),
            TransactionDateLabels = await db.SaleRoznamchaTransactionDateLabels.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
                .Select(x => new SaleRoznamchaLookupRow { Id = x.Id, Code = x.Code, Name = x.Name })
                .ToListAsync(ct),
            Currencies = await db.AccountsCurrencies.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Code)
                .Select(x => new SaleRoznamchaCurrencyRow
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name,
                    IsDefault = x.IsDefault,
                })
                .ToListAsync(ct),
            PayChargeEffects = await db.SaleRoznamchaAmountEffects.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderByDescending(x => x.Multiplier).ThenBy(x => x.Name)
                .Select(x => new SaleRoznamchaAmountEffectRow
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name,
                    Multiplier = x.Multiplier,
                })
                .ToListAsync(ct),
        };

    public async Task<SaleRoznamchaDailySaleRow> CreateAsync(
        CreateSaleRoznamchaDailySaleRequest request,
        CancellationToken ct = default)
    {
        if (request.Quantity <= 0)
            throw new InvalidOperationException("Quantity must be greater than zero.");

        if (request.SaleCurrencyId <= 0 || request.PayChargeEffectId <= 0)
            throw new InvalidOperationException("Sale currency and Pay Charge treatment are required.");

        var monetaryValues = new[]
        {
            request.UnitSalePrice, request.PayCharges, request.DeliveryFee,
            request.OtherAmount, request.PromotionAmount, request.RtoChargeAmount,
        };
        if (monetaryValues.Any(value => value < 0) || request.Charges.Any(x => x.Amount < 0))
            throw new InvalidOperationException("Amounts cannot be negative.");
        if (request.Charges.Any(x => x.ChargeLabelId <= 0) || request.TransactionDates.Any(x => x.DateLabelId <= 0))
            throw new InvalidOperationException("Every added row must have a valid label.");

        var hierarchyValid = await (
            from product in db.SaleRoznamchaProducts.AsNoTracking()
            join productCategory in db.SaleRoznamchaProductCategories.AsNoTracking()
                on product.ProductCategoryId equals productCategory.Id
            join platform in db.SaleRoznamchaPlatforms.AsNoTracking()
                on productCategory.PlatformId equals platform.Id
            join company in db.SaleRoznamchaCompanies.AsNoTracking()
                on platform.CompanyId equals company.Id
            join category in db.SaleRoznamchaCategories.AsNoTracking()
                on company.CategoryId equals category.Id
            where product.Id == request.ProductId && !product.IsDeleted && product.IsActive &&
                  platform.Id == request.PlatformId
            select product.Id).AnyAsync(ct);
        if (!hierarchyValid)
            throw new InvalidOperationException("Selected product does not belong to the selected platform.");

        try
        {
            var created = await SpListQuery.ExecAsync<SaleRoznamchaCreatedId>(
                db,
                "EXEC dbo.usp_SaleRoznamcha_DailySaleCreate @TenantId, @ProductId, @SaleStatusId, @Quantity, @MarketplaceCompanyId, @OrderId, @UnitSalePrice, @SaleCurrencyId, @PayChargeEffectId, @PayCharges, @DeliveryFee, @OtherAmount, @PromotionAmount, @RtoChargeAmount, @TransactionTypeId, @PaidStatusId, @ToAccountId, @InterProReference, @OrderReference, @SaleDate, @Description, @ChargesJson, @TransactionDatesJson, @UserId",
                ct,
                SpListQuery.TenantId(current.TenantId),
                SpListQuery.BigInt("@ProductId", request.ProductId),
                SpListQuery.Int("@SaleStatusId", request.SaleStatusId),
                SpListQuery.Decimal("@Quantity", request.Quantity),
                SpListQuery.NVarChar("@MarketplaceCompanyId", Clean(request.MarketplaceCompanyId, 100)),
                SpListQuery.NVarChar("@OrderId", Clean(request.OrderId, 100)),
                SpListQuery.Decimal("@UnitSalePrice", request.UnitSalePrice),
                SpListQuery.Int("@SaleCurrencyId", request.SaleCurrencyId),
                SpListQuery.Int("@PayChargeEffectId", request.PayChargeEffectId),
                SpListQuery.Decimal("@PayCharges", request.PayCharges),
                SpListQuery.Decimal("@DeliveryFee", request.DeliveryFee),
                SpListQuery.Decimal("@OtherAmount", request.OtherAmount),
                SpListQuery.Decimal("@PromotionAmount", request.PromotionAmount),
                SpListQuery.Decimal("@RtoChargeAmount", request.RtoChargeAmount),
                SpListQuery.IntNullable("@TransactionTypeId", request.TransactionTypeId),
                SpListQuery.IntNullable("@PaidStatusId", request.PaidStatusId),
                SpListQuery.IntNullable("@ToAccountId", request.ToAccountId),
                SpListQuery.NVarChar("@InterProReference", Clean(request.InterProReference, 100)),
                SpListQuery.NVarChar("@OrderReference", Clean(request.OrderReference, 100)),
                SpListQuery.DateNullable("@SaleDate", request.SaleDate),
                SpListQuery.NVarChar("@Description", Clean(request.Description, 2000)),
                SpListQuery.NVarChar("@ChargesJson", JsonSerializer.Serialize(request.Charges)),
                SpListQuery.NVarChar("@TransactionDatesJson", JsonSerializer.Serialize(request.TransactionDates)),
                SpListQuery.NVarChar("@UserId", current.UserId));
            var id = created.SingleOrDefault()?.Id
                ?? throw new InvalidOperationException("Daily sale could not be created.");
            return (await ListAsync(null, null, null, null, ct)).First(x => x.Id == id);
        }
        catch (SqlException ex) when (ex.Number is 51000 or 51001)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    private static string? Clean(string? value, int maxLength)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (cleaned?.Length > maxLength)
            throw new InvalidOperationException($"Text cannot exceed {maxLength} characters.");
        return cleaned;
    }
}
