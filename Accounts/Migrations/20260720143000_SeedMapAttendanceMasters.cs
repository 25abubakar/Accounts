using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260720143000_SeedMapAttendanceMasters")]
public sealed class SeedMapAttendanceMasters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @AttendanceTypes TABLE
            (
                Code nvarchar(30) NOT NULL,
                Name nvarchar(100) NOT NULL
            );

            INSERT @AttendanceTypes (Code, Name) VALUES
                (N'LOGIN', N'Login'),
                (N'CHECK', N'Check in/Out'),
                (N'MACHINE', N'Machine'),
                (N'CAMERA', N'Camera'),
                (N'STAFF_GUARD', N'Staff(Gurad)'),
                (N'REMOTE', N'Remote'),
                (N'SYSTEM_IP', N'System(IP)'),
                (N'NONE', N'Not Required'),
                (N'BY_SUPERVISOR', N'By Supervisor');

            MERGE dbo.AttendanceEntryTypes AS target
            USING @AttendanceTypes AS source ON target.Code = source.Code
            WHEN MATCHED THEN UPDATE SET Name = source.Name, IsActive = 1
            WHEN NOT MATCHED THEN INSERT (Code, Name, IsActive)
                VALUES (source.Code, source.Name, 1);

            UPDATE entryType
            SET IsActive = 0
            FROM dbo.AttendanceEntryTypes entryType
            WHERE NOT EXISTS (
                SELECT 1 FROM @AttendanceTypes configured
                WHERE configured.Code = entryType.Code
            );

            DECLARE @ShiftTypeId int = (
                SELECT TOP (1) LookupTypeId
                FROM dbo.AppLookupTypes
                WHERE LookupTypeCode = N'ATTENDANCE_SHIFT'
            );

            IF @ShiftTypeId IS NULL
            BEGIN
                INSERT dbo.AppLookupTypes
                    (LookupTypeCode, LookupTypeName, IsActive, CreatedOn)
                VALUES
                    (N'ATTENDANCE_SHIFT', N'Attendance Shift', 1, SYSUTCDATETIME());
                SET @ShiftTypeId = SCOPE_IDENTITY();
            END
            ELSE
            BEGIN
                UPDATE dbo.AppLookupTypes
                SET LookupTypeName = N'Attendance Shift', IsActive = 1
                WHERE LookupTypeId = @ShiftTypeId;
            END;

            DECLARE @Shifts TABLE
            (
                ValueCode nvarchar(100) NOT NULL,
                DisplayText nvarchar(150) NOT NULL,
                SortOrder int NOT NULL
            );

            INSERT @Shifts (ValueCode, DisplayText, SortOrder) VALUES
                (N'MORNING', N'Morning', 10),
                (N'EVENING', N'Evening', 20),
                (N'NIGHT', N'Night', 30),
                (N'SPECIAL', N'Special', 40),
                (N'EXTRA', N'Extra', 50);

            MERGE dbo.AppLookupValues AS target
            USING @Shifts AS source
               ON target.LookupTypeId = @ShiftTypeId
              AND target.ValueCode = source.ValueCode
            WHEN MATCHED THEN UPDATE SET
                DisplayText = source.DisplayText,
                SortOrder = source.SortOrder,
                IsDefault = CASE WHEN source.ValueCode = N'MORNING' THEN 1 ELSE 0 END,
                IsActive = 1
            WHEN NOT MATCHED THEN INSERT
                (LookupTypeId, ValueCode, DisplayText, SortOrder, IsDefault, IsActive, CreatedOn)
            VALUES
                (@ShiftTypeId, source.ValueCode, source.DisplayText, source.SortOrder,
                 CASE WHEN source.ValueCode = N'MORNING' THEN 1 ELSE 0 END,
                 1, SYSUTCDATETIME());

            UPDATE lookupValue
            SET IsActive = 0
            FROM dbo.AppLookupValues lookupValue
            WHERE lookupValue.LookupTypeId = @ShiftTypeId
              AND NOT EXISTS (
                  SELECT 1 FROM @Shifts configured
                  WHERE configured.ValueCode = lookupValue.ValueCode
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE dbo.AttendanceEntryTypes SET IsActive = 0
            WHERE Code IN (N'LOGIN', N'MACHINE', N'CAMERA', N'STAFF_GUARD', N'REMOTE', N'SYSTEM_IP', N'BY_SUPERVISOR');
            UPDATE dbo.AttendanceEntryTypes SET Name = N'Check In / Out', IsActive = 1 WHERE Code = N'CHECK';
            UPDATE dbo.AttendanceEntryTypes SET Name = N'No attendance', IsActive = 1 WHERE Code = N'NONE';
            UPDATE dbo.AttendanceEntryTypes SET IsActive = 1 WHERE Code = N'MANUAL';

            UPDATE dbo.AppLookupTypes SET IsActive = 0
            WHERE LookupTypeCode = N'ATTENDANCE_SHIFT';
            """);
    }
}
