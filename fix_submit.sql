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

                {{routeSql}}

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
                IF EXISTS(SELECT 1 FROM @Route WHERE HasCycle=1 OR StaffId=@RequesterStaffId)
                    THROW 51204, 'The reporting hierarchy contains a circular or self-approval route.', 1;
                SELECT TOP(1) @FirstApprover=StaffId FROM @Route ORDER BY StepOrder;

                BEGIN TRANSACTION;
                INSERT dbo.ProcessReports
                    (TenantId,RequesterStaffId,SubjectStaffId,CurrentApproverStaffId,CategoryId,PriorityId,
                     StatusId,Title,Description,SourceModule,SourceRecordId,CreatedByUserId)
                VALUES
                    (@TenantId,@RequesterStaffId,@SubjectStaffId,@FirstApprover,@CategoryId,@PriorityId,
                     @PendingId,@Title,@Description,@SourceModule,@SourceRecordId,@ActorUserId);
                SET @ReportId=SCOPE_IDENTITY();
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
            END
            
GO
