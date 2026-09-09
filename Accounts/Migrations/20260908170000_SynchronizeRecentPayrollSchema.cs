using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Synchronizes indexes that are part of the EF model but were omitted from the
/// earlier SQL-guarded payroll, EOBI, and salary-package migrations.
/// </summary>
public partial class SynchronizeRecentPayrollSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.PersonHrProfiles', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.PersonHrProfiles', N'SalaryPackageId') IS NOT NULL
               AND NOT EXISTS
               (
                   SELECT 1 FROM sys.indexes
                   WHERE object_id = OBJECT_ID(N'dbo.PersonHrProfiles')
                     AND name = N'IX_PersonHrProfiles_SalaryPackageId'
               )
                CREATE INDEX IX_PersonHrProfiles_SalaryPackageId
                    ON dbo.PersonHrProfiles (SalaryPackageId);

            IF OBJECT_ID(N'dbo.PayrollStaffTaxes', N'U') IS NOT NULL
               AND NOT EXISTS
               (
                   SELECT 1 FROM sys.indexes
                   WHERE object_id = OBJECT_ID(N'dbo.PayrollStaffTaxes')
                     AND name = N'IX_PayrollStaffTaxes_PersonId'
               )
                CREATE INDEX IX_PayrollStaffTaxes_PersonId
                    ON dbo.PayrollStaffTaxes (PersonId);

            IF OBJECT_ID(N'dbo.StaffMonthlyEobis', N'U') IS NOT NULL
               AND NOT EXISTS
               (
                   SELECT 1 FROM sys.indexes
                   WHERE object_id = OBJECT_ID(N'dbo.StaffMonthlyEobis')
                     AND name = N'IX_StaffMonthlyEobis_PersonId'
               )
                CREATE INDEX IX_StaffMonthlyEobis_PersonId
                    ON dbo.StaffMonthlyEobis (PersonId);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.PersonHrProfiles')
                  AND name = N'IX_PersonHrProfiles_SalaryPackageId'
            )
                DROP INDEX IX_PersonHrProfiles_SalaryPackageId ON dbo.PersonHrProfiles;

            IF EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.PayrollStaffTaxes')
                  AND name = N'IX_PayrollStaffTaxes_PersonId'
            )
                DROP INDEX IX_PayrollStaffTaxes_PersonId ON dbo.PayrollStaffTaxes;

            IF EXISTS
            (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.StaffMonthlyEobis')
                  AND name = N'IX_StaffMonthlyEobis_PersonId'
            )
                DROP INDEX IX_StaffMonthlyEobis_PersonId ON dbo.StaffMonthlyEobis;
            """);
    }
}
