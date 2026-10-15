using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleRoznamchaPurchaseLedgerDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentContentType",
                table: "SaleRoznamchaStockReceipts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentOriginalName",
                table: "SaleRoznamchaStockReceipts",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AttachmentSize",
                table: "SaleRoznamchaStockReceipts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentStoredName",
                table: "SaleRoznamchaStockReceipts",
                type: "nvarchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "SaleRoznamchaStockReceipts",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "SaleRoznamchaStockReceipts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalPurchaseCost",
                table: "SaleRoznamchaStockReceipts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UsdAmount",
                table: "SaleRoznamchaStockReceipts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE dbo.SaleRoznamchaStockReceipts
                SET TotalPurchaseCost = CAST(PurchasedQuantity * UnitCost AS decimal(18,2));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaStockReceipts_TenantId_Reference",
                table: "SaleRoznamchaStockReceipts",
                columns: new[] { "TenantId", "Reference" });

            migrationBuilder.Sql(ExecutableBatch(StockAddBeforeDebitRename()));
            migrationBuilder.Sql(ExecutableBatch(PurchaseListBeforeDebitRename()));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ExecutableBatch(StockAddWithoutLedgerDetails()));
            migrationBuilder.Sql(ExecutableBatch(PurchaseListWithoutLedgerDetails()));

            migrationBuilder.DropIndex(
                name: "IX_SaleRoznamchaStockReceipts_TenantId_Reference",
                table: "SaleRoznamchaStockReceipts");

            migrationBuilder.DropColumn(
                name: "AttachmentContentType",
                table: "SaleRoznamchaStockReceipts");

            migrationBuilder.DropColumn(
                name: "AttachmentOriginalName",
                table: "SaleRoznamchaStockReceipts");

            migrationBuilder.DropColumn(
                name: "AttachmentSize",
                table: "SaleRoznamchaStockReceipts");

            migrationBuilder.DropColumn(
                name: "AttachmentStoredName",
                table: "SaleRoznamchaStockReceipts");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "SaleRoznamchaStockReceipts");

            migrationBuilder.DropColumn(
                name: "Reference",
                table: "SaleRoznamchaStockReceipts");

            migrationBuilder.DropColumn(
                name: "TotalPurchaseCost",
                table: "SaleRoznamchaStockReceipts");

            migrationBuilder.DropColumn(
                name: "UsdAmount",
                table: "SaleRoznamchaStockReceipts");
        }

        private static string StockAddWithoutLedgerDetails()
        {
            return StockAddBeforeDebitRename()
                .Replace("            @Reference nvarchar(100) = NULL,\n", string.Empty, StringComparison.Ordinal)
                .Replace("            @TotalPurchaseCost decimal(18,2),\n", string.Empty, StringComparison.Ordinal)
                .Replace("            @UsdAmount decimal(18,2) = NULL,\n", string.Empty, StringComparison.Ordinal)
                .Replace("            @ExchangeRate decimal(18,6) = NULL,\n", string.Empty, StringComparison.Ordinal)
                .Replace("            IF @Quantity < 0 THROW 51000, 'Quantity cannot be negative.', 1;\n", "            IF @Quantity <= 0 THROW 51000, 'Stock quantity must be greater than zero.', 1;\n", StringComparison.Ordinal)
                .Replace("            IF @TotalPurchaseCost < 0 THROW 51000, 'Debit cannot be negative.', 1;\n            IF @Quantity = 0 AND @TotalPurchaseCost <= 0\n                THROW 51000, 'A zero-quantity charge or adjustment must have a Debit amount greater than zero.', 1;\n            IF @UsdAmount < 0 THROW 51000, 'UsdAmt cannot be negative.', 1;\n            IF @ExchangeRate < 0 THROW 51000, 'Exchange rate cannot be negative.', 1;\n", string.Empty, StringComparison.Ordinal)
                .Replace("                    (TenantId, ProductId, Reference, PurchasedQuantity, RemainingQuantity, UnitCost,\n                     TotalPurchaseCost, UsdAmount, ExchangeRate, CurrencyId,\n", "                    (TenantId, ProductId, PurchasedQuantity, RemainingQuantity, UnitCost, CurrencyId,\n", StringComparison.Ordinal)
                .Replace("                VALUES (@TenantId, @ProductId, NULLIF(LTRIM(RTRIM(@Reference)), N''), @Quantity, @Quantity, @UnitCost,\n                        @TotalPurchaseCost, @UsdAmount, @ExchangeRate, @CurrencyId,\n", "                VALUES (@TenantId, @ProductId, @Quantity, @Quantity, @UnitCost, @CurrencyId,\n", StringComparison.Ordinal)
                .Replace("                IF @Quantity > 0\n                BEGIN\n                    UPDATE dbo.SaleRoznamchaProducts\n", "                UPDATE dbo.SaleRoznamchaProducts\n", StringComparison.Ordinal)
                .Replace("\n                    INSERT INTO dbo.SaleRoznamchaInventoryMovements\n                        (TenantId, ProductId, StockReceiptId, MovementType, QuantityDelta, BalanceAfter, CreatedByUserId, CreatedOnUtc)\n                    VALUES (@TenantId, @ProductId, @ReceiptId, N'PURCHASE', @Quantity, @BalanceAfter, @UserId, SYSUTCDATETIME());\n                END;", "\n\n                INSERT INTO dbo.SaleRoznamchaInventoryMovements\n                    (TenantId, ProductId, StockReceiptId, MovementType, QuantityDelta, BalanceAfter, CreatedByUserId, CreatedOnUtc)\n                VALUES (@TenantId, @ProductId, @ReceiptId, N'PURCHASE', @Quantity, @BalanceAfter, @UserId, SYSUTCDATETIME());", StringComparison.Ordinal);
        }

        private static string PurchaseListWithoutLedgerDetails()
        {
            return PurchaseListBeforeDebitRename()
                .Replace("                   receipt.Reference,\n", string.Empty, StringComparison.Ordinal)
                .Replace("                   receipt.TotalPurchaseCost,\n                   receipt.UsdAmount,\n                   receipt.ExchangeRate,\n", "                   CAST(receipt.PurchasedQuantity * receipt.UnitCost AS decimal(38,2)) TotalPurchaseCost,\n", StringComparison.Ordinal)
                .Replace("                   CONVERT(bit, CASE WHEN receipt.AttachmentStoredName IS NULL THEN 0 ELSE 1 END) HasAttachment,\n                   receipt.AttachmentOriginalName AttachmentName,\n", string.Empty, StringComparison.Ordinal)
                .Replace("                       WHEN receipt.PurchasedQuantity = 0 THEN N'Charge / Adjustment'\n", string.Empty, StringComparison.Ordinal);
        }

        private static string StockAddBeforeDebitRename() =>
            global::Accounts.Data.SaleRoznamchaSql.InventoryStockAddProcedure
                .Replace("                     Debit, UsdAmt, ExchangeRate, CurrencyId,\n",
                         "                     TotalPurchaseCost, UsdAmount, ExchangeRate, CurrencyId,\n",
                         StringComparison.Ordinal);

        private static string PurchaseListBeforeDebitRename() =>
            global::Accounts.Data.SaleRoznamchaSql.InventoryPurchaseListProcedure
                .Replace("                   receipt.Debit TotalPurchaseCost,\n",
                         "                   receipt.TotalPurchaseCost,\n",
                         StringComparison.Ordinal)
                .Replace("                   receipt.UsdAmt UsdAmount,\n",
                         "                   receipt.UsdAmount,\n",
                         StringComparison.Ordinal);

        private static string ExecutableBatch(string sql) => $"EXEC(N'{sql.Replace("'", "''")}');";
    }
}
