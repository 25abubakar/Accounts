using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Persists the legacy payroll breakdown without reverting the normalized
/// PayrollRuns/PayrollLines design. Existing historical totals are preserved.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260915130000_AddLegacyPayrollBreakdownColumns")]
public sealed class AddLegacyPayrollBreakdownColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ContractId",
            table: "PayrollLines",
            type: "int",
            nullable: true);

        foreach (var column in new[]
        {
            "MedicalAllowanceAmount",
            "NightAllowanceAmount",
            "TelephoneAllowanceAmount",
            "TransportAllowanceAmount"
        })
        {
            migrationBuilder.AddColumn<decimal>(
                name: column,
                table: "PayrollLines",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        // Contract is a historical snapshot. Resolve existing rows where the
        // saved ContractType matches the tenant's master Name or Code.
        migrationBuilder.Sql(
            """
            UPDATE payroll
            SET ContractId = contractType.Id
            FROM dbo.PayrollLines payroll
            INNER JOIN PlatformTypes.ContractTypes contractType
                ON contractType.TenantId = payroll.TenantId
               AND (UPPER(contractType.Name) = UPPER(payroll.ContractType)
                    OR UPPER(contractType.Code) = UPPER(payroll.ContractType))
            WHERE payroll.ContractId IS NULL
              AND payroll.ContractType IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ContractId", table: "PayrollLines");
        migrationBuilder.DropColumn(name: "MedicalAllowanceAmount", table: "PayrollLines");
        migrationBuilder.DropColumn(name: "NightAllowanceAmount", table: "PayrollLines");
        migrationBuilder.DropColumn(name: "TelephoneAllowanceAmount", table: "PayrollLines");
        migrationBuilder.DropColumn(name: "TransportAllowanceAmount", table: "PayrollLines");
    }
}
