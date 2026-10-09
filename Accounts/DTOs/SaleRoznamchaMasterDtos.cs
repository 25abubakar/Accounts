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
    public bool IsActive { get; set; } = true;
}

public sealed class AddSaleRoznamchaStockRequest
{
    public decimal Quantity { get; set; }
    public decimal? PurchasePrice { get; set; }
    public int? CurrencyId { get; set; }
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
    public short InventoryEffect { get; set; }
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
    public decimal Quantity { get; set; }
    public decimal InventoryDelta { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public DateTime CreatedOnUtc { get; set; }
}

public sealed class CreateSaleRoznamchaDailySaleRequest
{
    public int CategoryId { get; set; }
    public int CompanyId { get; set; }
    public int PlatformId { get; set; }
    public int ProductCategoryId { get; set; }
    public long ProductId { get; set; }
    public int SaleStatusId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class SaleRoznamchaCreatedId
{
    public long Id { get; set; }
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
