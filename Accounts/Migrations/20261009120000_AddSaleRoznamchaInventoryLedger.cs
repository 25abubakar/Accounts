using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261009120000_AddSaleRoznamchaInventoryLedger")]
public sealed class AddSaleRoznamchaInventoryLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("Code", "SaleRoznamchaPlatforms", "nvarchar(20)", maxLength: 20, nullable: true);
        migrationBuilder.AddColumn<string>("Code", "SaleRoznamchaProductCategories", "nvarchar(20)", maxLength: 20, nullable: true);
        migrationBuilder.AddColumn<string>("ProductCode", "SaleRoznamchaProducts", "nvarchar(80)", maxLength: 80, nullable: true);
        migrationBuilder.AddColumn<long>("StockReceiptId", "SaleRoznamchaInventoryMovements", "bigint", nullable: true);

        migrationBuilder.Sql(
            """
            ;WITH Codes AS
            (
                SELECT Id,
                       CASE WHEN COUNT(*) OVER (PARTITION BY TenantId, CompanyId, Candidate) = 1
                            THEN Candidate ELSE LEFT(Candidate, 14) + CONVERT(nvarchar(6), Id) END Code
                FROM
                (
                    SELECT Id, TenantId, CompanyId,
                           COALESCE(NULLIF(LEFT(UPPER(REPLACE(REPLACE(REPLACE(Name, N' ', N''), N'-', N''), N'_', N'')), 3), N''), N'PLT') Candidate
                    FROM dbo.SaleRoznamchaPlatforms
                ) source
            )
            UPDATE platform SET Code = codes.Code
            FROM dbo.SaleRoznamchaPlatforms platform INNER JOIN Codes codes ON codes.Id = platform.Id;

            ;WITH Codes AS
            (
                SELECT Id,
                       CASE WHEN COUNT(*) OVER (PARTITION BY TenantId, PlatformId, Candidate) = 1
                            THEN Candidate ELSE LEFT(Candidate, 14) + CONVERT(nvarchar(6), Id) END Code
                FROM
                (
                    SELECT Id, TenantId, PlatformId,
                           COALESCE(NULLIF(LEFT(UPPER(REPLACE(REPLACE(REPLACE(Name, N' ', N''), N'-', N''), N'_', N'')), 3), N''), N'CAT') Candidate
                    FROM dbo.SaleRoznamchaProductCategories
                ) source
            )
            UPDATE productCategory SET Code = codes.Code
            FROM dbo.SaleRoznamchaProductCategories productCategory INNER JOIN Codes codes ON codes.Id = productCategory.Id;

            ;WITH ProductCodes AS
            (
                SELECT product.Id,
                       CONCAT(platform.Code, N'-', productCategory.Code, N'-',
                              RIGHT(N'000' + CONVERT(nvarchar(10),
                                  ROW_NUMBER() OVER (PARTITION BY product.TenantId, platform.Code, productCategory.Code ORDER BY product.Id)), 3)) ProductCode
                FROM dbo.SaleRoznamchaProducts product
                INNER JOIN dbo.SaleRoznamchaProductCategories productCategory
                    ON productCategory.TenantId = product.TenantId AND productCategory.Id = product.ProductCategoryId
                INNER JOIN dbo.SaleRoznamchaPlatforms platform
                    ON platform.TenantId = productCategory.TenantId AND platform.Id = productCategory.PlatformId
            )
            UPDATE product SET ProductCode = codes.ProductCode
            FROM dbo.SaleRoznamchaProducts product INNER JOIN ProductCodes codes ON codes.Id = product.Id;
            """);

        migrationBuilder.AlterColumn<string>("Code", "SaleRoznamchaPlatforms", "nvarchar(20)", maxLength: 20, nullable: false, oldClrType: typeof(string), oldType: "nvarchar(20)", oldMaxLength: 20, oldNullable: true);
        migrationBuilder.AlterColumn<string>("Code", "SaleRoznamchaProductCategories", "nvarchar(20)", maxLength: 20, nullable: false, oldClrType: typeof(string), oldType: "nvarchar(20)", oldMaxLength: 20, oldNullable: true);
        migrationBuilder.AlterColumn<string>("ProductCode", "SaleRoznamchaProducts", "nvarchar(80)", maxLength: 80, nullable: false, oldClrType: typeof(string), oldType: "nvarchar(80)", oldMaxLength: 80, oldNullable: true);

        migrationBuilder.CreateIndex("IX_SaleRoznamchaPlatforms_TenantId_CompanyId_Code", "SaleRoznamchaPlatforms", new[] { "TenantId", "CompanyId", "Code" }, unique: true);
        migrationBuilder.CreateIndex("IX_SaleRoznamchaProductCategories_TenantId_PlatformId_Code", "SaleRoznamchaProductCategories", new[] { "TenantId", "PlatformId", "Code" }, unique: true);
        migrationBuilder.CreateIndex("IX_SaleRoznamchaProducts_TenantId_ProductCode", "SaleRoznamchaProducts", new[] { "TenantId", "ProductCode" }, unique: true);

        migrationBuilder.CreateTable(
            name: "SaleRoznamchaStockReceipts",
            columns: table => new
            {
                Id = table.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>("int", nullable: false),
                ProductId = table.Column<long>("bigint", nullable: false),
                PurchasedQuantity = table.Column<decimal>("decimal(18,4)", nullable: false),
                RemainingQuantity = table.Column<decimal>("decimal(18,4)", nullable: false),
                UnitCost = table.Column<decimal>("decimal(18,2)", nullable: false),
                CurrencyId = table.Column<int>("int", nullable: false),
                PurchasedOnUtc = table.Column<DateTime>("datetime2", nullable: false),
                CreatedByUserId = table.Column<string>("nvarchar(450)", maxLength: 450, nullable: true),
                CreatedOnUtc = table.Column<DateTime>("datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SaleRoznamchaStockReceipts", x => x.Id);
                table.UniqueConstraint("AK_SaleRoznamchaStockReceipts_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_SaleRoznamchaStockReceipts_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SaleRoznamchaStockReceipts_SaleRoznamchaProducts_TenantId_ProductId", x => new { x.TenantId, x.ProductId }, "SaleRoznamchaProducts", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SaleRoznamchaStockReceipts_AccountsCurrencies_CurrencyId", x => x.CurrencyId, "AccountsCurrencies", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_SaleRoznamchaStockReceipts_CurrencyId", "SaleRoznamchaStockReceipts", "CurrencyId");
        migrationBuilder.CreateIndex("IX_SaleRoznamchaStockReceipts_TenantId_ProductId_PurchasedOnUtc_Id", "SaleRoznamchaStockReceipts", new[] { "TenantId", "ProductId", "PurchasedOnUtc", "Id" });
        migrationBuilder.CreateIndex("IX_SaleRoznamchaStockReceipts_TenantId_ProductId_RemainingQuantity", "SaleRoznamchaStockReceipts", new[] { "TenantId", "ProductId", "RemainingQuantity" });

        migrationBuilder.CreateTable(
            name: "SaleRoznamchaDailySaleStockAllocations",
            columns: table => new
            {
                Id = table.Column<long>("bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>("int", nullable: false),
                DailySaleId = table.Column<long>("bigint", nullable: false),
                StockReceiptId = table.Column<long>("bigint", nullable: false),
                Quantity = table.Column<decimal>("decimal(18,4)", nullable: false),
                UnitCost = table.Column<decimal>("decimal(18,2)", nullable: false),
                CurrencyId = table.Column<int>("int", nullable: false),
                CreatedOnUtc = table.Column<DateTime>("datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SaleRoznamchaDailySaleStockAllocations", x => x.Id);
                table.ForeignKey("FK_SaleRoznamchaDailySaleStockAllocations_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SaleRoznamchaDailySaleStockAllocations_SaleRoznamchaDailySales_TenantId_DailySaleId", x => new { x.TenantId, x.DailySaleId }, "SaleRoznamchaDailySales", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SaleRoznamchaDailySaleStockAllocations_SaleRoznamchaStockReceipts_TenantId_StockReceiptId", x => new { x.TenantId, x.StockReceiptId }, "SaleRoznamchaStockReceipts", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SaleRoznamchaDailySaleStockAllocations_AccountsCurrencies_CurrencyId", x => x.CurrencyId, "AccountsCurrencies", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_SaleRoznamchaDailySaleStockAllocations_CurrencyId", "SaleRoznamchaDailySaleStockAllocations", "CurrencyId");
        migrationBuilder.CreateIndex("IX_SaleRoznamchaDailySaleStockAllocations_TenantId_DailySaleId_StockReceiptId", "SaleRoznamchaDailySaleStockAllocations", new[] { "TenantId", "DailySaleId", "StockReceiptId" }, unique: true);
        migrationBuilder.CreateIndex("IX_SaleRoznamchaDailySaleStockAllocations_TenantId_StockReceiptId", "SaleRoznamchaDailySaleStockAllocations", new[] { "TenantId", "StockReceiptId" });

        migrationBuilder.Sql(
            """
            INSERT INTO dbo.SaleRoznamchaStockReceipts
                (TenantId, ProductId, PurchasedQuantity, RemainingQuantity, UnitCost, CurrencyId,
                 PurchasedOnUtc, CreatedByUserId, CreatedOnUtc)
            SELECT product.TenantId, product.Id,
                   CASE WHEN COALESCE(movement.PositiveQuantity, 0) > product.QuantityOnHand
                        THEN movement.PositiveQuantity ELSE product.QuantityOnHand END,
                   product.QuantityOnHand, product.PurchasePrice, product.CurrencyId,
                   product.CreatedOnUtc, product.CreatedByUserId, product.CreatedOnUtc
            FROM dbo.SaleRoznamchaProducts product
            OUTER APPLY
            (
                SELECT SUM(CASE WHEN QuantityDelta > 0 THEN QuantityDelta ELSE 0 END) PositiveQuantity
                FROM dbo.SaleRoznamchaInventoryMovements movement
                WHERE movement.TenantId = product.TenantId AND movement.ProductId = product.Id
            ) movement
            WHERE product.QuantityOnHand > 0 OR COALESCE(movement.PositiveQuantity, 0) > 0;
            """);

        migrationBuilder.CreateIndex("IX_SaleRoznamchaInventoryMovements_TenantId_StockReceiptId", "SaleRoznamchaInventoryMovements", new[] { "TenantId", "StockReceiptId" });
        migrationBuilder.AddForeignKey(
            name: "FK_SaleRoznamchaInventoryMovements_SaleRoznamchaStockReceipts_TenantId_StockReceiptId",
            table: "SaleRoznamchaInventoryMovements",
            columns: new[] { "TenantId", "StockReceiptId" },
            principalTable: "SaleRoznamchaStockReceipts",
            principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql(SaleRoznamchaSql.MasterHierarchyProcedure);
        migrationBuilder.Sql(SaleRoznamchaSql.InventoryListProcedure);
        migrationBuilder.Sql(SaleRoznamchaSql.InventoryProductCreateProcedure);
        migrationBuilder.Sql(SaleRoznamchaSql.InventoryProductUpdateProcedure);
        migrationBuilder.Sql(SaleRoznamchaSql.InventoryStockAddProcedure);
        migrationBuilder.Sql(SaleRoznamchaSql.DailySaleListProcedure);
        migrationBuilder.Sql(SaleRoznamchaSql.DailySaleCreateProcedure);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_InventoryStockAdd;");
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_InventoryProductUpdate;");
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_InventoryProductCreate;");
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_InventoryList;");
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_DailySaleCreate;");

        migrationBuilder.DropForeignKey("FK_SaleRoznamchaInventoryMovements_SaleRoznamchaStockReceipts_TenantId_StockReceiptId", "SaleRoznamchaInventoryMovements");
        migrationBuilder.DropIndex("IX_SaleRoznamchaInventoryMovements_TenantId_StockReceiptId", "SaleRoznamchaInventoryMovements");
        migrationBuilder.DropTable("SaleRoznamchaDailySaleStockAllocations");
        migrationBuilder.DropTable("SaleRoznamchaStockReceipts");
        migrationBuilder.DropIndex("IX_SaleRoznamchaPlatforms_TenantId_CompanyId_Code", "SaleRoznamchaPlatforms");
        migrationBuilder.DropIndex("IX_SaleRoznamchaProductCategories_TenantId_PlatformId_Code", "SaleRoznamchaProductCategories");
        migrationBuilder.DropIndex("IX_SaleRoznamchaProducts_TenantId_ProductCode", "SaleRoznamchaProducts");
        migrationBuilder.DropColumn("StockReceiptId", "SaleRoznamchaInventoryMovements");
        migrationBuilder.DropColumn("ProductCode", "SaleRoznamchaProducts");
        migrationBuilder.DropColumn("Code", "SaleRoznamchaProductCategories");
        migrationBuilder.DropColumn("Code", "SaleRoznamchaPlatforms");
    }
}
