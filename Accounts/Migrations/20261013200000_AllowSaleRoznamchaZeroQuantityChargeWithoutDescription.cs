using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AllowSaleRoznamchaZeroQuantityChargeWithoutDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ExecutableBatch(StockAddBeforeDebitRename()));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ExecutableBatch(
                StockAddBeforeDebitRename().Replace(
                    "            IF @Quantity < 0 THROW 51000, 'Quantity cannot be negative.', 1;\n",
                    "            IF @Quantity < 0 THROW 51000, 'Quantity cannot be negative.', 1;\n" +
                    "            IF @Quantity = 0 AND NULLIF(LTRIM(RTRIM(@Description)), N'') IS NULL\n" +
                    "                THROW 51000, 'Description is required when quantity is zero.', 1;\n",
                    StringComparison.Ordinal)));
        }

        private static string StockAddBeforeDebitRename() =>
            global::Accounts.Data.SaleRoznamchaSql.InventoryStockAddProcedure
                .Replace("                     Debit, UsdAmt, ExchangeRate, CurrencyId,\n",
                         "                     TotalPurchaseCost, UsdAmount, ExchangeRate, CurrencyId,\n",
                         StringComparison.Ordinal);

        private static string ExecutableBatch(string sql) => $"EXEC(N'{sql.Replace("'", "''")}');";
    }
}
