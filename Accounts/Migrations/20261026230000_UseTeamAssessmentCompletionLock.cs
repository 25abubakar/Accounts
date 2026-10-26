using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261026230000_UseTeamAssessmentCompletionLock")]
public sealed class UseTeamAssessmentCompletionLock : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            -- Repair the observed premature lock only. Scores, amounts and audit dates are preserved.
            UPDATE assessment SET IsLocked=0
            FROM dbo.StaffAssessments assessment
            WHERE assessment.TenantId=2 AND assessment.Id=1
              AND assessment.AssessmentYear=2026 AND assessment.AssessmentMonth=10
              AND assessment.IsFinalApproved=0 AND assessment.IsPostedToPayroll=0
              AND EXISTS (
                SELECT 1 FROM dbo.Persons subject
                WHERE subject.TenantId=assessment.TenantId AND subject.IsActive=1
                  AND (subject.ReportsToPersonId=assessment.AssessorPersonId OR subject.AlternativeReportsToPersonId=assessment.AssessorPersonId)
                  AND NOT EXISTS (SELECT 1 FROM dbo.StaffAssessments saved
                    WHERE saved.TenantId=assessment.TenantId AND saved.SubjectPersonId=subject.PersonId
                      AND saved.AssessmentYear=assessment.AssessmentYear AND saved.AssessmentMonth=assessment.AssessmentMonth
                      AND saved.Rating IS NOT NULL));

            DECLARE @sql nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_Assessment_FinalList'));
            IF @sql IS NULL THROW 51000,'Final Assessment procedure is missing; no projection patch applied.',1;
            SET @sql=REPLACE(@sql,N'WHEN sa.IsLocked = 1 OR sa.Rating IS NOT NULL THEN',N'WHEN sa.IsLocked = 1 THEN');
            DECLARE @start int=CHARINDEX(N'PROCEDURE',UPPER(@sql));
            IF @start=0 THROW 51000,'Unexpected Final Assessment procedure definition.',1;
            SET @sql=N'CREATE OR ALTER '+SUBSTRING(@sql,@start,LEN(@sql));
            EXEC sys.sp_executesql @sql;
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Do not re-lock incomplete teams or remove saved assessment data.
    }
}
