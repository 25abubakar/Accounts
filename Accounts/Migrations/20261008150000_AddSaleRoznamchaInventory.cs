using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261008150000_AddSaleRoznamchaInventory")]
public sealed class AddSaleRoznamchaInventory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddUniqueConstraint(
            name: "AK_SaleRoznamchaProductCategories_TenantId_Id",
            table: "SaleRoznamchaProductCategories",
            columns: new[] { "TenantId", "Id" });

        migrationBuilder.CreateTable(
            name: "SaleRoznamchaProducts",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                ProductCategoryId = table.Column<int>(type: "int", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                QuantityOnHand = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SaleRoznamchaProducts", x => x.Id);
                table.UniqueConstraint("AK_SaleRoznamchaProducts_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey(
                    "FK_SaleRoznamchaProducts_SaleRoznamchaProductCategories_TenantId_ProductCategoryId",
                    x => new { x.TenantId, x.ProductCategoryId },
                    "SaleRoznamchaProductCategories",
                    new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    "FK_SaleRoznamchaProducts_Tenants_TenantId",
                    x => x.TenantId,
                    "Tenants",
                    "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "SaleRoznamchaInventoryMovements",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                ProductId = table.Column<long>(type: "bigint", nullable: false),
                MovementType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                QuantityDelta = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                BalanceAfter = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SaleRoznamchaInventoryMovements", x => x.Id);
                table.ForeignKey(
                    "FK_SaleRoznamchaInventoryMovements_SaleRoznamchaProducts_TenantId_ProductId",
                    x => new { x.TenantId, x.ProductId },
                    "SaleRoznamchaProducts",
                    new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    "FK_SaleRoznamchaInventoryMovements_Tenants_TenantId",
                    x => x.TenantId,
                    "Tenants",
                    "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            "IX_SaleRoznamchaProducts_TenantId_ProductCategoryId_Name",
            "SaleRoznamchaProducts",
            new[] { "TenantId", "ProductCategoryId", "Name" },
            unique: true);
        migrationBuilder.CreateIndex(
            "IX_SaleRoznamchaProducts_TenantId_IsDeleted_IsActive",
            "SaleRoznamchaProducts",
            new[] { "TenantId", "IsDeleted", "IsActive" });
        migrationBuilder.CreateIndex(
            "IX_SaleRoznamchaInventoryMovements_TenantId_ProductId_CreatedOnUtc",
            "SaleRoznamchaInventoryMovements",
            new[] { "TenantId", "ProductId", "CreatedOnUtc" });

        migrationBuilder.Sql(SaleRoznamchaSql.InventoryListProcedure);

        // This module was introduced after several tenants already existed.
        // Backfill the route ceiling so the visible menu and its API agree.
        migrationBuilder.Sql(
            """
            UPDATE permission
            SET permission.CanView = 1,
                permission.CanAdd = 1,
                permission.CanEdit = 1,
                permission.CanDelete = 1,
                permission.IsAllow = 1
            FROM dbo.TenantMenuPermissions permission
            INNER JOIN dbo.Menus menu ON menu.Id = permission.MenuId
            WHERE menu.Route IN (
                N'/sale-roznamcha/management',
                N'/sale-roznamcha/inventory',
                N'/sale-roznamcha/daily-sale',
                N'/sale-roznamcha/history'
            );

            INSERT INTO dbo.TenantMenuPermissions
                (TenantId, MenuId, CanView, CanAdd, CanEdit, CanDelete, IsAllow, GrantedOnUtc)
            SELECT tenant.Id, menu.Id, 1, 1, 1, 1, 1, SYSUTCDATETIME()
            FROM dbo.Tenants tenant
            CROSS JOIN dbo.Menus menu
            WHERE menu.Route IN (
                N'/sale-roznamcha/management',
                N'/sale-roznamcha/inventory',
                N'/sale-roznamcha/daily-sale',
                N'/sale-roznamcha/history'
            )
            AND NOT EXISTS (
                SELECT 1
                FROM dbo.TenantMenuPermissions permission
                WHERE permission.TenantId = tenant.Id AND permission.MenuId = menu.Id
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_InventoryList;");
        migrationBuilder.DropTable("SaleRoznamchaInventoryMovements");
        migrationBuilder.DropTable("SaleRoznamchaProducts");
        migrationBuilder.DropUniqueConstraint(
            "AK_SaleRoznamchaProductCategories_TenantId_Id",
            "SaleRoznamchaProductCategories");
    }
}
