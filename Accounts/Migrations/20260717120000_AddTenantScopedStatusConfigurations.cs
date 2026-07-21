using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260717120000_AddTenantScopedStatusConfigurations")]
public sealed class AddTenantScopedStatusConfigurations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.ProcessStatusStyles','TenantId') IS NULL
                ALTER TABLE dbo.ProcessStatusStyles ADD TenantId int NULL;
            IF COL_LENGTH('dbo.ProcessStatusStyles','IsSystem') IS NULL
                ALTER TABLE dbo.ProcessStatusStyles ADD IsSystem bit NOT NULL
                    CONSTRAINT DF_ProcessStatusStyles_IsSystem DEFAULT(1);
            """);

        // This must be a separate command. SQL Server compiles a complete batch
        // before executing ALTER TABLE, and otherwise reports TenantId as invalid
        // when compiling the UPDATE/index statements below.
        migrationBuilder.Sql("""
            UPDATE dbo.ProcessStatusStyles SET IsSystem=1 WHERE TenantId IS NULL;

            IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name='UQ_ProcessStatusStyles_Code')
                ALTER TABLE dbo.ProcessStatusStyles DROP CONSTRAINT UQ_ProcessStatusStyles_Code;
            IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name='UQ_ProcessStatusStyles_Assignment')
                ALTER TABLE dbo.ProcessStatusStyles DROP CONSTRAINT UQ_ProcessStatusStyles_Assignment;
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.ProcessStatusStyles') AND name='IX_ProcessStatusStyles_ProcessId_Code')
                DROP INDEX IX_ProcessStatusStyles_ProcessId_Code ON dbo.ProcessStatusStyles;
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.ProcessStatusStyles') AND name='IX_ProcessStatusStyles_ProcessId_StatusId_ColorStyleId')
                DROP INDEX IX_ProcessStatusStyles_ProcessId_StatusId_ColorStyleId ON dbo.ProcessStatusStyles;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.ProcessStatusStyles') AND name='IX_ProcessStatusStyles_Global_Process_Code')
                CREATE UNIQUE INDEX IX_ProcessStatusStyles_Global_Process_Code
                    ON dbo.ProcessStatusStyles(ProcessId,Code) WHERE TenantId IS NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.ProcessStatusStyles') AND name='IX_ProcessStatusStyles_Tenant_Process_Code')
                CREATE UNIQUE INDEX IX_ProcessStatusStyles_Tenant_Process_Code
                    ON dbo.ProcessStatusStyles(TenantId,ProcessId,Code) WHERE TenantId IS NOT NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.ProcessStatusStyles') AND name='IX_ProcessStatusStyles_TenantId')
                CREATE INDEX IX_ProcessStatusStyles_TenantId ON dbo.ProcessStatusStyles(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_ProcessStatusStyles_Tenants_TenantId')
                ALTER TABLE dbo.ProcessStatusStyles ADD CONSTRAINT FK_ProcessStatusStyles_Tenants_TenantId
                    FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name='FK_ProcessStatusStyles_Tenants_TenantId')
                ALTER TABLE dbo.ProcessStatusStyles DROP CONSTRAINT FK_ProcessStatusStyles_Tenants_TenantId;
            DROP INDEX IF EXISTS IX_ProcessStatusStyles_Global_Process_Code ON dbo.ProcessStatusStyles;
            DROP INDEX IF EXISTS IX_ProcessStatusStyles_Tenant_Process_Code ON dbo.ProcessStatusStyles;
            DROP INDEX IF EXISTS IX_ProcessStatusStyles_TenantId ON dbo.ProcessStatusStyles;
            ALTER TABLE dbo.ProcessStatusStyles DROP CONSTRAINT DF_ProcessStatusStyles_IsSystem;
            ALTER TABLE dbo.ProcessStatusStyles DROP COLUMN IsSystem, TenantId;
            ALTER TABLE dbo.ProcessStatusStyles ADD CONSTRAINT UQ_ProcessStatusStyles_Code UNIQUE(ProcessId,Code);
            """);
    }
}
