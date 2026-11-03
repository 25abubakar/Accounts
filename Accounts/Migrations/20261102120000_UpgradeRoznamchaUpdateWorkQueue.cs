using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261102120000_UpgradeRoznamchaUpdateWorkQueue")]
public sealed class UpgradeRoznamchaUpdateWorkQueue : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "OldCheckNo", table: "RoznamchaEntryProcessLogs", type: "nvarchar(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>(name: "NewCheckNo", table: "RoznamchaEntryProcessLogs", type: "nvarchar(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<DateOnly>(name: "RemindDate", table: "RoznamchaEntryProcessLogs", type: "date", nullable: true);
        migrationBuilder.AddColumn<int>(name: "StatusId", table: "RoznamchaEntryProcessLogs", type: "int", nullable: true);

        migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.RoznamchaEntryAccesses') AND name = N'IX_RoznamchaEntryAccesses_TenantId_RoznamchaEntryId_DepartmentId')
    DROP INDEX IX_RoznamchaEntryAccesses_TenantId_RoznamchaEntryId_DepartmentId ON dbo.RoznamchaEntryAccesses;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.RoznamchaEntryAccesses') AND name = N'IX_RoznamchaEntryAccesses_TenantId_RoznamchaEntryId_DepartmentId_StaffId')
    CREATE UNIQUE INDEX IX_RoznamchaEntryAccesses_TenantId_RoznamchaEntryId_DepartmentId_StaffId
    ON dbo.RoznamchaEntryAccesses(TenantId, RoznamchaEntryId, DepartmentId, StaffId);");

        var assembly = typeof(UpgradeRoznamchaUpdateWorkQueue).Assembly;
        using var stream = assembly.GetManifestResourceStream("Accounts.Sql.StoredProcedures.usp_Accounts_RoznamchaUpdate_List.sql")
            ?? throw new InvalidOperationException("Missing Roznamcha Update procedure resource.");
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd().Replace("\r\nGO\r\n", "\r\n", StringComparison.OrdinalIgnoreCase)
            .Replace("\nGO\n", "\n", StringComparison.OrdinalIgnoreCase);
        migrationBuilder.Sql(sql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_Accounts_RoznamchaUpdate_List;");
        migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.RoznamchaEntryAccesses') AND name = N'IX_RoznamchaEntryAccesses_TenantId_RoznamchaEntryId_DepartmentId_StaffId')
    DROP INDEX IX_RoznamchaEntryAccesses_TenantId_RoznamchaEntryId_DepartmentId_StaffId ON dbo.RoznamchaEntryAccesses;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.RoznamchaEntryAccesses') AND name = N'IX_RoznamchaEntryAccesses_TenantId_RoznamchaEntryId_DepartmentId')
    CREATE UNIQUE INDEX IX_RoznamchaEntryAccesses_TenantId_RoznamchaEntryId_DepartmentId
    ON dbo.RoznamchaEntryAccesses(TenantId, RoznamchaEntryId, DepartmentId);");
        migrationBuilder.DropColumn(name: "OldCheckNo", table: "RoznamchaEntryProcessLogs");
        migrationBuilder.DropColumn(name: "NewCheckNo", table: "RoznamchaEntryProcessLogs");
        migrationBuilder.DropColumn(name: "RemindDate", table: "RoznamchaEntryProcessLogs");
        migrationBuilder.DropColumn(name: "StatusId", table: "RoznamchaEntryProcessLogs");
    }
}
