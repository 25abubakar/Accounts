using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AllowSignedSaleRoznamchaPurchaseAmounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryStockAddProcedure));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ExecutableBatch(
                global::Accounts.Data.SaleRoznamchaSql.InventoryStockAddProcedure.Replace(
                    "            IF @Quantity < 0 THROW 51000, 'Quantity cannot be negative.', 1;\n",
                    "            IF @Quantity < 0 THROW 51000, 'Quantity cannot be negative.', 1;\n" +
                    "            IF @UnitCost < 0 THROW 51000, 'Purchase price cannot be negative.', 1;\n" +
                    "            IF @TotalPurchaseCost < 0 THROW 51000, 'Debit cannot be negative.', 1;\n" +
                    "            IF @Quantity = 0 AND @TotalPurchaseCost <= 0\n" +
                    "                THROW 51000, 'A zero-quantity charge or adjustment must have a Debit amount greater than zero.', 1;\n" +
                    "            IF @UsdAmount < 0 THROW 51000, 'UsdAmt cannot be negative.', 1;\n",
                    StringComparison.Ordinal)));
        }

        private static string ExecutableBatch(string sql) => $"EXEC(N'{sql.Replace("'", "''")}');";
    }
}
