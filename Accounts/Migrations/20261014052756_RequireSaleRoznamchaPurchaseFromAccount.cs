using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class RequireSaleRoznamchaPurchaseFromAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM dbo.SaleRoznamchaStockReceipts WHERE FromAccountId IS NULL)
                    THROW 51000, 'From Account is required for every inventory purchase before the column can be made mandatory.', 1;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "FromAccountId",
                table: "SaleRoznamchaStockReceipts",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryStockAddProcedure));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "FromAccountId",
                table: "SaleRoznamchaStockReceipts",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.Sql(ExecutableBatch(
                global::Accounts.Data.SaleRoznamchaSql.InventoryStockAddProcedure.Replace(
                    "            @FromAccountId int,",
                    "            @FromAccountId int = NULL,",
                    StringComparison.Ordinal)));
        }

        private static string ExecutableBatch(string sql) => $"EXEC(N'{sql.Replace("'", "''")}');";
    }
}
