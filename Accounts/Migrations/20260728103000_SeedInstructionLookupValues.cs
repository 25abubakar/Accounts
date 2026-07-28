using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260728103000_SeedInstructionLookupValues")]
public sealed class SeedInstructionLookupValues : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @Types table
            (
                LookupTypeCode nvarchar(100) NOT NULL,
                LookupTypeName nvarchar(150) NOT NULL
            );

            INSERT INTO @Types VALUES
                (N'NOTE_TYPE', N'Instruction Note Type'),
                (N'PRIORITY', N'Instruction Priority'),
                (N'CATEGORY', N'Instruction Category'),
                (N'VISIBILITY_TYPE', N'Instruction Visibility Type');

            MERGE dbo.AppLookupTypes AS target
            USING @Types AS source
               ON target.LookupTypeCode = source.LookupTypeCode
            WHEN MATCHED THEN UPDATE SET
                LookupTypeName = source.LookupTypeName,
                IsActive = 1
            WHEN NOT MATCHED THEN INSERT
                (LookupTypeCode, LookupTypeName, IsActive, CreatedOn)
            VALUES
                (source.LookupTypeCode, source.LookupTypeName, 1, SYSUTCDATETIME());

            DECLARE @Values table
            (
                LookupTypeCode nvarchar(100) NOT NULL,
                ValueCode nvarchar(100) NOT NULL,
                DisplayText nvarchar(150) NOT NULL,
                SortOrder int NOT NULL,
                IsDefault bit NOT NULL,
                MetadataJson nvarchar(2000) NULL
            );

            INSERT INTO @Values VALUES
                (N'NOTE_TYPE', N'INSTRUCTION', N'Instruction', 10, 1, NULL),
                (N'NOTE_TYPE', N'POLICY', N'Policy', 20, 0, NULL),
                (N'NOTE_TYPE', N'ANNOUNCEMENT', N'Announcement', 30, 0, NULL),
                (N'NOTE_TYPE', N'NOTE', N'Note', 40, 0, NULL),
                (N'NOTE_TYPE', N'USER_NOTE', N'Personal Note', 50, 0, NULL),

                (N'PRIORITY', N'CRITICAL', N'Critical', 10, 0, NULL),
                (N'PRIORITY', N'HIGH', N'High', 20, 0, NULL),
                (N'PRIORITY', N'NORMAL', N'Normal', 30, 1, NULL),
                (N'PRIORITY', N'LOW', N'Low', 40, 0, NULL),

                (N'CATEGORY', N'GENERAL', N'General', 10, 1, NULL),
                (N'CATEGORY', N'SYSTEM', N'System', 20, 0, NULL),
                (N'CATEGORY', N'HR', N'HR', 30, 0, NULL),
                (N'CATEGORY', N'FINANCE', N'Finance', 40, 0, NULL),
                (N'CATEGORY', N'OPERATIONS', N'Operations', 50, 0, NULL),
                (N'CATEGORY', N'SETUP', N'Setup', 60, 0, NULL),
                (N'CATEGORY', N'ONBOARDING', N'Onboarding', 70, 0, NULL),

                (N'VISIBILITY_TYPE', N'GENERAL', N'Everyone / General', 10, 1, NULL),
                (N'VISIBILITY_TYPE', N'STAFF', N'Selected Staff', 20, 0, NULL),
                (N'VISIBILITY_TYPE', N'MENU', N'Specific Menu', 30, 0, NULL),
                (N'VISIBILITY_TYPE', N'RECORD', N'Specific Record', 40, 0, NULL),
                (N'VISIBILITY_TYPE', N'PRIVATE', N'Private', 50, 0, NULL);

            MERGE dbo.AppLookupValues AS target
            USING (
                SELECT
                    lookupType.LookupTypeId,
                    value.ValueCode,
                    value.DisplayText,
                    value.SortOrder,
                    value.IsDefault,
                    value.MetadataJson
                FROM @Values value
                INNER JOIN dbo.AppLookupTypes lookupType
                    ON lookupType.LookupTypeCode = value.LookupTypeCode
            ) AS source
               ON target.LookupTypeId = source.LookupTypeId
              AND target.ValueCode = source.ValueCode
            WHEN MATCHED THEN UPDATE SET
                DisplayText = source.DisplayText,
                SortOrder = source.SortOrder,
                IsDefault = source.IsDefault,
                MetadataJson = source.MetadataJson,
                IsActive = 1
            WHEN NOT MATCHED THEN INSERT
                (LookupTypeId, ValueCode, DisplayText, SortOrder, IsDefault, IsActive, MetadataJson, CreatedOn)
            VALUES
                (source.LookupTypeId, source.ValueCode, source.DisplayText, source.SortOrder, source.IsDefault, 1, source.MetadataJson, SYSUTCDATETIME());
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE value
            SET IsActive = 0, IsDefault = 0
            FROM dbo.AppLookupValues value
            INNER JOIN dbo.AppLookupTypes lookupType
                ON lookupType.LookupTypeId = value.LookupTypeId
            WHERE lookupType.LookupTypeCode IN (N'NOTE_TYPE', N'PRIORITY', N'CATEGORY', N'VISIBILITY_TYPE');
            """);
    }
}
