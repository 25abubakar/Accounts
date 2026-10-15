using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261013143000_AddSaleRoznamchaPurchaseCategoryAndDate")]
public sealed class AddSaleRoznamchaPurchaseCategoryAndDate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "FromCategoryId",
            table: "SaleRoznamchaStockReceipts",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "PurchasedDate",
            table: "SaleRoznamchaStockReceipts",
            type: "date",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE receipt
            SET PurchasedDate = CONVERT(date, receipt.PurchasedOnUtc),
                FromCategoryId = account.CategoryId
            FROM dbo.SaleRoznamchaStockReceipts receipt
            LEFT JOIN dbo.AccountsChartAccounts account
              ON account.TenantId = receipt.TenantId
             AND account.Id = receipt.FromAccountId
            WHERE receipt.PurchasedDate IS NULL;
            """);

        migrationBuilder.AlterColumn<DateOnly>(
            name: "PurchasedDate",
            table: "SaleRoznamchaStockReceipts",
            type: "date",
            nullable: false,
            oldClrType: typeof(DateOnly),
            oldType: "date",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_SaleRoznamchaStockReceipts_FromCategoryId",
            table: "SaleRoznamchaStockReceipts",
            column: "FromCategoryId");

        migrationBuilder.AddForeignKey(
            name: "FK_SaleRoznamchaStockReceipts_FromCategory",
            table: "SaleRoznamchaStockReceipts",
            column: "FromCategoryId",
            principalTable: "AccountsCategories",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryStockAddProcedure));
        migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryPurchaseListProcedure));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ExecutableBatch(WithoutPurchaseCategoryAndDate(global::Accounts.Data.SaleRoznamchaSql.InventoryStockAddProcedure)));
        migrationBuilder.Sql(ExecutableBatch(PurchaseListWithoutCategoryAndDate()));

        migrationBuilder.DropForeignKey(
            name: "FK_SaleRoznamchaStockReceipts_FromCategory",
            table: "SaleRoznamchaStockReceipts");

        migrationBuilder.DropIndex(
            name: "IX_SaleRoznamchaStockReceipts_FromCategoryId",
            table: "SaleRoznamchaStockReceipts");

        migrationBuilder.DropColumn(
            name: "FromCategoryId",
            table: "SaleRoznamchaStockReceipts");

        migrationBuilder.DropColumn(
            name: "PurchasedDate",
            table: "SaleRoznamchaStockReceipts");
    }

    private static string WithoutPurchaseCategoryAndDate(string sql)
    {
        var normalized = sql.Replace("\r\n", "\n", StringComparison.Ordinal);
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"(?m)^\s*@FromCategoryId int = NULL,\n", string.Empty);
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"(?m)^\s*@PurchasedDate date = NULL,\n", string.Empty);
        normalized = System.Text.RegularExpressions.Regex.Replace(
            normalized,
            @"(?ms)^\s*IF @FromCategoryId IS NOT NULL AND NOT EXISTS.*?^\s*THROW 51000, 'Selected From Category was not found or is inactive\.', 1;\n",
            string.Empty);
        normalized = System.Text.RegularExpressions.Regex.Replace(
            normalized,
            @"(?ms)^\s*IF @FromCategoryId IS NOT NULL AND @FromAccountId IS NOT NULL AND NOT EXISTS.*?^\s*THROW 51000, 'Selected From Account does not belong to the selected From Category\.', 1;\n",
            string.Empty);
        return normalized
            .Replace("FromCategoryId, FromAccountId, ToAccountId, PurchasedDate, Description,", "FromAccountId, ToAccountId, Description,", StringComparison.Ordinal)
            .Replace("COALESCE(@FromCategoryId, (SELECT CategoryId FROM dbo.AccountsChartAccounts WHERE TenantId = @TenantId AND Id = @FromAccountId)),\n                            @FromAccountId, @ToAccountId, COALESCE(@PurchasedDate, CONVERT(date, SYSUTCDATETIME())),", "@FromAccountId, @ToAccountId,", StringComparison.Ordinal);
    }

    private static string PurchaseListWithoutCategoryAndDate()
    {
        var sql = global::Accounts.Data.SaleRoznamchaSql.InventoryPurchaseListProcedure.Replace("\r\n", "\n", StringComparison.Ordinal);
        return sql
            .Replace("                   COALESCE(receipt.FromCategoryId, fromAccount.CategoryId) FromCategoryId,\n                   fromCategory.Code FromCategoryCode,\n                   fromCategory.Name FromCategoryName,\n", string.Empty, StringComparison.Ordinal)
            .Replace("                   receipt.PurchasedDate,\n", string.Empty, StringComparison.Ordinal)
            .Replace("            LEFT JOIN dbo.AccountsCategories fromCategory\n                ON fromCategory.TenantId = receipt.TenantId\n               AND fromCategory.Id = COALESCE(receipt.FromCategoryId, fromAccount.CategoryId)\n", string.Empty, StringComparison.Ordinal);
    }

    private static string ExecutableBatch(string sql) => $"EXEC(N'{sql.Replace("'", "''")}');";
}
