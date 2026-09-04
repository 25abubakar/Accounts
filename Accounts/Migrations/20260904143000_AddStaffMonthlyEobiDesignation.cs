using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260904143000_AddStaffMonthlyEobiDesignation")]
public sealed class AddStaffMonthlyEobiDesignation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH(N'dbo.StaffMonthlyEobis', N'Designation') IS NULL
                ALTER TABLE dbo.StaffMonthlyEobis ADD Designation nvarchar(200) NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH(N'dbo.StaffMonthlyEobis', N'Designation') IS NOT NULL
                ALTER TABLE dbo.StaffMonthlyEobis DROP COLUMN Designation;
            """);
    }
}
