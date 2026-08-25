using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260825120000_AddLibraryModule")]
public sealed class AddLibraryModule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "GeneratedInvoices",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                InvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                CustomerEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                CustomerAddress = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                TaxRate = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GeneratedInvoices", x => x.Id);
                table.ForeignKey("FK_GeneratedInvoices_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "LibraryTypes",
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
                table.PrimaryKey("PK_LibraryTypes", x => x.Id);
                table.ForeignKey("FK_LibraryTypes_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "GeneratedInvoiceLines",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                InvoiceId = table.Column<long>(type: "bigint", nullable: false),
                Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                LineTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                DisplayOrder = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GeneratedInvoiceLines", x => x.Id);
                table.ForeignKey("FK_GeneratedInvoiceLines_GeneratedInvoices_InvoiceId", x => x.InvoiceId, "GeneratedInvoices", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "LibraryDocuments",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                LibraryTypeId = table.Column<int>(type: "int", nullable: false),
                Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                StoredFileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ContentType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                FileExtension = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                UploadedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                CreatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedOnUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LibraryDocuments", x => x.Id);
                table.ForeignKey("FK_LibraryDocuments_LibraryTypes_LibraryTypeId", x => x.LibraryTypeId, "LibraryTypes", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_LibraryDocuments_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_GeneratedInvoiceLines_InvoiceId_DisplayOrder", "GeneratedInvoiceLines", new[] { "InvoiceId", "DisplayOrder" });
        migrationBuilder.CreateIndex("IX_GeneratedInvoices_TenantId_InvoiceNumber", "GeneratedInvoices", new[] { "TenantId", "InvoiceNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_GeneratedInvoices_TenantId_IssueDate_Status", "GeneratedInvoices", new[] { "TenantId", "IssueDate", "Status" });
        migrationBuilder.CreateIndex("IX_LibraryDocuments_LibraryTypeId", "LibraryDocuments", "LibraryTypeId");
        migrationBuilder.CreateIndex("IX_LibraryDocuments_TenantId_LibraryTypeId_IsActive_CreatedOnUtc", "LibraryDocuments", new[] { "TenantId", "LibraryTypeId", "IsActive", "CreatedOnUtc" });
        migrationBuilder.CreateIndex("IX_LibraryDocuments_TenantId_Title", "LibraryDocuments", new[] { "TenantId", "Title" });
        migrationBuilder.CreateIndex("IX_LibraryTypes_TenantId_Code", "LibraryTypes", new[] { "TenantId", "Code" }, unique: true);
        migrationBuilder.CreateIndex("IX_LibraryTypes_TenantId_IsActive_DisplayOrder", "LibraryTypes", new[] { "TenantId", "IsActive", "DisplayOrder" });
        migrationBuilder.CreateIndex("IX_LibraryTypes_TenantId_Name", "LibraryTypes", new[] { "TenantId", "Name" }, unique: true);

        if (ActiveProvider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            migrationBuilder.Sql(MenuSeedSql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("GeneratedInvoiceLines");
        migrationBuilder.DropTable("LibraryDocuments");
        migrationBuilder.DropTable("GeneratedInvoices");
        migrationBuilder.DropTable("LibraryTypes");
        // Keep menu/access rows because administrators may have customized assignments.
    }

    private const string MenuSeedSql = """
        DECLARE @LibraryId int = (SELECT TOP (1) Id FROM dbo.Menus WHERE ParentId IS NULL AND Title = N'Library' ORDER BY Id);
        IF @LibraryId IS NULL BEGIN INSERT dbo.Menus(Title,Icon,Route,ParentId,SortOrder,IsActive) VALUES(N'Library',N'LibraryBig',NULL,NULL,80,1); SET @LibraryId=SCOPE_IDENTITY(); END
        ELSE UPDATE dbo.Menus SET Icon=N'LibraryBig',Route=NULL,SortOrder=80,IsActive=1 WHERE Id=@LibraryId;

        DECLARE @TypeId int = (SELECT TOP (1) Id FROM dbo.Menus WHERE Route=N'/library/types');
        IF @TypeId IS NULL BEGIN INSERT dbo.Menus(Title,Icon,Route,ParentId,SortOrder,IsActive) VALUES(N'Library Type',N'Tags',N'/library/types',@LibraryId,1,1); SET @TypeId=SCOPE_IDENTITY(); END
        ELSE UPDATE dbo.Menus SET Title=N'Library Type',Icon=N'Tags',ParentId=@LibraryId,SortOrder=1,IsActive=1 WHERE Id=@TypeId;

        DECLARE @DocumentsId int = (SELECT TOP (1) Id FROM dbo.Menus WHERE Route=N'/library');
        IF @DocumentsId IS NULL BEGIN INSERT dbo.Menus(Title,Icon,Route,ParentId,SortOrder,IsActive) VALUES(N'Library',N'Files',N'/library',@LibraryId,2,1); SET @DocumentsId=SCOPE_IDENTITY(); END
        ELSE UPDATE dbo.Menus SET Title=N'Library',Icon=N'Files',ParentId=@LibraryId,SortOrder=2,IsActive=1 WHERE Id=@DocumentsId;

        DECLARE @ConverterId int = (SELECT TOP (1) Id FROM dbo.Menus WHERE Route=N'/library/file-converter');
        IF @ConverterId IS NULL BEGIN INSERT dbo.Menus(Title,Icon,Route,ParentId,SortOrder,IsActive) VALUES(N'File Converter',N'FileCog',N'/library/file-converter',@LibraryId,3,1); SET @ConverterId=SCOPE_IDENTITY(); END
        ELSE UPDATE dbo.Menus SET Title=N'File Converter',Icon=N'FileCog',ParentId=@LibraryId,SortOrder=3,IsActive=1 WHERE Id=@ConverterId;

        DECLARE @InvoiceId int = (SELECT TOP (1) Id FROM dbo.Menus WHERE Route=N'/library/generate-invoice');
        IF @InvoiceId IS NULL BEGIN INSERT dbo.Menus(Title,Icon,Route,ParentId,SortOrder,IsActive) VALUES(N'Generate Invoice',N'ReceiptText',N'/library/generate-invoice',@LibraryId,4,1); SET @InvoiceId=SCOPE_IDENTITY(); END
        ELSE UPDATE dbo.Menus SET Title=N'Generate Invoice',Icon=N'ReceiptText',ParentId=@LibraryId,SortOrder=4,IsActive=1 WHERE Id=@InvoiceId;

        DECLARE @Seed TABLE(MenuId int,Title nvarchar(100));
        INSERT @Seed VALUES(@LibraryId,N'Library'),(@TypeId,N'Library Type'),(@DocumentsId,N'Library'),(@ConverterId,N'File Converter'),(@InvoiceId,N'Generate Invoice');

        INSERT dbo.Features(FeatureKey,FeatureName,Module,Description,CreatedDate)
        SELECT CONCAT(N'MENU_',seed.MenuId,suffix.Suffix),CONCAT(seed.Title,suffix.DisplayName),N'Library',CONCAT(suffix.ActionName,N' ',seed.Title),SYSUTCDATETIME()
        FROM @Seed seed CROSS JOIN (VALUES(N'',N'',N'Open'),(N'_VIEW',N' - View',N'View'),(N'_ADD',N' - Add',N'Add'),(N'_EDIT',N' - Edit',N'Edit'),(N'_DELETE',N' - Delete',N'Delete')) suffix(Suffix,DisplayName,ActionName)
        WHERE NOT EXISTS(SELECT 1 FROM dbo.Features f WHERE f.FeatureKey=CONCAT(N'MENU_',seed.MenuId,suffix.Suffix));

        INSERT dbo.MenuPermissions(MenuId,PermissionId)
        SELECT seed.MenuId,feature.PermissionId FROM @Seed seed
        JOIN dbo.Features feature ON feature.FeatureKey IN(CONCAT(N'MENU_',seed.MenuId),CONCAT(N'MENU_',seed.MenuId,N'_VIEW'),CONCAT(N'MENU_',seed.MenuId,N'_ADD'),CONCAT(N'MENU_',seed.MenuId,N'_EDIT'),CONCAT(N'MENU_',seed.MenuId,N'_DELETE'))
        WHERE NOT EXISTS(SELECT 1 FROM dbo.MenuPermissions mp WHERE mp.MenuId=seed.MenuId AND mp.PermissionId=feature.PermissionId);
        """;
}
