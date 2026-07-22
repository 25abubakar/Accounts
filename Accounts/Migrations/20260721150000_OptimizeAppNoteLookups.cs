using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260721150000_OptimizeAppNoteLookups")]
public sealed class OptimizeAppNoteLookups : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_AppNotes_TenantId_PublishedActiveDeleted'
                  AND object_id = OBJECT_ID(N'dbo.AppNotes')
            )
            CREATE INDEX IX_AppNotes_TenantId_PublishedActiveDeleted
            ON dbo.AppNotes (TenantId, IsPublished, IsActive, IsDeleted)
            INCLUDE (StartDateUtc, EndDateUtc, IsPinned, PriorityCode, CreatedOnUtc);
            """);

        migrationBuilder.Sql(
            """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_AppNotes_PublishedActiveDeleted_Dates'
                  AND object_id = OBJECT_ID(N'dbo.AppNotes')
            )
            CREATE INDEX IX_AppNotes_PublishedActiveDeleted_Dates
            ON dbo.AppNotes (IsPublished, IsActive, IsDeleted, StartDateUtc, EndDateUtc)
            INCLUDE (TenantId, IsPinned, PriorityCode, CreatedOnUtc);
            """);

        migrationBuilder.Sql(
            """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_AppNoteTargets_TypeValueNoteId'
                  AND object_id = OBJECT_ID(N'dbo.AppNoteTargets')
            )
            CREATE INDEX IX_AppNoteTargets_TypeValueNoteId
            ON dbo.AppNoteTargets (TargetTypeCode, TargetValue, NoteId);
            """);

        migrationBuilder.Sql(
            """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_AppNoteTargets_NoteId_TypeValue'
                  AND object_id = OBJECT_ID(N'dbo.AppNoteTargets')
            )
            CREATE INDEX IX_AppNoteTargets_NoteId_TypeValue
            ON dbo.AppNoteTargets (NoteId, TargetTypeCode, TargetValue);
            """);

        migrationBuilder.Sql(
            """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_AppNoteUserStates_StaffId_NoteId'
                  AND object_id = OBJECT_ID(N'dbo.AppNoteUserStates')
            )
            CREATE INDEX IX_AppNoteUserStates_StaffId_NoteId
            ON dbo.AppNoteUserStates (StaffId, NoteId)
            INCLUDE (IsRead, IsAcknowledged, IsDismissed, ReadOnUtc, AcknowledgedOnUtc, DismissedOnUtc);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS IX_AppNoteUserStates_StaffId_NoteId ON dbo.AppNoteUserStates;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS IX_AppNoteTargets_NoteId_TypeValue ON dbo.AppNoteTargets;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS IX_AppNoteTargets_TypeValueNoteId ON dbo.AppNoteTargets;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS IX_AppNotes_PublishedActiveDeleted_Dates ON dbo.AppNotes;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS IX_AppNotes_TenantId_PublishedActiveDeleted ON dbo.AppNotes;");
    }
}
