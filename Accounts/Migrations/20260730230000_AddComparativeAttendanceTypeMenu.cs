using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260730230000_AddComparativeAttendanceTypeMenu")]
public sealed class AddComparativeAttendanceTypeMenu : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @AttendancePortalId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Title] = N'Attendance Portal' AND [ParentId] IS NULL
                ORDER BY [Id]
            );
            DECLARE @TypeId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [ParentId] = @AttendancePortalId AND [Title] = N'Type'
                ORDER BY [Id]
            );
            DECLARE @ComparativeId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/types/comparative'
                   OR ([ParentId] = @TypeId AND [Title] = N'Comparative')
                ORDER BY CASE WHEN [Route] = N'/attendance/types/comparative' THEN 0 ELSE 1 END, [Id]
            );

            IF @ComparativeId IS NULL AND @TypeId IS NOT NULL
            BEGIN
                INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Comparative', N'GitCompareArrows', N'/attendance/types/comparative', @TypeId, 5, 1);
                SET @ComparativeId = SCOPE_IDENTITY();
            END;

            IF @ComparativeId IS NOT NULL
                UPDATE [Menus]
                SET [Title] = N'Comparative',
                    [Icon] = N'GitCompareArrows',
                    [Route] = N'/attendance/types/comparative',
                    [ParentId] = @TypeId,
                    [SortOrder] = 5,
                    [IsActive] = 1
                WHERE [Id] = @ComparativeId;

            IF @ComparativeId IS NOT NULL
            BEGIN
                DECLARE @FeaturePrefix nvarchar(50) = CONCAT(N'MENU_', @ComparativeId);

                IF NOT EXISTS (SELECT 1 FROM [Features] WHERE [FeatureKey] = @FeaturePrefix)
                    INSERT INTO [Features] ([FeatureKey], [FeatureName], [Module], [Description], [CreatedDate])
                    VALUES (@FeaturePrefix, N'Attendance Comparative', N'Menu',
                            N'Open the read-only attendance comparison screen.', SYSUTCDATETIME());

                IF NOT EXISTS (SELECT 1 FROM [Features] WHERE [FeatureKey] = CONCAT(@FeaturePrefix, N'_VIEW'))
                    INSERT INTO [Features] ([FeatureKey], [FeatureName], [Module], [Description], [CreatedDate])
                    VALUES (CONCAT(@FeaturePrefix, N'_VIEW'), N'Attendance Comparative - View', N'Menu',
                            N'View system and camera attendance side by side.', SYSUTCDATETIME());

                INSERT INTO [MenuPermissions] ([MenuId], [PermissionId])
                SELECT @ComparativeId, featureRow.[PermissionId]
                FROM [Features] featureRow
                WHERE featureRow.[FeatureKey] IN (@FeaturePrefix, CONCAT(@FeaturePrefix, N'_VIEW'))
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [MenuPermissions] existingPermission
                      WHERE existingPermission.[MenuId] = @ComparativeId
                        AND existingPermission.[PermissionId] = featureRow.[PermissionId]
                  );
            END;

            IF @ComparativeId IS NOT NULL
            BEGIN
                INSERT INTO [TenantMenuPermissions]
                    ([TenantId], [MenuId], [IsAllow], [CanView], [CanAdd], [CanEdit], [CanDelete], [GrantedOnUtc], [GrantedByUserId])
                SELECT tenant.[Id], @ComparativeId, 1, 1, 0, 0, 0,
                       SYSUTCDATETIME(), N'System: Comparative Attendance Menu'
                FROM [Tenants] tenant
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [TenantMenuPermissions] existingPermission
                    WHERE existingPermission.[TenantId] = tenant.[Id]
                      AND existingPermission.[MenuId] = @ComparativeId
                );
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @ComparativeId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/types/comparative'
                ORDER BY [Id]
            );

            DELETE feature
            FROM [AccessFeatures] feature
            JOIN [StaffMenuAccess] accessRow ON accessRow.[Id] = feature.[StaffMenuAccessId]
            WHERE accessRow.[MenuId] = @ComparativeId;

            DELETE FROM [StaffMenuAccess] WHERE [MenuId] = @ComparativeId;
            DELETE FROM [TenantMenuPermissions] WHERE [MenuId] = @ComparativeId;
            DELETE FROM [MenuPermissions] WHERE [MenuId] = @ComparativeId;
            DELETE FROM [Menus] WHERE [Id] = @ComparativeId;
            """);
    }
}
