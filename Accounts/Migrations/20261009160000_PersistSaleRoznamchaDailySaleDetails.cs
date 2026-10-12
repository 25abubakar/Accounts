using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261009160000_PersistSaleRoznamchaDailySaleDetails")]
public sealed class PersistSaleRoznamchaDailySaleDetails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "UnitCost",
            table: "SaleRoznamchaDailySales",
            type: "decimal(18,2)",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<int>(
            name: "UnitCostCurrencyId",
            table: "SaleRoznamchaDailySales",
            type: "int",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE sale
            SET sale.UnitCost = product.PurchasePrice,
                sale.UnitCostCurrencyId = product.CurrencyId
            FROM dbo.SaleRoznamchaDailySales sale
            INNER JOIN dbo.SaleRoznamchaProducts product
                ON product.TenantId = sale.TenantId AND product.Id = sale.ProductId;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_SaleRoznamchaDailySales_UnitCostCurrencyId",
            table: "SaleRoznamchaDailySales",
            column: "UnitCostCurrencyId");

        migrationBuilder.AddForeignKey(
            name: "FK_SaleRoznamchaDailySales_AccountsCurrencies_UnitCostCurrencyId",
            table: "SaleRoznamchaDailySales",
            column: "UnitCostCurrencyId",
            principalTable: "AccountsCurrencies",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql(ExecutableBatch(SaleRoznamchaSql.DailySaleListProcedure));
        migrationBuilder.Sql(ExecutableBatch(SaleRoznamchaSql.DailySaleCreateProcedure));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ExecutableBatch(DailySaleListWithoutUnitCost()));
        migrationBuilder.Sql(ExecutableBatch(DailySaleCreateWithoutUnitCost()));

        migrationBuilder.DropForeignKey(
            name: "FK_SaleRoznamchaDailySales_AccountsCurrencies_UnitCostCurrencyId",
            table: "SaleRoznamchaDailySales");

        migrationBuilder.DropIndex(
            name: "IX_SaleRoznamchaDailySales_UnitCostCurrencyId",
            table: "SaleRoznamchaDailySales");

        migrationBuilder.DropColumn(name: "UnitCost", table: "SaleRoznamchaDailySales");
        migrationBuilder.DropColumn(name: "UnitCostCurrencyId", table: "SaleRoznamchaDailySales");
    }

    private static string DailySaleListWithoutUnitCost() => SaleRoznamchaSql.DailySaleListProcedure
        .Replace("                   sale.UnitCost, sale.UnitCostCurrencyId, unitCostCurrency.Code UnitCostCurrencyCode,\r\n", string.Empty)
        .Replace("                   sale.UnitCost, sale.UnitCostCurrencyId, unitCostCurrency.Code UnitCostCurrencyCode,\n", string.Empty)
        .Replace("            LEFT JOIN dbo.AccountsCurrencies unitCostCurrency\r\n                ON unitCostCurrency.Id = sale.UnitCostCurrencyId\r\n", string.Empty)
        .Replace("            LEFT JOIN dbo.AccountsCurrencies unitCostCurrency\n                ON unitCostCurrency.Id = sale.UnitCostCurrencyId\n", string.Empty);

    private static string DailySaleCreateWithoutUnitCost() => SaleRoznamchaSql.DailySaleCreateProcedure
        .Replace("                    @RequiredStock decimal(18,4), @AvailableBatchStock decimal(18,4),\r\n                    @UnitCost decimal(18,2), @UnitCostCurrencyId int;", "                    @RequiredStock decimal(18,4), @AvailableBatchStock decimal(18,4);")
        .Replace("                    @RequiredStock decimal(18,4), @AvailableBatchStock decimal(18,4),\n                    @UnitCost decimal(18,2), @UnitCostCurrencyId int;", "                    @RequiredStock decimal(18,4), @AvailableBatchStock decimal(18,4);")
        .Replace("                SELECT @CurrentBalance = QuantityOnHand,\r\n                       @UnitCost = PurchasePrice,\r\n                       @UnitCostCurrencyId = CurrencyId", "                SELECT @CurrentBalance = QuantityOnHand")
        .Replace("                SELECT @CurrentBalance = QuantityOnHand,\n                       @UnitCost = PurchasePrice,\n                       @UnitCostCurrencyId = CurrencyId", "                SELECT @CurrentBalance = QuantityOnHand")
        .Replace("                     UnitCost, UnitCostCurrencyId,\r\n", string.Empty)
        .Replace("                     UnitCost, UnitCostCurrencyId,\n", string.Empty)
        .Replace("                     @UnitCost, @UnitCostCurrencyId,\r\n", string.Empty)
        .Replace("                     @UnitCost, @UnitCostCurrencyId,\n", string.Empty);

    private static string ExecutableBatch(string sql) => $"EXEC(N'{sql.Replace("'", "''")}');";
}
