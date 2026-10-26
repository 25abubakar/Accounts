using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class PreserveBlankLegacyTaxRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollTaxSlabs_TenantId_TaxYear_FromAmount",
                table: "PayrollTaxSlabs");

            migrationBuilder.AlterColumn<decimal>(
                name: "FixedTaxAmount",
                table: "PayrollTaxSlabs",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollTaxSlabs_TenantId_TaxYear_FromAmount",
                table: "PayrollTaxSlabs",
                columns: new[] { "TenantId", "TaxYear", "FromAmount" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.Sql(TaxSlabListSql);
            migrationBuilder.Sql(NullableFixedTaxSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.PayrollTaxSlabs WHERE FixedTaxAmount IS NULL) OR EXISTS (SELECT 1 FROM dbo.PayrollTaxSlabs GROUP BY TenantId, TaxYear, FromAmount HAVING COUNT(*) > 1) THROW 51000, 'Preserve blank tax cells and historical bands before reverting this migration.', 1;");
            migrationBuilder.Sql(PreviousTaxSlabListSql);

            migrationBuilder.DropIndex(
                name: "IX_PayrollTaxSlabs_TenantId_TaxYear_FromAmount",
                table: "PayrollTaxSlabs");

            migrationBuilder.AlterColumn<decimal>(
                name: "FixedTaxAmount",
                table: "PayrollTaxSlabs",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollTaxSlabs_TenantId_TaxYear_FromAmount",
                table: "PayrollTaxSlabs",
                columns: new[] { "TenantId", "TaxYear", "FromAmount" },
                unique: true);
        }
    }
}
