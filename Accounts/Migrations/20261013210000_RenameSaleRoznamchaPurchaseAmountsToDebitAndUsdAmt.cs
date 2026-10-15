using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class RenameSaleRoznamchaPurchaseAmountsToDebitAndUsdAmt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TotalPurchaseCost",
                table: "SaleRoznamchaStockReceipts",
                newName: "Debit");

            migrationBuilder.RenameColumn(
                name: "UsdAmount",
                table: "SaleRoznamchaStockReceipts",
                newName: "UsdAmt");

            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryStockAddProcedure));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryPurchaseListProcedure));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Debit",
                table: "SaleRoznamchaStockReceipts",
                newName: "TotalPurchaseCost");

            migrationBuilder.RenameColumn(
                name: "UsdAmt",
                table: "SaleRoznamchaStockReceipts",
                newName: "UsdAmount");

            migrationBuilder.Sql(ExecutableBatch(StockAddBeforeDebitRename()));
            migrationBuilder.Sql(ExecutableBatch(PurchaseListBeforeDebitRename()));
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
