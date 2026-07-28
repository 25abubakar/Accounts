using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260727203000_AddInstructionMenuUnderPlatformSettings")]
public sealed class AddInstructionMenuUnderPlatformSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @PlatformId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Title] IN (N'Platform Settings', N'Settings')
                  AND [ParentId] IS NULL
                ORDER BY CASE WHEN [Title] = N'Platform Settings' THEN 0 ELSE 1 END, [Id]
            );

            DECLARE @SourceMenuId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] IN (N'/settings/statuses', N'/settings/scales', N'/settings/job-titles', N'/settings/menus')
                ORDER BY CASE [Route]
                    WHEN N'/settings/statuses' THEN 0
                    WHEN N'/settings/scales' THEN 1
                    WHEN N'/settings/job-titles' THEN 2
                    WHEN N'/settings/menus' THEN 3
                    ELSE 4
                END, [Id]
            );

            DECLARE @InstructionId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] IN (N'/settings/instruction', N'/instructions')
                   OR ([ParentId] = @PlatformId AND [Title] IN (N'Instruction', N'Instructions'))
                ORDER BY CASE WHEN [Route] = N'/settings/instruction' THEN 0 ELSE 1 END, [Id]
            );

            IF @InstructionId IS NULL AND @PlatformId IS NOT NULL
            BEGIN
                INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Instruction', N'Megaphone', N'/settings/instruction', @PlatformId, 9, 1);
                SET @InstructionId = SCOPE_IDENTITY();
            END;

            IF @InstructionId IS NOT NULL
            BEGIN
                UPDATE [Menus]
                SET [Title] = N'Instruction',
                    [Icon] = N'Megaphone',
                    [Route] = N'/settings/instruction',
                    [ParentId] = @PlatformId,
                    [SortOrder] = 9,
                    [IsActive] = 1
                WHERE [Id] = @InstructionId;
            END;

            IF @InstructionId IS NOT NULL
            BEGIN
                DECLARE @FeaturePrefix nvarchar(50) = CONCAT(N'MENU_', @InstructionId);

                IF NOT EXISTS (SELECT 1 FROM [Features] WHERE [FeatureKey] = @FeaturePrefix)
                    INSERT INTO [Features] ([FeatureKey], [FeatureName], [Module], [Description], [CreatedDate])
                    VALUES (@FeaturePrefix, N'Instruction', N'Menu', N'Open the instruction management screen.', SYSUTCDATETIME());

                IF NOT EXISTS (SELECT 1 FROM [Features] WHERE [FeatureKey] = CONCAT(@FeaturePrefix, N'_VIEW'))
                    INSERT INTO [Features] ([FeatureKey], [FeatureName], [Module], [Description], [CreatedDate])
                    VALUES (CONCAT(@FeaturePrefix, N'_VIEW'), N'Instruction - View', N'Menu', N'View instruction management.', SYSUTCDATETIME());

                IF NOT EXISTS (SELECT 1 FROM [Features] WHERE [FeatureKey] = CONCAT(@FeaturePrefix, N'_ADD'))
                    INSERT INTO [Features] ([FeatureKey], [FeatureName], [Module], [Description], [CreatedDate])
                    VALUES (CONCAT(@FeaturePrefix, N'_ADD'), N'Instruction - Add', N'Menu', N'Create instructions.', SYSUTCDATETIME());

                IF NOT EXISTS (SELECT 1 FROM [Features] WHERE [FeatureKey] = CONCAT(@FeaturePrefix, N'_EDIT'))
                    INSERT INTO [Features] ([FeatureKey], [FeatureName], [Module], [Description], [CreatedDate])
                    VALUES (CONCAT(@FeaturePrefix, N'_EDIT'), N'Instruction - Edit', N'Menu', N'Update instructions.', SYSUTCDATETIME());

                IF NOT EXISTS (SELECT 1 FROM [Features] WHERE [FeatureKey] = CONCAT(@FeaturePrefix, N'_DELETE'))
                    INSERT INTO [Features] ([FeatureKey], [FeatureName], [Module], [Description], [CreatedDate])
                    VALUES (CONCAT(@FeaturePrefix, N'_DELETE'), N'Instruction - Delete', N'Menu', N'Delete instructions.', SYSUTCDATETIME());

                INSERT INTO [MenuPermissions] ([MenuId], [PermissionId])
                SELECT @InstructionId, featureRow.[PermissionId]
                FROM [Features] featureRow
                WHERE featureRow.[FeatureKey] IN (@FeaturePrefix, CONCAT(@FeaturePrefix, N'_VIEW'))
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [MenuPermissions] existingPermission
                      WHERE existingPermission.[MenuId] = @InstructionId
                        AND existingPermission.[PermissionId] = featureRow.[PermissionId]);

                IF @SourceMenuId IS NOT NULL
                BEGIN
                    INSERT INTO [StaffMenuAccess] ([StaffId], [MenuId], [IsAllow], [GrantedBy], [GrantedDate])
                    SELECT sourceAccess.[StaffId], @InstructionId, sourceAccess.[IsAllow],
                           sourceAccess.[GrantedBy], sourceAccess.[GrantedDate]
                    FROM [StaffMenuAccess] sourceAccess
                    WHERE sourceAccess.[MenuId] = @SourceMenuId
                      AND NOT EXISTS (
                          SELECT 1 FROM [StaffMenuAccess] existingAccess
                          WHERE existingAccess.[StaffId] = sourceAccess.[StaffId]
                            AND existingAccess.[MenuId] = @InstructionId);

                    INSERT INTO [AccessFeatures] ([StaffMenuAccessId], [PermissionId], [IsAllow])
                    SELECT targetAccess.[Id], targetFeature.[PermissionId], sourceFeature.[IsAllow]
                    FROM [StaffMenuAccess] sourceAccess
                    JOIN [AccessFeatures] sourceFeature
                      ON sourceFeature.[StaffMenuAccessId] = sourceAccess.[Id]
                    JOIN [Features] sourceFeatureRow
                      ON sourceFeatureRow.[PermissionId] = sourceFeature.[PermissionId]
                    JOIN [StaffMenuAccess] targetAccess
                      ON targetAccess.[StaffId] = sourceAccess.[StaffId]
                     AND targetAccess.[MenuId] = @InstructionId
                    JOIN [Features] targetFeature
                      ON targetFeature.[FeatureKey] = CASE
                          WHEN sourceFeatureRow.[FeatureKey] LIKE N'%_VIEW' THEN CONCAT(@FeaturePrefix, N'_VIEW')
                          WHEN sourceFeatureRow.[FeatureKey] LIKE N'%_ADD' THEN CONCAT(@FeaturePrefix, N'_ADD')
                          WHEN sourceFeatureRow.[FeatureKey] LIKE N'%_EDIT' THEN CONCAT(@FeaturePrefix, N'_EDIT')
                          WHEN sourceFeatureRow.[FeatureKey] LIKE N'%_DELETE' THEN CONCAT(@FeaturePrefix, N'_DELETE')
                          ELSE @FeaturePrefix
                      END
                    WHERE sourceAccess.[MenuId] = @SourceMenuId
                      AND NOT EXISTS (
                          SELECT 1
                          FROM [AccessFeatures] existingFeature
                          WHERE existingFeature.[StaffMenuAccessId] = targetAccess.[Id]
                            AND existingFeature.[PermissionId] = targetFeature.[PermissionId]);
                END;

                INSERT INTO [TenantMenuPermissions]
                    ([TenantId], [MenuId], [IsAllow], [CanView], [CanAdd], [CanEdit], [CanDelete], [GrantedOnUtc], [GrantedByUserId])
                SELECT tenant.[Id], @InstructionId,
                       COALESCE(sourcePermission.[IsAllow], 1),
                       COALESCE(sourcePermission.[CanView], 1),
                       COALESCE(sourcePermission.[CanAdd], 1),
                       COALESCE(sourcePermission.[CanEdit], 1),
                       COALESCE(sourcePermission.[CanDelete], 1),
                       SYSUTCDATETIME(),
                       N'System: Instruction Menu'
                FROM [Tenants] tenant
                LEFT JOIN [TenantMenuPermissions] sourcePermission
                  ON sourcePermission.[TenantId] = tenant.[Id]
                 AND sourcePermission.[MenuId] = @SourceMenuId
                WHERE NOT EXISTS (
                    SELECT 1 FROM [TenantMenuPermissions] existingPermission
                    WHERE existingPermission.[TenantId] = tenant.[Id]
                      AND existingPermission.[MenuId] = @InstructionId);
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @InstructionId int = (
                SELECT TOP (1) [Id]
                FROM [Menus]
                WHERE [Route] = N'/settings/instruction'
                ORDER BY [Id]
            );

            IF @InstructionId IS NOT NULL
            BEGIN
                DELETE featureAccess
                FROM [AccessFeatures] featureAccess
                JOIN [StaffMenuAccess] accessRow ON accessRow.[Id] = featureAccess.[StaffMenuAccessId]
                WHERE accessRow.[MenuId] = @InstructionId;

                DELETE FROM [StaffMenuAccess] WHERE [MenuId] = @InstructionId;
                DELETE FROM [TenantMenuPermissions] WHERE [MenuId] = @InstructionId;
                DELETE FROM [MenuPermissions] WHERE [MenuId] = @InstructionId;
                DELETE FROM [Menus] WHERE [Id] = @InstructionId;
            END;
            """);
    }
}
