namespace Accounts.DTOs;

public sealed class SaleRoznamchaMasterRow
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public int? ParentId { get; set; }
    public string? ParentName { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int? CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public int? PlatformId { get; set; }
    public string? PlatformName { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
}

public sealed class SaveSaleRoznamchaMasterRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public int? ParentId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class SaleRoznamchaSettingRow
{
    public int Id { get; set; }
    public string SettingType { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ColorCode { get; set; }
    public int SortOrder { get; set; }
    public short InventoryEffect { get; set; }
    public bool IsActive { get; set; }
    public bool IsPlatform { get; set; }
}

public sealed class SaveSaleRoznamchaSettingRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ColorCode { get; set; }
    public int SortOrder { get; set; }
    public short InventoryEffect { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class SaleRoznamchaInventoryRow
{
    public long Id { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public int PlatformId { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public int ProductCategoryId { get; set; }
    public string ProductCategoryName { get; set; } = string.Empty;
    public decimal QuantityOnHand { get; set; }
    public decimal TotalPurchasedQuantity { get; set; }
    public decimal SoldQuantity { get; set; }
    public decimal PurchasePrice { get; set; }
    public int CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal TotalStockValue { get; set; }
    public string StockValueBreakdownJson { get; set; } = "[]";
    public string StockStatus { get; set; } = string.Empty;
    public DateTime? LastPurchasedOnUtc { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
}

public sealed class SaveSaleRoznamchaInventoryRequest
{
    public int CategoryId { get; set; }
    public int CompanyId { get; set; }
    public int PlatformId { get; set; }
    public int ProductCategoryId { get; set; }
    public string? ProductCode { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal? PurchasePrice { get; set; }
    public int? CurrencyId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class AddSaleRoznamchaStockRequest
{
    public decimal Quantity { get; set; }
    public decimal? PurchasePrice { get; set; }
    public int? CurrencyId { get; set; }
    public string? Description { get; set; }
}

public sealed class SaleRoznamchaInventoryPurchaseRow
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public int PlatformId { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public int ProductCategoryId { get; set; }
    public string ProductCategoryName { get; set; } = string.Empty;
    public decimal PurchasedQuantity { get; set; }
    public decimal SoldQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public int CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal TotalPurchaseCost { get; set; }
    public decimal RemainingStockValue { get; set; }
    public DateTime PurchasedOnUtc { get; set; }
    public string? Description { get; set; }
    public string BatchStatus { get; set; } = string.Empty;
}

public sealed class SaleRoznamchaCurrencyRow
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public sealed class SaleRoznamchaSaleStatusRow
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ColorCode { get; set; }
    public short InventoryEffect { get; set; }
}

public sealed class SaleRoznamchaLookupRow
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ColorCode { get; set; }
}

public sealed class SaleRoznamchaAccountLookupRow
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class SaleRoznamchaDailySaleLookups
{
    public IReadOnlyList<SaleRoznamchaLookupRow> TransactionTypes { get; set; } = [];
    public IReadOnlyList<SaleRoznamchaLookupRow> PaidStatuses { get; set; } = [];
    public IReadOnlyList<SaleRoznamchaAccountLookupRow> Accounts { get; set; } = [];
    public IReadOnlyList<SaleRoznamchaLookupRow> ChargeLabels { get; set; } = [];
    public IReadOnlyList<SaleRoznamchaLookupRow> TransactionDateLabels { get; set; } = [];
    public IReadOnlyList<SaleRoznamchaCurrencyRow> Currencies { get; set; } = [];
    public IReadOnlyList<SaleRoznamchaAmountEffectRow> PayChargeEffects { get; set; } = [];
}

public sealed class SaleRoznamchaAmountEffectRow
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public short Multiplier { get; set; }
}

public sealed class SaleRoznamchaDailySaleRow
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public int PlatformId { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public int ProductCategoryId { get; set; }
    public string ProductCategoryName { get; set; } = string.Empty;
    public int SaleStatusId { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public string? MarketplaceCompanyId { get; set; }
    public string? OrderId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public int? UnitCostCurrencyId { get; set; }
    public string? UnitCostCurrencyCode { get; set; }
    public int SaleCurrencyId { get; set; }
    public string SaleCurrencyCode { get; set; } = string.Empty;
    public int PayChargeEffectId { get; set; }
    public string PayChargeEffectName { get; set; } = string.Empty;
    public short PayChargeMultiplier { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal BaseSaleAmount { get; set; }
    public decimal TotalAdditions { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetSaleTotal { get; set; }
    public decimal GrossTotal { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal CostAmount { get; set; }
    public decimal PayCharges { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal OtherAmount { get; set; }
    public decimal PromotionAmount { get; set; }
    public decimal RtoChargeAmount { get; set; }
    public int? TransactionTypeId { get; set; }
    public string? TransactionTypeName { get; set; }
    public int? PaidStatusId { get; set; }
    public string? PaidStatusName { get; set; }
    public int? ToAccountId { get; set; }
    public string? ToAccountName { get; set; }
    public string? InterProReference { get; set; }
    public string? OrderReference { get; set; }
    public DateOnly? SaleDate { get; set; }
    public string? Description { get; set; }
    public int ChargeCount { get; set; }
    public decimal AdditionalChargeTotal { get; set; }
    public string ChargeDetails { get; set; } = string.Empty;
    public int TransactionDateCount { get; set; }
    public string TransactionDateDetails { get; set; } = string.Empty;
    public decimal InventoryDelta { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public DateTime CreatedOnUtc { get; set; }
}

public sealed class CreateSaleRoznamchaDailySaleRequest
{
    public int PlatformId { get; set; }
    public long ProductId { get; set; }
    public int SaleStatusId { get; set; }
    public decimal Quantity { get; set; }
    public string? MarketplaceCompanyId { get; set; }
    public string? OrderId { get; set; }
    public decimal UnitSalePrice { get; set; }
    public int SaleCurrencyId { get; set; }
    public int PayChargeEffectId { get; set; }
    public decimal PayCharges { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal OtherAmount { get; set; }
    public decimal PromotionAmount { get; set; }
    public decimal RtoChargeAmount { get; set; }
    public int? TransactionTypeId { get; set; }
    public int? PaidStatusId { get; set; }
    public int? ToAccountId { get; set; }
    public string? InterProReference { get; set; }
    public string? OrderReference { get; set; }
    public DateOnly? SaleDate { get; set; }
    public string? Description { get; set; }
    public IReadOnlyList<CreateSaleRoznamchaChargeRequest> Charges { get; set; } = [];
    public IReadOnlyList<CreateSaleRoznamchaTransactionDateRequest> TransactionDates { get; set; } = [];
}

public sealed class CreateSaleRoznamchaChargeRequest
{
    public int ChargeLabelId { get; set; }
    public decimal Amount { get; set; }
}

public sealed class CreateSaleRoznamchaTransactionDateRequest
{
    public int DateLabelId { get; set; }
    public DateOnly Value { get; set; }
}

public sealed class SaleRoznamchaCreatedId
{
    public long Id { get; set; }
}

public sealed class SaleRoznamchaInvoiceLineRequest
{
    public int PlatformId { get; set; }
    public long ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitSalePrice { get; set; }
}

public sealed class SaveSaleRoznamchaInvoiceRequest
{
    public string? OrderId { get; set; }
    public int SaleStatusId { get; set; }
    public int SaleCurrencyId { get; set; }
    public int PayChargeEffectId { get; set; }
    public decimal PayCharges { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal OtherAmount { get; set; }
    public decimal PromotionAmount { get; set; }
    public decimal RtoChargeAmount { get; set; }
    public int? TransactionTypeId { get; set; }
    public int? PaidStatusId { get; set; }
    public int? ToAccountId { get; set; }
    public string? InterProReference { get; set; }
    public string? OrderReference { get; set; }
    public DateOnly? SaleDate { get; set; }
    public string? Description { get; set; }
    public IReadOnlyList<SaleRoznamchaInvoiceLineRequest> Items { get; set; } = [];
    public IReadOnlyList<CreateSaleRoznamchaChargeRequest> Charges { get; set; } = [];
    public IReadOnlyList<CreateSaleRoznamchaTransactionDateRequest> TransactionDates { get; set; } = [];
}

public sealed class UpdateSaleRoznamchaInvoiceRequest
{
    public string? OrderId { get; set; }
    public int PayChargeEffectId { get; set; }
    public decimal PayCharges { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal OtherAmount { get; set; }
    public decimal PromotionAmount { get; set; }
    public decimal RtoChargeAmount { get; set; }
    public int? TransactionTypeId { get; set; }
    public int? PaidStatusId { get; set; }
    public int? ToAccountId { get; set; }
    public string? InterProReference { get; set; }
    public string? OrderReference { get; set; }
    public DateOnly? SaleDate { get; set; }
    public string? Description { get; set; }
    public IReadOnlyList<CreateSaleRoznamchaChargeRequest> Charges { get; set; } = [];
    public IReadOnlyList<CreateSaleRoznamchaTransactionDateRequest> TransactionDates { get; set; } = [];
}

public sealed class SaleRoznamchaInvoiceLineRow
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public int PlatformId { get; set; }
    public string PlatformName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitSalePrice { get; set; }
    public decimal LineTotal { get; set; }
    public decimal PurchaseUnitCost { get; set; }
    public string? PurchaseCurrencyCode { get; set; }
}

public sealed class SaleRoznamchaInvoiceRow
{
    public long Id { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public string? OrderId { get; set; }
    public int SaleStatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public int SaleCurrencyId { get; set; }
    public string SaleCurrencyCode { get; set; } = string.Empty;
    public int PayChargeEffectId { get; set; }
    public string PayChargeEffectName { get; set; } = string.Empty;
    public decimal PayCharges { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal OtherAmount { get; set; }
    public decimal PromotionAmount { get; set; }
    public decimal RtoChargeAmount { get; set; }
    public decimal ItemsSubtotal { get; set; }
    public decimal TotalAdditions { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetTotal { get; set; }
    public int? TransactionTypeId { get; set; }
    public string? TransactionTypeName { get; set; }
    public int? PaidStatusId { get; set; }
    public string? PaidStatusName { get; set; }
    public int? ToAccountId { get; set; }
    public string? ToAccountName { get; set; }
    public string? InterProReference { get; set; }
    public string? OrderReference { get; set; }
    public DateOnly? SaleDate { get; set; }
    public string? Description { get; set; }
    public decimal TotalQuantity { get; set; }
    public int ItemCount { get; set; }
    public string ProductSummary { get; set; } = string.Empty;
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
    public IReadOnlyList<SaleRoznamchaInvoiceLineRow> Items { get; set; } = [];
    public IReadOnlyList<SaleRoznamchaInvoiceChargeRow> Charges { get; set; } = [];
    public IReadOnlyList<SaleRoznamchaInvoiceTransactionDateRow> TransactionDates { get; set; } = [];
}

public sealed class SaleRoznamchaInvoiceChargeRow
{
    public long Id { get; set; }
    public int ChargeLabelId { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public sealed class SaleRoznamchaInvoiceTransactionDateRow
{
    public long Id { get; set; }
    public int DateLabelId { get; set; }
    public string Label { get; set; } = string.Empty;
    public DateOnly Value { get; set; }
}

public sealed class SaleRoznamchaHistoryAggregateRow
{
    public string Dimension { get; set; } = string.Empty;
    public string GroupKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DateTime? PeriodStart { get; set; }
    public long SortOrder { get; set; }
    public long TransactionCount { get; set; }
    public decimal Quantity { get; set; }
    public decimal InventoryDelta { get; set; }
    public int ProductCount { get; set; }
}
