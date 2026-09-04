using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Seeds Attendance access FeatureKeys + MenuPermissions for Access Control / Roles.
/// Does not auto-grant Previous Months, View Employees, or View All Employees.
/// Safe defaults (Self + Current Month) are resolved in code when module VIEW exists.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260904120000_SeedAttendanceAccessFeatures")]
public sealed class SeedAttendanceAccessFeatures : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @Routes TABLE (Route nvarchar(200) NOT NULL);
            INSERT INTO @Routes (Route) VALUES
                (N'/attendance'),
                (N'/attendance/staff'),
                (N'/attendance/daily-report'),
                (N'/attendance/report'),
                (N'/attendance/remote'),
                (N'/attendance/login'),
                (N'/attendance/monthly-chart'),
                (N'/attendance/timing-chart'),
                (N'/attendance/by-supervisor'),
                (N'/attendance/camera'),
                (N'/attendance/check-in');

            DECLARE @Suffixes TABLE
            (
                Suffix nvarchar(50) NOT NULL,
                DisplaySuffix nvarchar(80) NOT NULL
            );
            INSERT INTO @Suffixes (Suffix, DisplaySuffix) VALUES
                (N'VIEW_SELF', N'View Self Attendance'),
                (N'CURRENT_MONTH', N'View Current Month'),
                (N'PREVIOUS_MONTHS', N'View Previous Months'),
                (N'VIEW_EMPLOYEES', N'View Employee Attendance'),
                (N'VIEW_ALL_EMPLOYEES', N'View All Employees Attendance');

            -- Keep legacy history keys present so existing AccessFeatures FKs remain valid.
            INSERT INTO @Suffixes (Suffix, DisplaySuffix) VALUES
                (N'OWN_HISTORY', N'View Own Previous Months'),
                (N'TEAM_HISTORY', N'View Team Previous Months'),
                (N'HISTORY', N'View Previous Months');

            DECLARE @MenuId int, @Title nvarchar(200), @Suffix nvarchar(50), @DisplaySuffix nvarchar(80);
            DECLARE @FeatureKey nvarchar(100), @FeatureName nvarchar(200), @PermissionId int;

            DECLARE menu_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT m.Id, m.Title
                FROM dbo.Menus m
                INNER JOIN @Routes r ON r.Route = m.Route
                WHERE m.IsActive = 1;

            OPEN menu_cursor;
            FETCH NEXT FROM menu_cursor INTO @MenuId, @Title;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                DECLARE suffix_cursor CURSOR LOCAL FAST_FORWARD FOR
                    SELECT Suffix, DisplaySuffix FROM @Suffixes;
                OPEN suffix_cursor;
                FETCH NEXT FROM suffix_cursor INTO @Suffix, @DisplaySuffix;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    SET @FeatureKey = N'MENU_' + CAST(@MenuId AS nvarchar(20)) + N'_' + @Suffix;
                    SET @FeatureName = @Title + N' - ' + @DisplaySuffix;

                    IF NOT EXISTS (SELECT 1 FROM dbo.Features WHERE FeatureKey = @FeatureKey)
                    BEGIN
                        INSERT INTO dbo.Features (FeatureKey, FeatureName, Module)
                        VALUES (@FeatureKey, @FeatureName, N'Menu');
                    END;

                    SELECT @PermissionId = PermissionId FROM dbo.Features WHERE FeatureKey = @FeatureKey;

                    IF @PermissionId IS NOT NULL
                       AND NOT EXISTS (
                            SELECT 1 FROM dbo.MenuPermissions
                            WHERE MenuId = @MenuId AND PermissionId = @PermissionId)
                    BEGIN
                        INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
                        VALUES (@MenuId, @PermissionId);
                    END;

                    FETCH NEXT FROM suffix_cursor INTO @Suffix, @DisplaySuffix;
                END;
                CLOSE suffix_cursor;
                DEALLOCATE suffix_cursor;

                FETCH NEXT FROM menu_cursor INTO @MenuId, @Title;
            END;
            CLOSE menu_cursor;
            DEALLOCATE menu_cursor;

            -- Map legacy OWN_HISTORY / TEAM_HISTORY AccessFeatures onto PREVIOUS_MONTHS
            -- for the same staff+menu without granting VIEW_EMPLOYEES / VIEW_ALL.
            -- OWN_HISTORY → PREVIOUS_MONTHS only (self remains gated by employee scope).
            -- TEAM_HISTORY → PREVIOUS_MONTHS only (employee scope still separate; TEAM_HISTORY
            -- continues to be recognized at runtime for CanViewEmployees compatibility).
            INSERT INTO dbo.AccessFeatures (StaffMenuAccessId, PermissionId, IsAllow)
            SELECT sma.Id, prev.PermissionId, 1
            FROM dbo.StaffMenuAccess sma
            INNER JOIN dbo.AccessFeatures af ON af.StaffMenuAccessId = sma.Id AND af.IsAllow = 1
            INNER JOIN dbo.Features legacy ON legacy.PermissionId = af.PermissionId
            INNER JOIN dbo.Menus m ON m.Id = sma.MenuId
            INNER JOIN dbo.Features prev
                ON prev.FeatureKey = N'MENU_' + CAST(m.Id AS nvarchar(20)) + N'_PREVIOUS_MONTHS'
            WHERE sma.IsAllow = 1
              AND (
                    legacy.FeatureKey LIKE N'MENU_%_OWN_HISTORY'
                 OR legacy.FeatureKey LIKE N'MENU_%_TEAM_HISTORY'
                 OR legacy.FeatureKey LIKE N'MENU_%_HISTORY'
              )
              AND NOT EXISTS (
                    SELECT 1 FROM dbo.AccessFeatures existing
                    WHERE existing.StaffMenuAccessId = sma.Id
                      AND existing.PermissionId = prev.PermissionId);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Non-destructive: keep seeded features; do not delete grants.
    }
}
