using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261008143000_AddSaleRoznamchaMasterHierarchy")]
public sealed class AddSaleRoznamchaMasterHierarchy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SaleRoznamchaCategories",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SaleRoznamchaCategories", x => x.Id);
                table.UniqueConstraint("AK_SaleRoznamchaCategories_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_SaleRoznamchaCategories_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "SaleRoznamchaCompanies",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                CategoryId = table.Column<int>(type: "int", nullable: false),
                Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SaleRoznamchaCompanies", x => x.Id);
                table.UniqueConstraint("AK_SaleRoznamchaCompanies_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_SaleRoznamchaCompanies_SaleRoznamchaCategories_TenantId_CategoryId", x => new { x.TenantId, x.CategoryId }, "SaleRoznamchaCategories", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SaleRoznamchaCompanies_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "SaleRoznamchaPlatforms",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                CompanyId = table.Column<int>(type: "int", nullable: false),
                Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SaleRoznamchaPlatforms", x => x.Id);
                table.UniqueConstraint("AK_SaleRoznamchaPlatforms_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey("FK_SaleRoznamchaPlatforms_SaleRoznamchaCompanies_TenantId_CompanyId", x => new { x.TenantId, x.CompanyId }, "SaleRoznamchaCompanies", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SaleRoznamchaPlatforms_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "SaleRoznamchaProductCategories",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                PlatformId = table.Column<int>(type: "int", nullable: false),
                Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SaleRoznamchaProductCategories", x => x.Id);
                table.ForeignKey("FK_SaleRoznamchaProductCategories_SaleRoznamchaPlatforms_TenantId_PlatformId", x => new { x.TenantId, x.PlatformId }, "SaleRoznamchaPlatforms", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SaleRoznamchaProductCategories_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_SaleRoznamchaCategories_TenantId_Name", "SaleRoznamchaCategories", new[] { "TenantId", "Name" }, unique: true);
        migrationBuilder.CreateIndex("IX_SaleRoznamchaCompanies_TenantId_CategoryId_Name", "SaleRoznamchaCompanies", new[] { "TenantId", "CategoryId", "Name" }, unique: true);
        migrationBuilder.CreateIndex("IX_SaleRoznamchaPlatforms_TenantId_CompanyId_Name", "SaleRoznamchaPlatforms", new[] { "TenantId", "CompanyId", "Name" }, unique: true);
        migrationBuilder.CreateIndex("IX_SaleRoznamchaProductCategories_TenantId_PlatformId_Name", "SaleRoznamchaProductCategories", new[] { "TenantId", "PlatformId", "Name" }, unique: true);

        migrationBuilder.Sql(SaleRoznamchaSql.MasterHierarchyProcedure);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_SaleRoznamcha_MasterHierarchy;");
        migrationBuilder.DropTable("SaleRoznamchaProductCategories");
        migrationBuilder.DropTable("SaleRoznamchaPlatforms");
        migrationBuilder.DropTable("SaleRoznamchaCompanies");
        migrationBuilder.DropTable("SaleRoznamchaCategories");
    }
}
