using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260731013000_CompleteProcessWorkflowStateMachine")]
public sealed class CompleteProcessWorkflowStateMachine : Migration
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

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_ProcessReport_Action
                @TenantId int,
                @ActorUserId nvarchar(450),
                @ReportId bigint,
                @ActionCode nvarchar(40),
                @Comments nvarchar(2000) = NULL,
                @ExpectedRowVersionHex varchar(24)
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                DECLARE @ActorStaffId uniqueidentifier,@RequesterStaffId uniqueidentifier,
                        @CurrentApprover uniqueidentifier,@CurrentStatusId int,
                        @CurrentStatusCode nvarchar(40),@ActionTypeId int,@RequiresComments bit,
                        @CurrentStepId bigint,@CurrentStep int,@NextStepId bigint,
                        @NextApprover uniqueidentifier,@NextStatusCode nvarchar(40),@NextStatusId int,
                        @ExpectedRowVersion binary(8)=CONVERT(binary(8),@ExpectedRowVersionHex,2);

                SELECT TOP(1) @ActorStaffId=s.StaffId
                FROM dbo.Persons p
                JOIN dbo.StaffVacancy s ON s.PersonId=p.PersonId AND s.TenantId=p.TenantId
                WHERE p.TenantId=@TenantId AND p.IdentityUserId=@ActorUserId AND p.IsActive=1;
                SELECT @ActionTypeId=Id,@RequiresComments=RequiresComments
                FROM dbo.ProcessWorkflowActionTypes
                WHERE Code=@ActionCode AND IsActive=1;
                IF @ActorStaffId IS NULL OR @ActionTypeId IS NULL
                    THROW 51230, 'Invalid workflow actor or action.', 1;
                IF @RequiresComments=1 AND NULLIF(LTRIM(RTRIM(@Comments)),N'') IS NULL
                    THROW 51231, 'Comments are required for this action.', 1;

                BEGIN TRANSACTION;
                SELECT @RequesterStaffId=RequesterStaffId,
                       @CurrentApprover=CurrentApproverStaffId,@CurrentStatusId=StatusId
                FROM dbo.ProcessReports WITH(UPDLOCK,ROWLOCK)
                WHERE Id=@ReportId AND TenantId=@TenantId AND RowVersion=@ExpectedRowVersion;
                IF @CurrentStatusId IS NULL
                    THROW 51232, 'This task changed after it was loaded. Refresh and try again.', 1;
                SELECT @CurrentStatusCode=Code
                FROM dbo.ProcessWorkflowStatuses WHERE Id=@CurrentStatusId;

                IF @ActionCode=N'RESUBMIT'
                BEGIN
                    IF @RequesterStaffId<>@ActorStaffId OR @CurrentStatusCode<>N'RETURNED'
                        THROW 51233, 'Only the requester can resubmit a returned report.', 1;

                    SELECT TOP(1) @CurrentStepId=step.Id,@CurrentStep=step.StepOrder,
                                  @NextApprover=step.ApproverStaffId
                    FROM dbo.ProcessReportActions history
                    JOIN dbo.ProcessWorkflowActionTypes historyType ON historyType.Id=history.ActionTypeId
                    JOIN dbo.ProcessReportRouteSteps step ON step.Id=history.RouteStepId
                    JOIN dbo.StaffVacancy staff ON staff.StaffId=step.ApproverStaffId
                    JOIN dbo.Persons person ON person.PersonId=staff.PersonId
                    WHERE history.ReportId=@ReportId
                      AND historyType.Code IN(N'RETURN_CORRECTION',N'RETURN_INFORMATION')
                      AND person.IsActive=1
                    ORDER BY history.ActionDateUtc DESC,history.Id DESC;
                    IF @CurrentStepId IS NULL
                        THROW 51235, 'The return route is no longer available.', 1;

                    SET @NextStatusCode=N'PENDING';
                    SELECT @NextStatusId=Id FROM dbo.ProcessWorkflowStatuses
                    WHERE Code=@NextStatusCode AND IsActive=1;
                    IF @NextStatusId IS NULL
                        THROW 51237, 'The target workflow status is not configured.', 1;

                    UPDATE dbo.ProcessReportRouteSteps SET IsCurrent=0 WHERE ReportId=@ReportId;
                    UPDATE dbo.ProcessReportRouteSteps
                    SET IsCurrent=1,AssignedDateUtc=SYSUTCDATETIME(),
                        ActedDateUtc=NULL,StatusId=@NextStatusId
                    WHERE Id=@CurrentStepId;
                    UPDATE dbo.ProcessReports
                    SET StatusId=@NextStatusId,CurrentApproverStaffId=@NextApprover,
                        ModifiedDateUtc=SYSUTCDATETIME(),CompletedDateUtc=NULL
                    WHERE Id=@ReportId;
                    INSERT dbo.ProcessReportActions
                        (ReportId,RouteStepId,ActionTypeId,FromStatusId,ToStatusId,
                         ActorStaffId,ActorUserId,Comments)
                    VALUES
                        (@ReportId,@CurrentStepId,@ActionTypeId,@CurrentStatusId,@NextStatusId,
                         @ActorStaffId,@ActorUserId,@Comments);
                END
                ELSE
                BEGIN
                    IF @CurrentApprover<>@ActorStaffId
                        THROW 51233, 'This task is not assigned to you.', 1;
                    IF @RequesterStaffId=@ActorStaffId
                        THROW 51234, 'Self-approval is not allowed.', 1;

                    SELECT @CurrentStepId=Id,@CurrentStep=StepOrder
                    FROM dbo.ProcessReportRouteSteps
                    WHERE ReportId=@ReportId AND IsCurrent=1;
                    IF @CurrentStepId IS NULL
                        THROW 51235, 'The active workflow step is missing.', 1;

                    IF @ActionCode IN(N'APPROVE_FORWARD',N'ESCALATE')
                    BEGIN
                        IF @CurrentStatusCode NOT IN(N'PENDING',N'ESCALATED')
                            THROW 51236, 'Approval is not valid in the current workflow state.', 1;
                        SELECT TOP(1) @NextStepId=Id,@NextApprover=ApproverStaffId
                        FROM dbo.ProcessReportRouteSteps
                        WHERE ReportId=@ReportId AND StepOrder>@CurrentStep
                        ORDER BY StepOrder;

                        IF @NextStepId IS NULL
                        BEGIN
                            IF @ActionCode=N'ESCALATE'
                                THROW 51238, 'No higher active approver exists in the saved route.', 1;
                            SET @NextStepId=@CurrentStepId;
                            SET @NextApprover=@ActorStaffId;
                            SET @NextStatusCode=N'PENDING_RESOLUTION';
                        END
                        ELSE
                            SET @NextStatusCode=CASE
                                WHEN @ActionCode=N'ESCALATE' THEN N'ESCALATED'
                                ELSE N'PENDING' END;
                    END
                    ELSE IF @ActionCode=N'RESOLVE'
                    BEGIN
                        IF @CurrentStatusCode<>N'PENDING_RESOLUTION'
                            THROW 51236, 'The report must receive final approval before it can be resolved.', 1;
                        SET @NextStatusCode=N'RESOLVED';
                    END
                    ELSE IF @ActionCode=N'REJECT'
                        SET @NextStatusCode=N'REJECTED';
                    ELSE IF @ActionCode IN(N'RETURN_CORRECTION',N'RETURN_INFORMATION')
                        SET @NextStatusCode=N'RETURNED';
                    ELSE
                        THROW 51236, 'This action is not valid for an assigned task.', 1;

                    SELECT @NextStatusId=Id FROM dbo.ProcessWorkflowStatuses
                    WHERE Code=@NextStatusCode AND IsActive=1;
                    IF @NextStatusId IS NULL
                        THROW 51237, 'The target workflow status is not configured.', 1;

                    IF @NextStatusCode=N'PENDING_RESOLUTION'
                    BEGIN
                        UPDATE dbo.ProcessReportRouteSteps
                        SET IsCurrent=1,StatusId=@NextStatusId
                        WHERE Id=@CurrentStepId;
                    END
                    ELSE
                    BEGIN
                        UPDATE dbo.ProcessReportRouteSteps
                        SET IsCurrent=0,ActedDateUtc=SYSUTCDATETIME(),StatusId=@NextStatusId
                        WHERE Id=@CurrentStepId;
                        IF @NextStepId IS NOT NULL
                            UPDATE dbo.ProcessReportRouteSteps
                            SET IsCurrent=1,AssignedDateUtc=SYSUTCDATETIME(),
                                ActedDateUtc=NULL,StatusId=@NextStatusId
                            WHERE Id=@NextStepId;
                    END;

                    UPDATE dbo.ProcessReports
                    SET StatusId=@NextStatusId,
                        CurrentApproverStaffId=CASE
                            WHEN @NextStatusCode=N'PENDING_RESOLUTION' THEN @ActorStaffId
                            WHEN @NextStepId IS NOT NULL THEN @NextApprover ELSE NULL END,
                        ModifiedDateUtc=SYSUTCDATETIME(),
                        CompletedDateUtc=CASE
                            WHEN @NextStatusCode IN(N'RESOLVED',N'REJECTED')
                            THEN SYSUTCDATETIME() ELSE NULL END
                    WHERE Id=@ReportId;
                    INSERT dbo.ProcessReportActions
                        (ReportId,RouteStepId,ActionTypeId,FromStatusId,ToStatusId,
                         ActorStaffId,ActorUserId,Comments)
                    VALUES
                        (@ReportId,@CurrentStepId,@ActionTypeId,@CurrentStatusId,@NextStatusId,
                         @ActorStaffId,@ActorUserId,@Comments);
                END;

                COMMIT TRANSACTION;
                SELECT r.Id,r.RequestNumber,status.Code AS StatusCode,status.Name AS StatusName,
                       CONVERT(varchar(16),CONVERT(varbinary(8),r.RowVersion),2) AS RowVersion
                FROM dbo.ProcessReports r
                JOIN dbo.ProcessWorkflowStatuses status ON status.Id=r.StatusId
                WHERE r.Id=@ReportId;
            END
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_ProcessReport_Lookups
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT N'CATEGORY' AS LookupType,Code,Name,
                       CAST(NULL AS nvarchar(20)) AS ColorCode,DisplayOrder
                FROM dbo.ProcessWorkflowCategories WHERE IsActive=1
                UNION ALL
                SELECT N'PRIORITY',Code,Name,ColorCode,DisplayOrder
                FROM dbo.ProcessWorkflowPriorities WHERE IsActive=1
                UNION ALL
                SELECT N'ACTION',Code,Name,ColorCode,DisplayOrder
                FROM dbo.ProcessWorkflowActionTypes WHERE IsActive=1 AND Code<>N'SUBMIT'
                ORDER BY LookupType,DisplayOrder;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Forward-only workflow correction: retaining the corrected procedures is safer than
        // restoring behavior that allowed incomplete finalization and blocked resubmission.
    }
}
