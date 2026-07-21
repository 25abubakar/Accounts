using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260717210000_AddTimingChartMenu")]
public sealed class AddTimingChartMenu : Migration
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
            DECLARE @TimingMenuId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/timing-chart'
                ORDER BY [Id]
            );

            IF @TimingMenuId IS NULL AND @ParentId IS NOT NULL
            BEGIN
                INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Timing Chart', N'Clock3', N'/attendance/timing-chart', @ParentId, 5, 1);
                SET @TimingMenuId = SCOPE_IDENTITY();
            END;

            IF @TimingMenuId IS NOT NULL
            BEGIN
                UPDATE [Menus]
                SET [Title] = N'Timing Chart', [Icon] = N'Clock3',
                    [Route] = N'/attendance/timing-chart',
                    [ParentId] = COALESCE(@ParentId, [ParentId]),
                    [SortOrder] = 5, [IsActive] = 1
                WHERE [Id] = @TimingMenuId;
            END;

            -- Until Timing Chart receives its final workflow, expose the empty
            -- child screen to the same current audience as Daily Attendance.
            IF @DailyMenuId IS NOT NULL AND @TimingMenuId IS NOT NULL
            BEGIN
                INSERT INTO [StaffMenuAccess]
                    ([StaffId], [MenuId], [IsAllow], [GrantedBy], [GrantedDate])
                SELECT dailyAccess.[StaffId], @TimingMenuId, 1,
                       N'System: Timing Chart', SYSUTCDATETIME()
                FROM [StaffMenuAccess] dailyAccess
                WHERE dailyAccess.[MenuId] = @DailyMenuId
                  AND dailyAccess.[IsAllow] = 1
                  AND NOT EXISTS (
                      SELECT 1 FROM [StaffMenuAccess] timingAccess
                      WHERE timingAccess.[StaffId] = dailyAccess.[StaffId]
                        AND timingAccess.[MenuId] = @TimingMenuId
                  );

                INSERT INTO [TenantMenuPermissions]
                    ([TenantId], [MenuId], [IsAllow], [CanView], [CanAdd], [CanEdit], [CanDelete],
                     [GrantedOnUtc], [GrantedByUserId])
                SELECT dailyAccess.[TenantId], @TimingMenuId, 1,
                       dailyAccess.[CanView], 0, 0, 0,
                       SYSUTCDATETIME(), N'System: Timing Chart'
                FROM [TenantMenuPermissions] dailyAccess
                WHERE dailyAccess.[MenuId] = @DailyMenuId
                  AND dailyAccess.[IsAllow] = 1
                  AND NOT EXISTS (
                      SELECT 1 FROM [TenantMenuPermissions] timingAccess
                      WHERE timingAccess.[TenantId] = dailyAccess.[TenantId]
                        AND timingAccess.[MenuId] = @TimingMenuId
                  );
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @TimingMenuId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/timing-chart'
                ORDER BY [Id]
            );

            IF @TimingMenuId IS NOT NULL
            BEGIN
                DELETE FROM [StaffMenuAccess]
                WHERE [MenuId] = @TimingMenuId
                  AND [GrantedBy] = N'System: Timing Chart';

                DELETE FROM [TenantMenuPermissions]
                WHERE [MenuId] = @TimingMenuId
                  AND [GrantedByUserId] = N'System: Timing Chart';

                UPDATE [Menus]
                SET [IsActive] = 0
                WHERE [Id] = @TimingMenuId;
            END;
            """);
    }
}
