using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// EF Core omits bool columns from INSERT when the value equals the configured
/// store default. The model said CanAdd/CanEdit/CanDelete default to true, but
/// SQL Server defaults were 0 — so "full CRUD" grants were silently stored as
/// View-only. Align defaults and keep existing rows unchanged.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260812140000_FixTenantMenuPermissionCrudDefaults")]
public sealed class FixTenantMenuPermissionCrudDefaults : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DECLARE @sql nvarchar(max) = N'';

            SELECT @sql = @sql + N'ALTER TABLE dbo.TenantMenuPermissions DROP CONSTRAINT [' + dc.name + N'];'
            FROM sys.default_constraints dc
            INNER JOIN sys.columns c
                ON c.default_object_id = dc.object_id
               AND c.object_id = dc.parent_object_id
            WHERE dc.parent_object_id = OBJECT_ID(N'dbo.TenantMenuPermissions')
              AND c.name IN (N'CanAdd', N'CanEdit', N'CanDelete', N'CanView');

            IF LEN(@sql) > 0 EXEC sp_executesql @sql;

            ALTER TABLE dbo.TenantMenuPermissions
                ADD CONSTRAINT DF_TenantMenuPermissions_CanView DEFAULT (CONVERT(bit, 1)) FOR CanView;
            ALTER TABLE dbo.TenantMenuPermissions
                ADD CONSTRAINT DF_TenantMenuPermissions_CanAdd DEFAULT (CONVERT(bit, 1)) FOR CanAdd;
            ALTER TABLE dbo.TenantMenuPermissions
                ADD CONSTRAINT DF_TenantMenuPermissions_CanEdit DEFAULT (CONVERT(bit, 1)) FOR CanEdit;
            ALTER TABLE dbo.TenantMenuPermissions
                ADD CONSTRAINT DF_TenantMenuPermissions_CanDelete DEFAULT (CONVERT(bit, 1)) FOR CanDelete;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE dbo.TenantMenuPermissions DROP CONSTRAINT IF EXISTS DF_TenantMenuPermissions_CanView;
            ALTER TABLE dbo.TenantMenuPermissions DROP CONSTRAINT IF EXISTS DF_TenantMenuPermissions_CanAdd;
            ALTER TABLE dbo.TenantMenuPermissions DROP CONSTRAINT IF EXISTS DF_TenantMenuPermissions_CanEdit;
            ALTER TABLE dbo.TenantMenuPermissions DROP CONSTRAINT IF EXISTS DF_TenantMenuPermissions_CanDelete;

            ALTER TABLE dbo.TenantMenuPermissions
                ADD CONSTRAINT DF_TenantMenuPermissions_CanView DEFAULT (CONVERT(bit, 1)) FOR CanView;
            ALTER TABLE dbo.TenantMenuPermissions
                ADD CONSTRAINT DF_TenantMenuPermissions_CanAdd DEFAULT (CONVERT(bit, 0)) FOR CanAdd;
            ALTER TABLE dbo.TenantMenuPermissions
                ADD CONSTRAINT DF_TenantMenuPermissions_CanEdit DEFAULT (CONVERT(bit, 0)) FOR CanEdit;
            ALTER TABLE dbo.TenantMenuPermissions
                ADD CONSTRAINT DF_TenantMenuPermissions_CanDelete DEFAULT (CONVERT(bit, 0)) FOR CanDelete;
            """);
    }
}
