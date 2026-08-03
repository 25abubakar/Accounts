using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260731130000_FinalizeReportsAtCategoryApprover")]
public sealed class FinalizeReportsAtCategoryApprover : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var submitProcedure = UseAlternativeReporterWhenPrimaryIsOnLeave
            .BuildSubmitProcedure(useAlternativeReporter: true)
            .Replace(
                "IF NOT EXISTS(SELECT 1 FROM @Route) THROW 51203, 'No active reporting manager is configured for this staff member.', 1;",
                """
                IF NOT EXISTS(SELECT 1 FROM @Route)
                    THROW 51203, 'No active reporting manager is configured for this staff member.', 1;

                IF NOT EXISTS
                (
                    SELECT 1 FROM dbo.ProcessCategoryApprovers
                    WHERE TenantId=@TenantId AND CategoryId=@CategoryId
                )
                    THROW 51205, 'No final approver is configured for the selected category.', 1;

                DECLARE @CategoryApprovalStep int;
                SELECT @CategoryApprovalStep=MIN(route.StepOrder)
                FROM @Route route
                JOIN dbo.ProcessCategoryApprovers categoryApprover
                  ON categoryApprover.TenantId=@TenantId
                 AND categoryApprover.CategoryId=@CategoryId
                 AND categoryApprover.StaffId=route.StaffId;

                IF @CategoryApprovalStep IS NULL
                    THROW 51206, 'None of the category approvers are present in this employee reporting route.', 1;

                -- The first configured category approver is the final authority.
                -- Higher reporting posts are deliberately excluded from this snapshot.
                DELETE FROM @Route WHERE StepOrder>@CategoryApprovalStep;
                """);

        migrationBuilder.Sql(submitProcedure);
        migrationBuilder.Sql(ActionProcedureSql);
        migrationBuilder.Sql(ListProcedureSql);
        migrationBuilder.Sql(TimelineProcedureSql);
        migrationBuilder.Sql(
            """
            UPDATE dbo.ProcessWorkflowActionTypes
            SET Name=N'Recommend & Forward'
            WHERE Code=N'APPROVE_FORWARD';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE dbo.ProcessWorkflowActionTypes
            SET Name=N'Approve & Forward'
            WHERE Code=N'APPROVE_FORWARD';
            """);
    }

    internal const string ActionProcedureSql = """
        CREATE OR ALTER PROCEDURE dbo.usp_ProcessReport_Action
            @TenantId int,@ActorUserId nvarchar(450),@ReportId bigint,
            @ActionCode nvarchar(40),@Comments nvarchar(2000)=NULL,
            @ExpectedRowVersionHex varchar(24)
        AS
        BEGIN
            SET NOCOUNT ON; SET XACT_ABORT ON;
            DECLARE @ActorStaffId uniqueidentifier,@RequesterStaffId uniqueidentifier,
                    @CurrentApprover uniqueidentifier,@CurrentStatusId int,@CategoryId int,
                    @CurrentStatusCode nvarchar(40),@ActionTypeId int,@RequiresComments bit,
                    @CurrentStepId bigint,@CurrentStep int,@NextStepId bigint,
                    @NextApprover uniqueidentifier,@NextStatusCode nvarchar(40),@NextStatusId int,
                    @IsCategoryApprover bit=0,
                    @ExpectedRowVersion binary(8)=CONVERT(binary(8),@ExpectedRowVersionHex,2);

            SELECT TOP(1) @ActorStaffId=staff.StaffId
            FROM dbo.Persons person
            JOIN dbo.StaffVacancy staff ON staff.PersonId=person.PersonId AND staff.TenantId=person.TenantId
            WHERE person.TenantId=@TenantId AND person.IdentityUserId=@ActorUserId AND person.IsActive=1;
            SELECT @ActionTypeId=Id,@RequiresComments=RequiresComments
            FROM dbo.ProcessWorkflowActionTypes WHERE Code=@ActionCode AND IsActive=1;
            IF @ActorStaffId IS NULL OR @ActionTypeId IS NULL THROW 51230, 'Invalid workflow actor or action.',1;
            IF @RequiresComments=1 AND NULLIF(LTRIM(RTRIM(@Comments)),N'') IS NULL
                THROW 51231, 'Comments are required for this action.',1;

            BEGIN TRANSACTION;
            SELECT @RequesterStaffId=RequesterStaffId,@CurrentApprover=CurrentApproverStaffId,
                   @CurrentStatusId=StatusId,@CategoryId=CategoryId
            FROM dbo.ProcessReports WITH(UPDLOCK,ROWLOCK)
            WHERE Id=@ReportId AND TenantId=@TenantId AND RowVersion=@ExpectedRowVersion;
            IF @CurrentStatusId IS NULL THROW 51232, 'This task changed after it was loaded. Refresh and try again.',1;
            SELECT @CurrentStatusCode=Code FROM dbo.ProcessWorkflowStatuses WHERE Id=@CurrentStatusId;

            IF @ActionCode=N'RESUBMIT'
            BEGIN
                IF @RequesterStaffId<>@ActorStaffId OR @CurrentStatusCode<>N'RETURNED'
                    THROW 51233, 'Only the requester can resubmit a returned report.',1;
                SELECT TOP(1) @CurrentStepId=step.Id,@CurrentStep=step.StepOrder,@NextApprover=step.ApproverStaffId
                FROM dbo.ProcessReportActions history
                JOIN dbo.ProcessWorkflowActionTypes historyType ON historyType.Id=history.ActionTypeId
                JOIN dbo.ProcessReportRouteSteps step ON step.Id=history.RouteStepId
                JOIN dbo.StaffVacancy staff ON staff.StaffId=step.ApproverStaffId
                JOIN dbo.Persons person ON person.PersonId=staff.PersonId
                WHERE history.ReportId=@ReportId
                  AND historyType.Code IN(N'RETURN_CORRECTION',N'RETURN_INFORMATION') AND person.IsActive=1
                ORDER BY history.ActionDateUtc DESC,history.Id DESC;
                IF @CurrentStepId IS NULL THROW 51235, 'The return route is no longer available.',1;
                SET @NextStatusCode=N'PENDING';
                SELECT @NextStatusId=Id FROM dbo.ProcessWorkflowStatuses WHERE Code=@NextStatusCode AND IsActive=1;
                UPDATE dbo.ProcessReportRouteSteps SET IsCurrent=0 WHERE ReportId=@ReportId;
                UPDATE dbo.ProcessReportRouteSteps SET IsCurrent=1,AssignedDateUtc=SYSUTCDATETIME(),ActedDateUtc=NULL,StatusId=@NextStatusId WHERE Id=@CurrentStepId;
                UPDATE dbo.ProcessReports SET StatusId=@NextStatusId,CurrentApproverStaffId=@NextApprover,ModifiedDateUtc=SYSUTCDATETIME(),CompletedDateUtc=NULL WHERE Id=@ReportId;
                INSERT dbo.ProcessReportActions(ReportId,RouteStepId,ActionTypeId,FromStatusId,ToStatusId,ActorStaffId,ActorUserId,Comments)
                VALUES(@ReportId,@CurrentStepId,@ActionTypeId,@CurrentStatusId,@NextStatusId,@ActorStaffId,@ActorUserId,@Comments);
            END
            ELSE
            BEGIN
                IF @CurrentApprover<>@ActorStaffId THROW 51233, 'This task is not assigned to you.',1;
                IF @RequesterStaffId=@ActorStaffId THROW 51234, 'Self-approval is not allowed.',1;
                SELECT @CurrentStepId=Id,@CurrentStep=StepOrder FROM dbo.ProcessReportRouteSteps WHERE ReportId=@ReportId AND IsCurrent=1;
                IF @CurrentStepId IS NULL THROW 51235, 'The active workflow step is missing.',1;
                IF EXISTS(SELECT 1 FROM dbo.ProcessCategoryApprovers WHERE TenantId=@TenantId AND CategoryId=@CategoryId AND StaffId=@ActorStaffId)
                    SET @IsCategoryApprover=1;

                IF @ActionCode=N'APPROVE_FORWARD'
                BEGIN
                    IF @CurrentStatusCode NOT IN(N'PENDING',N'FORWARDED',N'ESCALATED') THROW 51236, 'Approval is not valid in the current workflow state.',1;
                    IF @IsCategoryApprover=1
                    BEGIN
                        SET @NextStepId=NULL; SET @NextApprover=NULL; SET @NextStatusCode=N'RESOLVED';
                    END
                    ELSE
                    BEGIN
                        SELECT TOP(1) @NextStepId=Id,@NextApprover=ApproverStaffId
                        FROM dbo.ProcessReportRouteSteps WHERE ReportId=@ReportId AND StepOrder>@CurrentStep ORDER BY StepOrder;
                        IF @NextStepId IS NULL THROW 51239, 'A configured category approver must give the final approval.',1;
                        SET @NextStatusCode=N'FORWARDED';
                    END
                END
                ELSE IF @ActionCode=N'ESCALATE'
                BEGIN
                    IF @IsCategoryApprover=1 THROW 51236, 'A category approver must approve, reject, or return this request.',1;
                    SELECT TOP(1) @NextStepId=Id,@NextApprover=ApproverStaffId
                    FROM dbo.ProcessReportRouteSteps WHERE ReportId=@ReportId AND StepOrder>@CurrentStep ORDER BY StepOrder;
                    IF @NextStepId IS NULL THROW 51238, 'No higher active approver exists in the saved route.',1;
                    SET @NextStatusCode=N'ESCALATED';
                END
                ELSE IF @ActionCode=N'REJECT' SET @NextStatusCode=N'REJECTED';
                ELSE IF @ActionCode IN(N'RETURN_CORRECTION',N'RETURN_INFORMATION') SET @NextStatusCode=N'RETURNED';
                ELSE IF @ActionCode=N'RESOLVE' AND @CurrentStatusCode=N'PENDING_RESOLUTION' SET @NextStatusCode=N'RESOLVED';
                ELSE THROW 51236, 'This action is not valid for an assigned task.',1;

                SELECT @NextStatusId=Id FROM dbo.ProcessWorkflowStatuses WHERE Code=@NextStatusCode AND IsActive=1;
                IF @NextStatusId IS NULL THROW 51237, 'The target workflow status is not configured.',1;
                UPDATE dbo.ProcessReportRouteSteps SET IsCurrent=0,ActedDateUtc=SYSUTCDATETIME(),StatusId=@NextStatusId WHERE Id=@CurrentStepId;
                IF @NextStepId IS NOT NULL UPDATE dbo.ProcessReportRouteSteps SET IsCurrent=1,AssignedDateUtc=SYSUTCDATETIME(),ActedDateUtc=NULL,StatusId=@NextStatusId WHERE Id=@NextStepId;
                UPDATE dbo.ProcessReports SET StatusId=@NextStatusId,
                    CurrentApproverStaffId=CASE WHEN @NextStepId IS NOT NULL THEN @NextApprover ELSE NULL END,
                    ModifiedDateUtc=SYSUTCDATETIME(),
                    CompletedDateUtc=CASE WHEN @NextStatusCode IN(N'RESOLVED',N'REJECTED') THEN SYSUTCDATETIME() ELSE NULL END
                WHERE Id=@ReportId;
                INSERT dbo.ProcessReportActions(ReportId,RouteStepId,ActionTypeId,FromStatusId,ToStatusId,ActorStaffId,ActorUserId,Comments)
                VALUES(@ReportId,@CurrentStepId,@ActionTypeId,@CurrentStatusId,@NextStatusId,@ActorStaffId,@ActorUserId,@Comments);
            END;
            COMMIT TRANSACTION;
            SELECT report.Id,report.RequestNumber,status.Code AS StatusCode,status.Name AS StatusName,
                   CONVERT(varchar(16),CONVERT(varbinary(8),report.RowVersion),2) AS RowVersion
            FROM dbo.ProcessReports report JOIN dbo.ProcessWorkflowStatuses status ON status.Id=report.StatusId
            WHERE report.Id=@ReportId;
        END
        """;

    internal const string ListProcedureSql = """
        CREATE OR ALTER PROCEDURE dbo.usp_ProcessReport_List
            @TenantId int,@ActorUserId nvarchar(450),@Mode nvarchar(20)=N'INBOX'
        AS
        BEGIN
            SET NOCOUNT ON;
            DECLARE @StaffId uniqueidentifier;
            SELECT TOP(1) @StaffId=staff.StaffId FROM dbo.Persons person
            JOIN dbo.StaffVacancy staff ON staff.PersonId=person.PersonId AND staff.TenantId=person.TenantId
            WHERE person.TenantId=@TenantId AND person.IdentityUserId=@ActorUserId AND person.IsActive=1;
            IF @StaffId IS NULL THROW 51210, 'No active staff profile is linked to this account.',1;
            SELECT report.Id,report.RequestNumber,report.Title,report.Description,report.SourceModule,report.SourceRecordId,
                   category.Code AS CategoryCode,category.Name AS CategoryName,priority.Code AS PriorityCode,
                   priority.Name AS PriorityName,priority.ColorCode AS PriorityColor,status.Code AS StatusCode,
                   status.Name AS StatusName,status.ColorCode AS StatusColor,status.IsTerminal,
                   requester.StaffId AS RequesterStaffId,requesterPerson.FullName AS RequesterName,requester.LoginId AS RequesterNumber,
                   subject.StaffId AS SubjectStaffId,subjectPerson.FullName AS SubjectName,subject.LoginId AS SubjectNumber,
                   approverPerson.FullName AS CurrentApproverName,
                   CONVERT(bit,CASE WHEN report.CurrentApproverStaffId=@StaffId AND EXISTS
                   (SELECT 1 FROM dbo.ProcessCategoryApprovers finalApprover WHERE finalApprover.TenantId=report.TenantId AND finalApprover.CategoryId=report.CategoryId AND finalApprover.StaffId=@StaffId)
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
            WHERE report.TenantId=@TenantId AND
              ((@Mode=N'INBOX' AND report.CurrentApproverStaffId=@StaffId AND status.IsTerminal=0) OR
               (@Mode=N'MINE' AND report.RequesterStaffId=@StaffId) OR
               (@Mode=N'COMPLETED' AND status.IsTerminal=1 AND
                (report.RequesterStaffId=@StaffId OR EXISTS(SELECT 1 FROM dbo.ProcessReportRouteSteps route WHERE route.ReportId=report.Id AND route.ApproverStaffId=@StaffId AND route.ActedDateUtc IS NOT NULL))))
            ORDER BY priority.DisplayOrder DESC,report.CreatedDateUtc DESC;
        END
        """;

    internal const string TimelineProcedureSql = """
        CREATE OR ALTER PROCEDURE dbo.usp_ProcessReport_Timeline
            @TenantId int,@ActorUserId nvarchar(450),@ReportId bigint
        AS
        BEGIN
            SET NOCOUNT ON;
            DECLARE @StaffId uniqueidentifier;
            SELECT TOP(1) @StaffId=staff.StaffId FROM dbo.Persons person
            JOIN dbo.StaffVacancy staff ON staff.PersonId=person.PersonId AND staff.TenantId=person.TenantId
            WHERE person.TenantId=@TenantId AND person.IdentityUserId=@ActorUserId AND person.IsActive=1;
            IF NOT EXISTS
            (
                SELECT 1 FROM dbo.ProcessReports report
                WHERE report.Id=@ReportId AND report.TenantId=@TenantId AND
                (report.RequesterStaffId=@StaffId OR report.SubjectStaffId=@StaffId OR report.CurrentApproverStaffId=@StaffId OR
                 EXISTS(SELECT 1 FROM dbo.ProcessReportRouteSteps route WHERE route.ReportId=report.Id AND route.ApproverStaffId=@StaffId))
            ) THROW 51220, 'You cannot view this workflow.',1;

            SELECT step.Id,step.StepOrder,person.FullName AS ApproverName,staff.LoginId AS ApproverNumber,
                   status.Code AS StatusCode,status.Name AS StatusName,status.ColorCode AS StatusColor,
                   step.AssignedDateUtc,step.ActedDateUtc,step.IsCurrent
            FROM dbo.ProcessReportRouteSteps step
            JOIN dbo.StaffVacancy staff ON staff.StaffId=step.ApproverStaffId
            JOIN dbo.Persons person ON person.PersonId=staff.PersonId
            JOIN dbo.ProcessWorkflowStatuses status ON status.Id=step.StatusId
            WHERE step.ReportId=@ReportId ORDER BY step.StepOrder;

            SELECT action.Id,actionType.Code AS ActionCode,
                   CASE WHEN actionType.Code=N'APPROVE_FORWARD' AND EXISTS
                   (
                       SELECT 1 FROM dbo.ProcessReports report
                       JOIN dbo.ProcessCategoryApprovers categoryApprover
                         ON categoryApprover.TenantId=report.TenantId
                        AND categoryApprover.CategoryId=report.CategoryId
                        AND categoryApprover.StaffId=action.ActorStaffId
                       WHERE report.Id=action.ReportId
                   ) THEN N'Approve' ELSE actionType.Name END AS ActionName,
                   actor.FullName AS ActorName,action.Comments,action.ActionDateUtc,
                   status.Code AS ToStatusCode,status.Name AS ToStatusName,status.ColorCode AS ToStatusColor
            FROM dbo.ProcessReportActions action
            JOIN dbo.ProcessWorkflowActionTypes actionType ON actionType.Id=action.ActionTypeId
            JOIN dbo.StaffVacancy staff ON staff.StaffId=action.ActorStaffId
            JOIN dbo.Persons actor ON actor.PersonId=staff.PersonId
            JOIN dbo.ProcessWorkflowStatuses status ON status.Id=action.ToStatusId
            WHERE action.ReportId=@ReportId ORDER BY action.ActionDateUtc,action.Id;
        END
        """;
}
