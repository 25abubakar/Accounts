using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260720250000_AddRuntimePerformanceIndexes")]
public sealed class AddRuntimePerformanceIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // These indexes are present in the EF model but were absent from the
        // deployed database because the RBAC tables predate that model mapping.
        // Permission checks run on virtually every protected API request.
        migrationBuilder.Sql(
            """
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StaffMenuAccess') AND name = N'IX_StaffMenuAccess_StaffId_MenuId')
                CREATE UNIQUE INDEX IX_StaffMenuAccess_StaffId_MenuId ON dbo.StaffMenuAccess (StaffId, MenuId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StaffMenuAccess') AND name = N'IX_StaffMenuAccess_StaffId')
                CREATE INDEX IX_StaffMenuAccess_StaffId ON dbo.StaffMenuAccess (StaffId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StaffMenuAccess') AND name = N'IX_StaffMenuAccess_MenuId')
                CREATE INDEX IX_StaffMenuAccess_MenuId ON dbo.StaffMenuAccess (MenuId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AccessFeatures') AND name = N'IX_AccessFeatures_StaffMenuAccessId_PermissionId')
                CREATE UNIQUE INDEX IX_AccessFeatures_StaffMenuAccessId_PermissionId ON dbo.AccessFeatures (StaffMenuAccessId, PermissionId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AccessFeatures') AND name = N'IX_AccessFeatures_StaffMenuAccessId')
                CREATE INDEX IX_AccessFeatures_StaffMenuAccessId ON dbo.AccessFeatures (StaffMenuAccessId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AccessFeatures') AND name = N'IX_AccessFeatures_PermissionId')
                CREATE INDEX IX_AccessFeatures_PermissionId ON dbo.AccessFeatures (PermissionId);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AccessFeatures') AND name = N'IX_AccessFeatures_PermissionId')
                DROP INDEX IX_AccessFeatures_PermissionId ON dbo.AccessFeatures;
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AccessFeatures') AND name = N'IX_AccessFeatures_StaffMenuAccessId')
                DROP INDEX IX_AccessFeatures_StaffMenuAccessId ON dbo.AccessFeatures;
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AccessFeatures') AND name = N'IX_AccessFeatures_StaffMenuAccessId_PermissionId')
                DROP INDEX IX_AccessFeatures_StaffMenuAccessId_PermissionId ON dbo.AccessFeatures;
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StaffMenuAccess') AND name = N'IX_StaffMenuAccess_MenuId')
                DROP INDEX IX_StaffMenuAccess_MenuId ON dbo.StaffMenuAccess;
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StaffMenuAccess') AND name = N'IX_StaffMenuAccess_StaffId')
                DROP INDEX IX_StaffMenuAccess_StaffId ON dbo.StaffMenuAccess;
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StaffMenuAccess') AND name = N'IX_StaffMenuAccess_StaffId_MenuId')
                DROP INDEX IX_StaffMenuAccess_StaffId_MenuId ON dbo.StaffMenuAccess;
            """);
    }
}
