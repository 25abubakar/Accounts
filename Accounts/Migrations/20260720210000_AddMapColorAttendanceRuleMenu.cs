using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260720210000_AddMapColorAttendanceRuleMenu")]
public sealed class AddMapColorAttendanceRuleMenu : Migration
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
            DECLARE @AttendanceRulesId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [ParentId] = @AttendancePortalId AND [Title] = N'Attendance Rules'
                ORDER BY [Id]
            );
            DECLARE @MapColorId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/rules/map-color'
                   OR ([ParentId] = @AttendanceRulesId AND [Title] = N'Map Color')
                ORDER BY CASE WHEN [Route] = N'/attendance/rules/map-color' THEN 0 ELSE 1 END, [Id]
            );

            IF @MapColorId IS NULL AND @AttendanceRulesId IS NOT NULL
            BEGIN
                INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Map Color', N'Palette', N'/attendance/rules/map-color', @AttendanceRulesId, 3, 1);
                SET @MapColorId = SCOPE_IDENTITY();
            END;

            IF @MapColorId IS NOT NULL
                UPDATE [Menus]
                SET [Title] = N'Map Color', [Icon] = N'Palette', [Route] = N'/attendance/rules/map-color',
                    [ParentId] = @AttendanceRulesId, [SortOrder] = 3, [IsActive] = 1
                WHERE [Id] = @MapColorId;

            IF @MapColorId IS NOT NULL AND @AttendanceRulesId IS NOT NULL
            BEGIN
                INSERT INTO [MenuPermissions] ([MenuId], [PermissionId])
                SELECT @MapColorId, sourcePermission.[PermissionId]
                FROM [MenuPermissions] sourcePermission
                WHERE sourcePermission.[MenuId] = @AttendanceRulesId
                  AND NOT EXISTS (
                      SELECT 1 FROM [MenuPermissions] existingPermission
                      WHERE existingPermission.[MenuId] = @MapColorId
                        AND existingPermission.[PermissionId] = sourcePermission.[PermissionId]);

                INSERT INTO [StaffMenuAccess] ([StaffId], [MenuId], [IsAllow], [GrantedBy], [GrantedDate])
                SELECT sourceAccess.[StaffId], @MapColorId, sourceAccess.[IsAllow],
                       sourceAccess.[GrantedBy], sourceAccess.[GrantedDate]
                FROM [StaffMenuAccess] sourceAccess
                WHERE sourceAccess.[MenuId] = @AttendanceRulesId
                  AND NOT EXISTS (
                      SELECT 1 FROM [StaffMenuAccess] existingAccess
                      WHERE existingAccess.[StaffId] = sourceAccess.[StaffId]
                        AND existingAccess.[MenuId] = @MapColorId);

                INSERT INTO [AccessFeatures] ([StaffMenuAccessId], [PermissionId], [IsAllow])
                SELECT targetAccess.[Id], sourceFeature.[PermissionId], sourceFeature.[IsAllow]
                FROM [StaffMenuAccess] sourceAccess
                JOIN [AccessFeatures] sourceFeature
                  ON sourceFeature.[StaffMenuAccessId] = sourceAccess.[Id]
                JOIN [StaffMenuAccess] targetAccess
                  ON targetAccess.[StaffId] = sourceAccess.[StaffId]
                 AND targetAccess.[MenuId] = @MapColorId
                WHERE sourceAccess.[MenuId] = @AttendanceRulesId
                  AND NOT EXISTS (
                      SELECT 1 FROM [AccessFeatures] existingFeature
                      WHERE existingFeature.[StaffMenuAccessId] = targetAccess.[Id]
                        AND existingFeature.[PermissionId] = sourceFeature.[PermissionId]);
            END;

            IF @MapColorId IS NOT NULL
                INSERT INTO [TenantMenuPermissions]
                    ([TenantId], [MenuId], [IsAllow], [CanView], [CanAdd], [CanEdit], [CanDelete], [GrantedOnUtc], [GrantedByUserId])
                SELECT tenant.[Id], @MapColorId,
                       COALESCE(sourcePermission.[IsAllow], 1), COALESCE(sourcePermission.[CanView], 1),
                       COALESCE(sourcePermission.[CanAdd], 1), COALESCE(sourcePermission.[CanEdit], 1),
                       COALESCE(sourcePermission.[CanDelete], 1), SYSUTCDATETIME(), N'System: Map Color Attendance Rule Menu'
                FROM [Tenants] tenant
                LEFT JOIN [TenantMenuPermissions] sourcePermission
                  ON sourcePermission.[TenantId] = tenant.[Id]
                 AND sourcePermission.[MenuId] = @AttendanceRulesId
                WHERE NOT EXISTS (
                    SELECT 1 FROM [TenantMenuPermissions] existingPermission
                    WHERE existingPermission.[TenantId] = tenant.[Id]
                      AND existingPermission.[MenuId] = @MapColorId);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @MapColorId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/rules/map-color'
                ORDER BY [Id]
            );

            DELETE feature
            FROM [AccessFeatures] feature
            JOIN [StaffMenuAccess] accessRow ON accessRow.[Id] = feature.[StaffMenuAccessId]
            WHERE accessRow.[MenuId] = @MapColorId;

            DELETE FROM [StaffMenuAccess] WHERE [MenuId] = @MapColorId;
            DELETE FROM [TenantMenuPermissions] WHERE [MenuId] = @MapColorId;
            DELETE FROM [MenuPermissions] WHERE [MenuId] = @MapColorId;
            DELETE FROM [Menus] WHERE [Id] = @MapColorId;
            """);
    }
}
