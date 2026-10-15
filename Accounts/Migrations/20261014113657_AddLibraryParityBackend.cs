using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryParityBackend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LibraryCategoryId",
                table: "LibraryTypes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LibrarySubTypeId",
                table: "LibraryTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentReference",
                table: "LibraryDocuments",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LibrarySubTypeId",
                table: "LibraryDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LibraryHardCopyLocations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    LibraryDocumentId = table.Column<long>(type: "bigint", nullable: false),
                    Store = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Rack = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    FolderReference = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    PageNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IssuedTo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IssuedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryHardCopyLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryHardCopyLocations_LibraryDocuments_LibraryDocumentId",
                        column: x => x.LibraryDocumentId,
                        principalTable: "LibraryDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LibraryHardCopyLocations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoznamchaLibraryDocuments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    RoznamchaEntryId = table.Column<long>(type: "bigint", nullable: false),
                    LibraryDocumentId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoznamchaLibraryDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoznamchaLibraryDocuments_LibraryDocuments_LibraryDocumentId",
                        column: x => x.LibraryDocumentId,
                        principalTable: "LibraryDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoznamchaLibraryDocuments_RoznamchaEntries_RoznamchaEntryId",
                        column: x => x.RoznamchaEntryId,
                        principalTable: "RoznamchaEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoznamchaLibraryDocuments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryTypes_LibraryCategoryId",
                table: "LibraryTypes",
                column: "LibraryCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryTemplates_LibrarySubTypeId",
                table: "LibraryTemplates",
                column: "LibrarySubTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryDocuments_LibrarySubTypeId",
                table: "LibraryDocuments",
                column: "LibrarySubTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryDocuments_TenantId_DocumentReference",
                table: "LibraryDocuments",
                columns: new[] { "TenantId", "DocumentReference" },
                unique: true,
                filter: "[DocumentReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryHardCopyLocations_LibraryDocumentId",
                table: "LibraryHardCopyLocations",
                column: "LibraryDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryHardCopyLocations_TenantId_LibraryDocumentId",
                table: "LibraryHardCopyLocations",
                columns: new[] { "TenantId", "LibraryDocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoznamchaLibraryDocuments_LibraryDocumentId",
                table: "RoznamchaLibraryDocuments",
                column: "LibraryDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_RoznamchaLibraryDocuments_RoznamchaEntryId",
                table: "RoznamchaLibraryDocuments",
                column: "RoznamchaEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_RoznamchaLibraryDocuments_TenantId_LibraryDocumentId",
                table: "RoznamchaLibraryDocuments",
                columns: new[] { "TenantId", "LibraryDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoznamchaLibraryDocuments_TenantId_RoznamchaEntryId_LibraryDocumentId",
                table: "RoznamchaLibraryDocuments",
                columns: new[] { "TenantId", "RoznamchaEntryId", "LibraryDocumentId" },
                unique: true);

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
                name: "FK_LibraryTypes_LibraryCategories_LibraryCategoryId",
                table: "LibraryTypes",
                column: "LibraryCategoryId",
                principalTable: "LibraryCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                UPDATE dbo.LibraryDocuments
                SET DocumentReference = CONCAT(N'Doc-', Id)
                WHERE DocumentReference IS NULL;
                """);

            migrationBuilder.Sql(
                """
                CREATE OR ALTER PROCEDURE dbo.usp_Library_Documents_List
                    @TenantId INT, @AssetKind NVARCHAR(30), @TypeId INT = NULL
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SELECT d.Id, d.TenantId, d.DocumentReference, d.LibraryTypeId,
                        t.Name AS LibraryTypeName, d.LibrarySubTypeId, st.Name AS LibrarySubTypeName,
                        d.AssetKind, d.Title, d.Description, d.OriginalFileName, d.StoredFileName,
                        d.ContentType, d.FileExtension, d.FileSizeBytes, d.IsActive,
                        d.UploadedByUserId, d.CreatedOnUtc, d.UpdatedOnUtc
                    FROM dbo.LibraryDocuments d
                    LEFT JOIN dbo.LibraryTypes t ON t.Id=d.LibraryTypeId AND t.TenantId=d.TenantId
                    LEFT JOIN dbo.LibrarySubTypes st ON st.Id=d.LibrarySubTypeId AND st.TenantId=d.TenantId
                    WHERE d.TenantId=@TenantId AND d.AssetKind=@AssetKind
                      AND (@TypeId IS NULL OR d.LibraryTypeId=@TypeId)
                    ORDER BY d.CreatedOnUtc DESC, d.Id DESC;
                END
                """);

            migrationBuilder.Sql(
                """
                CREATE OR ALTER PROCEDURE dbo.usp_Library_Templates_List
                    @TenantId INT, @TypeId INT = NULL
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SELECT x.Id, x.TenantId, x.LibraryTypeId, t.Name AS LibraryTypeName,
                        x.LibrarySubTypeId, st.Name AS LibrarySubTypeName, x.Name, x.Description,
                        x.Content, x.IsActive, x.CreatedByUserId, x.CreatedOnUtc, x.UpdatedOnUtc
                    FROM dbo.LibraryTemplates x
                    LEFT JOIN dbo.LibraryTypes t ON t.Id=x.LibraryTypeId AND t.TenantId=x.TenantId
                    LEFT JOIN dbo.LibrarySubTypes st ON st.Id=x.LibrarySubTypeId AND st.TenantId=x.TenantId
                    WHERE x.TenantId=@TenantId AND (@TypeId IS NULL OR x.LibraryTypeId=@TypeId)
                    ORDER BY x.CreatedOnUtc DESC, x.Id DESC;
                END
                """);

            migrationBuilder.Sql(
                """
                CREATE OR ALTER PROCEDURE dbo.usp_Library_Invoices_List @TenantId INT
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SELECT i.Id, i.TenantId, i.InvoiceNumber, i.CustomerName, i.CustomerEmail,
                        i.CustomerAddress, i.IssueDate, i.DueDate, i.Currency, i.Subtotal,
                        i.TaxRate, i.TaxAmount, i.DiscountAmount, i.TotalAmount, i.Status,
                        i.Notes, i.CreatedByUserId, i.CreatedOnUtc, i.UpdatedOnUtc
                    FROM dbo.GeneratedInvoices i WHERE i.TenantId=@TenantId
                    ORDER BY i.CreatedOnUtc DESC, i.Id DESC;
                END
                """);

            RecreateRoznamchaListProcedures(migrationBuilder);
        }

        private static void RecreateRoznamchaListProcedures(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RoznamchaListSql.Payment);
            migrationBuilder.Sql(RoznamchaListSql.Receipt);
            migrationBuilder.Sql(RoznamchaListSql.Unified);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RoznamchaListSql.LegacyPayment);
            migrationBuilder.Sql(RoznamchaListSql.LegacyReceipt);
            migrationBuilder.Sql(RoznamchaListSql.LegacyUnified);
            migrationBuilder.Sql(
                """
                CREATE OR ALTER PROCEDURE dbo.usp_Library_Documents_List
                    @TenantId INT, @AssetKind NVARCHAR(30), @TypeId INT=NULL
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SELECT d.Id,d.TenantId,d.LibraryTypeId,t.Name AS LibraryTypeName,d.AssetKind,
                        d.Title,d.Description,d.OriginalFileName,d.StoredFileName,d.ContentType,
                        d.FileExtension,d.FileSizeBytes,d.IsActive,d.UploadedByUserId,d.CreatedOnUtc,d.UpdatedOnUtc
                    FROM dbo.LibraryDocuments d LEFT JOIN dbo.LibraryTypes t ON t.Id=d.LibraryTypeId
                    WHERE d.TenantId=@TenantId AND d.AssetKind=@AssetKind
                      AND (@TypeId IS NULL OR d.LibraryTypeId=@TypeId)
                    ORDER BY d.CreatedOnUtc DESC;
                END
                """);
            migrationBuilder.Sql(
                """
                CREATE OR ALTER PROCEDURE dbo.usp_Library_Templates_List
                    @TenantId INT, @TypeId INT=NULL
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SELECT x.Id,x.TenantId,x.LibraryTypeId,t.Name AS LibraryTypeName,x.Name,
                        x.Description,x.Content,x.IsActive,x.CreatedByUserId,x.CreatedOnUtc,x.UpdatedOnUtc
                    FROM dbo.LibraryTemplates x LEFT JOIN dbo.LibraryTypes t ON t.Id=x.LibraryTypeId
                    WHERE x.TenantId=@TenantId AND (@TypeId IS NULL OR x.LibraryTypeId=@TypeId)
                    ORDER BY x.CreatedOnUtc DESC;
                END
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_LibraryDocuments_LibrarySubTypes_LibrarySubTypeId",
                table: "LibraryDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_LibraryTemplates_LibrarySubTypes_LibrarySubTypeId",
                table: "LibraryTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_LibraryTypes_LibraryCategories_LibraryCategoryId",
                table: "LibraryTypes");

            migrationBuilder.DropTable(
                name: "LibraryHardCopyLocations");

            migrationBuilder.DropTable(
                name: "RoznamchaLibraryDocuments");

            migrationBuilder.DropIndex(
                name: "IX_LibraryTypes_LibraryCategoryId",
                table: "LibraryTypes");

            migrationBuilder.DropIndex(
                name: "IX_LibraryTemplates_LibrarySubTypeId",
                table: "LibraryTemplates");

            migrationBuilder.DropIndex(
                name: "IX_LibraryDocuments_LibrarySubTypeId",
                table: "LibraryDocuments");

            migrationBuilder.DropIndex(
                name: "IX_LibraryDocuments_TenantId_DocumentReference",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "LibraryCategoryId",
                table: "LibraryTypes");

            migrationBuilder.DropColumn(
                name: "LibrarySubTypeId",
                table: "LibraryTemplates");

            migrationBuilder.DropColumn(
                name: "DocumentReference",
                table: "LibraryDocuments");

            migrationBuilder.DropColumn(
                name: "LibrarySubTypeId",
                table: "LibraryDocuments");
        }
    }
}
