using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleRoznamchaPurchaseEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "SaleRoznamchaStockReceipts",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.Sql(ExecutableBatch(WithoutPurchaseAccounts(
                global::Accounts.Data.SaleRoznamchaSql.InventoryProductCreateProcedure)));
            migrationBuilder.Sql(ExecutableBatch(WithoutPurchaseAccounts(
                global::Accounts.Data.SaleRoznamchaSql.InventoryStockAddProcedure)));
            migrationBuilder.Sql(ExecutableBatch(PurchaseListWithoutAccounts()));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_InventoryPurchaseList;");
            migrationBuilder.Sql(ExecutableBatch(WithoutReceiptDescription(WithoutPurchaseAccounts(
                global::Accounts.Data.SaleRoznamchaSql.InventoryProductCreateProcedure))));
            migrationBuilder.Sql(ExecutableBatch(WithoutReceiptDescription(WithoutPurchaseAccounts(
                global::Accounts.Data.SaleRoznamchaSql.InventoryStockAddProcedure))));

            migrationBuilder.DropColumn(
                name: "Description",
                table: "SaleRoznamchaStockReceipts");
        }

        private static string WithoutReceiptDescription(string sql) => sql
            .Replace(", Description,", ",", StringComparison.Ordinal)
            .Replace("NULLIF(LTRIM(RTRIM(@Description)), N''),", string.Empty, StringComparison.Ordinal);

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

        private static string PurchaseListWithoutAccounts()
        {
            var sql = global::Accounts.Data.SaleRoznamchaSql.InventoryPurchaseListProcedure
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            return sql
                .Replace("                   receipt.FromAccountId,\n                   COALESCE(fromAccount.AccountCode, fromAccount.AccountNumber) FromAccountCode,\n                   fromAccount.AccountName FromAccountName,\n                   receipt.ToAccountId,\n                   COALESCE(toAccount.AccountCode, toAccount.AccountNumber) ToAccountCode,\n                   toAccount.AccountName ToAccountName,\n", string.Empty, StringComparison.Ordinal)
                .Replace("            LEFT JOIN dbo.AccountsChartAccounts fromAccount\n                ON fromAccount.TenantId = receipt.TenantId AND fromAccount.Id = receipt.FromAccountId\n            LEFT JOIN dbo.AccountsChartAccounts toAccount\n                ON toAccount.TenantId = receipt.TenantId AND toAccount.Id = receipt.ToAccountId\n", string.Empty, StringComparison.Ordinal);
        }

        private static string ExecutableBatch(string sql) => $"EXEC(N'{sql.Replace("'", "''")}');";
    }
}
