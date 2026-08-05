using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260804220000_AddAssessmentBonusRules")]
public sealed class AddAssessmentBonusRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        IF OBJECT_ID(N'dbo.AssessmentBonusRules',N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.AssessmentBonusRules
            (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AssessmentBonusRules PRIMARY KEY,
                TenantId int NOT NULL,
                RankNumber int NOT NULL,
                BonusAmount decimal(18,2) NOT NULL,
                AppliesToHigherRanks bit NOT NULL CONSTRAINT DF_AssessmentBonusRules_Fallback DEFAULT(0),
                IsActive bit NOT NULL CONSTRAINT DF_AssessmentBonusRules_Active DEFAULT(1),
                CreatedDateUtc datetime2 NOT NULL CONSTRAINT DF_AssessmentBonusRules_Created DEFAULT SYSUTCDATETIME(),
                ModifiedDateUtc datetime2 NULL,
                CONSTRAINT FK_AssessmentBonusRules_Tenant FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id),
                CONSTRAINT UQ_AssessmentBonusRules_Rank UNIQUE(TenantId,RankNumber),
                CONSTRAINT CK_AssessmentBonusRules_Rank CHECK(RankNumber > 0),
                CONSTRAINT CK_AssessmentBonusRules_Amount CHECK(BonusAmount >= 0)
            );
            CREATE UNIQUE INDEX UX_AssessmentBonusRules_OneFallback ON dbo.AssessmentBonusRules(TenantId) WHERE AppliesToHigherRanks=1;
        END;

        IF OBJECT_ID(N'dbo.StaffAssessments',N'U') IS NOT NULL
        BEGIN
            IF OBJECT_ID(N'dbo.CK_StaffAssessments_Rating',N'C') IS NOT NULL ALTER TABLE dbo.StaffAssessments DROP CONSTRAINT CK_StaffAssessments_Rating;
            ALTER TABLE dbo.StaffAssessments ADD CONSTRAINT CK_StaffAssessments_Rating CHECK(Rating BETWEEN 1 AND 255);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.StaffAssessments') AND name=N'UX_StaffAssessments_UniqueMonthlyRank')
                CREATE UNIQUE INDEX UX_StaffAssessments_UniqueMonthlyRank
                ON dbo.StaffAssessments(TenantId,AssessorPersonId,AssessmentYear,AssessmentMonth,Rating);
        END;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE IF EXISTS dbo.AssessmentBonusRules;
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.StaffAssessments') AND name=N'UX_StaffAssessments_UniqueMonthlyRank')
            DROP INDEX UX_StaffAssessments_UniqueMonthlyRank ON dbo.StaffAssessments;
        """);
}
