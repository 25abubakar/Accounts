using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260720240000_EnforceRequiredWeekendSchedules")]
public sealed class EnforceRequiredWeekendSchedules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.EmployeeTimingSchedules', N'U') IS NOT NULL
            BEGIN
                IF OBJECT_ID(N'dbo.CK_EmployeeTimingSchedules_RequiredWeekend', N'C') IS NOT NULL
                    ALTER TABLE dbo.EmployeeTimingSchedules
                        DROP CONSTRAINT CK_EmployeeTimingSchedules_RequiredWeekend;

                -- 1900-01-01 was a Monday: modulo 5 is Saturday and modulo 6 is Sunday.
                UPDATE dbo.EmployeeTimingSchedules
                SET HolidayType = N'DAY_OFF',
                    IsOn = 0,
                    TimeFrom = NULL,
                    TimeTo = NULL,
                    ModifiedDate = SYSUTCDATETIME()
                WHERE ((DATEDIFF(day, '19000101', ScheduleDate) % 7 + 7) % 7) = 5;

                UPDATE dbo.EmployeeTimingSchedules
                SET HolidayType = N'HOLIDAY',
                    IsOn = 0,
                    TimeFrom = NULL,
                    TimeTo = NULL,
                    ModifiedDate = SYSUTCDATETIME()
                WHERE ((DATEDIFF(day, '19000101', ScheduleDate) % 7 + 7) % 7) = 6;

                ALTER TABLE dbo.EmployeeTimingSchedules WITH CHECK
                    ADD CONSTRAINT CK_EmployeeTimingSchedules_RequiredWeekend CHECK
                    (
                        ((DATEDIFF(day, '19000101', ScheduleDate) % 7 + 7) % 7) NOT IN (5, 6)
                        OR
                        (
                            ((DATEDIFF(day, '19000101', ScheduleDate) % 7 + 7) % 7) = 5
                            AND HolidayType = N'DAY_OFF'
                            AND IsOn = 0
                            AND TimeFrom IS NULL
                            AND TimeTo IS NULL
                        )
                        OR
                        (
                            ((DATEDIFF(day, '19000101', ScheduleDate) % 7 + 7) % 7) = 6
                            AND HolidayType = N'HOLIDAY'
                            AND IsOn = 0
                            AND TimeFrom IS NULL
                            AND TimeTo IS NULL
                        )
                    );
            END;
            """);

        // Keep database-generated attendance statuses aligned with the current
        // holiday lookup codes and treat an unsaved Sunday as Holiday by default.
        migrationBuilder.Sql(
            """
            DECLARE @Definition nvarchar(max), @OldCondition nvarchar(max), @NewCondition nvarchar(max), @ProcedureKeyword int;

            SET @Definition = OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_Attendance_EvaluateStatuses'));
            IF @Definition IS NOT NULL
            BEGIN
                SET @OldCondition = N'timing.HolidayType IN(N''PUBLIC_HOLIDAY'',N''COMPANY_HOLIDAY'')';
                SET @NewCondition = N'(timing.HolidayType IN(N''HOLIDAY'',N''ANNUAL_HOLIDAY'',N''PUBLIC_HOLIDAY'',N''COMPANY_HOLIDAY'') OR (timing.Id IS NULL AND ((DATEDIFF(day,''19000101'',attendance.AttendanceDate) % 7 + 7) % 7) = 6))';
                IF CHARINDEX(@OldCondition, @Definition) = 0
                    THROW 51020, 'Unable to update weekend rules in usp_Attendance_EvaluateStatuses.', 1;

                SET @Definition = REPLACE(@Definition, @OldCondition, @NewCondition);
                SET @ProcedureKeyword = CHARINDEX(N'PROCEDURE', UPPER(@Definition));
                IF @ProcedureKeyword = 0
                    THROW 51022, 'Unable to locate the procedure declaration for usp_Attendance_EvaluateStatuses.', 1;
                -- OBJECT_DEFINITION preserves the procedure's original spacing, so
                -- rebuild the declaration instead of relying on an exact CREATE match.
                SET @Definition = N'ALTER ' + SUBSTRING(@Definition, @ProcedureKeyword, LEN(@Definition));
                EXEC sys.sp_executesql @Definition;
            END;

            SET @Definition = OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_Attendance_DailyReport'));
            IF @Definition IS NOT NULL
            BEGIN
                SET @OldCondition = N'timing.HolidayType IN(N''PUBLIC_HOLIDAY'',N''COMPANY_HOLIDAY'')';
                SET @NewCondition = N'(timing.HolidayType IN(N''HOLIDAY'',N''ANNUAL_HOLIDAY'',N''PUBLIC_HOLIDAY'',N''COMPANY_HOLIDAY'') OR (timing.Id IS NULL AND ((DATEDIFF(day,''19000101'',dates.AttendanceDate) % 7 + 7) % 7) = 6))';
                IF CHARINDEX(@OldCondition, @Definition) = 0
                    THROW 51021, 'Unable to update weekend rules in usp_Attendance_DailyReport.', 1;

                SET @Definition = REPLACE(@Definition, @OldCondition, @NewCondition);
                SET @ProcedureKeyword = CHARINDEX(N'PROCEDURE', UPPER(@Definition));
                IF @ProcedureKeyword = 0
                    THROW 51023, 'Unable to locate the procedure declaration for usp_Attendance_DailyReport.', 1;
                SET @Definition = N'ALTER ' + SUBSTRING(@Definition, @ProcedureKeyword, LEN(@Definition));
                EXEC sys.sp_executesql @Definition;
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.CK_EmployeeTimingSchedules_RequiredWeekend', N'C') IS NOT NULL
                ALTER TABLE dbo.EmployeeTimingSchedules
                    DROP CONSTRAINT CK_EmployeeTimingSchedules_RequiredWeekend;
            """);
    }
}
