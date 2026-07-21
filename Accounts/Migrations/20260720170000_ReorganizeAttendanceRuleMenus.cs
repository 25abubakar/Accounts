using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260720170000_ReorganizeAttendanceRuleMenus")]
public sealed class ReorganizeAttendanceRuleMenus : Migration
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
                WHERE [ParentId] = @AttendancePortalId
                  AND ([Route] = N'/attendance/rules' OR [Title] = N'Attendance Rules')
                ORDER BY CASE WHEN [Route] = N'/attendance/rules' THEN 0 ELSE 1 END, [Id]
            );

            IF @AttendanceRulesId IS NULL AND @AttendancePortalId IS NOT NULL
            BEGIN
                INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Attendance Rules', N'ListChecks', NULL, @AttendancePortalId, 6, 1);
                SET @AttendanceRulesId = SCOPE_IDENTITY();
            END;

            IF @AttendanceRulesId IS NOT NULL
                UPDATE [Menus]
                SET [Title] = N'Attendance Rules', [Icon] = N'ListChecks', [Route] = NULL,
                    [ParentId] = @AttendancePortalId, [SortOrder] = 6, [IsActive] = 1
                WHERE [Id] = @AttendanceRulesId;

            DECLARE @RuleId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/rules/rule'
                   OR ([ParentId] = @AttendanceRulesId AND [Title] = N'Rule')
                ORDER BY CASE WHEN [Route] = N'/attendance/rules/rule' THEN 0 ELSE 1 END, [Id]
            );

            IF @RuleId IS NULL AND @AttendanceRulesId IS NOT NULL
            BEGIN
                INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Rule', N'ListChecks', N'/attendance/rules/rule', @AttendanceRulesId, 1, 1);
                SET @RuleId = SCOPE_IDENTITY();
            END;

            IF @RuleId IS NOT NULL
                UPDATE [Menus]
                SET [Title] = N'Rule', [Icon] = N'ListChecks', [Route] = N'/attendance/rules/rule',
                    [ParentId] = @AttendanceRulesId, [SortOrder] = 1, [IsActive] = 1
                WHERE [Id] = @RuleId;

            DECLARE @MapAttendanceId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/map-attendance'
                   OR ([ParentId] = @AttendancePortalId AND [Title] = N'Map Attendance')
                ORDER BY CASE WHEN [Route] = N'/attendance/map-attendance' THEN 0 ELSE 1 END, [Id]
            );

            IF @MapAttendanceId IS NULL AND @AttendancePortalId IS NOT NULL
            BEGIN
                INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Map Attendance', N'MapPin', N'/attendance/map-attendance', @AttendancePortalId, 7, 1);
                SET @MapAttendanceId = SCOPE_IDENTITY();
            END;

            IF @MapAttendanceId IS NOT NULL
                UPDATE [Menus]
                SET [Title] = N'Map Attendance', [Icon] = N'MapPin', [Route] = N'/attendance/map-attendance',
                    [ParentId] = @AttendancePortalId, [SortOrder] = 7, [IsActive] = 1
                WHERE [Id] = @MapAttendanceId;

            IF @AttendanceRulesId IS NOT NULL AND @RuleId IS NOT NULL
            BEGIN
                INSERT INTO [MenuPermissions] ([MenuId], [PermissionId])
                SELECT @RuleId, sourcePermission.[PermissionId]
                FROM [MenuPermissions] sourcePermission
                WHERE sourcePermission.[MenuId] = @AttendanceRulesId
                  AND NOT EXISTS (
                      SELECT 1 FROM [MenuPermissions] targetPermission
                      WHERE targetPermission.[MenuId] = @RuleId
                        AND targetPermission.[PermissionId] = sourcePermission.[PermissionId]);

                INSERT INTO [StaffMenuAccess] ([StaffId], [MenuId], [IsAllow], [GrantedBy], [GrantedDate])
                SELECT sourceAccess.[StaffId], @RuleId, sourceAccess.[IsAllow], sourceAccess.[GrantedBy], sourceAccess.[GrantedDate]
                FROM [StaffMenuAccess] sourceAccess
                WHERE sourceAccess.[MenuId] = @AttendanceRulesId
                  AND NOT EXISTS (
                      SELECT 1 FROM [StaffMenuAccess] targetAccess
                      WHERE targetAccess.[StaffId] = sourceAccess.[StaffId] AND targetAccess.[MenuId] = @RuleId);
            END;

            IF @AttendanceRulesId IS NOT NULL AND @MapAttendanceId IS NOT NULL
            BEGIN
                INSERT INTO [MenuPermissions] ([MenuId], [PermissionId])
                SELECT @MapAttendanceId, sourcePermission.[PermissionId]
                FROM [MenuPermissions] sourcePermission
                WHERE sourcePermission.[MenuId] = @AttendanceRulesId
                  AND NOT EXISTS (
                      SELECT 1 FROM [MenuPermissions] targetPermission
                      WHERE targetPermission.[MenuId] = @MapAttendanceId
                        AND targetPermission.[PermissionId] = sourcePermission.[PermissionId]);

                INSERT INTO [StaffMenuAccess] ([StaffId], [MenuId], [IsAllow], [GrantedBy], [GrantedDate])
                SELECT sourceAccess.[StaffId], @MapAttendanceId, sourceAccess.[IsAllow], sourceAccess.[GrantedBy], sourceAccess.[GrantedDate]
                FROM [StaffMenuAccess] sourceAccess
                WHERE sourceAccess.[MenuId] = @AttendanceRulesId
                  AND NOT EXISTS (
                      SELECT 1 FROM [StaffMenuAccess] targetAccess
                      WHERE targetAccess.[StaffId] = sourceAccess.[StaffId] AND targetAccess.[MenuId] = @MapAttendanceId);
            END;

            IF @RuleId IS NOT NULL
                INSERT INTO [TenantMenuPermissions]
                    ([TenantId], [MenuId], [IsAllow], [CanView], [CanAdd], [CanEdit], [CanDelete], [GrantedOnUtc], [GrantedByUserId])
                SELECT tenant.[Id], @RuleId,
                    COALESCE(parentPermission.[IsAllow], 1), COALESCE(parentPermission.[CanView], 1),
                    COALESCE(parentPermission.[CanAdd], 1), COALESCE(parentPermission.[CanEdit], 1),
                    COALESCE(parentPermission.[CanDelete], 1), SYSUTCDATETIME(), N'System: Attendance Rule Menu Split'
                FROM [Tenants] tenant
                LEFT JOIN [TenantMenuPermissions] parentPermission
                  ON parentPermission.[TenantId] = tenant.[Id] AND parentPermission.[MenuId] = @AttendanceRulesId
                WHERE NOT EXISTS (
                    SELECT 1 FROM [TenantMenuPermissions] existingPermission
                    WHERE existingPermission.[TenantId] = tenant.[Id] AND existingPermission.[MenuId] = @RuleId);

            IF @MapAttendanceId IS NOT NULL
                INSERT INTO [TenantMenuPermissions]
                    ([TenantId], [MenuId], [IsAllow], [CanView], [CanAdd], [CanEdit], [CanDelete], [GrantedOnUtc], [GrantedByUserId])
                SELECT tenant.[Id], @MapAttendanceId,
                    COALESCE(parentPermission.[IsAllow], 1), COALESCE(parentPermission.[CanView], 1),
                    COALESCE(parentPermission.[CanAdd], 1), COALESCE(parentPermission.[CanEdit], 1),
                    COALESCE(parentPermission.[CanDelete], 1), SYSUTCDATETIME(), N'System: Map Attendance Menu Split'
                FROM [Tenants] tenant
                LEFT JOIN [TenantMenuPermissions] parentPermission
                  ON parentPermission.[TenantId] = tenant.[Id] AND parentPermission.[MenuId] = @AttendanceRulesId
                WHERE NOT EXISTS (
                    SELECT 1 FROM [TenantMenuPermissions] existingPermission
                    WHERE existingPermission.[TenantId] = tenant.[Id] AND existingPermission.[MenuId] = @MapAttendanceId);

            IF @RuleId IS NOT NULL
                INSERT INTO [AccessFeatures] ([StaffMenuAccessId], [PermissionId], [IsAllow])
                SELECT targetAccess.[Id], sourceFeature.[PermissionId], sourceFeature.[IsAllow]
                FROM [StaffMenuAccess] sourceAccess
                JOIN [AccessFeatures] sourceFeature ON sourceFeature.[StaffMenuAccessId] = sourceAccess.[Id]
                JOIN [StaffMenuAccess] targetAccess ON targetAccess.[StaffId] = sourceAccess.[StaffId] AND targetAccess.[MenuId] = @RuleId
                WHERE sourceAccess.[MenuId] = @AttendanceRulesId
                  AND NOT EXISTS (
                      SELECT 1 FROM [AccessFeatures] targetFeature
                      WHERE targetFeature.[StaffMenuAccessId] = targetAccess.[Id]
                        AND targetFeature.[PermissionId] = sourceFeature.[PermissionId]);

            IF @MapAttendanceId IS NOT NULL
                INSERT INTO [AccessFeatures] ([StaffMenuAccessId], [PermissionId], [IsAllow])
                SELECT targetAccess.[Id], sourceFeature.[PermissionId], sourceFeature.[IsAllow]
                FROM [StaffMenuAccess] sourceAccess
                JOIN [AccessFeatures] sourceFeature ON sourceFeature.[StaffMenuAccessId] = sourceAccess.[Id]
                JOIN [StaffMenuAccess] targetAccess ON targetAccess.[StaffId] = sourceAccess.[StaffId] AND targetAccess.[MenuId] = @MapAttendanceId
                WHERE sourceAccess.[MenuId] = @AttendanceRulesId
                  AND NOT EXISTS (
                      SELECT 1 FROM [AccessFeatures] targetFeature
                      WHERE targetFeature.[StaffMenuAccessId] = targetAccess.[Id]
                        AND targetFeature.[PermissionId] = sourceFeature.[PermissionId]);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @AttendanceRulesId int = (
                SELECT TOP (1) [Id] FROM [Menus] WHERE [Title] = N'Attendance Rules' ORDER BY [Id]
            );
            DECLARE @RuleId int = (
                SELECT TOP (1) [Id] FROM [Menus] WHERE [Route] = N'/attendance/rules/rule' ORDER BY [Id]
            );
            DECLARE @MapAttendanceId int = (
                SELECT TOP (1) [Id] FROM [Menus] WHERE [Route] = N'/attendance/map-attendance' ORDER BY [Id]
            );

            DELETE FROM [Menus] WHERE [Id] IN (@RuleId, @MapAttendanceId);

            IF @AttendanceRulesId IS NOT NULL
                UPDATE [Menus]
                SET [Route] = N'/attendance/rules', [SortOrder] = 6, [IsActive] = 1
                WHERE [Id] = @AttendanceRulesId;
            """);
    }
}
