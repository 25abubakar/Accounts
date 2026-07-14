using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260714200000_OptimizeInstructionManagement")]
    public sealed class OptimizeInstructionManagement : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_AppNotes_Tenant_AdminManagement'
      AND object_id = OBJECT_ID('dbo.AppNotes')
)
CREATE INDEX IX_AppNotes_Tenant_AdminManagement
ON dbo.AppNotes (TenantId, SourceTypeCode, IsDeleted, CreatedOnUtc DESC)
INCLUDE (IsPublished, IsBanner, IsPopup, IsPinned, PriorityCode);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_AppNotes_Tenant_AdminManagement'
      AND object_id = OBJECT_ID('dbo.AppNotes')
)
DROP INDEX IX_AppNotes_Tenant_AdminManagement ON dbo.AppNotes;");
        }
    }
}
