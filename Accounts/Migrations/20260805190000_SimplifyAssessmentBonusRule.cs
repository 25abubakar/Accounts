using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[Migration("20260805190000_SimplifyAssessmentBonusRule")]
public sealed class SimplifyAssessmentBonusRule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        IF COL_LENGTH(N'dbo.AssessmentBonusRules', N'DecrementAmount') IS NULL
            ALTER TABLE dbo.AssessmentBonusRules ADD DecrementAmount decimal(18,2) NOT NULL CONSTRAINT DF_AssessmentBonusRules_Decrement DEFAULT(0);
        """);
        migrationBuilder.Sql("""
        IF COL_LENGTH(N'dbo.AssessmentBonusRules', N'MinimumBonusAmount') IS NULL
            ALTER TABLE dbo.AssessmentBonusRules ADD MinimumBonusAmount decimal(18,2) NOT NULL CONSTRAINT DF_AssessmentBonusRules_Minimum DEFAULT(0);
        """);
        migrationBuilder.Sql("""
        ;WITH duplicates AS (
            SELECT Id, ROW_NUMBER() OVER(PARTITION BY TenantId ORDER BY CASE WHEN RankNumber=1 THEN 0 ELSE 1 END, Id) AS rn
            FROM dbo.AssessmentBonusRules
        )
        DELETE FROM duplicates WHERE rn > 1;

        UPDATE dbo.AssessmentBonusRules SET RankNumber=1, AppliesToHigherRanks=1,
            MinimumBonusAmount=CASE WHEN MinimumBonusAmount=0 THEN BonusAmount ELSE MinimumBonusAmount END;

        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AssessmentBonusRules') AND name=N'IX_AssessmentBonusRules_TenantId_RankNumber')
            DROP INDEX IX_AssessmentBonusRules_TenantId_RankNumber ON dbo.AssessmentBonusRules;
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AssessmentBonusRules') AND name=N'UX_AssessmentBonusRules_OneFallback')
            DROP INDEX UX_AssessmentBonusRules_OneFallback ON dbo.AssessmentBonusRules;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AssessmentBonusRules') AND name=N'IX_AssessmentBonusRules_TenantId')
            CREATE UNIQUE INDEX IX_AssessmentBonusRules_TenantId ON dbo.AssessmentBonusRules(TenantId);
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.AssessmentBonusRules') AND name=N'IX_AssessmentBonusRules_TenantId')
            DROP INDEX IX_AssessmentBonusRules_TenantId ON dbo.AssessmentBonusRules;
        CREATE UNIQUE INDEX IX_AssessmentBonusRules_TenantId_RankNumber ON dbo.AssessmentBonusRules(TenantId,RankNumber);
        ALTER TABLE dbo.AssessmentBonusRules DROP COLUMN DecrementAmount, MinimumBonusAmount;
        """);
}
