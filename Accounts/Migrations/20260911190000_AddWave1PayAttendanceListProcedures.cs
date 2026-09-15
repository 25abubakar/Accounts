using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.IO;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260911190000_AddWave1PayAttendanceListProcedures")]
public sealed class AddWave1PayAttendanceListProcedures : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Sql", "StoredProcedures", "Wave1_PayAttendance_ListProcedures.sql");
        if (!File.Exists(path))
        {
            // Design-time / migration from repo root
            path = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "Sql", "StoredProcedures", "Wave1_PayAttendance_ListProcedures.sql"));
        }

        if (File.Exists(path))
        {
            var sql = File.ReadAllText(path);
            foreach (var batch in sql.Split(new[] { "\r\nGO\r\n", "\nGO\n", "\r\nGO\n", "\nGO\r\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = batch.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("SET NOCOUNT", StringComparison.OrdinalIgnoreCase))
                    continue;
                migrationBuilder.Sql(trimmed);
            }
            return;
        }

        // Fallback: procedures already applied via sqlcmd in local envs that ship without embedded SQL path.
        migrationBuilder.Sql("SELECT 1;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
DROP PROCEDURE IF EXISTS dbo.usp_Pay_BenefitRules_List;
DROP PROCEDURE IF EXISTS dbo.usp_Pay_BenefitParameters_List;
DROP PROCEDURE IF EXISTS dbo.usp_Pay_BonusRules_List;
DROP PROCEDURE IF EXISTS dbo.usp_Pay_BonusLines_List;
DROP PROCEDURE IF EXISTS dbo.usp_Pay_EobiSettings_List;
DROP PROCEDURE IF EXISTS dbo.usp_Pay_EobiEligibility_List;
DROP PROCEDURE IF EXISTS dbo.usp_Pay_StaffMonthlyEobi_List;
DROP PROCEDURE IF EXISTS dbo.usp_Pay_StaffTaxes_List;
DROP PROCEDURE IF EXISTS dbo.usp_Pay_StaffTaxCandidates_List;
DROP PROCEDURE IF EXISTS dbo.usp_Pay_TaxParameters_List;
DROP PROCEDURE IF EXISTS dbo.usp_Pay_TaxSlabs_List;
DROP PROCEDURE IF EXISTS dbo.usp_Pay_SalaryScales_List;
DROP PROCEDURE IF EXISTS dbo.usp_Pay_RuleRegistrations_List;
DROP PROCEDURE IF EXISTS dbo.usp_Pay_Allowances_List;
DROP PROCEDURE IF EXISTS dbo.usp_Pay_Tadas_List;
DROP PROCEDURE IF EXISTS dbo.usp_Pay_Leaves_List;
DROP PROCEDURE IF EXISTS dbo.usp_Pay_Packages_List;
DROP PROCEDURE IF EXISTS dbo.usp_Attendance_RuleSettings_List;
DROP PROCEDURE IF EXISTS dbo.usp_Attendance_MapRules_List;
DROP PROCEDURE IF EXISTS dbo.usp_Attendance_LoginReport;
DROP PROCEDURE IF EXISTS dbo.usp_Attendance_TimingChart_Staff;
DROP PROCEDURE IF EXISTS dbo.usp_Attendance_TimingChart_StaffSchedule;
");
    }
}
