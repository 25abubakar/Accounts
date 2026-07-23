using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260722090000_AddAttendanceDeductionMenu")]
public sealed class AddAttendanceDeductionMenu : Migration
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
                WHERE [ParentId] = @AttendancePortalId
                  AND [Route] IN (N'/attendance/daily-report', N'/attendance/staff', N'/attendance/monthly-chart', N'/attendance/timing-chart')
                ORDER BY CASE [Route]
                    WHEN N'/attendance/daily-report' THEN 0
                    WHEN N'/attendance/staff' THEN 1
                    WHEN N'/attendance/monthly-chart' THEN 2
                    WHEN N'/attendance/timing-chart' THEN 3
                    ELSE 4
                END, [Id]
            );
            DECLARE @DeductionId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/deduction'
                   OR ([ParentId] = @AttendancePortalId AND [Title] = N'Deduction')
                ORDER BY CASE WHEN [Route] = N'/attendance/deduction' THEN 0 ELSE 1 END, [Id]
            );

            IF @DeductionId IS NULL AND @AttendancePortalId IS NOT NULL
            BEGIN
                INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Deduction', N'DollarSign', N'/attendance/deduction', @AttendancePortalId, 8, 1);
                SET @DeductionId = SCOPE_IDENTITY();
            END;

            IF @DeductionId IS NOT NULL
                UPDATE [Menus]
                SET [Title] = N'Deduction', [Icon] = N'DollarSign', [Route] = N'/attendance/deduction',
                    [ParentId] = @AttendancePortalId, [SortOrder] = 8, [IsActive] = 1
                WHERE [Id] = @DeductionId;

            IF @DeductionId IS NOT NULL AND @PermissionSourceMenuId IS NOT NULL
            BEGIN
                INSERT INTO [MenuPermissions] ([MenuId], [PermissionId])
                SELECT @DeductionId, sourcePermission.[PermissionId]
                FROM [MenuPermissions] sourcePermission
                WHERE sourcePermission.[MenuId] = @PermissionSourceMenuId
                  AND NOT EXISTS (
                      SELECT 1 FROM [MenuPermissions] existingPermission
                      WHERE existingPermission.[MenuId] = @DeductionId
                        AND existingPermission.[PermissionId] = sourcePermission.[PermissionId]);

                INSERT INTO [StaffMenuAccess] ([StaffId], [MenuId], [IsAllow], [GrantedBy], [GrantedDate])
                SELECT sourceAccess.[StaffId], @DeductionId, sourceAccess.[IsAllow],
                       sourceAccess.[GrantedBy], sourceAccess.[GrantedDate]
                FROM [StaffMenuAccess] sourceAccess
                WHERE sourceAccess.[MenuId] = @PermissionSourceMenuId
                  AND NOT EXISTS (
                      SELECT 1 FROM [StaffMenuAccess] existingAccess
                      WHERE existingAccess.[StaffId] = sourceAccess.[StaffId]
                        AND existingAccess.[MenuId] = @DeductionId);

                INSERT INTO [AccessFeatures] ([StaffMenuAccessId], [PermissionId], [IsAllow])
                SELECT targetAccess.[Id], sourceFeature.[PermissionId], sourceFeature.[IsAllow]
                FROM [StaffMenuAccess] sourceAccess
                JOIN [AccessFeatures] sourceFeature
                  ON sourceFeature.[StaffMenuAccessId] = sourceAccess.[Id]
                JOIN [StaffMenuAccess] targetAccess
                  ON targetAccess.[StaffId] = sourceAccess.[StaffId]
                 AND targetAccess.[MenuId] = @DeductionId
                WHERE sourceAccess.[MenuId] = @PermissionSourceMenuId
                  AND NOT EXISTS (
                      SELECT 1 FROM [AccessFeatures] existingFeature
                      WHERE existingFeature.[StaffMenuAccessId] = targetAccess.[Id]
                        AND existingFeature.[PermissionId] = sourceFeature.[PermissionId]);
            END;

            IF @DeductionId IS NOT NULL
                INSERT INTO [TenantMenuPermissions]
                    ([TenantId], [MenuId], [IsAllow], [CanView], [CanAdd], [CanEdit], [CanDelete], [GrantedOnUtc], [GrantedByUserId])
                SELECT tenant.[Id], @DeductionId,
                       COALESCE(sourcePermission.[IsAllow], 1), COALESCE(sourcePermission.[CanView], 1),
                       COALESCE(sourcePermission.[CanAdd], 1), COALESCE(sourcePermission.[CanEdit], 1),
                       COALESCE(sourcePermission.[CanDelete], 1), SYSUTCDATETIME(), N'System: Attendance Deduction Menu'
                FROM [Tenants] tenant
                LEFT JOIN [TenantMenuPermissions] sourcePermission
                  ON sourcePermission.[TenantId] = tenant.[Id]
                 AND sourcePermission.[MenuId] = @PermissionSourceMenuId
                WHERE NOT EXISTS (
                    SELECT 1 FROM [TenantMenuPermissions] existingPermission
                    WHERE existingPermission.[TenantId] = tenant.[Id]
                      AND existingPermission.[MenuId] = @DeductionId);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @DeductionId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/attendance/deduction'
                ORDER BY [Id]
            );

            DELETE feature
            FROM [AccessFeatures] feature
            JOIN [StaffMenuAccess] accessRow ON accessRow.[Id] = feature.[StaffMenuAccessId]
            WHERE accessRow.[MenuId] = @DeductionId;

            DELETE FROM [StaffMenuAccess] WHERE [MenuId] = @DeductionId;
            DELETE FROM [TenantMenuPermissions] WHERE [MenuId] = @DeductionId;
            DELETE FROM [MenuPermissions] WHERE [MenuId] = @DeductionId;
            DELETE FROM [Menus] WHERE [Id] = @DeductionId;
            """);
    }
}
