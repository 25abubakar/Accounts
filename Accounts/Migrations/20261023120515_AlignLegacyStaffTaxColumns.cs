using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AlignLegacyStaffTaxColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "TaxAdjustment",
                table: "PayrollStaffTaxes",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AddColumn<long>(
                name: "LegacyStaffTaxId",
                table: "PayrollStaffTaxes",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql(StaffTaxListSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM dbo.PayrollStaffTaxes WHERE TaxAdjustment IS NULL OR LegacyStaffTaxId IS NOT NULL) THROW 51000, 'Preserve imported source IDs and blank adjustments before reverting this migration.', 1;");
            migrationBuilder.Sql(PreviousStaffTaxListSql);

            migrationBuilder.DropColumn(
                name: "LegacyStaffTaxId",
                table: "PayrollStaffTaxes");

            migrationBuilder.AlterColumn<decimal>(
                name: "TaxAdjustment",
                table: "PayrollStaffTaxes",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);
        }
    }
}
