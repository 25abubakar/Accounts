using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260717190000_ConfigureDailyAndStaffAttendanceMenus")]
public sealed class ConfigureDailyAndStaffAttendanceMenus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @ParentId int = (
                SELECT TOP (1) [Id] FROM [Menus]
                WHERE [Title] = N'Attendance Portal' AND [ParentId] IS NULL
                ORDER BY [Id]
            );
            DECLARE @DailyMenuId int = (
                SELECT TOP (1) [Id] FROM [Menus]
                WHERE [Route] = N'/attendance/daily-report'
                ORDER BY [Id]
            );
            DECLARE @TeamMenuId int = (
                SELECT TOP (1) [Id] FROM [Menus]
                WHERE [Route] = N'/attendance/team'
                ORDER BY [Id]
            );
            DECLARE @StaffMenuId int = (
                SELECT TOP (1) [Id] FROM [Menus]
                WHERE [Route] = N'/attendance/staff'
                ORDER BY [Id]
            );

            -- If an earlier deployment consolidated Daily into Team, restore that
            -- same menu row as Daily so all of its existing permissions remain intact.
            IF @DailyMenuId IS NULL AND @TeamMenuId IS NOT NULL AND @StaffMenuId IS NULL
            BEGIN
                SET @DailyMenuId = @TeamMenuId;
                SET @TeamMenuId = NULL;
            END;

            IF @DailyMenuId IS NULL AND @ParentId IS NOT NULL
            BEGIN
                INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Daily Attendance Report', N'CalendarDays', N'/attendance/daily-report', @ParentId, 2, 1);
                SET @DailyMenuId = SCOPE_IDENTITY();
            END;

            IF @DailyMenuId IS NOT NULL
            BEGIN
                UPDATE [Menus]
                SET [Title] = N'Daily Attendance Report', [Icon] = N'CalendarDays',
                    [Route] = N'/attendance/daily-report', [SortOrder] = 2, [IsActive] = 1
                WHERE [Id] = @DailyMenuId;
            END;

            -- The former Team Attendance placeholder becomes the self-only Staff Attendance module.
            IF @StaffMenuId IS NULL AND @TeamMenuId IS NOT NULL
            BEGIN
                SET @StaffMenuId = @TeamMenuId;
                UPDATE [Menus]
                SET [Title] = N'Staff Attendance', [Icon] = N'UserRoundCheck',
                    [Route] = N'/attendance/staff', [SortOrder] = 3, [IsActive] = 1
                WHERE [Id] = @StaffMenuId;
            END;

            IF @StaffMenuId IS NULL AND @ParentId IS NOT NULL
            BEGIN
                INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Staff Attendance', N'UserRoundCheck', N'/attendance/staff', @ParentId, 3, 1);
                SET @StaffMenuId = SCOPE_IDENTITY();
            END;

            IF @StaffMenuId IS NOT NULL
            BEGIN
                UPDATE [Menus]
                SET [Title] = N'Staff Attendance', [Icon] = N'UserRoundCheck',
                    [Route] = N'/attendance/staff', [SortOrder] = 3, [IsActive] = 1
                WHERE [Id] = @StaffMenuId;

                -- Staff Attendance is a self-service screen. Make it available to
                -- every current staff profile; row visibility remains enforced by
                -- the token-bound API and is never derived from these menu grants.
                UPDATE [StaffMenuAccess]
                SET [IsAllow] = 1
                WHERE [MenuId] = @StaffMenuId;

                INSERT INTO [StaffMenuAccess]
                    ([StaffId], [MenuId], [IsAllow], [GrantedBy], [GrantedDate])
                SELECT sv.[StaffId], @StaffMenuId, 1, N'System: Staff Attendance', SYSUTCDATETIME()
                FROM [StaffVacancy] sv
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [StaffMenuAccess] sma
                    WHERE sma.[StaffId] = sv.[StaffId]
                      AND sma.[MenuId] = @StaffMenuId
                );

                -- Tenant administrators receive view-only menu delegation. An
                -- employee profile is still required before attendance can load.
                UPDATE [TenantMenuPermissions]
                SET [IsAllow] = 1, [CanView] = 1,
                    [CanAdd] = 0, [CanEdit] = 0, [CanDelete] = 0
                WHERE [MenuId] = @StaffMenuId;

                INSERT INTO [TenantMenuPermissions]
                    ([TenantId], [MenuId], [IsAllow], [CanView], [CanAdd], [CanEdit], [CanDelete],
                     [GrantedOnUtc], [GrantedByUserId])
                SELECT t.[Id], @StaffMenuId, 1, 1, 0, 0, 0,
                       SYSUTCDATETIME(), N'System: Staff Attendance'
                FROM [Tenants] t
                WHERE t.[IsActive] = 1
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [TenantMenuPermissions] tmp
                      WHERE tmp.[TenantId] = t.[Id]
                        AND tmp.[MenuId] = @StaffMenuId
                  );
            END;

            -- No active Team Attendance module remains after this migration.
            UPDATE [Menus]
            SET [IsActive] = 0, [Route] = NULL
            WHERE [Route] = N'/attendance/team';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE [Menus]
            SET [Title] = N'Team Attendance', [Icon] = N'Users',
                [Route] = N'/attendance/team', [SortOrder] = 3, [IsActive] = 1
            WHERE [Route] = N'/attendance/staff';
            """);
    }
}
