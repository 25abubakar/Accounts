using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260729114500_RespectOpenAttendanceWhenEvaluatingStatuses")]
public sealed class RespectOpenAttendanceWhenEvaluatingStatuses : Migration
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

                IF CHARINDEX(N'WHEN ISNULL(mapRule.IsOpenAttendance,0)=1 AND attendance.CheckInUtc IS NOT NULL THEN @Present', @Definition) = 0
                BEGIN
                    SET @Definition = REPLACE(
                        @Definition,
                        N'UPDATE attendance SET AttendanceStatusId=CASE',
                        N'UPDATE attendance SET AttendanceStatusId=CASE
                    WHEN ISNULL(mapRule.IsOpenAttendance,0)=1 AND attendance.CheckInUtc IS NOT NULL THEN @Present');
                END;

                EXEC sp_executesql @Definition;
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
