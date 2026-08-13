using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class MigrateAttendancePlatformSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                -- 1. Create the Action if it does not exist
                DECLARE @TenantId INT;
                DECLARE @ActionId INT;

                DECLARE tenant_cursor CURSOR FOR SELECT Id FROM dbo.Tenants;
                OPEN tenant_cursor;
                FETCH NEXT FROM tenant_cursor INTO @TenantId;

                WHILE @@FETCH_STATUS = 0
                BEGIN
                    -- Find or Create 'Attendance' action
                    SELECT @ActionId = Id FROM PlatformSettings.Actions WHERE TenantId = @TenantId AND Name = N'Attendance';
                    IF @ActionId IS NULL
                    BEGIN
                        INSERT INTO PlatformSettings.Actions (TenantId, Name, IsActive, CreatedOnUtc)
                        VALUES (@TenantId, N'Attendance', 1, SYSUTCDATETIME());
                        SET @ActionId = SCOPE_IDENTITY();
                    END

                    -- Migrate Colors
                    INSERT INTO PlatformSettings.Colors (TenantId, ColorCode, FontColor, CreatedOnUtc)
                    SELECT DISTINCT pss.TenantId, cs.ColorCode, cs.FontColor, SYSUTCDATETIME()
                    FROM dbo.ProcessStatusStyles pss
                    JOIN dbo.Processes p ON p.Id = pss.ProcessId
                    JOIN dbo.ColorStyles cs ON cs.Id = pss.ColorStyleId
                    WHERE p.ProcessName = N'Attendance' AND pss.TenantId = @TenantId
                      AND NOT EXISTS (SELECT 1 FROM PlatformSettings.Colors c WHERE c.TenantId = @TenantId AND c.ColorCode = cs.ColorCode);

                    -- Migrate Statuses
                    INSERT INTO PlatformSettings.Statuses (TenantId, Name, IsActive, CreatedOnUtc)
                    SELECT DISTINCT pss.TenantId, s.StatusName, 1, SYSUTCDATETIME()
                    FROM dbo.ProcessStatusStyles pss
                    JOIN dbo.Processes p ON p.Id = pss.ProcessId
                    JOIN dbo.Statuses s ON s.Id = pss.StatusId
                    WHERE p.ProcessName = N'Attendance' AND pss.TenantId = @TenantId
                      AND NOT EXISTS (SELECT 1 FROM PlatformSettings.Statuses s2 WHERE s2.TenantId = @TenantId AND s2.Name = s.StatusName);

                    -- Migrate StatusCrDbValues (for the underlying code mapping)
                    INSERT INTO PlatformSettings.StatusCrDbValues (TenantId, StatusId, CrValue, DbValue, CreatedOnUtc)
                    SELECT DISTINCT pss.TenantId, snew.Id, pss.Code, pss.Code, SYSUTCDATETIME()
                    FROM dbo.ProcessStatusStyles pss
                    JOIN dbo.Processes p ON p.Id = pss.ProcessId
                    JOIN dbo.Statuses s ON s.Id = pss.StatusId
                    JOIN PlatformSettings.Statuses snew ON snew.TenantId = pss.TenantId AND snew.Name = s.StatusName
                    WHERE p.ProcessName = N'Attendance' AND pss.TenantId = @TenantId
                      AND NOT EXISTS (SELECT 1 FROM PlatformSettings.StatusCrDbValues scv WHERE scv.TenantId = @TenantId AND scv.DbValue = pss.Code);

                    -- Link everything in ActionStatuses
                    INSERT INTO PlatformSettings.ActionStatuses (TenantId, ActionId, StatusId, ColorId, CreatedOnUtc)
                    SELECT DISTINCT pss.TenantId, @ActionId, snew.Id, cnew.Id, SYSUTCDATETIME()
                    FROM dbo.ProcessStatusStyles pss
                    JOIN dbo.Processes p ON p.Id = pss.ProcessId
                    JOIN dbo.Statuses s ON s.Id = pss.StatusId
                    JOIN dbo.ColorStyles cs ON cs.Id = pss.ColorStyleId
                    JOIN PlatformSettings.Statuses snew ON snew.TenantId = pss.TenantId AND snew.Name = s.StatusName
                    JOIN PlatformSettings.Colors cnew ON cnew.TenantId = pss.TenantId AND cnew.ColorCode = cs.ColorCode
                    WHERE p.ProcessName = N'Attendance' AND pss.TenantId = @TenantId
                      AND NOT EXISTS (SELECT 1 FROM PlatformSettings.ActionStatuses act 
                                      WHERE act.TenantId = @TenantId AND act.ActionId = @ActionId AND act.StatusId = snew.Id);

                    FETCH NEXT FROM tenant_cursor INTO @TenantId;
                END
                CLOSE tenant_cursor;
                DEALLOCATE tenant_cursor;
            ");

        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
