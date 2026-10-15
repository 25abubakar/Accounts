using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleRoznamchaInventoryPurchaseAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FromAccountId",
                table: "SaleRoznamchaStockReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToAccountId",
                table: "SaleRoznamchaStockReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaStockReceipts_FromAccountId",
                table: "SaleRoznamchaStockReceipts",
                column: "FromAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaStockReceipts_ToAccountId",
                table: "SaleRoznamchaStockReceipts",
                column: "ToAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_SaleRoznamchaStockReceipts_FromAccount",
                table: "SaleRoznamchaStockReceipts",
                column: "FromAccountId",
                principalTable: "AccountsChartAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleRoznamchaStockReceipts_ToAccount",
                table: "SaleRoznamchaStockReceipts",
                column: "ToAccountId",
                principalTable: "AccountsChartAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryProductCreateProcedure));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryStockAddProcedure));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryPurchaseListProcedure));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ExecutableBatch(WithoutPurchaseAccounts(global::Accounts.Data.SaleRoznamchaSql.InventoryProductCreateProcedure)));
            migrationBuilder.Sql(ExecutableBatch(WithoutPurchaseAccounts(global::Accounts.Data.SaleRoznamchaSql.InventoryStockAddProcedure)));
            migrationBuilder.Sql(ExecutableBatch(PurchaseListWithoutAccounts()));

            migrationBuilder.DropForeignKey(
                name: "FK_SaleRoznamchaStockReceipts_FromAccount",
                table: "SaleRoznamchaStockReceipts");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleRoznamchaStockReceipts_ToAccount",
                table: "SaleRoznamchaStockReceipts");

            migrationBuilder.DropIndex(
                name: "IX_SaleRoznamchaStockReceipts_FromAccountId",
                table: "SaleRoznamchaStockReceipts");

            migrationBuilder.DropIndex(
                name: "IX_SaleRoznamchaStockReceipts_ToAccountId",
                table: "SaleRoznamchaStockReceipts");

            migrationBuilder.DropColumn(
                name: "FromAccountId",
                table: "SaleRoznamchaStockReceipts");

            migrationBuilder.DropColumn(
                name: "ToAccountId",
                table: "SaleRoznamchaStockReceipts");
        }

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
