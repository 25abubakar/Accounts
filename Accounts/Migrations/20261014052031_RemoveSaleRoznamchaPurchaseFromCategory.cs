using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSaleRoznamchaPurchaseFromCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ;WITH UniqueCategoryAccount AS
                (
                    SELECT TenantId, CategoryId, MIN(Id) AccountId
                    FROM dbo.AccountsChartAccounts
                    WHERE IsActive = 1 AND CategoryId IS NOT NULL
                    GROUP BY TenantId, CategoryId
                    HAVING COUNT_BIG(*) = 1
                )
                UPDATE receipt
                SET FromAccountId = candidate.AccountId
                FROM dbo.SaleRoznamchaStockReceipts receipt
                INNER JOIN UniqueCategoryAccount candidate
                    ON candidate.TenantId = receipt.TenantId
                   AND candidate.CategoryId = receipt.FromCategoryId
                WHERE receipt.FromAccountId IS NULL
                  AND receipt.FromCategoryId IS NOT NULL;

                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.SaleRoznamchaStockReceipts
                    WHERE FromAccountId IS NULL AND FromCategoryId IS NOT NULL
                )
                    THROW 51000, 'FromCategoryId cannot be removed because one or more purchase rows have no uniquely resolvable From Account.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_SaleRoznamchaStockReceipts_FromCategory",
                table: "SaleRoznamchaStockReceipts");

            migrationBuilder.DropIndex(
                name: "IX_SaleRoznamchaStockReceipts_FromCategoryId",
                table: "SaleRoznamchaStockReceipts");

            migrationBuilder.DropColumn(
                name: "FromCategoryId",
                table: "SaleRoznamchaStockReceipts");

            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.AccountHierarchyView));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryPurchaseDetailsView));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryStockAddProcedure));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryPurchaseListProcedure));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FromCategoryId",
                table: "SaleRoznamchaStockReceipts",
                type: "int",
                nullable: true);

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

            migrationBuilder.Sql(
                """
                UPDATE receipt
                SET FromCategoryId = account.CategoryId
                FROM dbo.SaleRoznamchaStockReceipts receipt
                INNER JOIN dbo.AccountsChartAccounts account
                    ON account.TenantId = receipt.TenantId
                   AND account.Id = receipt.FromAccountId;
                """);

            migrationBuilder.Sql(ExecutableBatch(LegacyInventoryStockAddProcedure()));
        }

        private static string LegacyInventoryStockAddProcedure() =>
            global::Accounts.Data.SaleRoznamchaSql.InventoryStockAddProcedure
                .Replace(
                    "            @CurrencyId int,\n            @FromAccountId int,",
                    "            @CurrencyId int,\n            @FromCategoryId int = NULL,\n            @FromAccountId int = NULL,",
                    StringComparison.Ordinal)
                .Replace(
                    "            IF @FromAccountId IS NOT NULL AND @FromAccountId = @ToAccountId",
                    "            IF @FromCategoryId IS NOT NULL AND NOT EXISTS\n" +
                    "               (SELECT 1 FROM dbo.AccountsCategories WHERE TenantId = @TenantId AND Id = @FromCategoryId AND IsActive = 1)\n" +
                    "                THROW 51000, 'Selected From Category was not found or is inactive.', 1;\n" +
                    "            IF @FromAccountId IS NOT NULL AND @FromAccountId = @ToAccountId",
                    StringComparison.Ordinal)
                .Replace(
                    "                     FromAccountId, ToAccountId, PurchasedDate, Description,",
                    "                     FromCategoryId, FromAccountId, ToAccountId, PurchasedDate, Description,",
                    StringComparison.Ordinal)
                .Replace(
                    "                        @FromAccountId, @ToAccountId, COALESCE(@PurchasedDate, CONVERT(date, SYSUTCDATETIME())),",
                    "                        COALESCE(@FromCategoryId, (SELECT CategoryId FROM dbo.AccountsChartAccounts WHERE TenantId = @TenantId AND Id = @FromAccountId)),\n" +
                    "                        @FromAccountId, @ToAccountId, COALESCE(@PurchasedDate, CONVERT(date, SYSUTCDATETIME())),",
                    StringComparison.Ordinal);

        private static string ExecutableBatch(string sql) => $"EXEC(N'{sql.Replace("'", "''")}');";
    }
}
