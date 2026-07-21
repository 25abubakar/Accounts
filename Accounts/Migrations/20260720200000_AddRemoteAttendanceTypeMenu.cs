using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260720200000_AddRemoteAttendanceTypeMenu")]
public sealed class AddRemoteAttendanceTypeMenu : Migration
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
            DECLARE @PermissionSourceMenuId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [ParentId] = @AttendancePortalId AND [Title] = N'Attendance Rules'
                ORDER BY [Id]
            );
            DECLARE @RemoteId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/types/remote'
                   OR ([ParentId] = @TypeId AND [Title] = N'Remote')
                ORDER BY CASE WHEN [Route] = N'/attendance/types/remote' THEN 0 ELSE 1 END, [Id]
            );

            IF @RemoteId IS NULL AND @TypeId IS NOT NULL
            BEGIN
                INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Remote', N'Wifi', N'/attendance/types/remote', @TypeId, 4, 1);
                SET @RemoteId = SCOPE_IDENTITY();
            END;

            IF @RemoteId IS NOT NULL
                UPDATE [Menus]
                SET [Title] = N'Remote', [Icon] = N'Wifi', [Route] = N'/attendance/types/remote',
                    [ParentId] = @TypeId, [SortOrder] = 4, [IsActive] = 1
                WHERE [Id] = @RemoteId;

            IF @RemoteId IS NOT NULL AND @PermissionSourceMenuId IS NOT NULL
            BEGIN
                INSERT INTO [MenuPermissions] ([MenuId], [PermissionId])
                SELECT @RemoteId, sourcePermission.[PermissionId]
                FROM [MenuPermissions] sourcePermission
                WHERE sourcePermission.[MenuId] = @PermissionSourceMenuId
                  AND NOT EXISTS (
                      SELECT 1 FROM [MenuPermissions] existingPermission
                      WHERE existingPermission.[MenuId] = @RemoteId
                        AND existingPermission.[PermissionId] = sourcePermission.[PermissionId]);

                INSERT INTO [StaffMenuAccess] ([StaffId], [MenuId], [IsAllow], [GrantedBy], [GrantedDate])
                SELECT sourceAccess.[StaffId], @RemoteId, sourceAccess.[IsAllow],
                       sourceAccess.[GrantedBy], sourceAccess.[GrantedDate]
                FROM [StaffMenuAccess] sourceAccess
                WHERE sourceAccess.[MenuId] = @PermissionSourceMenuId
                  AND NOT EXISTS (
                      SELECT 1 FROM [StaffMenuAccess] existingAccess
                      WHERE existingAccess.[StaffId] = sourceAccess.[StaffId]
                        AND existingAccess.[MenuId] = @RemoteId);

                INSERT INTO [AccessFeatures] ([StaffMenuAccessId], [PermissionId], [IsAllow])
                SELECT targetAccess.[Id], sourceFeature.[PermissionId], sourceFeature.[IsAllow]
                FROM [StaffMenuAccess] sourceAccess
                JOIN [AccessFeatures] sourceFeature
                  ON sourceFeature.[StaffMenuAccessId] = sourceAccess.[Id]
                JOIN [StaffMenuAccess] targetAccess
                  ON targetAccess.[StaffId] = sourceAccess.[StaffId]
                 AND targetAccess.[MenuId] = @RemoteId
                WHERE sourceAccess.[MenuId] = @PermissionSourceMenuId
                  AND NOT EXISTS (
                      SELECT 1 FROM [AccessFeatures] existingFeature
                      WHERE existingFeature.[StaffMenuAccessId] = targetAccess.[Id]
                        AND existingFeature.[PermissionId] = sourceFeature.[PermissionId]);
            END;

            IF @RemoteId IS NOT NULL
                INSERT INTO [TenantMenuPermissions]
                    ([TenantId], [MenuId], [IsAllow], [CanView], [CanAdd], [CanEdit], [CanDelete], [GrantedOnUtc], [GrantedByUserId])
                SELECT tenant.[Id], @RemoteId,
                       COALESCE(sourcePermission.[IsAllow], 1), COALESCE(sourcePermission.[CanView], 1),
                       COALESCE(sourcePermission.[CanAdd], 1), COALESCE(sourcePermission.[CanEdit], 1),
                       COALESCE(sourcePermission.[CanDelete], 1), SYSUTCDATETIME(), N'System: Remote Attendance Type Menu'
                FROM [Tenants] tenant
                LEFT JOIN [TenantMenuPermissions] sourcePermission
                  ON sourcePermission.[TenantId] = tenant.[Id]
                 AND sourcePermission.[MenuId] = @TypeId
                WHERE NOT EXISTS (
                    SELECT 1 FROM [TenantMenuPermissions] existingPermission
                    WHERE existingPermission.[TenantId] = tenant.[Id]
                      AND existingPermission.[MenuId] = @RemoteId);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @RemoteId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/types/remote'
                ORDER BY [Id]
            );

            DELETE feature
            FROM [AccessFeatures] feature
            JOIN [StaffMenuAccess] accessRow ON accessRow.[Id] = feature.[StaffMenuAccessId]
            WHERE accessRow.[MenuId] = @RemoteId;

            DELETE FROM [StaffMenuAccess] WHERE [MenuId] = @RemoteId;
            DELETE FROM [TenantMenuPermissions] WHERE [MenuId] = @RemoteId;
            DELETE FROM [MenuPermissions] WHERE [MenuId] = @RemoteId;
            DELETE FROM [Menus] WHERE [Id] = @RemoteId;
            """);
    }
}
