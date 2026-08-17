using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260812200000_HardenChatCompanyBoundary")]
public sealed class HardenChatCompanyBoundary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        IF OBJECT_ID(N'dbo.ChatWorkspaces', N'U') IS NOT NULL
        BEGIN
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.ChatWorkspaces')
                  AND name = N'UX_ChatWorkspaces_TenantId')
                DROP INDEX UX_ChatWorkspaces_TenantId ON dbo.ChatWorkspaces;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.ChatWorkspaces')
                  AND name = N'UX_ChatWorkspaces_TenantCompany')
                CREATE UNIQUE INDEX UX_ChatWorkspaces_TenantCompany
                    ON dbo.ChatWorkspaces(TenantId, OrganizationTreeId);

            DELETE workspace
            FROM dbo.ChatWorkspaces workspace
            JOIN dbo.OrganizationTree node ON node.Id = workspace.OrganizationTreeId
            WHERE node.Label NOT IN (N'Company', N'Group')
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.ChatConversations conversation
                  WHERE conversation.WorkspaceId = workspace.Id)
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.ChatContactRequests request
                  WHERE request.WorkspaceId = workspace.Id);
        END;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        IF OBJECT_ID(N'dbo.ChatWorkspaces', N'U') IS NOT NULL
        BEGIN
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.ChatWorkspaces')
                  AND name = N'UX_ChatWorkspaces_TenantCompany')
                DROP INDEX UX_ChatWorkspaces_TenantCompany ON dbo.ChatWorkspaces;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.ChatWorkspaces')
                  AND name = N'UX_ChatWorkspaces_TenantId')
                CREATE UNIQUE INDEX UX_ChatWorkspaces_TenantId
                    ON dbo.ChatWorkspaces(TenantId);
        END;
        """);
}
