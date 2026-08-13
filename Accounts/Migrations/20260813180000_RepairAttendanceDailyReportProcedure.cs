using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <inheritdoc />
public partial class RepairAttendanceDailyReportProcedure : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[AttendanceEntryTypes]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[dbo].[AttendanceEntryTypes]', N'Code') IS NULL
            BEGIN
                ALTER TABLE [dbo].[AttendanceEntryTypes] ADD [Code] nvarchar(30) NULL;
                UPDATE dbo.AttendanceEntryTypes
                   SET Code = N'CHECK'
                 WHERE Name IN (N'Check In / Out', N'Check In/Out', N'Check')
                    OR Name LIKE N'%Check%In%';
                UPDATE dbo.AttendanceEntryTypes
                   SET Code = N'NONE'
                 WHERE Name IN (N'No attendance', N'No Attendance')
                    OR Name LIKE N'%No attendance%';
                UPDATE dbo.AttendanceEntryTypes
                   SET Code = N'MANUAL'
                 WHERE Name LIKE N'%Manual%';
                UPDATE dbo.AttendanceEntryTypes
                   SET Code = CONCAT(N'TYPE_', Id)
                 WHERE Code IS NULL OR LTRIM(RTRIM(Code)) = N'';
                ALTER TABLE [dbo].[AttendanceEntryTypes] ALTER COLUMN [Code] nvarchar(30) NOT NULL;
            END;

            IF OBJECT_ID(N'[dbo].[AttendanceRecords]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[dbo].[AttendanceRecords]', N'PlatformActionStatusId') IS NULL
                ALTER TABLE [dbo].[AttendanceRecords] ADD [PlatformActionStatusId] int NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
