using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260731014500_FixProcessReportRowVersionSerialization")]
public sealed class FixProcessReportRowVersionSerialization : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_ProcessReport_List
                @TenantId int,
                @ActorUserId nvarchar(450),
                @Mode nvarchar(20) = N'INBOX'
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @StaffId uniqueidentifier;
                SELECT TOP(1) @StaffId=s.StaffId
                FROM dbo.Persons p
                JOIN dbo.StaffVacancy s ON s.PersonId=p.PersonId AND s.TenantId=p.TenantId
                WHERE p.TenantId=@TenantId AND p.IdentityUserId=@ActorUserId AND p.IsActive=1;
                IF @StaffId IS NULL
                    THROW 51210, 'No active staff profile is linked to this account.', 1;

                SELECT r.Id,r.RequestNumber,r.Title,r.Description,r.SourceModule,r.SourceRecordId,
                       category.Code AS CategoryCode,category.Name AS CategoryName,
                       priority.Code AS PriorityCode,priority.Name AS PriorityName,
                       priority.ColorCode AS PriorityColor,
                       status.Code AS StatusCode,status.Name AS StatusName,
                       status.ColorCode AS StatusColor,status.IsTerminal,
                       requester.StaffId AS RequesterStaffId,
                       requesterPerson.FullName AS RequesterName,requester.LoginId AS RequesterNumber,
                       subject.StaffId AS SubjectStaffId,
                       subjectPerson.FullName AS SubjectName,subject.LoginId AS SubjectNumber,
                       approverPerson.FullName AS CurrentApproverName,
                       r.CreatedDateUtc,r.ModifiedDateUtc,r.CompletedDateUtc,
                       CONVERT(varchar(16),CONVERT(varbinary(8),r.RowVersion),2) AS RowVersion
                FROM dbo.ProcessReports r
                JOIN dbo.ProcessWorkflowCategories category ON category.Id=r.CategoryId
                JOIN dbo.ProcessWorkflowPriorities priority ON priority.Id=r.PriorityId
                JOIN dbo.ProcessWorkflowStatuses status ON status.Id=r.StatusId
                JOIN dbo.StaffVacancy requester ON requester.StaffId=r.RequesterStaffId
                JOIN dbo.Persons requesterPerson ON requesterPerson.PersonId=requester.PersonId
                JOIN dbo.StaffVacancy subject ON subject.StaffId=r.SubjectStaffId
                JOIN dbo.Persons subjectPerson ON subjectPerson.PersonId=subject.PersonId
                LEFT JOIN dbo.StaffVacancy approver ON approver.StaffId=r.CurrentApproverStaffId
                LEFT JOIN dbo.Persons approverPerson ON approverPerson.PersonId=approver.PersonId
                WHERE r.TenantId=@TenantId AND
                    ((@Mode=N'INBOX' AND r.CurrentApproverStaffId=@StaffId AND status.IsTerminal=0) OR
                     (@Mode=N'MINE' AND r.RequesterStaffId=@StaffId) OR
                     (@Mode=N'COMPLETED' AND status.IsTerminal=1 AND
                         (r.RequesterStaffId=@StaffId OR EXISTS
                            (SELECT 1 FROM dbo.ProcessReportRouteSteps step
                             WHERE step.ReportId=r.Id AND step.ApproverStaffId=@StaffId
                               AND step.ActedDateUtc IS NOT NULL))))
                ORDER BY priority.DisplayOrder DESC,r.CreatedDateUtc DESC;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep the safe hexadecimal row-version serialization on rollback.
    }
}
