using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddChatRulesAndViewOnce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsViewOnce",
                table: "ChatAttachments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ViewOnceConsumedOnUtc",
                table: "ChatAttachments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ViewOnceExpiredOnUtc",
                table: "ChatAttachments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ViewOnceOpenedByPersonId",
                table: "ChatAttachments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ViewOnceOpenedOnUtc",
                table: "ChatAttachments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatRuleSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    AllowMessageEditing = table.Column<bool>(type: "bit", nullable: false),
                    EditWindowMinutes = table.Column<int>(type: "int", nullable: false),
                    AllowDeleteForEveryone = table.Column<bool>(type: "bit", nullable: false),
                    DeleteForEveryoneWindowMinutes = table.Column<int>(type: "int", nullable: false),
                    AllowViewOnceMedia = table.Column<bool>(type: "bit", nullable: false),
                    ViewOnceUnopenedExpiryHours = table.Column<int>(type: "int", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatRuleSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatRuleSettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatAttachments_TenantId_IsViewOnce_ViewOnceConsumedOnUtc_ViewOnceExpiredOnUtc",
                table: "ChatAttachments",
                columns: new[] { "TenantId", "IsViewOnce", "ViewOnceConsumedOnUtc", "ViewOnceExpiredOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatRuleSettings_TenantId",
                table: "ChatRuleSettings",
                column: "TenantId",
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO [ChatRuleSettings]
                    ([TenantId], [AllowMessageEditing], [EditWindowMinutes],
                     [AllowDeleteForEveryone], [DeleteForEveryoneWindowMinutes],
                     [AllowViewOnceMedia], [ViewOnceUnopenedExpiryHours],
                     [UpdatedByUserId], [CreatedOnUtc], [UpdatedOnUtc])
                SELECT tenant.[Id], 1, 15, 1, 3600, 1, 336,
                       N'System: Chat Rules defaults', SYSUTCDATETIME(), SYSUTCDATETIME()
                FROM [Tenants] tenant
                WHERE NOT EXISTS (
                    SELECT 1 FROM [ChatRuleSettings] existing
                    WHERE existing.[TenantId] = tenant.[Id]);

                DECLARE @ChatParentId int = (
                    SELECT TOP (1) [Id]
                    FROM [Menus]
                    WHERE [ParentId] IS NULL AND [Title] = N'Chat'
                    ORDER BY [Id]
                );
                DECLARE @ChatRulesMenuId int = (
                    SELECT TOP (1) [Id]
                    FROM [Menus]
                    WHERE [Route] = N'/chat/rules'
                       OR ([ParentId] = @ChatParentId AND [Title] = N'Chat Rules')
                    ORDER BY CASE WHEN [Route] = N'/chat/rules' THEN 0 ELSE 1 END, [Id]
                );

                IF @ChatRulesMenuId IS NULL AND @ChatParentId IS NOT NULL
                BEGIN
                    INSERT INTO [Menus] ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                    VALUES (N'Chat Rules', N'ShieldCheck', N'/chat/rules', @ChatParentId, 2, 1);
                    SET @ChatRulesMenuId = SCOPE_IDENTITY();
                END;

                IF @ChatRulesMenuId IS NOT NULL
                BEGIN
                    UPDATE [Menus]
                    SET [Title] = N'Chat Rules', [Icon] = N'ShieldCheck',
                        [Route] = N'/chat/rules', [ParentId] = @ChatParentId,
                        [SortOrder] = 2, [IsActive] = 1
                    WHERE [Id] = @ChatRulesMenuId;

                    DECLARE @ChatRuleFeatures TABLE
                    (
                        [FeatureKey] nvarchar(100),
                        [FeatureName] nvarchar(150),
                        [Description] nvarchar(250)
                    );
                    INSERT INTO @ChatRuleFeatures VALUES
                        (CONCAT(N'MENU_', @ChatRulesMenuId), N'Chat Rules', N'Open Chat Rules'),
                        (CONCAT(N'MENU_', @ChatRulesMenuId, N'_VIEW'), N'Chat Rules - View', N'View Chat Rules'),
                        (CONCAT(N'MENU_', @ChatRulesMenuId, N'_ADD'), N'Chat Rules - Add', N'Create Chat Rules'),
                        (CONCAT(N'MENU_', @ChatRulesMenuId, N'_EDIT'), N'Chat Rules - Edit', N'Edit Chat Rules'),
                        (CONCAT(N'MENU_', @ChatRulesMenuId, N'_DELETE'), N'Chat Rules - Delete', N'Delete Chat Rules');

                    INSERT INTO [Features]
                        ([FeatureKey], [FeatureName], [Module], [Description], [CreatedDate])
                    SELECT source.[FeatureKey], source.[FeatureName], N'Chat',
                           source.[Description], SYSUTCDATETIME()
                    FROM @ChatRuleFeatures source
                    WHERE NOT EXISTS (
                        SELECT 1 FROM [Features] existing
                        WHERE existing.[FeatureKey] = source.[FeatureKey]);

                    INSERT INTO [MenuPermissions] ([MenuId], [PermissionId])
                    SELECT @ChatRulesMenuId, feature.[PermissionId]
                    FROM [Features] feature
                    JOIN @ChatRuleFeatures source ON source.[FeatureKey] = feature.[FeatureKey]
                    WHERE NOT EXISTS (
                        SELECT 1 FROM [MenuPermissions] existing
                        WHERE existing.[MenuId] = @ChatRulesMenuId
                          AND existing.[PermissionId] = feature.[PermissionId]);

                    INSERT INTO [TenantMenuPermissions]
                        ([TenantId], [MenuId], [IsAllow], [CanView], [CanAdd], [CanEdit], [CanDelete],
                         [GrantedOnUtc], [GrantedByUserId])
                    SELECT tenant.[Id], @ChatRulesMenuId, 1, 1, 1, 1, 0,
                           SYSUTCDATETIME(), N'System: Chat Rules'
                    FROM [Tenants] tenant
                    WHERE NOT EXISTS (
                        SELECT 1 FROM [TenantMenuPermissions] existing
                        WHERE existing.[TenantId] = tenant.[Id]
                          AND existing.[MenuId] = @ChatRulesMenuId);
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DECLARE @ChatRulesMenuId int = (
                    SELECT TOP (1) [Id] FROM [Menus]
                    WHERE [Route] = N'/chat/rules' ORDER BY [Id]
                );

                DELETE accessFeature
                FROM [AccessFeatures] accessFeature
                JOIN [StaffMenuAccess] accessRow
                  ON accessRow.[Id] = accessFeature.[StaffMenuAccessId]
                WHERE accessRow.[MenuId] = @ChatRulesMenuId;
                DELETE FROM [StaffMenuAccess] WHERE [MenuId] = @ChatRulesMenuId;
                DELETE FROM [TenantMenuPermissions] WHERE [MenuId] = @ChatRulesMenuId;
                DELETE FROM [MenuPermissions] WHERE [MenuId] = @ChatRulesMenuId;
                DELETE FROM [Menus] WHERE [Id] = @ChatRulesMenuId;

                DELETE FROM [Features]
                WHERE [FeatureKey] IN
                    (CONCAT(N'MENU_', @ChatRulesMenuId),
                     CONCAT(N'MENU_', @ChatRulesMenuId, N'_VIEW'),
                     CONCAT(N'MENU_', @ChatRulesMenuId, N'_ADD'),
                     CONCAT(N'MENU_', @ChatRulesMenuId, N'_EDIT'),
                     CONCAT(N'MENU_', @ChatRulesMenuId, N'_DELETE'))
                  AND NOT EXISTS (
                      SELECT 1 FROM [RolePermissions]
                      WHERE [RolePermissions].[PermissionId] = [Features].[PermissionId])
                  AND NOT EXISTS (
                      SELECT 1 FROM [UserPermissionOverrides]
                      WHERE [UserPermissionOverrides].[PermissionId] = [Features].[PermissionId]);
                """);

            migrationBuilder.DropTable(
                name: "ChatRuleSettings");

            migrationBuilder.DropIndex(
                name: "IX_ChatAttachments_TenantId_IsViewOnce_ViewOnceConsumedOnUtc_ViewOnceExpiredOnUtc",
                table: "ChatAttachments");

            migrationBuilder.DropColumn(
                name: "IsViewOnce",
                table: "ChatAttachments");

            migrationBuilder.DropColumn(
                name: "ViewOnceConsumedOnUtc",
                table: "ChatAttachments");

            migrationBuilder.DropColumn(
                name: "ViewOnceExpiredOnUtc",
                table: "ChatAttachments");

            migrationBuilder.DropColumn(
                name: "ViewOnceOpenedByPersonId",
                table: "ChatAttachments");

            migrationBuilder.DropColumn(
                name: "ViewOnceOpenedOnUtc",
                table: "ChatAttachments");
        }
    }
}
