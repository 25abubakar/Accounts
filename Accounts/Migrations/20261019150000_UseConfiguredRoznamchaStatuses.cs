using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class UseConfiguredRoznamchaStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- Existing numeric legacy IDs can mean a different shared status.
                -- Resolve by name before moving the FK, and stop on unknown statuses.
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.RoznamchaEntries')
                           AND referenced_object_id=OBJECT_ID(N'dbo.AccountsEntryStatuses'))
                BEGIN
                    SELECT legacy.Id AS OldId, canonical.Id AS NewId INTO #StatusRemap
                    FROM dbo.AccountsEntryStatuses legacy
                    LEFT JOIN PlatformSettings.Statuses canonical
                      ON canonical.Name=legacy.Name OR (legacy.Code=N'DRAFT' AND canonical.Name=N'Entered');
                    IF EXISTS (SELECT 1 FROM dbo.RoznamchaEntries e LEFT JOIN #StatusRemap m ON m.OldId=e.EnterStatusId
                               WHERE e.EnterStatusId IS NOT NULL AND m.NewId IS NULL)
                        THROW 51000, 'Configure matching shared status names for existing Roznamcha entries before migration.', 1;
                    DECLARE @DropLegacy nvarchar(max)=N'';
                    SELECT @DropLegacy+=N'ALTER TABLE dbo.RoznamchaEntries DROP CONSTRAINT '+QUOTENAME(name)+N';'
                    FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.RoznamchaEntries')
                         AND referenced_object_id=OBJECT_ID(N'dbo.AccountsEntryStatuses');
                    EXEC sys.sp_executesql @DropLegacy;
                    UPDATE e SET EnterStatusId=m.NewId FROM dbo.RoznamchaEntries e JOIN #StatusRemap m ON m.OldId=e.EnterStatusId;
                END;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.RoznamchaEntries') AND name=N'IX_RoznamchaEntries_EnterStatusId')
                    CREATE INDEX IX_RoznamchaEntries_EnterStatusId ON dbo.RoznamchaEntries(EnterStatusId);
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_RoznamchaEntries_Statuses_EnterStatusId",
                table: "RoznamchaEntries",
                column: "EnterStatusId",
                principalSchema: "PlatformSettings",
                principalTable: "Statuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(RoznamchaListSql.ConfiguredPayment);
            migrationBuilder.Sql(RoznamchaListSql.ConfiguredReceipt);
            migrationBuilder.Sql(RoznamchaListSql.ConfiguredUnified);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Shared status IDs cannot safely be restored to legacy IDs automatically. Restore a database backup to roll back.");
        }
    }
}
