using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260717200000_ConfigureMonthlyAttendanceChart")]
public sealed class ConfigureMonthlyAttendanceChart : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @ParentId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Title] = N'Attendance Portal' AND [ParentId] IS NULL
                ORDER BY [Id]
            );
            DECLARE @DailyMenuId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/daily-report'
                ORDER BY [Id]
            );
            DECLARE @MonthlyMenuId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/monthly-chart'
                ORDER BY [Id]
            );

            IF @MonthlyMenuId IS NULL AND @ParentId IS NOT NULL
            BEGIN
                INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Monthly Chart', N'CalendarRange', N'/attendance/monthly-chart', @ParentId, 4, 1);
                SET @MonthlyMenuId = SCOPE_IDENTITY();
            END;

            IF @MonthlyMenuId IS NOT NULL
            BEGIN
                UPDATE [Menus]
                SET [Title] = N'Monthly Chart', [Icon] = N'CalendarRange',
                    [Route] = N'/attendance/monthly-chart', [ParentId] = COALESCE(@ParentId, [ParentId]),
                    [SortOrder] = 4, [IsActive] = 1
                WHERE [Id] = @MonthlyMenuId;
            END;

            -- Monthly Chart follows the same organizational visibility policy as
            -- Daily Attendance, so copy the current Daily menu audience without
            -- changing any existing explicit Monthly Chart decisions.
            IF @DailyMenuId IS NOT NULL AND @MonthlyMenuId IS NOT NULL
            BEGIN
                INSERT INTO [StaffMenuAccess]
                    ([StaffId], [MenuId], [IsAllow], [GrantedBy], [GrantedDate])
                SELECT dailyAccess.[StaffId], @MonthlyMenuId, 1,
                       N'System: Monthly Chart', SYSUTCDATETIME()
                FROM [StaffMenuAccess] dailyAccess
                WHERE dailyAccess.[MenuId] = @DailyMenuId
                  AND dailyAccess.[IsAllow] = 1
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [StaffMenuAccess] monthlyAccess
                      WHERE monthlyAccess.[StaffId] = dailyAccess.[StaffId]
                        AND monthlyAccess.[MenuId] = @MonthlyMenuId
                  );

                INSERT INTO [TenantMenuPermissions]
                    ([TenantId], [MenuId], [IsAllow], [CanView], [CanAdd], [CanEdit], [CanDelete],
                     [GrantedOnUtc], [GrantedByUserId])
                SELECT dailyAccess.[TenantId], @MonthlyMenuId, 1,
                       dailyAccess.[CanView], 0, 0, 0,
                       SYSUTCDATETIME(), N'System: Monthly Chart'
                FROM [TenantMenuPermissions] dailyAccess
                WHERE dailyAccess.[MenuId] = @DailyMenuId
                  AND dailyAccess.[IsAllow] = 1
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [TenantMenuPermissions] monthlyAccess
                      WHERE monthlyAccess.[TenantId] = dailyAccess.[TenantId]
                        AND monthlyAccess.[MenuId] = @MonthlyMenuId
                  );
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @MonthlyMenuId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/monthly-chart'
                ORDER BY [Id]
            );

            IF @MonthlyMenuId IS NOT NULL
            BEGIN
                DELETE FROM [StaffMenuAccess]
                WHERE [MenuId] = @MonthlyMenuId
                  AND [GrantedBy] = N'System: Monthly Chart';

                DELETE FROM [TenantMenuPermissions]
                WHERE [MenuId] = @MonthlyMenuId
                  AND [GrantedByUserId] = N'System: Monthly Chart';
            END;
            """);
    }
}
