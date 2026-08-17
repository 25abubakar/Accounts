using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Makes the configured absent-after window inclusive. For example, a 09:00
/// shift with a 120-minute window becomes Absent at 11:00, not at a later poll.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260817131000_UseInclusiveAttendanceAbsentDeadline")]
public sealed class UseInclusiveAttendanceAbsentDeadline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.usp_Attendance_EvaluateStatuses', N'P') IS NULL
                THROW 51000, 'Attendance status evaluator procedure was not found.', 1;

            DECLARE @Definition nvarchar(max) =
                OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_Attendance_EvaluateStatuses'));

            SET @Definition = REPLACE(@Definition,
                N'CREATE PROCEDURE dbo.usp_Attendance_EvaluateStatuses',
                N'CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EvaluateStatuses');
            SET @Definition = REPLACE(@Definition,
                N'CREATE   PROCEDURE dbo.usp_Attendance_EvaluateStatuses',
                N'CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EvaluateStatuses');

            SET @Definition = REPLACE(@Definition,
                N'AND @AsOfUtc > DATEADD(',
                N'AND @AsOfUtc >= DATEADD(');

            EXEC sys.sp_executesql @Definition;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Inclusive deadline is the intended attendance rule semantics.
    }
}
