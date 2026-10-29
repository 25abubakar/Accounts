using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261029110000_AddAttendancePendingReviewStatus")]
public sealed class AddAttendancePendingReviewStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DECLARE @processId int =
                (SELECT Id FROM PlatformSettings.Processes WHERE Name = N'Attendance');
            IF @processId IS NULL
                THROW 51000, 'Attendance process configuration is missing.', 1;

            IF NOT EXISTS (
                SELECT 1 FROM PlatformSettings.Statuses WITH (UPDLOCK, HOLDLOCK)
                WHERE Name = N'Pending Review')
            BEGIN
                INSERT PlatformSettings.Statuses(Name, IsActive, CreatedOnUtc)
                VALUES(N'Pending Review', 1, SYSUTCDATETIME());
            END;

            DECLARE @statusId int =
                (SELECT Id FROM PlatformSettings.Statuses WHERE Name = N'Pending Review');
            DECLARE @colorId int =
                (SELECT TOP (1) Id FROM PlatformSettings.Colors
                 WHERE ColorCode = N'#f7f305' ORDER BY Id);

            IF @colorId IS NULL
                THROW 51000, 'The configured pending attendance color is missing.', 1;

            IF NOT EXISTS (
                SELECT 1 FROM PlatformSettings.ProcessStatuses WITH (UPDLOCK, HOLDLOCK)
                WHERE ProcessId = @processId AND StatusId = @statusId)
            BEGIN
                INSERT PlatformSettings.ProcessStatuses(ProcessId, StatusId, ColorId, CreatedOnUtc)
                VALUES(@processId, @statusId, @colorId, SYSUTCDATETIME());
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DECLARE @processId int =
                (SELECT Id FROM PlatformSettings.Processes WHERE Name = N'Attendance');
            DECLARE @statusId int =
                (SELECT Id FROM PlatformSettings.Statuses WHERE Name = N'Pending Review');

            DELETE PlatformSettings.ProcessStatuses
            WHERE ProcessId = @processId AND StatusId = @statusId;

            IF @statusId IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM dbo.AttendanceRecords WHERE CanonicalStatusId = @statusId)
               AND NOT EXISTS (SELECT 1 FROM PlatformSettings.ProcessStatuses WHERE StatusId = @statusId)
               AND NOT EXISTS (SELECT 1 FROM PlatformSettings.ActionStatuses WHERE StatusId = @statusId)
            BEGIN
                DELETE PlatformSettings.Statuses WHERE Id = @statusId;
            END;
            """);
    }
}
