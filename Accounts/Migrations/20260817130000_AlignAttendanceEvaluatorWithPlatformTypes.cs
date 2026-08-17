using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Keeps the status evaluator on the same tenant-scoped attendance-type source
/// used by mapping, rules and attendance reporting. Without this repair the
/// evaluator cannot see mappings whose ids belong to PlatformTypes and therefore
/// never creates the automatic Absent record after the configured shift window.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260817130000_AlignAttendanceEvaluatorWithPlatformTypes")]
public sealed class AlignAttendanceEvaluatorWithPlatformTypes : Migration
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
                N'CREATE PROC dbo.usp_Attendance_EvaluateStatuses',
                N'CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EvaluateStatuses');
            SET @Definition = REPLACE(@Definition,
                N'CREATE   PROC dbo.usp_Attendance_EvaluateStatuses',
                N'CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EvaluateStatuses');

            -- AttendanceMapRules now stores PlatformTypes.AttendanceTypes ids.
            SET @Definition = REPLACE(@Definition,
                N'JOIN dbo.AttendanceEntryTypes entryType',
                N'JOIN PlatformTypes.AttendanceTypes entryType');

            IF @Definition NOT LIKE N'%entryType.TenantId = mapRule.TenantId%'
            BEGIN
                SET @Definition = REPLACE(@Definition,
                    N'ON entryType.Id = mapRule.AttendanceEntryTypeId AND entryType.IsActive = 1',
                    N'ON entryType.Id = mapRule.AttendanceEntryTypeId AND entryType.TenantId = mapRule.TenantId AND entryType.IsActive = 1');
                SET @Definition = REPLACE(@Definition,
                    N'ON entryType.Id = mapRule.AttendanceEntryTypeId
                     AND entryType.IsActive = 1',
                    N'ON entryType.Id = mapRule.AttendanceEntryTypeId
                     AND entryType.TenantId = mapRule.TenantId
                     AND entryType.IsActive = 1');
            END;

            -- NOT_REQUIRED is the current platform code; NONE is retained only
            -- so databases upgraded from the legacy master remain compatible.
            SET @Definition = REPLACE(@Definition,
                N'effective.AttendanceTypeCode <> N''NONE''',
                N'effective.AttendanceTypeCode NOT IN (N''NONE'', N''NOT_REQUIRED'')');

            SET @Definition = REPLACE(@Definition,
                N'AND effective.PriorMonthlyAbsentCount >= effective.AdjustAbsentDays',
                N'');

            IF @Definition LIKE N'%JOIN dbo.AttendanceEntryTypes entryType%'
               OR @Definition NOT LIKE N'%JOIN PlatformTypes.AttendanceTypes entryType%'
               OR @Definition NOT LIKE N'%entryType.TenantId = mapRule.TenantId%'
                THROW 51000, 'Attendance evaluator could not be aligned with PlatformTypes.AttendanceTypes.', 1;

            EXEC sys.sp_executesql @Definition;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The platform attendance-type source is authoritative; reverting this
        // procedure to the removed legacy source would corrupt evaluation.
    }
}
