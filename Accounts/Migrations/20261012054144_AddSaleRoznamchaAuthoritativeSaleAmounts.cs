using Microsoft.EntityFrameworkCore.Migrations;

using Accounts.Data;
using System.Text.RegularExpressions;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleRoznamchaAuthoritativeSaleAmounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RtoRefundAmount",
                table: "SaleRoznamchaDailySales",
                newName: "RtoChargeAmount");

            migrationBuilder.AddColumn<decimal>(
                name: "UnitSalePrice",
                table: "SaleRoznamchaDailySales",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseSaleAmount",
                table: "SaleRoznamchaDailySales",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NetSaleTotal",
                table: "SaleRoznamchaDailySales",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PayChargeEffectId",
                table: "SaleRoznamchaDailySales",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SaleCurrencyId",
                table: "SaleRoznamchaDailySales",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalAdditions",
                table: "SaleRoznamchaDailySales",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalDeductions",
                table: "SaleRoznamchaDailySales",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "SaleRoznamchaAmountEffects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Multiplier = table.Column<short>(type: "smallint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleRoznamchaAmountEffects", x => x.Id);
                    table.UniqueConstraint("AK_SaleRoznamchaAmountEffects_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_SaleRoznamchaAmountEffects_Multiplier", "[Multiplier] IN (-1, 1)");
                    table.ForeignKey(
                        name: "FK_SaleRoznamchaAmountEffects_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO dbo.SaleRoznamchaAmountEffects (TenantId, Code, Name, Multiplier, IsActive)
                SELECT tenant.Id, seed.Code, seed.Name, seed.Multiplier, 1
                FROM dbo.Tenants tenant
                CROSS JOIN (VALUES
                    (N'CUSTOMER_ADDITION', N'Customer Addition', CAST(1 AS smallint)),
                    (N'SELLER_DEDUCTION', N'Seller Deduction', CAST(-1 AS smallint))
                ) seed(Code, Name, Multiplier)
                WHERE NOT EXISTS
                (
                    SELECT 1 FROM dbo.SaleRoznamchaAmountEffects existing
                    WHERE existing.TenantId = tenant.Id AND existing.Code = seed.Code
                );

                UPDATE sale
                SET sale.SaleCurrencyId = currencyChoice.Id,
                    sale.PayChargeEffectId = effectChoice.Id,
                    sale.UnitSalePrice = CASE WHEN sale.Quantity > 0 THEN ROUND(sale.GrossTotal / sale.Quantity, 2) ELSE 0 END,
                    sale.BaseSaleAmount = sale.GrossTotal,
                    sale.TotalAdditions = ROUND(sale.DeliveryFee + sale.OtherAmount + COALESCE(chargeTotals.Amount, 0), 2),
                    sale.TotalDeductions = ROUND(sale.PromotionAmount + sale.RtoChargeAmount + sale.PayCharges, 2),
                    sale.NetSaleTotal = CASE
                        WHEN sale.GrossTotal + sale.DeliveryFee + sale.OtherAmount + COALESCE(chargeTotals.Amount, 0)
                             - sale.PromotionAmount - sale.RtoChargeAmount - sale.PayCharges < 0 THEN 0
                        ELSE ROUND(sale.GrossTotal + sale.DeliveryFee + sale.OtherAmount + COALESCE(chargeTotals.Amount, 0)
                             - sale.PromotionAmount - sale.RtoChargeAmount - sale.PayCharges, 2)
                    END,
                    sale.TotalRevenue = CASE
                        WHEN sale.GrossTotal + sale.DeliveryFee + sale.OtherAmount + COALESCE(chargeTotals.Amount, 0)
                             - sale.PromotionAmount - sale.RtoChargeAmount - sale.PayCharges < 0 THEN 0
                        ELSE ROUND(sale.GrossTotal + sale.DeliveryFee + sale.OtherAmount + COALESCE(chargeTotals.Amount, 0)
                             - sale.PromotionAmount - sale.RtoChargeAmount - sale.PayCharges, 2)
                    END
                FROM dbo.SaleRoznamchaDailySales sale
                INNER JOIN dbo.SaleRoznamchaProducts product
                    ON product.TenantId = sale.TenantId AND product.Id = sale.ProductId
                OUTER APPLY
                (
                    SELECT TOP (1) currency.Id
                    FROM dbo.AccountsCurrencies currency
                    WHERE currency.IsActive = 1
                      AND (currency.TenantId IS NULL OR currency.TenantId = sale.TenantId)
                    ORDER BY CASE
                        WHEN currency.Id = sale.UnitCostCurrencyId THEN 0
                        WHEN currency.Id = product.CurrencyId THEN 1
                        ELSE 2 END,
                        currency.Id
                ) currencyChoice
                OUTER APPLY
                (
                    SELECT TOP (1) effect.Id
                    FROM dbo.SaleRoznamchaAmountEffects effect
                    WHERE effect.TenantId = sale.TenantId AND effect.Code = N'SELLER_DEDUCTION'
                ) effectChoice
                OUTER APPLY
                (
                    SELECT SUM(charge.Amount) Amount
                    FROM dbo.SaleRoznamchaDailySaleCharges charge
                    WHERE charge.TenantId = sale.TenantId AND charge.DailySaleId = sale.Id
                ) chargeTotals;

                IF EXISTS
                (
                    SELECT 1 FROM dbo.SaleRoznamchaDailySales
                    WHERE SaleCurrencyId IS NULL OR PayChargeEffectId IS NULL
                ) THROW 51000, 'Daily-sale migration could not resolve currency or pay-charge treatment.', 1;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "PayChargeEffectId",
                table: "SaleRoznamchaDailySales",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SaleCurrencyId",
                table: "SaleRoznamchaDailySales",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaDailySales_SaleCurrencyId",
                table: "SaleRoznamchaDailySales",
                column: "SaleCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaDailySales_TenantId_PayChargeEffectId",
                table: "SaleRoznamchaDailySales",
                columns: new[] { "TenantId", "PayChargeEffectId" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaAmountEffects_TenantId_Code",
                table: "SaleRoznamchaAmountEffects",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleRoznamchaDailySales_AccountsCurrencies_SaleCurrencyId",
                table: "SaleRoznamchaDailySales",
                column: "SaleCurrencyId",
                principalTable: "AccountsCurrencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleRoznamchaDailySales_SaleRoznamchaAmountEffects_TenantId_PayChargeEffectId",
                table: "SaleRoznamchaDailySales",
                columns: new[] { "TenantId", "PayChargeEffectId" },
                principalTable: "SaleRoznamchaAmountEffects",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(ExecutableBatch(SaleRoznamchaSql.DailySaleListProcedure));
            migrationBuilder.Sql(ExecutableBatch(SaleRoznamchaSql.DailySaleCreateProcedure));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_DailySaleList;");
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_DailySaleCreate;");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleRoznamchaDailySales_AccountsCurrencies_SaleCurrencyId",
                table: "SaleRoznamchaDailySales");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleRoznamchaDailySales_SaleRoznamchaAmountEffects_TenantId_PayChargeEffectId",
                table: "SaleRoznamchaDailySales");

            migrationBuilder.DropTable(
                name: "SaleRoznamchaAmountEffects");

            migrationBuilder.DropIndex(
                name: "IX_SaleRoznamchaDailySales_SaleCurrencyId",
                table: "SaleRoznamchaDailySales");

            migrationBuilder.DropIndex(
                name: "IX_SaleRoznamchaDailySales_TenantId_PayChargeEffectId",
                table: "SaleRoznamchaDailySales");

            migrationBuilder.DropColumn(
                name: "BaseSaleAmount",
                table: "SaleRoznamchaDailySales");

            migrationBuilder.DropColumn(
                name: "NetSaleTotal",
                table: "SaleRoznamchaDailySales");

            migrationBuilder.DropColumn(
                name: "PayChargeEffectId",
                table: "SaleRoznamchaDailySales");

            migrationBuilder.DropColumn(
                name: "SaleCurrencyId",
                table: "SaleRoznamchaDailySales");

            migrationBuilder.DropColumn(
                name: "TotalAdditions",
                table: "SaleRoznamchaDailySales");

            migrationBuilder.DropColumn(
                name: "TotalDeductions",
                table: "SaleRoznamchaDailySales");

            migrationBuilder.DropColumn(
                name: "UnitSalePrice",
                table: "SaleRoznamchaDailySales");

            migrationBuilder.RenameColumn(
                name: "RtoChargeAmount",
                table: "SaleRoznamchaDailySales",
                newName: "RtoRefundAmount");

            migrationBuilder.Sql(ExecutableBatch(BuildLegacyDailySaleListProcedure()));
            migrationBuilder.Sql(ExecutableBatch(BuildLegacyDailySaleCreateProcedure()));
        }

        private static string ExecutableBatch(string sql) => $"EXEC(N'{sql.Replace("'", "''")}');";

        private static string LegacyDailySaleListProcedure() => SaleRoznamchaSql.DailySaleListProcedure
            .Replace(
                """
                       sale.SaleCurrencyId, saleCurrency.Code SaleCurrencyCode,
                       sale.PayChargeEffectId, payChargeEffect.Name PayChargeEffectName,
                       payChargeEffect.Multiplier PayChargeMultiplier,
                       sale.UnitSalePrice, sale.BaseSaleAmount, sale.TotalAdditions,
                       sale.TotalDeductions, sale.NetSaleTotal,
                """,
                string.Empty)
            .Replace(
                """
                    INNER JOIN dbo.AccountsCurrencies saleCurrency
                        ON saleCurrency.Id = sale.SaleCurrencyId
                    INNER JOIN dbo.SaleRoznamchaAmountEffects payChargeEffect
                        ON payChargeEffect.TenantId = sale.TenantId AND payChargeEffect.Id = sale.PayChargeEffectId
                """,
                string.Empty)
            .Replace("RtoChargeAmount", "RtoRefundAmount");

        private static string BuildLegacyDailySaleListProcedure()
        {
            var sql = SaleRoznamchaSql.DailySaleListProcedure;
            sql = Regex.Replace(sql,
                @"\s*sale\.SaleCurrencyId, saleCurrency\.Code SaleCurrencyCode,\s*sale\.PayChargeEffectId, payChargeEffect\.Name PayChargeEffectName,\s*payChargeEffect\.Multiplier PayChargeMultiplier,\s*sale\.UnitSalePrice, sale\.BaseSaleAmount, sale\.TotalAdditions,\s*sale\.TotalDeductions, sale\.NetSaleTotal,",
                string.Empty);
            sql = Regex.Replace(sql,
                @"\s*INNER JOIN dbo\.AccountsCurrencies saleCurrency\s*ON saleCurrency\.Id = sale\.SaleCurrencyId\s*INNER JOIN dbo\.SaleRoznamchaAmountEffects payChargeEffect\s*ON payChargeEffect\.TenantId = sale\.TenantId AND payChargeEffect\.Id = sale\.PayChargeEffectId",
                string.Empty);
            return sql.Replace("RtoChargeAmount", "RtoRefundAmount");
        }

        private static string BuildLegacyDailySaleCreateProcedure()
        {
            var sql = SaleRoznamchaSql.DailySaleCreateProcedure;
            sql = Regex.Replace(sql,
                @"@UnitSalePrice decimal\(18,2\),\s*@SaleCurrencyId int,\s*@PayChargeEffectId int,",
                "@GrossTotal decimal(18,2) = 0,\r\n        @TotalRevenue decimal(18,2) = 0,\r\n        @CostAmount decimal(18,2) = 0,");
            sql = Regex.Replace(sql,
                @"IF @UnitSalePrice <= 0 OR @PayCharges < 0\s*OR @DeliveryFee < 0 OR @OtherAmount < 0 OR @PromotionAmount < 0 OR @RtoChargeAmount < 0",
                "IF @GrossTotal < 0 OR @TotalRevenue < 0 OR @CostAmount < 0 OR @PayCharges < 0\r\n           OR @DeliveryFee < 0 OR @OtherAmount < 0 OR @PromotionAmount < 0 OR @RtoChargeAmount < 0");
            sql = Regex.Replace(sql,
                @"@UnitCost decimal\(18,2\), @UnitCostCurrencyId int,\s*@PayChargeMultiplier smallint, @AdditionalChargeTotal decimal\(18,2\),\s*@BaseSaleAmount decimal\(18,2\), @TotalAdditions decimal\(18,2\),\s*@TotalDeductions decimal\(18,2\), @NetSaleTotal decimal\(18,2\);",
                "@UnitCost decimal(18,2), @UnitCostCurrencyId int;");
            sql = Regex.Replace(sql,
                @"\s*IF NOT EXISTS\s*\(SELECT 1 FROM dbo\.AccountsCurrencies WHERE Id = @SaleCurrencyId.*?IF @NetSaleTotal < 0 THROW 51000, 'Net sale total cannot be negative\.', 1;",
                string.Empty,
                RegexOptions.Singleline);
            sql = Regex.Replace(sql,
                @"\s*SaleCurrencyId, PayChargeEffectId, UnitSalePrice, BaseSaleAmount,\s*TotalAdditions, TotalDeductions, NetSaleTotal,",
                string.Empty);
            sql = Regex.Replace(sql,
                @"\s*@SaleCurrencyId, @PayChargeEffectId, @UnitSalePrice, @BaseSaleAmount,\s*@TotalAdditions, @TotalDeductions, @NetSaleTotal,\s*@BaseSaleAmount, @NetSaleTotal, 0, @PayCharges, @DeliveryFee, @OtherAmount,",
                "\r\n                     @GrossTotal, @TotalRevenue, @CostAmount, @PayCharges, @DeliveryFee, @OtherAmount,");
            return sql.Replace("RtoChargeAmount", "RtoRefundAmount");
        }

        private static string LegacyDailySaleCreateProcedure() => SaleRoznamchaSql.DailySaleCreateProcedure
            .Replace(
                """
                        @UnitSalePrice decimal(18,2),
                        @SaleCurrencyId int,
                        @PayChargeEffectId int,
                """,
                """
                        @GrossTotal decimal(18,2) = 0,
                        @TotalRevenue decimal(18,2) = 0,
                        @CostAmount decimal(18,2) = 0,
                """)
            .Replace(
                """
                    IF @UnitSalePrice <= 0 OR @PayCharges < 0
                       OR @DeliveryFee < 0 OR @OtherAmount < 0 OR @PromotionAmount < 0 OR @RtoChargeAmount < 0
                """,
                """
                    IF @GrossTotal < 0 OR @TotalRevenue < 0 OR @CostAmount < 0 OR @PayCharges < 0
                       OR @DeliveryFee < 0 OR @OtherAmount < 0 OR @PromotionAmount < 0 OR @RtoChargeAmount < 0
                """)
            .Replace(
                """
                            @UnitCost decimal(18,2), @UnitCostCurrencyId int,
                            @PayChargeMultiplier smallint, @AdditionalChargeTotal decimal(18,2),
                            @BaseSaleAmount decimal(18,2), @TotalAdditions decimal(18,2),
                            @TotalDeductions decimal(18,2), @NetSaleTotal decimal(18,2);
                """,
                """
                            @UnitCost decimal(18,2), @UnitCostCurrencyId int;
                """)
            .Replace(
                """
                    IF NOT EXISTS
                       (SELECT 1 FROM dbo.AccountsCurrencies WHERE Id = @SaleCurrencyId AND IsActive = 1
                        AND (TenantId IS NULL OR TenantId = @TenantId))
                        THROW 51000, 'Selected sale currency is invalid or inactive.', 1;

                    SELECT @PayChargeMultiplier = Multiplier
                    FROM dbo.SaleRoznamchaAmountEffects
                    WHERE TenantId = @TenantId AND Id = @PayChargeEffectId AND IsActive = 1;
                    IF @PayChargeMultiplier NOT IN (-1, 1)
                        THROW 51000, 'Selected Pay Charge treatment is invalid or inactive.', 1;

                    SELECT @AdditionalChargeTotal = COALESCE(SUM(Amount), 0) FROM @Charges;
                    SET @BaseSaleAmount = ROUND(@Quantity * @UnitSalePrice, 2);
                    SET @TotalAdditions = ROUND(@DeliveryFee + @OtherAmount + @AdditionalChargeTotal
                                              + CASE WHEN @PayChargeMultiplier = 1 THEN @PayCharges ELSE 0 END, 2);
                    SET @TotalDeductions = ROUND(@PromotionAmount + @RtoChargeAmount
                                               + CASE WHEN @PayChargeMultiplier = -1 THEN @PayCharges ELSE 0 END, 2);
                    SET @NetSaleTotal = ROUND(@BaseSaleAmount + @TotalAdditions - @TotalDeductions, 2);
                    IF @NetSaleTotal < 0 THROW 51000, 'Net sale total cannot be negative.', 1;

                """,
                string.Empty)
            .Replace(
                """
                         SaleCurrencyId, PayChargeEffectId, UnitSalePrice, BaseSaleAmount,
                         TotalAdditions, TotalDeductions, NetSaleTotal,
                """,
                string.Empty)
            .Replace(
                """
                         @SaleCurrencyId, @PayChargeEffectId, @UnitSalePrice, @BaseSaleAmount,
                         @TotalAdditions, @TotalDeductions, @NetSaleTotal,
                         @BaseSaleAmount, @NetSaleTotal, 0, @PayCharges, @DeliveryFee, @OtherAmount,
                """,
                """
                         @GrossTotal, @TotalRevenue, @CostAmount, @PayCharges, @DeliveryFee, @OtherAmount,
                """)
            .Replace("RtoChargeAmount", "RtoRefundAmount");
    }
}
