using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class SeparateSaleRoznamchaProductAndPurchaseImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SaleRoznamchaProducts_TenantId_ProductCategoryId_Name",
                table: "SaleRoznamchaProducts");

            migrationBuilder.AlterColumn<decimal>(
                name: "PurchasePrice",
                table: "SaleRoznamchaProducts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<int>(
                name: "CurrencyId",
                table: "SaleRoznamchaProducts",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateTable(
                name: "SaleRoznamchaPurchaseImports",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    FileHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    ImportedRows = table.Column<int>(type: "int", nullable: false),
                    ProductCount = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleRoznamchaPurchaseImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleRoznamchaPurchaseImports_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaProducts_TenantId_ProductCategoryId_Name",
                table: "SaleRoznamchaProducts",
                columns: new[] { "TenantId", "ProductCategoryId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaPurchaseImports_TenantId_CreatedOnUtc",
                table: "SaleRoznamchaPurchaseImports",
                columns: new[] { "TenantId", "CreatedOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaPurchaseImports_TenantId_FileHash",
                table: "SaleRoznamchaPurchaseImports",
                columns: new[] { "TenantId", "FileHash" },
                unique: true);

            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.ProductMasterCreateProcedure));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryListProcedure));
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_InventoryProductCreate;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SaleRoznamchaPurchaseImports");

            migrationBuilder.DropIndex(
                name: "IX_SaleRoznamchaProducts_TenantId_ProductCategoryId_Name",
                table: "SaleRoznamchaProducts");

            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.SaleRoznamchaProducts WHERE CurrencyId IS NULL)
                BEGIN
                    DECLARE @FallbackCurrencyId int = (SELECT TOP (1) Id FROM dbo.AccountsCurrencies WHERE IsActive = 1 ORDER BY CASE WHEN TenantId IS NULL THEN 1 ELSE 0 END, Id);
                    IF @FallbackCurrencyId IS NULL THROW 51000, 'Cannot roll back because no active currency exists.', 1;
                    UPDATE dbo.SaleRoznamchaProducts
                    SET PurchasePrice = COALESCE(PurchasePrice, 0), CurrencyId = @FallbackCurrencyId
                    WHERE CurrencyId IS NULL;
                END
                """);

            migrationBuilder.AlterColumn<decimal>(
                name: "PurchasePrice",
                table: "SaleRoznamchaProducts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "CurrencyId",
                table: "SaleRoznamchaProducts",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleRoznamchaProducts_TenantId_ProductCategoryId_Name",
                table: "SaleRoznamchaProducts",
                columns: new[] { "TenantId", "ProductCategoryId", "Name" },
                unique: true);

            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryProductCreateProcedure));
            migrationBuilder.Sql(ExecutableBatch(global::Accounts.Data.SaleRoznamchaSql.InventoryListProcedure));
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_ProductCreate;");
        }

        private static string ExecutableBatch(string sql) => $"EXEC(N'{sql.Replace("'", "''")}');";
    }
}
