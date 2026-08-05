using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[Migration("20260805150000_AddAttendanceHistoryPermissions")]
public sealed class AddAttendanceHistoryPermissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @StaffMenuId int = (SELECT TOP(1) Id FROM dbo.Menus WHERE Route=N'/attendance/staff');
            DECLARE @DailyMenuId int = (SELECT TOP(1) Id FROM dbo.Menus WHERE Route=N'/attendance/daily-report');

            IF @StaffMenuId IS NOT NULL
            BEGIN
                DECLARE @OwnKey nvarchar(100)=CONCAT(N'MENU_',@StaffMenuId,N'_OWN_HISTORY');
                IF NOT EXISTS(SELECT 1 FROM dbo.Features WHERE FeatureKey=@OwnKey)
                    INSERT dbo.Features(FeatureKey,FeatureName,Module,Description,CreatedDate)
                    VALUES(@OwnKey,N'View Own Previous Months',N'Menu',N'Allows this employee to view personal attendance from previous months.',SYSUTCDATETIME());
                DECLARE @OwnId int=(SELECT PermissionId FROM dbo.Features WHERE FeatureKey=@OwnKey);
                IF NOT EXISTS(SELECT 1 FROM dbo.MenuPermissions WHERE MenuId=@StaffMenuId AND PermissionId=@OwnId)
                    INSERT dbo.MenuPermissions(MenuId,PermissionId) VALUES(@StaffMenuId,@OwnId);
            END;

            IF @DailyMenuId IS NOT NULL
            BEGIN
                DECLARE @TeamKey nvarchar(100)=CONCAT(N'MENU_',@DailyMenuId,N'_TEAM_HISTORY');
                IF NOT EXISTS(SELECT 1 FROM dbo.Features WHERE FeatureKey=@TeamKey)
                    INSERT dbo.Features(FeatureKey,FeatureName,Module,Description,CreatedDate)
                    VALUES(@TeamKey,N'View Team Previous Months',N'Menu',N'Allows historical attendance for staff already visible through the saved organization hierarchy.',SYSUTCDATETIME());
                DECLARE @TeamId int=(SELECT PermissionId FROM dbo.Features WHERE FeatureKey=@TeamKey);
                IF NOT EXISTS(SELECT 1 FROM dbo.MenuPermissions WHERE MenuId=@DailyMenuId AND PermissionId=@TeamId)
                    INSERT dbo.MenuPermissions(MenuId,PermissionId) VALUES(@DailyMenuId,@TeamId);
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE mp FROM dbo.MenuPermissions mp JOIN dbo.Features f ON f.PermissionId=mp.PermissionId
            WHERE f.FeatureKey LIKE N'MENU_%_OWN_HISTORY' OR f.FeatureKey LIKE N'MENU_%_TEAM_HISTORY';
            DELETE FROM dbo.Features WHERE FeatureKey LIKE N'MENU_%_OWN_HISTORY' OR FeatureKey LIKE N'MENU_%_TEAM_HISTORY';
            """);
    }
}
