using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[Migration("20260805203000_AddAssessmentScheduling")]
public sealed class AddAssessmentScheduling : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
        IF COL_LENGTH(N'dbo.StaffAssessments', N'Amount') IS NULL
            ALTER TABLE dbo.StaffAssessments ADD Amount decimal(18,2) NULL;
        """);

        // Rating participates in this index, so remove the dependency first.
        // Separate migration commands are intentional: SQL Server compiles each
        // command only after the preceding schema change has completed.
        migrationBuilder.Sql("""
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.StaffAssessments') AND name=N'UX_StaffAssessments_UniqueMonthlyRank')
            DROP INDEX UX_StaffAssessments_UniqueMonthlyRank ON dbo.StaffAssessments;
        """);

        migrationBuilder.Sql("""
        IF COL_LENGTH(N'dbo.StaffAssessments', N'Rating') IS NOT NULL
            ALTER TABLE dbo.StaffAssessments ALTER COLUMN Rating tinyint NULL;
        """);

        migrationBuilder.Sql("""
        IF COL_LENGTH(N'dbo.StaffAssessments', N'Remarks') IS NOT NULL
            ALTER TABLE dbo.StaffAssessments DROP COLUMN Remarks;
        """);

        migrationBuilder.Sql("""
        IF OBJECT_ID(N'dbo.StaffAssessments', N'U') IS NOT NULL
           AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.StaffAssessments') AND name=N'UX_StaffAssessments_UniqueMonthlyRank')
            CREATE UNIQUE INDEX UX_StaffAssessments_UniqueMonthlyRank
                ON dbo.StaffAssessments(TenantId,AssessorPersonId,AssessmentYear,AssessmentMonth,Rating)
                WHERE Rating IS NOT NULL;
        """);

        migrationBuilder.Sql("""
        IF OBJECT_ID(N'dbo.AssessmentSchedules',N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.AssessmentSchedules
            (
                Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AssessmentSchedules PRIMARY KEY,
                TenantId int NOT NULL,
                AssessmentYear int NOT NULL,
                AssessmentMonth tinyint NOT NULL,
                OpenDay tinyint NOT NULL CONSTRAINT DF_AssessmentSchedules_OpenDay DEFAULT(25),
                IsManualOverride bit NOT NULL CONSTRAINT DF_AssessmentSchedules_Manual DEFAULT(0),
                IsActive bit NOT NULL CONSTRAINT DF_AssessmentSchedules_Active DEFAULT(1),
                CreatedDateUtc datetime2 NOT NULL CONSTRAINT DF_AssessmentSchedules_Created DEFAULT SYSUTCDATETIME(),
                CreatedByUserId nvarchar(450) NULL,
                CONSTRAINT FK_AssessmentSchedules_Tenant FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id),
                CONSTRAINT CK_AssessmentSchedules_Month CHECK(AssessmentMonth BETWEEN 1 AND 12),
                CONSTRAINT CK_AssessmentSchedules_OpenDay CHECK(OpenDay BETWEEN 1 AND 31),
                CONSTRAINT UQ_AssessmentSchedules_Period UNIQUE(TenantId,AssessmentYear,AssessmentMonth)
            );
        END
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
