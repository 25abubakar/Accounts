using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260817123000_FixAttendanceAbsentWindowNoMonthlyGate")]
public sealed class FixAttendanceAbsentWindowNoMonthlyGate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.usp_Attendance_EvaluateStatuses', N'P') IS NOT NULL
            BEGIN
                DECLARE @Definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_Attendance_EvaluateStatuses'));
                SET @Definition = REPLACE(@Definition, N'CREATE PROCEDURE dbo.usp_Attendance_EvaluateStatuses', N'CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EvaluateStatuses');
                SET @Definition = REPLACE(@Definition, N'CREATE   PROCEDURE dbo.usp_Attendance_EvaluateStatuses', N'CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EvaluateStatuses');
                SET @Definition = REPLACE(@Definition, N'CREATE PROC dbo.usp_Attendance_EvaluateStatuses', N'CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EvaluateStatuses');
                SET @Definition = REPLACE(@Definition, N'CREATE   PROC dbo.usp_Attendance_EvaluateStatuses', N'CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EvaluateStatuses');
                SET @Definition = REPLACE(@Definition, N'AND effective.PriorMonthlyAbsentCount >= effective.AdjustAbsentDays', N'');

                EXEC sp_executesql @Definition;
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
