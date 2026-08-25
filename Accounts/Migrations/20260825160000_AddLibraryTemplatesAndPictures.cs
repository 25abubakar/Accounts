using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260825160000_AddLibraryTemplatesAndPictures")]
public sealed class AddLibraryTemplatesAndPictures : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AssetKind",
            table: "LibraryDocuments",
            type: "nvarchar(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Document");

        migrationBuilder.CreateTable(
            name: "LibraryTemplates",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                LibraryTypeId = table.Column<int>(type: "int", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LibraryTemplates", x => x.Id);
                table.ForeignKey(
                    name: "FK_LibraryTemplates_LibraryTypes_LibraryTypeId",
                    column: x => x.LibraryTypeId,
                    principalTable: "LibraryTypes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_LibraryTemplates_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LibraryDocuments_TenantId_AssetKind_LibraryTypeId_IsActive_CreatedOnUtc",
            table: "LibraryDocuments",
            columns: new[] { "TenantId", "AssetKind", "LibraryTypeId", "IsActive", "CreatedOnUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_LibraryTemplates_LibraryTypeId",
            table: "LibraryTemplates",
            column: "LibraryTypeId");

        migrationBuilder.CreateIndex(
            name: "IX_LibraryTemplates_TenantId_LibraryTypeId_IsActive_CreatedOnUtc",
            table: "LibraryTemplates",
            columns: new[] { "TenantId", "LibraryTypeId", "IsActive", "CreatedOnUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_LibraryTemplates_TenantId_Name",
            table: "LibraryTemplates",
            columns: new[] { "TenantId", "Name" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "LibraryTemplates");
        migrationBuilder.DropIndex(
            name: "IX_LibraryDocuments_TenantId_AssetKind_LibraryTypeId_IsActive_CreatedOnUtc",
            table: "LibraryDocuments");
        migrationBuilder.DropColumn(name: "AssetKind", table: "LibraryDocuments");
    }
}
