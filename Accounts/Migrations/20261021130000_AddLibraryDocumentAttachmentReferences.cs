using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261021130000_AddLibraryDocumentAttachmentReferences")]
public sealed class AddLibraryDocumentAttachmentReferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DocumentListSql(true));

    // Keep the new nullable DTO column during rollback so existing callers can materialize the result.
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DocumentListSql(false));

    private static string DocumentListSql(bool includeReferences) => $"""
        CREATE OR ALTER PROCEDURE dbo.usp_Library_Documents_List
            @TenantId INT, @AssetKind NVARCHAR(30), @TypeId INT = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            SELECT d.Id, d.TenantId, d.DocumentReference, d.LibraryTypeId,
                t.Name AS LibraryTypeName, d.LibrarySubTypeId, st.Name AS LibrarySubTypeName,
                d.AssetKind, d.Title, d.Description, d.OriginalFileName, d.StoredFileName,
                d.ContentType, d.FileExtension, d.FileSizeBytes, d.IsActive,
                d.UploadedByUserId, d.CreatedOnUtc, d.UpdatedOnUtc,
                {(includeReferences ? "attached.AttachmentReference" : "CAST(NULL AS NVARCHAR(MAX)) AS AttachmentReference")}
            FROM dbo.LibraryDocuments d
            LEFT JOIN dbo.LibraryTypes t ON t.Id = d.LibraryTypeId AND t.ParentId IS NULL
            LEFT JOIN dbo.LibraryTypes st ON st.Id = d.LibrarySubTypeId AND st.ParentId = d.LibraryTypeId
            {(includeReferences ? ReferencesJoin : "")}
            WHERE d.TenantId = @TenantId AND d.AssetKind = @AssetKind
              AND (@TypeId IS NULL OR d.LibraryTypeId = @TypeId)
            ORDER BY d.CreatedOnUtc DESC, d.Id DESC;
        END
        """;

    private const string ReferencesJoin = """
        OUTER APPLY (
            SELECT STRING_AGG(CAST(e.Ref AS NVARCHAR(MAX)), N', ')
                WITHIN GROUP (ORDER BY e.TransDate, e.Id) AS AttachmentReference
            FROM dbo.RoznamchaLibraryDocuments link
            JOIN dbo.RoznamchaEntries e
              ON e.Id = link.RoznamchaEntryId AND e.TenantId = link.TenantId
            WHERE link.LibraryDocumentId = d.Id AND link.TenantId = d.TenantId
              AND e.IsDeleted = 0 AND e.RoznamchaTypeId IN (1, 2)
              AND e.Ref IS NOT NULL AND LTRIM(RTRIM(e.Ref)) <> N''
        ) attached
        """;
}
