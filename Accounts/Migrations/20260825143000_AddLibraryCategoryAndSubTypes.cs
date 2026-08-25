using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260825143000_AddLibraryCategoryAndSubTypes")]
public sealed class AddLibraryCategoryAndSubTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsHardCopyRequired",
            table: "LibraryTypes",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "LibraryCategories",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                DisplayOrder = table.Column<int>(type: "int", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LibraryCategories", x => x.Id);
                table.ForeignKey("FK_LibraryCategories_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "LibrarySubTypes",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                LibraryTypeId = table.Column<int>(type: "int", nullable: false),
                Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                DisplayOrder = table.Column<int>(type: "int", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LibrarySubTypes", x => x.Id);
                table.ForeignKey("FK_LibrarySubTypes_LibraryTypes_LibraryTypeId", x => x.LibraryTypeId, "LibraryTypes", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_LibrarySubTypes_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_LibraryCategories_TenantId_Code", "LibraryCategories", new[] { "TenantId", "Code" }, unique: true);
        migrationBuilder.CreateIndex("IX_LibraryCategories_TenantId_IsActive_DisplayOrder", "LibraryCategories", new[] { "TenantId", "IsActive", "DisplayOrder" });
        migrationBuilder.CreateIndex("IX_LibraryCategories_TenantId_Name", "LibraryCategories", new[] { "TenantId", "Name" }, unique: true);
        migrationBuilder.CreateIndex("IX_LibrarySubTypes_LibraryTypeId", "LibrarySubTypes", "LibraryTypeId");
        migrationBuilder.CreateIndex("IX_LibrarySubTypes_TenantId_IsActive_DisplayOrder", "LibrarySubTypes", new[] { "TenantId", "IsActive", "DisplayOrder" });
        migrationBuilder.CreateIndex("IX_LibrarySubTypes_TenantId_LibraryTypeId_Code", "LibrarySubTypes", new[] { "TenantId", "LibraryTypeId", "Code" }, unique: true);
        migrationBuilder.CreateIndex("IX_LibrarySubTypes_TenantId_LibraryTypeId_Name", "LibrarySubTypes", new[] { "TenantId", "LibraryTypeId", "Name" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("LibraryCategories");
        migrationBuilder.DropTable("LibrarySubTypes");
        migrationBuilder.DropColumn("IsHardCopyRequired", "LibraryTypes");
    }
}
