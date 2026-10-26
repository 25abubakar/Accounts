using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261027000000_PreserveHistoricalAssessmentBlanks")]
public sealed class PreserveHistoricalAssessmentBlanks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropUniqueConstraint("UQ_StaffAssessments_Period", "StaffAssessments", "dbo");
        migrationBuilder.AlterColumn<Guid>("AssessorPersonId", "StaffAssessments", schema:"dbo", type:"uniqueidentifier",
            nullable:true, oldClrType:typeof(Guid), oldType:"uniqueidentifier");
        migrationBuilder.AddUniqueConstraint("UQ_StaffAssessments_Period", "StaffAssessments",
            new[]{"TenantId","AssessorPersonId","SubjectPersonId","AssessmentYear","AssessmentMonth"}, "dbo");
        migrationBuilder.AddColumn<string>("SourceSnapshotJson", "StaffAssessments", schema:"dbo", type:"nvarchar(max)", nullable:true);
        migrationBuilder.Sql("""
            DECLARE @sql nvarchar(max)=OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_Assessment_FinalList'));
            IF @sql IS NULL THROW 51000,'Final Assessment procedure missing.',1;
            IF CHARINDEX(N'AND p.IsActive = 1',@sql)=0 THROW 51000,'Historical staff projection did not match expected definition.',1;
            SET @sql=REPLACE(@sql,N'AND p.IsActive = 1',N'AND (p.IsActive = 1 OR EXISTS (SELECT 1 FROM dbo.StaffAssessments historical WHERE historical.TenantId=@TenantId AND historical.SubjectPersonId=p.PersonId AND historical.AssessmentYear=@Year AND historical.AssessmentMonth=@Month))');
            SET @sql=REPLACE(@sql,N'vac.Department,',N'vac.Department, p.LastDepartment,');
            SET @sql=REPLACE(@sql,N'COALESCE(des.TitleName, vac.JobTitle,',N'COALESCE(des.TitleName, vac.JobTitle, p.LastJobTitle,');
            DECLARE @start int=CHARINDEX(N'PROCEDURE',UPPER(@sql));
            IF @start=0 THROW 51000,'Unexpected Final Assessment procedure definition.',1;
            SET @sql=N'CREATE OR ALTER '+SUBSTRING(@sql,@start,LEN(@sql));
            EXEC sys.sp_executesql @sql;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Historical blank assessors and source snapshots must be preserved before rollback.");
}
