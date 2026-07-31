using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260731018000_ExposeFinalProcessApprover")]
public sealed class ExposeFinalProcessApprover : Migration
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
                SELECT TOP(1) @StaffId=staff.StaffId
                FROM dbo.Persons person
                JOIN dbo.StaffVacancy staff
                  ON staff.PersonId=person.PersonId
                 AND staff.TenantId=person.TenantId
                WHERE person.TenantId=@TenantId
                  AND person.IdentityUserId=@ActorUserId
                  AND person.IsActive=1;

                IF @StaffId IS NULL
                    THROW 51210, 'No active staff profile is linked to this account.', 1;

                SELECT report.Id,report.RequestNumber,report.Title,report.Description,
                       report.SourceModule,report.SourceRecordId,
                       category.Code AS CategoryCode,category.Name AS CategoryName,
                       priority.Code AS PriorityCode,priority.Name AS PriorityName,
                       priority.ColorCode AS PriorityColor,
                       status.Code AS StatusCode,status.Name AS StatusName,
                       status.ColorCode AS StatusColor,status.IsTerminal,
                       requester.StaffId AS RequesterStaffId,
                       requesterPerson.FullName AS RequesterName,
                       requester.LoginId AS RequesterNumber,
                       subject.StaffId AS SubjectStaffId,
                       subjectPerson.FullName AS SubjectName,
                       subject.LoginId AS SubjectNumber,
                       approverPerson.FullName AS CurrentApproverName,
                       CONVERT(bit,CASE
                           WHEN report.CurrentApproverStaffId=@StaffId
                            AND currentStep.Id IS NOT NULL
                            AND NOT EXISTS
                            (
                                SELECT 1
                                FROM dbo.ProcessReportRouteSteps laterStep
                                WHERE laterStep.ReportId=report.Id
                                  AND laterStep.StepOrder>currentStep.StepOrder
                            )
                           THEN 1 ELSE 0 END) AS IsFinalApprover,
                       report.CreatedDateUtc,report.ModifiedDateUtc,report.CompletedDateUtc,
                       CONVERT(varchar(16),CONVERT(varbinary(8),report.RowVersion),2) AS RowVersion
                FROM dbo.ProcessReports report
                JOIN dbo.ProcessWorkflowCategories category ON category.Id=report.CategoryId
                JOIN dbo.ProcessWorkflowPriorities priority ON priority.Id=report.PriorityId
                JOIN dbo.ProcessWorkflowStatuses status ON status.Id=report.StatusId
                JOIN dbo.StaffVacancy requester ON requester.StaffId=report.RequesterStaffId
                JOIN dbo.Persons requesterPerson ON requesterPerson.PersonId=requester.PersonId
                JOIN dbo.StaffVacancy subject ON subject.StaffId=report.SubjectStaffId
                JOIN dbo.Persons subjectPerson ON subjectPerson.PersonId=subject.PersonId
                LEFT JOIN dbo.StaffVacancy approver ON approver.StaffId=report.CurrentApproverStaffId
                LEFT JOIN dbo.Persons approverPerson ON approverPerson.PersonId=approver.PersonId
                OUTER APPLY
                (
                    SELECT TOP(1) route.Id,route.StepOrder
                    FROM dbo.ProcessReportRouteSteps route
                    WHERE route.ReportId=report.Id AND route.IsCurrent=1
                    ORDER BY route.StepOrder DESC
                ) currentStep
                WHERE report.TenantId=@TenantId AND
                    ((@Mode=N'INBOX' AND report.CurrentApproverStaffId=@StaffId AND status.IsTerminal=0) OR
                     (@Mode=N'MINE' AND report.RequesterStaffId=@StaffId) OR
                     (@Mode=N'COMPLETED' AND status.IsTerminal=1 AND
                         (report.RequesterStaffId=@StaffId OR EXISTS
                            (SELECT 1 FROM dbo.ProcessReportRouteSteps route
                             WHERE route.ReportId=report.Id
                               AND route.ApproverStaffId=@StaffId
                               AND route.ActedDateUtc IS NOT NULL))))
                ORDER BY priority.DisplayOrder DESC,report.CreatedDateUtc DESC;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Forward-only API contract correction.
    }
}
