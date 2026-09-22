using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Hours after assessment open when Alternative reporter may mark if Primary has not.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260916140000_AssessmentAlternativeFallbackHours")]
public sealed class AssessmentAlternativeFallbackHours : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Separate dynamic SQL so CHECK compiles after the column exists (same-batch ALTER fails on SQL Server).
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.AssessmentBonusRules', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.AssessmentBonusRules', N'AlternativeFallbackAfterHours') IS NULL
            BEGIN
                ALTER TABLE dbo.AssessmentBonusRules
                ADD AlternativeFallbackAfterHours int NOT NULL
                    CONSTRAINT DF_AssessmentBonusRules_AltFallbackHours DEFAULT (48);
            END

            IF OBJECT_ID(N'dbo.AssessmentBonusRules', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.AssessmentBonusRules', N'AlternativeFallbackAfterHours') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1 FROM sys.check_constraints
                    WHERE name = N'CK_AssessmentBonusRules_AltFallbackHours'
                      AND parent_object_id = OBJECT_ID(N'dbo.AssessmentBonusRules')
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE dbo.AssessmentBonusRules
                    ADD CONSTRAINT CK_AssessmentBonusRules_AltFallbackHours
                        CHECK (AlternativeFallbackAfterHours BETWEEN 1 AND 720);
                ');
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF EXISTS (
                SELECT 1 FROM sys.check_constraints
                WHERE name = N'CK_AssessmentBonusRules_AltFallbackHours'
                  AND parent_object_id = OBJECT_ID(N'dbo.AssessmentBonusRules')
            )
                ALTER TABLE dbo.AssessmentBonusRules DROP CONSTRAINT CK_AssessmentBonusRules_AltFallbackHours;

            IF COL_LENGTH(N'dbo.AssessmentBonusRules', N'AlternativeFallbackAfterHours') IS NOT NULL
            BEGIN
                DECLARE @df sysname =
                (
                    SELECT dc.name
                    FROM sys.default_constraints dc
                    INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
                    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.AssessmentBonusRules')
                      AND c.name = N'AlternativeFallbackAfterHours'
                );
                IF @df IS NOT NULL
                    EXEC(N'ALTER TABLE dbo.AssessmentBonusRules DROP CONSTRAINT [' + @df + N']');
                ALTER TABLE dbo.AssessmentBonusRules DROP COLUMN AlternativeFallbackAfterHours;
            END
            """);
    }
}
