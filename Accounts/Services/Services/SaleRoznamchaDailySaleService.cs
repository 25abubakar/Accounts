using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
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
                .Where(x => x.IsActive && (x.TenantId == null || x.TenantId == current.TenantId))
                .OrderBy(x => x.Code).ThenBy(x => x.Id)
                .Select(x => new SaleRoznamchaCurrencyRow
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name,
                    IsDefault = false,
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
        if (request.UnitSalePrice <= 0)
            throw new InvalidOperationException("Unit Sale Price must be greater than zero.");

        var monetaryValues = new[]
        {
            request.UnitSalePrice, request.PayCharges, request.DeliveryFee,
            request.OtherAmount, request.PromotionAmount, request.RtoChargeAmount,
        };
        if (monetaryValues.Skip(1).Any(value => value < 0) || request.Charges.Any(x => x.Amount < 0))
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

    public async Task<IReadOnlyList<SaleRoznamchaInvoiceRow>> ListInvoicesAsync(
        int? categoryId, int? companyId, int? platformId, int? productCategoryId,
        CancellationToken ct = default)
    {
        var invoices = await db.SaleRoznamchaInvoices.AsNoTracking()
            .Include(x => x.SaleStatus).Include(x => x.SaleCurrency).Include(x => x.PayChargeEffect)
            .Include(x => x.Lines).ThenInclude(x => x.Product).ThenInclude(x => x.ProductCategory)
                .ThenInclude(x => x.Platform).ThenInclude(x => x.Company)
            .Include(x => x.Lines).ThenInclude(x => x.UnitCostCurrency)
            .Include(x => x.Charges).ThenInclude(x => x.ChargeLabel)
            .Include(x => x.TransactionDates).ThenInclude(x => x.DateLabel)
            .Where(invoice => invoice.Lines.Any(line =>
                (!categoryId.HasValue || line.Product.ProductCategory.Platform.Company.CategoryId == categoryId) &&
                (!companyId.HasValue || line.Product.ProductCategory.Platform.CompanyId == companyId) &&
                (!platformId.HasValue || line.Product.ProductCategory.PlatformId == platformId) &&
                (!productCategoryId.HasValue || line.Product.ProductCategoryId == productCategoryId)))
            .OrderByDescending(x => x.CreatedOnUtc).ThenByDescending(x => x.Id)
            .AsSplitQuery()
            .ToListAsync(ct);
        var names = await LoadInvoiceLookupNamesAsync(invoices, ct);
        return invoices.Select(x => MapInvoice(x, names.TransactionTypes, names.PaidStatuses, names.Accounts)).ToList();
    }

    public async Task<SaleRoznamchaInvoiceRow> GetInvoiceAsync(long id, CancellationToken ct = default)
    {
        var invoice = await db.SaleRoznamchaInvoices.AsNoTracking()
            .Include(x => x.SaleStatus).Include(x => x.SaleCurrency).Include(x => x.PayChargeEffect)
            .Include(x => x.Lines).ThenInclude(x => x.Product).ThenInclude(x => x.ProductCategory)
                .ThenInclude(x => x.Platform).ThenInclude(x => x.Company)
            .Include(x => x.Lines).ThenInclude(x => x.UnitCostCurrency)
            .Include(x => x.Charges).ThenInclude(x => x.ChargeLabel)
            .Include(x => x.TransactionDates).ThenInclude(x => x.DateLabel)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Invoice was not found.");
        var names = await LoadInvoiceLookupNamesAsync([invoice], ct);
        return MapInvoice(invoice, names.TransactionTypes, names.PaidStatuses, names.Accounts);
    }

    private static SaleRoznamchaInvoiceRow MapInvoice(
        SaleRoznamchaInvoice invoice,
        IReadOnlyDictionary<int, string> transactionTypes,
        IReadOnlyDictionary<int, string> paidStatuses,
        IReadOnlyDictionary<int, string> accounts)
    {
        var items = invoice.Lines.OrderBy(x => x.Id).Select(line => new SaleRoznamchaInvoiceLineRow
        {
            Id = line.Id, ProductId = line.ProductId, ProductCode = line.Product.ProductCode,
            ProductName = line.Product.Name, CompanyId = line.Product.ProductCategory.Platform.CompanyId,
            CompanyName = line.Product.ProductCategory.Platform.Company.Name,
            PlatformId = line.Product.ProductCategory.PlatformId, PlatformName = line.Product.ProductCategory.Platform.Name,
            Quantity = line.Quantity, UnitSalePrice = line.UnitSalePrice, LineTotal = line.BaseSaleAmount,
            PurchaseUnitCost = line.UnitCost, PurchaseCurrencyCode = line.UnitCostCurrency?.Code,
        }).ToList();

        return new SaleRoznamchaInvoiceRow
        {
            Id = invoice.Id, ReceiptNumber = invoice.ReceiptNumber, OrderId = invoice.OrderId,
            SaleStatusId = invoice.SaleStatusId, StatusName = invoice.SaleStatus.Name,
            SaleCurrencyId = invoice.SaleCurrencyId, SaleCurrencyCode = invoice.SaleCurrency.Code,
            PayChargeEffectId = invoice.PayChargeEffectId, PayChargeEffectName = invoice.PayChargeEffect.Name,
            PayCharges = invoice.PayCharges, DeliveryFee = invoice.DeliveryFee, OtherAmount = invoice.OtherAmount,
            PromotionAmount = invoice.PromotionAmount, RtoChargeAmount = invoice.RtoChargeAmount,
            ItemsSubtotal = invoice.ItemsSubtotal, TotalAdditions = invoice.TotalAdditions,
            TotalDeductions = invoice.TotalDeductions, NetTotal = invoice.NetTotal,
            TransactionTypeId = invoice.TransactionTypeId,
            TransactionTypeName = LookupName(transactionTypes, invoice.TransactionTypeId),
            PaidStatusId = invoice.PaidStatusId,
            PaidStatusName = LookupName(paidStatuses, invoice.PaidStatusId),
            ToAccountId = invoice.ToAccountId,
            ToAccountName = LookupName(accounts, invoice.ToAccountId),
            InterProReference = invoice.InterProReference, OrderReference = invoice.OrderReference,
            SaleDate = invoice.SaleDate, Description = invoice.Description,
            TotalQuantity = items.Sum(x => x.Quantity), ItemCount = items.Count,
            ProductSummary = string.Join(", ", items.Select(x => $"{x.ProductCode} - {x.ProductName}")),
            CreatedOnUtc = invoice.CreatedOnUtc, UpdatedOnUtc = invoice.UpdatedOnUtc, Items = items,
            Charges = invoice.Charges.OrderBy(x => x.Id).Select(x => new SaleRoznamchaInvoiceChargeRow
                { Id = x.Id, ChargeLabelId = x.ChargeLabelId, Label = x.ChargeLabel.Name, Amount = x.Amount }).ToList(),
            TransactionDates = invoice.TransactionDates.OrderBy(x => x.Value).ThenBy(x => x.Id)
                .Select(x => new SaleRoznamchaInvoiceTransactionDateRow
                    { Id = x.Id, DateLabelId = x.DateLabelId, Label = x.DateLabel.Name, Value = x.Value }).ToList(),
        };
    }

    private async Task<(
        IReadOnlyDictionary<int, string> TransactionTypes,
        IReadOnlyDictionary<int, string> PaidStatuses,
        IReadOnlyDictionary<int, string> Accounts)> LoadInvoiceLookupNamesAsync(
            IReadOnlyCollection<SaleRoznamchaInvoice> invoices, CancellationToken ct)
    {
        var transactionTypeIds = invoices.Where(x => x.TransactionTypeId.HasValue).Select(x => x.TransactionTypeId!.Value).Distinct().ToList();
        var paidStatusIds = invoices.Where(x => x.PaidStatusId.HasValue).Select(x => x.PaidStatusId!.Value).Distinct().ToList();
        var accountIds = invoices.Where(x => x.ToAccountId.HasValue).Select(x => x.ToAccountId!.Value).Distinct().ToList();
        var transactionTypes = await db.AccountsTransTypes.AsNoTracking().Where(x => transactionTypeIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var paidStatuses = await db.SaleRoznamchaPaidStatuses.AsNoTracking().Where(x => paidStatusIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var accounts = await db.AccountsChartAccounts.AsNoTracking().Where(x => accountIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.AccountName, ct);
        return (transactionTypes, paidStatuses, accounts);
    }

    private static string? LookupName(IReadOnlyDictionary<int, string> names, int? id)
        => id.HasValue && names.TryGetValue(id.Value, out var name) ? name : null;

    public async Task<SaleRoznamchaInvoiceRow> CreateInvoiceAsync(
        SaveSaleRoznamchaInvoiceRequest request, CancellationToken ct = default)
    {
        ValidateInvoice(request.Items, request.PayCharges, request.DeliveryFee, request.OtherAmount,
            request.PromotionAmount, request.RtoChargeAmount, request.Charges);
        if (request.SaleStatusId <= 0 || request.SaleCurrencyId <= 0 || request.PayChargeEffectId <= 0)
            throw new InvalidOperationException("Sale Status, Sale Currency and Pay Charge Treatment are required.");

        var strategy = db.Database.CreateExecutionStrategy();
        var invoiceId = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var effect = await db.SaleRoznamchaAmountEffects.SingleOrDefaultAsync(
                x => x.Id == request.PayChargeEffectId && x.IsActive, ct)
                ?? throw new InvalidOperationException("Selected Pay Charge treatment is invalid or inactive.");
            var invoice = new SaleRoznamchaInvoice
            {
                TenantId = current.TenantId, ReceiptNumber = $"PENDING-{Guid.NewGuid():N}",
                OrderId = Clean(request.OrderId, 100), SaleStatusId = request.SaleStatusId,
                SaleCurrencyId = request.SaleCurrencyId, PayChargeEffectId = request.PayChargeEffectId,
                PayCharges = request.PayCharges, DeliveryFee = request.DeliveryFee,
                OtherAmount = request.OtherAmount, PromotionAmount = request.PromotionAmount,
                RtoChargeAmount = request.RtoChargeAmount, TransactionTypeId = request.TransactionTypeId,
                PaidStatusId = request.PaidStatusId, ToAccountId = request.ToAccountId,
                InterProReference = Clean(request.InterProReference, 100), OrderReference = Clean(request.OrderReference, 100),
                SaleDate = request.SaleDate, Description = Clean(request.Description, 2000),
                CreatedByUserId = current.UserId, CreatedOnUtc = DateTime.UtcNow,
            };
            db.SaleRoznamchaInvoices.Add(invoice);
            await db.SaveChangesAsync(ct);
            invoice.ReceiptNumber = $"SR-{invoice.Id:00000000}";

            decimal subtotal = 0;
            foreach (var item in request.Items)
            {
                var line = await CreateAsync(new CreateSaleRoznamchaDailySaleRequest
                {
                    PlatformId = item.PlatformId, ProductId = item.ProductId,
                    SaleStatusId = request.SaleStatusId, Quantity = item.Quantity,
                    OrderId = request.OrderId, UnitSalePrice = item.UnitSalePrice,
                    SaleCurrencyId = request.SaleCurrencyId, PayChargeEffectId = request.PayChargeEffectId,
                    TransactionTypeId = request.TransactionTypeId, PaidStatusId = request.PaidStatusId,
                    ToAccountId = request.ToAccountId, InterProReference = request.InterProReference,
                    OrderReference = request.OrderReference, SaleDate = request.SaleDate,
                    Description = request.Description,
                }, ct);
                await db.SaleRoznamchaDailySales.Where(x => x.Id == line.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.InvoiceId, invoice.Id), ct);
                subtotal += Math.Round(item.Quantity * item.UnitSalePrice, 2, MidpointRounding.AwayFromZero);
            }

            ApplyInvoiceAmounts(invoice, subtotal, effect.Multiplier, request.PayCharges, request.DeliveryFee,
                request.OtherAmount, request.PromotionAmount, request.RtoChargeAmount,
                request.Charges.Sum(x => x.Amount));
            AddInvoiceChildren(invoice, request.Charges, request.TransactionDates);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return invoice.Id;
        });
        return await GetInvoiceAsync(invoiceId, ct);
    }

    public async Task<SaleRoznamchaInvoiceRow> UpdateInvoiceAsync(
        long id, UpdateSaleRoznamchaInvoiceRequest request, CancellationToken ct = default)
    {
        ValidateInvoice([], request.PayCharges, request.DeliveryFee, request.OtherAmount,
            request.PromotionAmount, request.RtoChargeAmount, request.Charges, requireItems: false);
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var invoice = await db.SaleRoznamchaInvoices
                .Include(x => x.Charges).Include(x => x.TransactionDates)
                .SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException("Invoice was not found.");
            var effect = await db.SaleRoznamchaAmountEffects.SingleOrDefaultAsync(
                x => x.Id == request.PayChargeEffectId && x.IsActive, ct)
                ?? throw new InvalidOperationException("Selected Pay Charge treatment is invalid or inactive.");

            invoice.OrderId = Clean(request.OrderId, 100); invoice.PayChargeEffectId = request.PayChargeEffectId;
            invoice.PayCharges = request.PayCharges; invoice.DeliveryFee = request.DeliveryFee;
            invoice.OtherAmount = request.OtherAmount; invoice.PromotionAmount = request.PromotionAmount;
            invoice.RtoChargeAmount = request.RtoChargeAmount; invoice.TransactionTypeId = request.TransactionTypeId;
            invoice.PaidStatusId = request.PaidStatusId; invoice.ToAccountId = request.ToAccountId;
            invoice.InterProReference = Clean(request.InterProReference, 100);
            invoice.OrderReference = Clean(request.OrderReference, 100); invoice.SaleDate = request.SaleDate;
            invoice.Description = Clean(request.Description, 2000); invoice.UpdatedByUserId = current.UserId;
            invoice.UpdatedOnUtc = DateTime.UtcNow;
            db.SaleRoznamchaInvoiceCharges.RemoveRange(invoice.Charges);
            db.SaleRoznamchaInvoiceTransactionDates.RemoveRange(invoice.TransactionDates);
            ApplyInvoiceAmounts(invoice, invoice.ItemsSubtotal, effect.Multiplier, request.PayCharges,
                request.DeliveryFee, request.OtherAmount, request.PromotionAmount, request.RtoChargeAmount,
                request.Charges.Sum(x => x.Amount));
            AddInvoiceChildren(invoice, request.Charges, request.TransactionDates);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
        return await GetInvoiceAsync(id, ct);
    }

    private static void ValidateInvoice(
        IReadOnlyList<SaleRoznamchaInvoiceLineRequest> items, decimal payCharges, decimal deliveryFee,
        decimal otherAmount, decimal promotionAmount, decimal rtoChargeAmount,
        IReadOnlyList<CreateSaleRoznamchaChargeRequest> charges, bool requireItems = true)
    {
        if (requireItems && items.Count == 0) throw new InvalidOperationException("Add at least one product to the invoice.");
        if (items.Any(x => x.ProductId <= 0 || x.PlatformId <= 0 || x.Quantity <= 0 || x.UnitSalePrice <= 0))
            throw new InvalidOperationException("Every product line requires a valid product, quantity and Unit Sale Price.");
        if (new[] { payCharges, deliveryFee, otherAmount, promotionAmount, rtoChargeAmount }.Any(x => x < 0)
            || charges.Any(x => x.ChargeLabelId <= 0 || x.Amount < 0))
            throw new InvalidOperationException("Charges and deductions cannot be negative.");
    }

    private static void ApplyInvoiceAmounts(
        SaleRoznamchaInvoice invoice, decimal subtotal, short payMultiplier, decimal payCharges,
        decimal deliveryFee, decimal otherAmount, decimal promotionAmount, decimal rtoChargeAmount,
        decimal dynamicCharges)
    {
        invoice.ItemsSubtotal = Math.Round(subtotal, 2, MidpointRounding.AwayFromZero);
        invoice.TotalAdditions = Math.Round(deliveryFee + otherAmount + dynamicCharges + (payMultiplier == 1 ? payCharges : 0), 2, MidpointRounding.AwayFromZero);
        invoice.TotalDeductions = Math.Round(promotionAmount + rtoChargeAmount + (payMultiplier == -1 ? payCharges : 0), 2, MidpointRounding.AwayFromZero);
        invoice.NetTotal = invoice.ItemsSubtotal + invoice.TotalAdditions - invoice.TotalDeductions;
        if (invoice.NetTotal < 0) throw new InvalidOperationException("Invoice deductions cannot exceed its subtotal and additions.");
    }

    private void AddInvoiceChildren(
        SaleRoznamchaInvoice invoice, IReadOnlyList<CreateSaleRoznamchaChargeRequest> charges,
        IReadOnlyList<CreateSaleRoznamchaTransactionDateRequest> dates)
    {
        foreach (var charge in charges) invoice.Charges.Add(new SaleRoznamchaInvoiceCharge
            { TenantId = current.TenantId, ChargeLabelId = charge.ChargeLabelId, Amount = charge.Amount });
        foreach (var date in dates) invoice.TransactionDates.Add(new SaleRoznamchaInvoiceTransactionDate
            { TenantId = current.TenantId, DateLabelId = date.DateLabelId, Value = date.Value });
    }

    private static string? Clean(string? value, int maxLength)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (cleaned?.Length > maxLength)
            throw new InvalidOperationException($"Text cannot exceed {maxLength} characters.");
        return cleaned;
    }
}
