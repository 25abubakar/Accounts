using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleRoznamchaProductDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "SaleRoznamchaProducts",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryListProcedure));
            migrationBuilder.Sql(ExecutableBatch(WithoutPurchaseAccounts(
                global::Accounts.Data.SaleRoznamchaSql.InventoryProductCreateProcedure)));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryProductUpdateProcedure));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ExecutableBatch(InventoryListWithoutProductDescription()));
            migrationBuilder.Sql(ExecutableBatch(InventoryCreateWithoutProductDescription()));
            migrationBuilder.Sql(ExecutableBatch(InventoryUpdateWithoutProductDescription()));

            migrationBuilder.DropColumn(
                name: "Description",
                table: "SaleRoznamchaProducts");
        }

        private static string InventoryListWithoutProductDescription() =>
            global::Accounts.Data.SaleRoznamchaSql.InventoryListProcedure
                .Replace("                   product.Description,\r\n", string.Empty, StringComparison.Ordinal)
                .Replace("                   product.Description,\n", string.Empty, StringComparison.Ordinal);

        private static string InventoryCreateWithoutProductDescription() =>
            WithoutPurchaseAccounts(global::Accounts.Data.SaleRoznamchaSql.InventoryProductCreateProcedure)
                .Replace("ProductCode, Name, Description, QuantityOnHand", "ProductCode, Name, QuantityOnHand", StringComparison.Ordinal)
                .Replace("@ResolvedCode, @ProductName,\r\n                     NULLIF(LTRIM(RTRIM(@Description)), N''), @Quantity", "@ResolvedCode, @ProductName, @Quantity", StringComparison.Ordinal)
                .Replace("@ResolvedCode, @ProductName,\n                     NULLIF(LTRIM(RTRIM(@Description)), N''), @Quantity", "@ResolvedCode, @ProductName, @Quantity", StringComparison.Ordinal);

        private static string InventoryUpdateWithoutProductDescription() =>
            global::Accounts.Data.SaleRoznamchaSql.InventoryProductUpdateProcedure
                .Replace("            @Description nvarchar(1000) = NULL,\r\n", string.Empty, StringComparison.Ordinal)
                .Replace("            @Description nvarchar(1000) = NULL,\n", string.Empty, StringComparison.Ordinal)
                .Replace("                Description = NULLIF(LTRIM(RTRIM(@Description)), N''),\r\n", string.Empty, StringComparison.Ordinal)
                .Replace("                Description = NULLIF(LTRIM(RTRIM(@Description)), N''),\n", string.Empty, StringComparison.Ordinal);

        private static string WithoutPurchaseAccounts(string sql)
        {
            var normalized = sql.Replace("\r\n", "\n", StringComparison.Ordinal);
            normalized = System.Text.RegularExpressions.Regex.Replace(
                normalized,
                @"(?m)^\s*@(From|To)AccountId int = NULL,\n",
                string.Empty);
            normalized = System.Text.RegularExpressions.Regex.Replace(
                normalized,
                @"(?ms)^\s*IF @FromAccountId IS NOT NULL AND @FromAccountId = @ToAccountId.*?^\s*THROW 51000, 'Selected To Account was not found or is inactive\.', 1;\n",
                string.Empty);

            return normalized
                .Replace("FromAccountId, ToAccountId, ", string.Empty, StringComparison.Ordinal)
                .Replace("@FromAccountId, @ToAccountId,\n", string.Empty, StringComparison.Ordinal);
        }

        private static string ExecutableBatch(string sql) => $"EXEC(N'{sql.Replace("'", "''")}');";
    }
}
