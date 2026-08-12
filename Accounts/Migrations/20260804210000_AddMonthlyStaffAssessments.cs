using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260804210000_AddMonthlyStaffAssessments")]
public sealed class AddMonthlyStaffAssessments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        IF OBJECT_ID(N'dbo.StaffAssessments',N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.StaffAssessments
            (
                Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_StaffAssessments PRIMARY KEY,
                TenantId int NOT NULL,
                AssessorPersonId uniqueidentifier NOT NULL,
                SubjectPersonId uniqueidentifier NOT NULL,
                AssessmentYear int NOT NULL,
                AssessmentMonth tinyint NOT NULL,
                Rating tinyint NOT NULL,
                Remarks nvarchar(2000) NOT NULL,
                CreatedDateUtc datetime2 NOT NULL CONSTRAINT DF_StaffAssessments_Created DEFAULT SYSUTCDATETIME(),
                ModifiedDateUtc datetime2 NULL,
                CONSTRAINT UQ_StaffAssessments_Period UNIQUE(TenantId,AssessorPersonId,SubjectPersonId,AssessmentYear,AssessmentMonth),
                CONSTRAINT CK_StaffAssessments_Month CHECK(AssessmentMonth BETWEEN 1 AND 12),
                CONSTRAINT CK_StaffAssessments_Rating CHECK(Rating BETWEEN 1 AND 5),
                CONSTRAINT CK_StaffAssessments_NotSelf CHECK(AssessorPersonId<>SubjectPersonId),
                CONSTRAINT FK_StaffAssessments_Tenant FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id),
                CONSTRAINT FK_StaffAssessments_Assessor FOREIGN KEY(AssessorPersonId) REFERENCES dbo.Persons(PersonId),
                CONSTRAINT FK_StaffAssessments_Subject FOREIGN KEY(SubjectPersonId) REFERENCES dbo.Persons(PersonId)
            );
            CREATE INDEX IX_StaffAssessments_SubjectPeriod ON dbo.StaffAssessments(TenantId,SubjectPersonId,AssessmentYear,AssessmentMonth);
        END
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE IF EXISTS dbo.StaffAssessments;");
}
