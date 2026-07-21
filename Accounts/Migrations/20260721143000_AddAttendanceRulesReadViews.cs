using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260721143000_AddAttendanceRulesReadViews")]
public sealed class AddAttendanceRulesReadViews : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR ALTER VIEW dbo.vw_AttendanceMapRules
            AS
            SELECT
                mapRule.TenantId,
                mapRule.Id,
                mapRule.StaffId,
                mapRule.AttendanceEntryTypeId,
                entryType.Code AS AttendanceTypeCode,
                entryType.Name AS AttendanceTypeName,
                mapRule.ShiftCode,
                COALESCE(shiftLookup.DisplayText, mapRule.ShiftCode) AS ShiftName,
                mapRule.TimeFrom,
                mapRule.TimeTo,
                mapRule.IsOpenAttendance
            FROM dbo.AttendanceMapRules AS mapRule
            INNER JOIN dbo.AttendanceEntryTypes AS entryType
                ON entryType.Id = mapRule.AttendanceEntryTypeId
            LEFT JOIN dbo.AppLookupTypes AS shiftType
                ON shiftType.LookupTypeCode = N'ATTENDANCE_SHIFT'
               AND shiftType.IsActive = 1
            LEFT JOIN dbo.AppLookupValues AS shiftLookup
                ON shiftLookup.LookupTypeId = shiftType.LookupTypeId
               AND shiftLookup.ValueCode = mapRule.ShiftCode
               AND shiftLookup.IsActive = 1;
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER VIEW dbo.vw_AttendanceHolidayColorMaps
            AS
            SELECT
                colorMap.TenantId,
                colorMap.Id,
                colorMap.HolidayTypeCode,
                COALESCE(holidayType.DisplayText, colorMap.HolidayTypeCode) AS HolidayTypeName,
                colorMap.ColorCode
            FROM dbo.AttendanceHolidayColorMaps AS colorMap
            LEFT JOIN dbo.AppLookupTypes AS timingType
                ON timingType.LookupTypeCode = N'TIMING_HOLIDAY_TYPE'
               AND timingType.IsActive = 1
            LEFT JOIN dbo.AppLookupValues AS holidayType
                ON holidayType.LookupTypeId = timingType.LookupTypeId
               AND holidayType.ValueCode = colorMap.HolidayTypeCode
               AND holidayType.IsActive = 1;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_AttendanceHolidayColorMaps;");
        migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_AttendanceMapRules;");
    }
}
