using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261030090000_FixProcessWorkflowHierarchyRouting")]
public sealed class FixProcessWorkflowHierarchyRouting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
SET ANSI_NULLS ON;
");
        migrationBuilder.Sql(@"
SET QUOTED_IDENTIFIER ON;
");
        migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.usp_ProcessReport_Submit
    @TenantId int,
    @ActorUserId nvarchar(450),
    @SubjectStaffId uniqueidentifier = NULL,
    @CategoryCode nvarchar(50),
    @PriorityCode nvarchar(30),
    @Title nvarchar(200),
    @Description nvarchar(max),
    @SourceModule nvarchar(80) = NULL,
    @SourceRecordId nvarchar(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @RequesterPersonId uniqueidentifier, @RequesterStaffId uniqueidentifier,
            @CategoryId int, @PriorityId int, @PendingId int, @SubmitActionId int,
            @ReportId bigint, @FirstApprover uniqueidentifier;

    SELECT TOP(1) @RequesterPersonId=p.PersonId,@RequesterStaffId=s.StaffId
    FROM dbo.Persons p
    JOIN dbo.StaffVacancy s ON s.PersonId=p.PersonId AND s.TenantId=p.TenantId
    WHERE p.TenantId=@TenantId AND p.IdentityUserId=@ActorUserId AND p.IsActive=1;
    IF @RequesterStaffId IS NULL THROW 51200, 'No active staff profile is linked to this account.', 1;
    IF @SubjectStaffId IS NULL SET @SubjectStaffId=@RequesterStaffId;
    IF NOT EXISTS(SELECT 1 FROM dbo.StaffVacancy WHERE TenantId=@TenantId AND StaffId=@SubjectStaffId)
        THROW 51201, 'The selected subject is outside your tenant.', 1;

    SELECT @CategoryId=Id FROM dbo.ProcessWorkflowCategories WHERE Code=@CategoryCode AND IsActive=1;
    SELECT @PriorityId=Id FROM dbo.ProcessWorkflowPriorities WHERE Code=@PriorityCode AND IsActive=1;
    SELECT @PendingId=Id FROM dbo.ProcessWorkflowStatuses WHERE Code=N'PENDING' AND IsActive=1;
    SELECT @SubmitActionId=Id FROM dbo.ProcessWorkflowActionTypes WHERE Code=N'SUBMIT' AND IsActive=1;
    IF @CategoryId IS NULL OR @PriorityId IS NULL OR @PendingId IS NULL OR @SubmitActionId IS NULL
        THROW 51202, 'Workflow lookup configuration is incomplete.', 1;

    DECLARE @RouteDate date=CONVERT(date,
        SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'Pakistan Standard Time');
    DECLARE @Route TABLE
    (
        StepOrder int PRIMARY KEY,
        PersonId uniqueidentifier,
        StaffId uniqueidentifier,
        HasCycle bit NOT NULL
    );

    ;WITH EffectiveReportingEdges AS
    (
        SELECT employee.PersonId,
               CASE
                   WHEN overrideManager.PersonId IS NOT NULL THEN overrideManager.PersonId
                   WHEN fallbackManager.PersonId IS NOT NULL THEN fallbackManager.PersonId
                   ELSE NULL
               END AS ManagerPersonId
        FROM dbo.Persons employee
        LEFT JOIN dbo.EmployeeTimingSchedules schedule
          ON schedule.StaffId = (SELECT TOP(1) StaffId FROM dbo.StaffVacancy WHERE PersonId=employee.PersonId AND TenantId=@TenantId)
         AND schedule.ScheduleDate = @RouteDate
         AND schedule.TenantId = @TenantId
        LEFT JOIN dbo.Persons overrideManager
          ON overrideManager.PersonId = schedule.TemporaryReportsToPersonId
         AND overrideManager.TenantId = @TenantId
         AND overrideManager.IsActive = 1
        LEFT JOIN dbo.Persons fallbackManager
          ON fallbackManager.PersonId = employee.ReportsToPersonId
         AND fallbackManager.TenantId = @TenantId
         AND fallbackManager.IsActive = 1
        WHERE employee.TenantId = @TenantId AND employee.IsActive = 1
    ),
    HierarchyPath AS
    (
        SELECT edge.PersonId,
               edge.ManagerPersonId,
               CAST(CONCAT('/', CONVERT(nvarchar(36), edge.PersonId), '/') AS nvarchar(max)) AS VisitedPath,
               1 AS StepOrder,
               CAST(0 AS bit) AS HasCycle
        FROM EffectiveReportingEdges edge
        WHERE edge.PersonId = (SELECT PersonId FROM dbo.StaffVacancy WHERE StaffId=@SubjectStaffId AND TenantId=@TenantId)
        UNION ALL
        SELECT parentEdge.PersonId,
               parentEdge.ManagerPersonId,
               CAST(CONCAT(currentStep.VisitedPath, CONVERT(nvarchar(36), parentEdge.PersonId), '/') AS nvarchar(max)),
               currentStep.StepOrder + 1,
               CAST(CASE WHEN currentStep.VisitedPath LIKE CONCAT('%/', CONVERT(nvarchar(36), parentEdge.PersonId), '/%') THEN 1 ELSE 0 END AS bit)
        FROM EffectiveReportingEdges parentEdge
        JOIN HierarchyPath currentStep ON currentStep.ManagerPersonId = parentEdge.PersonId
        WHERE currentStep.HasCycle = 0
    )
    INSERT INTO @Route(StepOrder, PersonId, StaffId, HasCycle)
    SELECT pathRow.StepOrder - 1,
           pathRow.PersonId,
           staff.StaffId,
           pathRow.HasCycle
    FROM HierarchyPath pathRow
    JOIN dbo.StaffVacancy staff ON staff.PersonId = pathRow.PersonId AND staff.TenantId = @TenantId
    WHERE pathRow.StepOrder > 1;

    DECLARE @RoutingMode nvarchar(40) = N'REPORTING_HIERARCHY';
    IF EXISTS(SELECT 1 FROM dbo.ProcessCategoryApprovers WHERE TenantId=@TenantId AND CategoryId=@CategoryId AND IsActive=1)
    BEGIN
        DECLARE @FirstCategoryApproverStep int = NULL;
        SELECT TOP(1) @FirstCategoryApproverStep = r.StepOrder
        FROM @Route r
        JOIN dbo.ProcessCategoryApprovers pca ON pca.StaffId = r.StaffId
        WHERE pca.TenantId = @TenantId AND pca.CategoryId = @CategoryId AND pca.IsActive = 1
        ORDER BY r.StepOrder;

        IF @FirstCategoryApproverStep IS NOT NULL
        BEGIN
            DELETE FROM @Route WHERE StepOrder > @FirstCategoryApproverStep;
        END
        ELSE
        BEGIN
            DECLARE @MaxStep int = ISNULL((SELECT MAX(StepOrder) FROM @Route), 0);
            INSERT INTO @Route(StepOrder, PersonId, StaffId, HasCycle)
            SELECT @MaxStep + ROW_NUMBER() OVER(ORDER BY pca.LevelOrder, pca.Id),
                   staff.PersonId,
                   pca.StaffId,
                   0
            FROM dbo.ProcessCategoryApprovers pca
            JOIN dbo.StaffVacancy staff ON staff.StaffId = pca.StaffId AND staff.TenantId = @TenantId
            JOIN dbo.Persons person ON person.PersonId = staff.PersonId AND person.IsActive = 1
            WHERE pca.TenantId = @TenantId AND pca.CategoryId = @CategoryId AND pca.IsActive = 1
              AND NOT EXISTS(SELECT 1 FROM @Route r WHERE r.StaffId = pca.StaffId);
        END;
    END;

    DELETE FROM @Route WHERE StaffId = @RequesterStaffId;

    IF NOT EXISTS(SELECT 1 FROM @Route)
    BEGIN
        INSERT INTO @Route(StepOrder, PersonId, StaffId, HasCycle)
        SELECT TOP(1) 1, staff.PersonId, staff.StaffId, 0
        FROM dbo.ProcessCategoryApprovers pca
        JOIN dbo.StaffVacancy staff ON staff.StaffId = pca.StaffId AND staff.TenantId = @TenantId
        JOIN dbo.Persons person ON person.PersonId = staff.PersonId AND person.IsActive = 1
        WHERE pca.TenantId = @TenantId AND pca.CategoryId = @CategoryId AND pca.IsActive = 1
          AND pca.StaffId <> @RequesterStaffId
        ORDER BY pca.LevelOrder, pca.Id;
    END;

    IF NOT EXISTS(SELECT 1 FROM @Route)
    BEGIN
        INSERT INTO @Route(StepOrder, PersonId, StaffId, HasCycle)
        SELECT TOP(1) 1, staff.PersonId, staff.StaffId, 0
        FROM dbo.StaffVacancy staff
        JOIN dbo.Persons person ON person.PersonId = staff.PersonId AND person.TenantId = @TenantId AND person.IsActive = 1
        LEFT JOIN dbo.JobTitles jt ON jt.Id = staff.VacancyId
        WHERE staff.TenantId = @TenantId AND staff.StaffId <> @RequesterStaffId
        ORDER BY CASE WHEN jt.TitleName LIKE N'%CEO%' OR jt.TitleName LIKE N'%Director%' THEN 0 ELSE 1 END,
                 person.FullName;
    END;

    IF NOT EXISTS(SELECT 1 FROM @Route) THROW 51203, 'No valid approver route could be resolved for this report.', 1;

    SELECT TOP(1) @FirstApprover = StaffId FROM @Route ORDER BY StepOrder;

    BEGIN TRANSACTION;
    INSERT dbo.ProcessReports
        (TenantId,RequestNumber,CategoryId,PriorityId,StatusId,
         RequesterStaffId,SubjectStaffId,CurrentApproverStaffId,
         Title,Description,SourceModule,SourceRecordId)
    VALUES
        (@TenantId,N'PENDING',@CategoryId,@PriorityId,@PendingId,
         @RequesterStaffId,@SubjectStaffId,@FirstApprover,
         @Title,@Description,@SourceModule,@SourceRecordId);
    SET @ReportId = SCOPE_IDENTITY();

    UPDATE dbo.ProcessReports
    SET RequestNumber=CONCAT(N'PR-',CONVERT(char(8),SYSUTCDATETIME(),112),N'-',RIGHT(CONCAT(N'000000',@ReportId),6))
    WHERE Id=@ReportId;

    INSERT dbo.ProcessReportRouteSteps(ReportId,StepOrder,ApproverStaffId,StatusId,AssignedDateUtc,IsCurrent)
    SELECT @ReportId,StepOrder,StaffId,@PendingId,
           CASE WHEN StepOrder=1 THEN SYSUTCDATETIME() END,
           CASE WHEN StepOrder=1 THEN 1 ELSE 0 END
    FROM @Route;

    INSERT dbo.ProcessReportActions
        (ReportId,ActionTypeId,ToStatusId,ActorStaffId,ActorUserId,Comments)
    VALUES(@ReportId,@SubmitActionId,@PendingId,@RequesterStaffId,@ActorUserId,N'Report submitted');
    COMMIT TRANSACTION;

    SELECT @ReportId AS Id,RequestNumber,
           CONVERT(varchar(16),CONVERT(varbinary(8),RowVersion),2) AS RowVersion
    FROM dbo.ProcessReports WHERE Id=@ReportId;
END;
");

        migrationBuilder.Sql(@"
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
        IF EXISTS(SELECT 1 FROM dbo.ProcessCategoryApprovers WHERE TenantId=@TenantId AND CategoryId=@CategoryId AND StaffId=@ActorStaffId AND IsActive=1)
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

                IF @NextStepId IS NULL
                BEGIN
                    DECLARE @NextManagerStaffId uniqueidentifier;
                    SELECT TOP(1) @NextManagerStaffId=mgrStaff.StaffId
                    FROM dbo.StaffVacancy actorStaff
                    JOIN dbo.Persons actorPerson ON actorPerson.PersonId=actorStaff.PersonId AND actorPerson.TenantId=actorStaff.TenantId
                    JOIN dbo.Persons mgrPerson ON mgrPerson.PersonId=actorPerson.ReportsToPersonId AND mgrPerson.TenantId=actorPerson.TenantId AND mgrPerson.IsActive=1
                    JOIN dbo.StaffVacancy mgrStaff ON mgrStaff.PersonId=mgrPerson.PersonId AND mgrStaff.TenantId=mgrPerson.TenantId
                    WHERE actorStaff.StaffId=@ActorStaffId AND actorStaff.TenantId=@TenantId;

                    IF @NextManagerStaffId IS NULL OR @NextManagerStaffId=@ActorStaffId OR @NextManagerStaffId=@RequesterStaffId
                    BEGIN
                        SELECT TOP(1) @NextManagerStaffId=pca.StaffId
                        FROM dbo.ProcessCategoryApprovers pca
                        JOIN dbo.StaffVacancy staff ON staff.StaffId=pca.StaffId AND staff.TenantId=@TenantId
                        JOIN dbo.Persons person ON person.PersonId=staff.PersonId AND person.IsActive=1
                        WHERE pca.TenantId=@TenantId AND pca.CategoryId=@CategoryId AND pca.IsActive=1
                          AND pca.StaffId<>@ActorStaffId AND pca.StaffId<>@RequesterStaffId
                        ORDER BY pca.LevelOrder, pca.Id;
                    END;

                    IF @NextManagerStaffId IS NOT NULL
                    BEGIN
                        INSERT dbo.ProcessReportRouteSteps(ReportId,StepOrder,ApproverStaffId,StatusId,AssignedDateUtc,IsCurrent)
                        VALUES(@ReportId, @CurrentStep + 1, @NextManagerStaffId, @CurrentStatusId, SYSUTCDATETIME(), 0);
                        SET @NextStepId = SCOPE_IDENTITY();
                        SET @NextApprover = @NextManagerStaffId;
                    END;
                END;

                IF @NextStepId IS NULL THROW 51239, 'A configured category approver must give the final approval.',1;
                SET @NextStatusCode=N'FORWARDED';
            END
        END
        ELSE IF @ActionCode=N'ESCALATE'
        BEGIN
            IF @IsCategoryApprover=1 THROW 51236, 'A category approver must approve, reject, or return this request.',1;
            SELECT TOP(1) @NextStepId=Id,@NextApprover=ApproverStaffId
            FROM dbo.ProcessReportRouteSteps WHERE ReportId=@ReportId AND StepOrder>@CurrentStep ORDER BY StepOrder;

            IF @NextStepId IS NULL
            BEGIN
                DECLARE @EscalateManagerStaffId uniqueidentifier;
                SELECT TOP(1) @EscalateManagerStaffId=mgrStaff.StaffId
                FROM dbo.StaffVacancy actorStaff
                JOIN dbo.Persons actorPerson ON actorPerson.PersonId=actorStaff.PersonId AND actorPerson.TenantId=actorStaff.TenantId
                JOIN dbo.Persons mgrPerson ON mgrPerson.PersonId=actorPerson.ReportsToPersonId AND mgrPerson.TenantId=actorPerson.TenantId AND mgrPerson.IsActive=1
                JOIN dbo.StaffVacancy mgrStaff ON mgrStaff.PersonId=mgrPerson.PersonId AND mgrStaff.TenantId=mgrPerson.TenantId
                WHERE actorStaff.StaffId=@ActorStaffId AND actorStaff.TenantId=@TenantId;

                IF @EscalateManagerStaffId IS NULL OR @EscalateManagerStaffId=@ActorStaffId OR @EscalateManagerStaffId=@RequesterStaffId
                BEGIN
                    SELECT TOP(1) @EscalateManagerStaffId=pca.StaffId
                    FROM dbo.ProcessCategoryApprovers pca
                    JOIN dbo.StaffVacancy staff ON staff.StaffId=pca.StaffId AND staff.TenantId=@TenantId
                    JOIN dbo.Persons person ON person.PersonId=staff.PersonId AND person.IsActive=1
                    WHERE pca.TenantId=@TenantId AND pca.CategoryId=@CategoryId AND pca.IsActive=1
                      AND pca.StaffId<>@ActorStaffId AND pca.StaffId<>@RequesterStaffId
                    ORDER BY pca.LevelOrder, pca.Id;
                END;

                IF @EscalateManagerStaffId IS NOT NULL
                BEGIN
                    INSERT dbo.ProcessReportRouteSteps(ReportId,StepOrder,ApproverStaffId,StatusId,AssignedDateUtc,IsCurrent)
                    VALUES(@ReportId, @CurrentStep + 1, @EscalateManagerStaffId, @CurrentStatusId, SYSUTCDATETIME(), 0);
                    SET @NextStepId = SCOPE_IDENTITY();
                    SET @NextApprover = @EscalateManagerStaffId;
                END;
            END;

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
END;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}

