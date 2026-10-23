using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class ShareLibraryCatalogAndUnifyTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The inspected installation has empty Library tables. Stop instead
            // of discarding or guessing mappings on another populated database.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.LibraryCategories WITH (TABLOCKX, HOLDLOCK))
                  OR EXISTS (SELECT 1 FROM dbo.LibraryTypes WITH (TABLOCKX, HOLDLOCK))
                  OR EXISTS (SELECT 1 FROM dbo.LibrarySubTypes WITH (TABLOCKX, HOLDLOCK))
                  OR EXISTS (SELECT 1 FROM dbo.LibraryDocuments WITH (TABLOCKX, HOLDLOCK))
                  OR EXISTS (SELECT 1 FROM dbo.LibraryTemplates WITH (TABLOCKX, HOLDLOCK))
                    THROW 51000, 'Library catalog migration requires empty legacy Library tables; preserve and map existing data before retrying.', 1;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_LibraryCategories_Tenants_TenantId",
                table: "LibraryCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_LibraryDocuments_LibrarySubTypes_LibrarySubTypeId",
                table: "LibraryDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_LibraryTemplates_LibrarySubTypes_LibrarySubTypeId",
                table: "LibraryTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_LibraryTypes_Tenants_TenantId",
                table: "LibraryTypes");

            migrationBuilder.DropTable(
                name: "LibrarySubTypes");

            migrationBuilder.DropIndex(
                name: "IX_LibraryTypes_TenantId_Code",
                table: "LibraryTypes");

            migrationBuilder.DropIndex(
                name: "IX_LibraryTypes_TenantId_IsActive_DisplayOrder",
                table: "LibraryTypes");

            migrationBuilder.DropIndex(
                name: "IX_LibraryTypes_TenantId_Name",
                table: "LibraryTypes");

            migrationBuilder.DropIndex(
                name: "IX_LibraryCategories_TenantId_Code",
                table: "LibraryCategories");

            migrationBuilder.DropIndex(
                name: "IX_LibraryCategories_TenantId_IsActive_DisplayOrder",
                table: "LibraryCategories");

            migrationBuilder.DropIndex(
                name: "IX_LibraryCategories_TenantId_Name",
                table: "LibraryCategories");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "LibraryTypes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "LibraryCategories");

            migrationBuilder.AddColumn<int>(
                name: "ParentId",
                table: "LibraryTypes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryTypes_ParentId_Code",
                table: "LibraryTypes",
                columns: new[] { "ParentId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryTypes_ParentId_IsActive_DisplayOrder",
                table: "LibraryTypes",
                columns: new[] { "ParentId", "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryTypes_ParentId_Name",
                table: "LibraryTypes",
                columns: new[] { "ParentId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryCategories_Code",
                table: "LibraryCategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryCategories_IsActive_DisplayOrder",
                table: "LibraryCategories",
                columns: new[] { "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryCategories_Name",
                table: "LibraryCategories",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryDocuments_LibraryTypes_LibrarySubTypeId",
                table: "LibraryDocuments",
                column: "LibrarySubTypeId",
                principalTable: "LibraryTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryTemplates_LibraryTypes_LibrarySubTypeId",
                table: "LibraryTemplates",
                column: "LibrarySubTypeId",
                principalTable: "LibraryTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryTypes_LibraryTypes_ParentId",
                table: "LibraryTypes",
                column: "ParentId",
                principalTable: "LibraryTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            SeedSharedCatalog(migrationBuilder);
            UpdateLibraryListProcedures(migrationBuilder, shared: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.LibraryTypes) OR EXISTS (SELECT 1 FROM dbo.LibraryCategories)
                    THROW 51000, 'Cannot downgrade a populated shared Library catalog to tenant-owned tables. Export and plan its ownership mapping first.', 1;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_LibraryDocuments_LibraryTypes_LibrarySubTypeId",
                table: "LibraryDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_LibraryTemplates_LibraryTypes_LibrarySubTypeId",
                table: "LibraryTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_LibraryTypes_LibraryTypes_ParentId",
                table: "LibraryTypes");

            migrationBuilder.DropIndex(
                name: "IX_LibraryTypes_ParentId_Code",
                table: "LibraryTypes");

            migrationBuilder.DropIndex(
                name: "IX_LibraryTypes_ParentId_IsActive_DisplayOrder",
                table: "LibraryTypes");

            migrationBuilder.DropIndex(
                name: "IX_LibraryTypes_ParentId_Name",
                table: "LibraryTypes");

            migrationBuilder.DropIndex(
                name: "IX_LibraryCategories_Code",
                table: "LibraryCategories");

            migrationBuilder.DropIndex(
                name: "IX_LibraryCategories_IsActive_DisplayOrder",
                table: "LibraryCategories");

            migrationBuilder.DropIndex(
                name: "IX_LibraryCategories_Name",
                table: "LibraryCategories");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "LibraryTypes");

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "LibraryTypes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "LibraryCategories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "LibrarySubTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LibraryTypeId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibrarySubTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibrarySubTypes_LibraryTypes_LibraryTypeId",
                        column: x => x.LibraryTypeId,
                        principalTable: "LibraryTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LibrarySubTypes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryTypes_TenantId_Code",
                table: "LibraryTypes",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryTypes_TenantId_IsActive_DisplayOrder",
                table: "LibraryTypes",
                columns: new[] { "TenantId", "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryTypes_TenantId_Name",
                table: "LibraryTypes",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryCategories_TenantId_Code",
                table: "LibraryCategories",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryCategories_TenantId_IsActive_DisplayOrder",
                table: "LibraryCategories",
                columns: new[] { "TenantId", "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryCategories_TenantId_Name",
                table: "LibraryCategories",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibrarySubTypes_LibraryTypeId",
                table: "LibrarySubTypes",
                column: "LibraryTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LibrarySubTypes_TenantId_IsActive_DisplayOrder",
                table: "LibrarySubTypes",
                columns: new[] { "TenantId", "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_LibrarySubTypes_TenantId_LibraryTypeId_Code",
                table: "LibrarySubTypes",
                columns: new[] { "TenantId", "LibraryTypeId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibrarySubTypes_TenantId_LibraryTypeId_Name",
                table: "LibrarySubTypes",
                columns: new[] { "TenantId", "LibraryTypeId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryCategories_Tenants_TenantId",
                table: "LibraryCategories",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryDocuments_LibrarySubTypes_LibrarySubTypeId",
                table: "LibraryDocuments",
                column: "LibrarySubTypeId",
                principalTable: "LibrarySubTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryTemplates_LibrarySubTypes_LibrarySubTypeId",
                table: "LibraryTemplates",
                column: "LibrarySubTypeId",
                principalTable: "LibrarySubTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryTypes_Tenants_TenantId",
                table: "LibraryTypes",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            UpdateLibraryListProcedures(migrationBuilder, shared: false);
        }
    }
}
