using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261008160000_AddSaleRoznamchaDailySales")]
public sealed class AddSaleRoznamchaDailySales : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SaleRoznamchaSaleStatuses",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                InventoryEffect = table.Column<short>(type: "smallint", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SaleRoznamchaSaleStatuses", x => x.Id);
                table.UniqueConstraint("AK_SaleRoznamchaSaleStatuses_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_SaleRoznamchaSaleStatuses_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "SaleRoznamchaDailySales",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                ProductId = table.Column<long>(type: "bigint", nullable: false),
                SaleStatusId = table.Column<int>(type: "int", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                InventoryDelta = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                BalanceAfter = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SaleRoznamchaDailySales", x => x.Id);
                table.UniqueConstraint("AK_SaleRoznamchaDailySales_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_SaleRoznamchaDailySales_SaleRoznamchaProducts_TenantId_ProductId",
                    x => new { x.TenantId, x.ProductId }, "SaleRoznamchaProducts", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SaleRoznamchaDailySales_SaleRoznamchaSaleStatuses_TenantId_SaleStatusId",
                    x => new { x.TenantId, x.SaleStatusId }, "SaleRoznamchaSaleStatuses", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SaleRoznamchaDailySales_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddColumn<long>(
            name: "DailySaleId",
            table: "SaleRoznamchaInventoryMovements",
            type: "bigint",
            nullable: true);

        migrationBuilder.CreateIndex("IX_SaleRoznamchaSaleStatuses_TenantId_Code", "SaleRoznamchaSaleStatuses", new[] { "TenantId", "Code" }, unique: true);
        migrationBuilder.CreateIndex("IX_SaleRoznamchaDailySales_TenantId_CreatedOnUtc", "SaleRoznamchaDailySales", new[] { "TenantId", "CreatedOnUtc" });
        migrationBuilder.CreateIndex("IX_SaleRoznamchaDailySales_TenantId_ProductId_CreatedOnUtc", "SaleRoznamchaDailySales", new[] { "TenantId", "ProductId", "CreatedOnUtc" });
        migrationBuilder.CreateIndex("IX_SaleRoznamchaInventoryMovements_TenantId_DailySaleId", "SaleRoznamchaInventoryMovements", new[] { "TenantId", "DailySaleId" });
        migrationBuilder.AddForeignKey(
            name: "FK_SaleRoznamchaInventoryMovements_SaleRoznamchaDailySales_TenantId_DailySaleId",
            table: "SaleRoznamchaInventoryMovements",
            columns: new[] { "TenantId", "DailySaleId" },
            principalTable: "SaleRoznamchaDailySales",
            principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql(
            """
            INSERT INTO dbo.SaleRoznamchaSaleStatuses
                (TenantId, Code, Name, InventoryEffect, IsActive, CreatedByUserId, CreatedOnUtc)
            SELECT tenant.Id, N'SOLD', N'Sold', -1, 1, N'System: Sale Roznamcha', SYSUTCDATETIME()
            FROM dbo.Tenants tenant
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.SaleRoznamchaSaleStatuses status
                WHERE status.TenantId = tenant.Id AND status.Code = N'SOLD'
            );
            """);

        migrationBuilder.Sql(SaleRoznamchaSql.DailySaleListProcedure);
        migrationBuilder.Sql(SaleRoznamchaSql.DailySaleCreateProcedure);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_DailySaleCreate;");
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_DailySaleList;");
        migrationBuilder.DropForeignKey(
            "FK_SaleRoznamchaInventoryMovements_SaleRoznamchaDailySales_TenantId_DailySaleId",
            "SaleRoznamchaInventoryMovements");
        migrationBuilder.DropIndex("IX_SaleRoznamchaInventoryMovements_TenantId_DailySaleId", "SaleRoznamchaInventoryMovements");
        migrationBuilder.DropColumn("DailySaleId", "SaleRoznamchaInventoryMovements");
        migrationBuilder.DropTable("SaleRoznamchaDailySales");
        migrationBuilder.DropTable("SaleRoznamchaSaleStatuses");
    }
}
