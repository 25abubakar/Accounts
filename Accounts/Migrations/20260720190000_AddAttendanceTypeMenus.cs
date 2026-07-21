using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260720190000_AddAttendanceTypeMenus")]
public sealed class AddAttendanceTypeMenus : Migration
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

            DECLARE @PermissionSourceMenuId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [ParentId] = @AttendancePortalId AND [Title] = N'Attendance Rules'
                ORDER BY [Id]
            );

            DECLARE @TypeId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [ParentId] = @AttendancePortalId AND [Title] = N'Type'
                ORDER BY [Id]
            );

            IF @TypeId IS NULL AND @AttendancePortalId IS NOT NULL
            BEGIN
                INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Type', N'Layers', NULL, @AttendancePortalId, 7, 1);
                SET @TypeId = SCOPE_IDENTITY();
            END;

            IF @TypeId IS NOT NULL
                UPDATE [Menus]
                SET [Title] = N'Type', [Icon] = N'Layers', [Route] = NULL,
                    [ParentId] = @AttendancePortalId, [SortOrder] = 7, [IsActive] = 1
                WHERE [Id] = @TypeId;

            DECLARE @CameraId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/types/camera'
                   OR ([ParentId] = @TypeId AND [Title] = N'Camera')
                ORDER BY CASE WHEN [Route] = N'/attendance/types/camera' THEN 0 ELSE 1 END, [Id]
            );

            IF @CameraId IS NULL AND @TypeId IS NOT NULL
            BEGIN
                INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Camera', N'Camera', N'/attendance/types/camera', @TypeId, 1, 1);
                SET @CameraId = SCOPE_IDENTITY();
            END;

            IF @CameraId IS NOT NULL
                UPDATE [Menus]
                SET [Title] = N'Camera', [Icon] = N'Camera', [Route] = N'/attendance/types/camera',
                    [ParentId] = @TypeId, [SortOrder] = 1, [IsActive] = 1
                WHERE [Id] = @CameraId;

            DECLARE @LoginId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/types/login'
                   OR ([ParentId] = @TypeId AND [Title] = N'Login')
                ORDER BY CASE WHEN [Route] = N'/attendance/types/login' THEN 0 ELSE 1 END, [Id]
            );

            IF @LoginId IS NULL AND @TypeId IS NOT NULL
            BEGIN
                INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Login', N'LogIn', N'/attendance/types/login', @TypeId, 2, 1);
                SET @LoginId = SCOPE_IDENTITY();
            END;

            IF @LoginId IS NOT NULL
                UPDATE [Menus]
                SET [Title] = N'Login', [Icon] = N'LogIn', [Route] = N'/attendance/types/login',
                    [ParentId] = @TypeId, [SortOrder] = 2, [IsActive] = 1
                WHERE [Id] = @LoginId;

            DECLARE @CheckInId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/types/check-in'
                   OR ([ParentId] = @TypeId AND [Title] IN (N'Check In', N'Check in'))
                ORDER BY CASE WHEN [Route] = N'/attendance/types/check-in' THEN 0 ELSE 1 END, [Id]
            );

            IF @CheckInId IS NULL AND @TypeId IS NOT NULL
            BEGIN
                INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Check In', N'BadgeCheck', N'/attendance/types/check-in', @TypeId, 3, 1);
                SET @CheckInId = SCOPE_IDENTITY();
            END;

            IF @CheckInId IS NOT NULL
                UPDATE [Menus]
                SET [Title] = N'Check In', [Icon] = N'BadgeCheck', [Route] = N'/attendance/types/check-in',
                    [ParentId] = @TypeId, [SortOrder] = 3, [IsActive] = 1
                WHERE [Id] = @CheckInId;

            DECLARE @TargetMenus table ([MenuId] int PRIMARY KEY);
            INSERT INTO @TargetMenus SELECT @TypeId WHERE @TypeId IS NOT NULL;
            INSERT INTO @TargetMenus SELECT @CameraId WHERE @CameraId IS NOT NULL;
            INSERT INTO @TargetMenus SELECT @LoginId WHERE @LoginId IS NOT NULL;
            INSERT INTO @TargetMenus SELECT @CheckInId WHERE @CheckInId IS NOT NULL;

            IF @PermissionSourceMenuId IS NOT NULL
            BEGIN
                INSERT INTO [MenuPermissions] ([MenuId], [PermissionId])
                SELECT target.[MenuId], sourcePermission.[PermissionId]
                FROM @TargetMenus target
                CROSS JOIN [MenuPermissions] sourcePermission
                WHERE sourcePermission.[MenuId] = @PermissionSourceMenuId
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [MenuPermissions] existingPermission
                      WHERE existingPermission.[MenuId] = target.[MenuId]
                        AND existingPermission.[PermissionId] = sourcePermission.[PermissionId]);

                INSERT INTO [StaffMenuAccess] ([StaffId], [MenuId], [IsAllow], [GrantedBy], [GrantedDate])
                SELECT sourceAccess.[StaffId], target.[MenuId], sourceAccess.[IsAllow],
                       sourceAccess.[GrantedBy], sourceAccess.[GrantedDate]
                FROM [StaffMenuAccess] sourceAccess
                CROSS JOIN @TargetMenus target
                WHERE sourceAccess.[MenuId] = @PermissionSourceMenuId
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [StaffMenuAccess] existingAccess
                      WHERE existingAccess.[StaffId] = sourceAccess.[StaffId]
                        AND existingAccess.[MenuId] = target.[MenuId]);

                INSERT INTO [AccessFeatures] ([StaffMenuAccessId], [PermissionId], [IsAllow])
                SELECT targetAccess.[Id], sourceFeature.[PermissionId], sourceFeature.[IsAllow]
                FROM [StaffMenuAccess] sourceAccess
                JOIN [AccessFeatures] sourceFeature
                  ON sourceFeature.[StaffMenuAccessId] = sourceAccess.[Id]
                CROSS JOIN @TargetMenus target
                JOIN [StaffMenuAccess] targetAccess
                  ON targetAccess.[StaffId] = sourceAccess.[StaffId]
                 AND targetAccess.[MenuId] = target.[MenuId]
                WHERE sourceAccess.[MenuId] = @PermissionSourceMenuId
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [AccessFeatures] existingFeature
                      WHERE existingFeature.[StaffMenuAccessId] = targetAccess.[Id]
                        AND existingFeature.[PermissionId] = sourceFeature.[PermissionId]);
            END;

            INSERT INTO [TenantMenuPermissions]
                ([TenantId], [MenuId], [IsAllow], [CanView], [CanAdd], [CanEdit], [CanDelete], [GrantedOnUtc], [GrantedByUserId])
            SELECT tenant.[Id], target.[MenuId],
                   COALESCE(sourcePermission.[IsAllow], 1), COALESCE(sourcePermission.[CanView], 1),
                   COALESCE(sourcePermission.[CanAdd], 1), COALESCE(sourcePermission.[CanEdit], 1),
                   COALESCE(sourcePermission.[CanDelete], 1), SYSUTCDATETIME(), N'System: Attendance Type Menus'
            FROM [Tenants] tenant
            CROSS JOIN @TargetMenus target
            LEFT JOIN [TenantMenuPermissions] sourcePermission
              ON sourcePermission.[TenantId] = tenant.[Id]
             AND sourcePermission.[MenuId] = @PermissionSourceMenuId
            WHERE NOT EXISTS (
                SELECT 1
                FROM [TenantMenuPermissions] existingPermission
                WHERE existingPermission.[TenantId] = tenant.[Id]
                  AND existingPermission.[MenuId] = target.[MenuId]);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
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

            DECLARE @TargetMenus table ([MenuId] int PRIMARY KEY);
            INSERT INTO @TargetMenus
            SELECT [Id] FROM [Menus]
            WHERE [ParentId] = @TypeId
              AND [Route] IN (
                  N'/attendance/types/camera',
                  N'/attendance/types/login',
                  N'/attendance/types/check-in');
            INSERT INTO @TargetMenus SELECT @TypeId WHERE @TypeId IS NOT NULL;

            DELETE feature
            FROM [AccessFeatures] feature
            JOIN [StaffMenuAccess] accessRow ON accessRow.[Id] = feature.[StaffMenuAccessId]
            JOIN @TargetMenus target ON target.[MenuId] = accessRow.[MenuId];

            DELETE accessRow
            FROM [StaffMenuAccess] accessRow
            JOIN @TargetMenus target ON target.[MenuId] = accessRow.[MenuId];

            DELETE tenantPermission
            FROM [TenantMenuPermissions] tenantPermission
            JOIN @TargetMenus target ON target.[MenuId] = tenantPermission.[MenuId];

            DELETE menuPermission
            FROM [MenuPermissions] menuPermission
            JOIN @TargetMenus target ON target.[MenuId] = menuPermission.[MenuId];

            DELETE FROM [Menus]
            WHERE [ParentId] = @TypeId
              AND [Route] IN (
                  N'/attendance/types/camera',
                  N'/attendance/types/login',
                  N'/attendance/types/check-in');

            DELETE FROM [Menus] WHERE [Id] = @TypeId;
            """);
    }
}
