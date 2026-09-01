using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class UseScaleBasedBenefitReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollBenefitRules_TenantId_BenefitReference",
                table: "PayrollBenefitRules");

            migrationBuilder.Sql(
                """
                UPDATE dbo.PayrollBenefitRules
                SET BenefitReference = LEFT(
                    'B-' + COALESCE(
                        NULLIF(REPLACE(UPPER(LTRIM(RTRIM(Scale))), ' ', ''), ''),
                        'UNASSIGNED'),
                    30);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBenefitRules_TenantId_BenefitReference",
                table: "PayrollBenefitRules",
                columns: new[] { "TenantId", "BenefitReference" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollBenefitRules_TenantId_BenefitReference",
                table: "PayrollBenefitRules");

            migrationBuilder.Sql(
                """
                UPDATE dbo.PayrollBenefitRules
                SET BenefitReference = LEFT(
                    'B-' + COALESCE(
                        NULLIF(REPLACE(UPPER(LTRIM(RTRIM(Scale))), ' ', ''), ''),
                        'UNASSIGNED')
                    + '-' + CONVERT(varchar(11), Id),
                    30);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollBenefitRules_TenantId_BenefitReference",
                table: "PayrollBenefitRules",
                columns: new[] { "TenantId", "BenefitReference" },
                unique: true);
        }
    }
}
