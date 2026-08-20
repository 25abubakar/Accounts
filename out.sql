Text
----
CREATE   PROCEDURE dbo.usp_ProcessReport_Action

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

        IF EXISTS(SELECT 1 FROM dbo.ProcessCategoryApprovers WHERE TenantId=@TenantId AND CategoryId=@CategoryId AND StaffId=@ActorStaffId AND IsActive=1 AND CanFinalApprove=1)

            SET @IsCategoryApprover=1;



        IF @ActionCode=N'FINAL_APPROVE'

BEGIN

    IF @CurrentStatusCode NOT IN(N'PENDING',N'FORWARDED',N'ESCALATED')

        THROW 51236, 'Approval is not valid in the current workflow state.',1;

    IF @IsCategoryApprover=0

        THROW 51236, 'You are not configured as a final approval authority for this category.',1;

    SET @NextStepId=NULL; SET @NextApprover=NULL; SET @NextStatusCode=N'RESOLVED';

END

ELSE IF @ActionCode=N'APPROVE_FORWARD'

BEGIN

    IF @CurrentStatusCode NOT IN(N'PENDING',N'FORWARDED',N'ESCALATED')

        THROW 51236, 'Recommendation is not valid in the current workflow state.',1;



    DECLARE @ActorPersonId uniqueidentifier,@PrimaryReporterPersonId uniqueidentifier,

            @AlternativeReporterPersonId uniqueidentifier,@NextReporterPersonId uniqueidentifier,

            @RouteDate date=CONVERT(date,SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'Pakistan Standard Time'),

            @RoutingModeSnapshot nvarchar(30);

    SELECT @RoutingModeSnapshot=RoutingModeSnapshot FROM dbo.ProcessReports WHERE Id=@ReportId;



    -- Configured non-hierarchy modes keep using their next saved step.

    SELECT TOP(1) @NextStepId=Id,@NextApprover=ApproverStaffId

    FROM dbo.ProcessReportRouteSteps

    WHERE ReportId=@ReportId AND StepOrder>@CurrentStep

    ORDER BY StepOrder;



    IF @RoutingModeSnapshot=N'REPORTING_HIERARCHY'

    BEGIN

        SET @NextStepId=NULL; SET @NextApprover=NULL;

        SELECT @ActorPersonId=person.PersonId,

               @PrimaryReporterPersonId=person.ReportsToPersonId,

               @AlternativeReporterPersonId=person.AlternativeReportsToPersonId

        FROM dbo.StaffVacancy staff

        JOIN dbo.Persons person ON person.PersonId=staff.PersonId

        WHERE staff.StaffId=@ActorStaffId AND staff.TenantId=@TenantId

          AND staff.IsEmploymentActive=1 AND person.IsActive=1;



        IF EXISTS(SELECT 1 FROM dbo.Persons person

                  JOIN dbo.StaffVacancy staff ON staff.PersonId=person.PersonId

                  WHERE person.PersonId=@PrimaryReporterPersonId AND person.TenantId=@TenantId

                    AND person.IsActive=1 AND staff.IsEmploymentActive=1)

            SET @NextReporterPersonId=@PrimaryReporterPersonId;



        IF EXISTS(SELECT 1 FROM dbo.Persons person

                  JOIN dbo.StaffVacancy staff ON staff.PersonId=person.PersonId

                  WHERE person.PersonId=@AlternativeReporterPersonId AND person.TenantId=@TenantId

                    AND person.IsActive=1 AND staff.IsEmploymentActive=1)

           AND (@NextReporterPersonId IS NULL OR EXISTS

               (SELECT 1 FROM dbo.PersonHrProfiles profile

                WHERE profile.PersonId=@PrimaryReporterPersonId AND profile.TenantId=@TenantId

                  AND profile.LeaveFrom IS NOT NULL

                  AND CONVERT(date,profile.LeaveFrom)<=@RouteDate

                  AND CONVERT(date,COALESCE(profile.LeaveTo,profile.LeaveFrom))>=@RouteDate))

            SET @NextReporterPersonId=@AlternativeReporterPersonId;



        SELECT TOP(1) @NextApprover=staff.StaffId

        FROM dbo.StaffVacancy staff

        JOIN dbo.Persons person ON person.PersonId=staff.PersonId

        WHERE person.PersonId=@NextReporterPersonId AND person.TenantId=@TenantId

          AND person.IsActive=1 AND staff.IsEmploymentActive=1;



        IF @NextApprover=@ActorStaffId OR EXISTS

           (SELECT 1 FROM dbo.ProcessReportRouteSteps prior

            WHERE prior.ReportId=@ReportId AND prior.ApproverStaffId=@NextApprover)

            THROW 51204, 'The reporting hierarchy contains a circular or repeated reporter.',1;



        IF @NextApprover IS NOT NULL

        BEGIN

            INSERT dbo.ProcessReportRouteSteps

                (ReportId,StepOrder,ApproverStaffId,StatusId,AssignedDateUtc,IsCurrent)

            VALUES(@ReportId,@CurrentStep+1,@NextApprover,@CurrentStatusId,NULL,0);

            SET @NextStepId=SCOPE_IDENTITY();

        END;

    END;



    -- The reporting chain has ended. Hand the case to the first active,

    -- eligible authority configured for its category. This remains a

    -- normal auditable route step; it does not silently complete the case.

    IF @NextStepId IS NULL

    BEGIN

        SET @NextApprover=NULL;

        SELECT TOP(1) @NextApprover=authority.StaffId

        FROM dbo.ProcessCategoryApprovers authority

        JOIN dbo.StaffVacancy staff

          ON staff.StaffId=authority.StaffId AND staff.TenantId=authority.TenantId

        JOIN dbo.Persons person ON person.PersonId=staff.PersonId

        WHERE authority.TenantId=@TenantId

          AND authority.CategoryId=@CategoryId

          AND authority.IsActive=1

          AND authority.CanFinalApprove=1

          AND staff.IsEmploymentActive=1

          AND person.IsActive=1

          AND authority.StaffId<>@ActorStaffId

          AND NOT EXISTS

              (SELECT 1 FROM dbo.ProcessReportRouteSteps prior

               WHERE prior.ReportId=@ReportId AND prior.ApproverStaffId=authority.StaffId)

        ORDER BY authority.LevelOrder,authority.Id;



        IF @NextApprover IS NOT NULL

        BEGIN

            INSERT dbo.ProcessReportRouteSteps

                (ReportId,StepOrder,ApproverStaffId,StatusId,AssignedDateUtc,IsCurrent)

            VALUES(@ReportId,@CurrentStep+1,@NextApprover,@CurrentStatusId,NULL,0);

            SET @NextStepId=SCOPE_IDENTITY();

        END;

    END;



    IF @NextStepId IS NULL

    BEGIN

        IF EXISTS

           (SELECT 1 FROM dbo.ProcessCategoryApprovers authority

            WHERE authority.TenantId=@TenantId AND authority.CategoryId=@CategoryId

              AND authority.StaffId=@ActorStaffId AND authority.IsActive=1

              AND authority.CanFinalApprove=1)

            THROW 51239, 'You are the final approval authority. Select Approve to complete this case.',1;



        THROW 51240, 'No active approval authority is configured for this category. Contact the tenant administrator.',1;

    END;



    SET @NextStatusCode=N'FORWARDED';

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

