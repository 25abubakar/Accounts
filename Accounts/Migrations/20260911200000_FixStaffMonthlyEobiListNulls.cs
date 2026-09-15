using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260911200000_FixStaffMonthlyEobiListNulls")]
public sealed class FixStaffMonthlyEobiListNulls : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.usp_Pay_StaffMonthlyEobi_List
    @TenantId INT,
    @Year INT,
    @Month INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        s.Id AS id,
        COALESCE(s.EobiRef, N'') AS eobiRef,
        s.StaffNumber AS staffId,
        s.FullName AS fullName,
        s.Department AS department,
        s.DateOfJoining AS doj,
        s.CompanyShare AS coyShare,
        s.StaffShare AS staffShare,
        s.TotalAmount AS totAmount,
        s.Remarks AS remarks,
        s.IsApproved AS isApproved,
        s.IsPaid AS isPaid
    FROM dbo.StaffMonthlyEobis s
    WHERE s.TenantId = @TenantId
      AND s.Year = @Year
      AND s.Month = @Month
    ORDER BY s.FullName;
END");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.usp_Pay_StaffMonthlyEobi_List
    @TenantId INT,
    @Year INT,
    @Month INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        s.Id AS id,
        s.EobiRef AS eobiRef,
        s.StaffNumber AS staffId,
        s.FullName AS fullName,
        s.Department AS department,
        s.DateOfJoining AS doj,
        s.CompanyShare AS coyShare,
        s.StaffShare AS staffShare,
        s.TotalAmount AS totAmount,
        s.Remarks AS remarks,
        s.IsApproved AS isApproved,
        s.IsPaid AS isPaid
    FROM dbo.StaffMonthlyEobis s
    WHERE s.TenantId = @TenantId
      AND s.Year = @Year
      AND s.Month = @Month
    ORDER BY s.FullName;
END");
    }
}
