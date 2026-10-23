using Microsoft.EntityFrameworkCore.Migrations;

namespace Accounts.Migrations;

public partial class ShareLibraryCatalogAndUnifyTypes
{
    private static void SeedSharedCatalog(MigrationBuilder migrationBuilder)
    {
        var created = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc);
        migrationBuilder.InsertData("LibraryCategories",
            ["Id", "Code", "Name", "DisplayOrder", "IsActive", "CreatedOnUtc"],
            new object[,] {
                { 1, "OFFICIAL", "Official", 1, true, created },
                { 2, "PRIVATE", "Private", 2, true, created }
            });

        (int Id, string Name, int? ParentId, bool HardCopy)[] rows = [
            (1, "Template", null, false),
            (2, "Documents", null, true),
            (3, "Picture", null, true),
            (4, "License & Certificates", null, true),
            (5, "Audios", null, false),
            (6, "Videos", null, false),
            (7, "Invoice", null, true),
            (8, "Receipt", null, true),
            (22, "Pay Bill", null, false),
            (23, "Daily Mail", null, false),
            (28, "Tax", null, false),
            (48, "Amazon", null, false),
            (56, "Misc", null, false),
            (58, "Bank Statement", null, false),
            (59, "Arsh Studio", null, false),
            (11, "Papers", 2, false),
            (12, "Rules/Regulations", 2, false),
            (13, "SoPs", 2, false),
            (24, "Letter", 2, false),
            (25, "Attachment", 2, false),
            (29, "A&ENT", 7, false),
            (30, "PUC", 7, false),
            (31, "SAASC", 7, false),
            (32, "Rent", 22, false),
            (33, "PTCL", 22, false),
            (34, "Nayatel", 22, false),
            (35, "Zong", 22, false),
            (36, "Electric", 22, false),
            (37, "Twillio", 22, false),
            (38, "Azure", 22, false),
            (39, "Al Shifa", 8, false),
            (40, "PSEB", 4, false),
            (41, "RCC", 4, false),
            (42, "Coy Regs", 4, false),
            (43, "Staff", 3, false),
            (44, "Returns", 28, false),
            (45, "LT Accts", 28, false),
            (46, "QB Accts", 28, false),
            (47, "Vouchers", 28, false),
            (49, "Amazon", 48, false),
            (50, "Fashion & Beauty", 48, false),
            (51, "Salt & Gem", 48, false),
            (52, "Home & Kitchen", 48, false),
            (53, "Health & Medical", 48, false),
            (54, "SAASC", 48, false),
            (55, "AMZ Misc", 48, false),
            (57, "E_Commerce", 48, false)
        ];
        var values = new object[rows.Length, 8];
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            values[index, 0] = row.Id;
            // Existing API requires a code; retain the supplied name rather than inventing a new classification.
            values[index, 1] = row.Name.ToUpperInvariant();
            values[index, 2] = row.Name;
            values[index, 3] = row.ParentId is int parentId ? parentId : null!;
            values[index, 4] = row.Id;
            values[index, 5] = true;
            values[index, 6] = row.HardCopy;
            values[index, 7] = created;
        }
        migrationBuilder.InsertData("LibraryTypes",
            ["Id", "Code", "Name", "ParentId", "DisplayOrder", "IsActive", "IsHardCopyRequired", "CreatedOnUtc"], values);
    }

    private static void UpdateLibraryListProcedures(MigrationBuilder migrationBuilder, bool shared)
    {
        var subTypeTable = shared ? "LibraryTypes" : "LibrarySubTypes";
        var typeCondition = shared ? "t.ParentId IS NULL" : "t.TenantId = d.TenantId";
        var subTypeCondition = shared ? "st.ParentId = d.LibraryTypeId" : "st.TenantId = d.TenantId";
        migrationBuilder.Sql($"""
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
                LEFT JOIN dbo.LibraryTypes t ON t.Id = d.LibraryTypeId AND {typeCondition}
                LEFT JOIN dbo.{subTypeTable} st ON st.Id = d.LibrarySubTypeId AND {subTypeCondition}
                WHERE d.TenantId = @TenantId AND d.AssetKind = @AssetKind
                    AND (@TypeId IS NULL OR d.LibraryTypeId = @TypeId)
                ORDER BY d.CreatedOnUtc DESC, d.Id DESC;
            END
            """);
        migrationBuilder.Sql($"""
            CREATE OR ALTER PROCEDURE dbo.usp_Library_Templates_List
                @TenantId INT, @TypeId INT = NULL
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT d.Id, d.TenantId, d.LibraryTypeId, t.Name AS LibraryTypeName,
                    d.LibrarySubTypeId, st.Name AS LibrarySubTypeName, d.Name, d.Description,
                    d.Content, d.IsActive, d.CreatedByUserId, d.CreatedOnUtc, d.UpdatedOnUtc
                FROM dbo.LibraryTemplates d
                LEFT JOIN dbo.LibraryTypes t ON t.Id = d.LibraryTypeId AND {typeCondition}
                LEFT JOIN dbo.{subTypeTable} st ON st.Id = d.LibrarySubTypeId AND {subTypeCondition}
                WHERE d.TenantId = @TenantId AND (@TypeId IS NULL OR d.LibraryTypeId = @TypeId)
                ORDER BY d.CreatedOnUtc DESC, d.Id DESC;
            END
            """);
    }
}
