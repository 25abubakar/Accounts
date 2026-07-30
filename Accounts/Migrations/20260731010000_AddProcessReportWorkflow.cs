using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260731010000_AddProcessReportWorkflow")]
public sealed class AddProcessReportWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.ProcessWorkflowCategories', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProcessWorkflowCategories
                (
                    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProcessWorkflowCategories PRIMARY KEY,
                    Code nvarchar(50) NOT NULL,
                    Name nvarchar(100) NOT NULL,
                    IsActive bit NOT NULL CONSTRAINT DF_ProcessWorkflowCategories_IsActive DEFAULT (1),
                    DisplayOrder int NOT NULL CONSTRAINT DF_ProcessWorkflowCategories_DisplayOrder DEFAULT (0),
                    CONSTRAINT UQ_ProcessWorkflowCategories_Code UNIQUE (Code)
                );
            END;

            IF OBJECT_ID(N'dbo.ProcessWorkflowPriorities', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProcessWorkflowPriorities
                (
                    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProcessWorkflowPriorities PRIMARY KEY,
                    Code nvarchar(30) NOT NULL,
                    Name nvarchar(60) NOT NULL,
                    ColorCode nvarchar(20) NOT NULL,
                    IsActive bit NOT NULL CONSTRAINT DF_ProcessWorkflowPriorities_IsActive DEFAULT (1),
                    DisplayOrder int NOT NULL CONSTRAINT DF_ProcessWorkflowPriorities_DisplayOrder DEFAULT (0),
                    CONSTRAINT UQ_ProcessWorkflowPriorities_Code UNIQUE (Code)
                );
            END;

            IF OBJECT_ID(N'dbo.ProcessWorkflowStatuses', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProcessWorkflowStatuses
                (
                    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProcessWorkflowStatuses PRIMARY KEY,
                    Code nvarchar(40) NOT NULL,
                    Name nvarchar(80) NOT NULL,
                    ColorCode nvarchar(20) NOT NULL,
                    IsTerminal bit NOT NULL CONSTRAINT DF_ProcessWorkflowStatuses_IsTerminal DEFAULT (0),
                    IsActive bit NOT NULL CONSTRAINT DF_ProcessWorkflowStatuses_IsActive DEFAULT (1),
                    DisplayOrder int NOT NULL CONSTRAINT DF_ProcessWorkflowStatuses_DisplayOrder DEFAULT (0),
                    CONSTRAINT UQ_ProcessWorkflowStatuses_Code UNIQUE (Code)
                );
            END;

            IF OBJECT_ID(N'dbo.ProcessWorkflowActionTypes', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProcessWorkflowActionTypes
                (
                    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProcessWorkflowActionTypes PRIMARY KEY,
                    Code nvarchar(40) NOT NULL,
                    Name nvarchar(80) NOT NULL,
                    ColorCode nvarchar(20) NOT NULL,
                    RequiresComments bit NOT NULL CONSTRAINT DF_ProcessWorkflowActionTypes_RequiresComments DEFAULT (0),
                    IsActive bit NOT NULL CONSTRAINT DF_ProcessWorkflowActionTypes_IsActive DEFAULT (1),
                    DisplayOrder int NOT NULL CONSTRAINT DF_ProcessWorkflowActionTypes_DisplayOrder DEFAULT (0),
                    CONSTRAINT UQ_ProcessWorkflowActionTypes_Code UNIQUE (Code)
                );
            END;

            MERGE dbo.ProcessWorkflowCategories AS target
            USING (VALUES
                (N'ATTENDANCE', N'Attendance', 10),
                (N'LEAVE', N'Leave', 20),
                (N'PAYROLL', N'Payroll', 30),
                (N'HR', N'Human Resources', 40),
                (N'GENERAL', N'General', 50)
            ) AS source(Code, Name, DisplayOrder)
            ON target.Code=source.Code
            WHEN MATCHED THEN UPDATE SET Name=source.Name, DisplayOrder=source.DisplayOrder, IsActive=1
            WHEN NOT MATCHED THEN INSERT(Code,Name,DisplayOrder) VALUES(source.Code,source.Name,source.DisplayOrder);

            MERGE dbo.ProcessWorkflowPriorities AS target
            USING (VALUES
                (N'LOW', N'Low', N'#64748B', 10),
                (N'NORMAL', N'Normal', N'#2563EB', 20),
                (N'HIGH', N'High', N'#F59E0B', 30),
                (N'CRITICAL', N'Critical', N'#EF4444', 40)
            ) AS source(Code, Name, ColorCode, DisplayOrder)
            ON target.Code=source.Code
            WHEN MATCHED THEN UPDATE SET Name=source.Name,ColorCode=source.ColorCode,DisplayOrder=source.DisplayOrder,IsActive=1
            WHEN NOT MATCHED THEN INSERT(Code,Name,ColorCode,DisplayOrder) VALUES(source.Code,source.Name,source.ColorCode,source.DisplayOrder);

            MERGE dbo.ProcessWorkflowStatuses AS target
            USING (VALUES
                (N'PENDING', N'Pending Approval', N'#F59E0B', 0, 10),
                (N'PENDING_RESOLUTION', N'Pending Resolution', N'#2563EB', 0, 20),
                (N'RETURNED', N'Returned for Correction', N'#F97316', 0, 30),
                (N'ESCALATED', N'Escalated', N'#7C3AED', 0, 40),
                (N'APPROVED', N'Approved', N'#10B981', 1, 50),
                (N'RESOLVED', N'Resolved', N'#059669', 1, 60),
                (N'REJECTED', N'Rejected', N'#EF4444', 1, 70),
                (N'CANCELLED', N'Cancelled', N'#64748B', 1, 80)
            ) AS source(Code, Name, ColorCode, IsTerminal, DisplayOrder)
            ON target.Code=source.Code
            WHEN MATCHED THEN UPDATE SET Name=source.Name,ColorCode=source.ColorCode,IsTerminal=source.IsTerminal,DisplayOrder=source.DisplayOrder,IsActive=1
            WHEN NOT MATCHED THEN INSERT(Code,Name,ColorCode,IsTerminal,DisplayOrder)
                VALUES(source.Code,source.Name,source.ColorCode,source.IsTerminal,source.DisplayOrder);

            MERGE dbo.ProcessWorkflowActionTypes AS target
            USING (VALUES
                (N'SUBMIT', N'Submitted', N'#2563EB', 0, 10),
                (N'APPROVE_FORWARD', N'Approve & Forward', N'#10B981', 0, 20),
                (N'RESOLVE', N'Resolve', N'#059669', 1, 30),
                (N'RETURN_CORRECTION', N'Return for Correction', N'#F97316', 1, 40),
                (N'RETURN_INFORMATION', N'Return for Information', N'#F59E0B', 1, 50),
                (N'ESCALATE', N'Escalate', N'#7C3AED', 1, 60),
                (N'REJECT', N'Reject', N'#EF4444', 1, 70),
                (N'RESUBMIT', N'Resubmitted', N'#2563EB', 1, 80)
            ) AS source(Code, Name, ColorCode, RequiresComments, DisplayOrder)
            ON target.Code=source.Code
            WHEN MATCHED THEN UPDATE SET Name=source.Name,ColorCode=source.ColorCode,
                RequiresComments=source.RequiresComments,DisplayOrder=source.DisplayOrder,IsActive=1
            WHEN NOT MATCHED THEN INSERT(Code,Name,ColorCode,RequiresComments,DisplayOrder)
                VALUES(source.Code,source.Name,source.ColorCode,source.RequiresComments,source.DisplayOrder);

            IF OBJECT_ID(N'dbo.ProcessReports', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProcessReports
                (
                    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProcessReports PRIMARY KEY,
                    TenantId int NOT NULL,
                    RequestNumber nvarchar(40) NULL,
                    RequesterStaffId uniqueidentifier NOT NULL,
                    SubjectStaffId uniqueidentifier NOT NULL,
                    CurrentApproverStaffId uniqueidentifier NULL,
                    CategoryId int NOT NULL,
                    PriorityId int NOT NULL,
                    StatusId int NOT NULL,
                    Title nvarchar(200) NOT NULL,
                    Description nvarchar(max) NOT NULL,
                    SourceModule nvarchar(80) NULL,
                    SourceRecordId nvarchar(100) NULL,
                    CreatedByUserId nvarchar(450) NOT NULL,
                    CreatedDateUtc datetime2 NOT NULL CONSTRAINT DF_ProcessReports_CreatedDateUtc DEFAULT SYSUTCDATETIME(),
                    ModifiedDateUtc datetime2 NULL,
                    CompletedDateUtc datetime2 NULL,
                    RowVersion rowversion NOT NULL,
                    CONSTRAINT FK_ProcessReports_Tenant FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_ProcessReports_Requester FOREIGN KEY(RequesterStaffId) REFERENCES dbo.StaffVacancy(StaffId),
                    CONSTRAINT FK_ProcessReports_Subject FOREIGN KEY(SubjectStaffId) REFERENCES dbo.StaffVacancy(StaffId),
                    CONSTRAINT FK_ProcessReports_CurrentApprover FOREIGN KEY(CurrentApproverStaffId) REFERENCES dbo.StaffVacancy(StaffId),
                    CONSTRAINT FK_ProcessReports_Category FOREIGN KEY(CategoryId) REFERENCES dbo.ProcessWorkflowCategories(Id),
                    CONSTRAINT FK_ProcessReports_Priority FOREIGN KEY(PriorityId) REFERENCES dbo.ProcessWorkflowPriorities(Id),
                    CONSTRAINT FK_ProcessReports_Status FOREIGN KEY(StatusId) REFERENCES dbo.ProcessWorkflowStatuses(Id)
                );
                CREATE UNIQUE INDEX UX_ProcessReports_RequestNumber ON dbo.ProcessReports(TenantId,RequestNumber) WHERE RequestNumber IS NOT NULL;
                CREATE INDEX IX_ProcessReports_Inbox ON dbo.ProcessReports(TenantId,CurrentApproverStaffId,StatusId,CreatedDateUtc DESC);
                CREATE INDEX IX_ProcessReports_Requester ON dbo.ProcessReports(TenantId,RequesterStaffId,CreatedDateUtc DESC);
                CREATE INDEX IX_ProcessReports_Source ON dbo.ProcessReports(TenantId,SourceModule,SourceRecordId);
            END;

            IF OBJECT_ID(N'dbo.ProcessReportRouteSteps', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProcessReportRouteSteps
                (
                    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProcessReportRouteSteps PRIMARY KEY,
                    ReportId bigint NOT NULL,
                    StepOrder int NOT NULL,
                    ApproverStaffId uniqueidentifier NOT NULL,
                    StatusId int NOT NULL,
                    AssignedDateUtc datetime2 NULL,
                    ActedDateUtc datetime2 NULL,
                    IsCurrent bit NOT NULL CONSTRAINT DF_ProcessReportRouteSteps_IsCurrent DEFAULT (0),
                    CONSTRAINT FK_ProcessReportRouteSteps_Report FOREIGN KEY(ReportId) REFERENCES dbo.ProcessReports(Id) ON DELETE CASCADE,
                    CONSTRAINT FK_ProcessReportRouteSteps_Approver FOREIGN KEY(ApproverStaffId) REFERENCES dbo.StaffVacancy(StaffId),
                    CONSTRAINT FK_ProcessReportRouteSteps_Status FOREIGN KEY(StatusId) REFERENCES dbo.ProcessWorkflowStatuses(Id),
                    CONSTRAINT UQ_ProcessReportRouteSteps_ReportStep UNIQUE(ReportId,StepOrder)
                );
                CREATE INDEX IX_ProcessReportRouteSteps_Approver ON dbo.ProcessReportRouteSteps(ApproverStaffId,IsCurrent,ReportId);
            END;

            IF OBJECT_ID(N'dbo.ProcessReportActions', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProcessReportActions
                (
                    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProcessReportActions PRIMARY KEY,
                    ReportId bigint NOT NULL,
                    RouteStepId bigint NULL,
                    ActionTypeId int NOT NULL,
                    FromStatusId int NULL,
                    ToStatusId int NOT NULL,
                    ActorStaffId uniqueidentifier NOT NULL,
                    ActorUserId nvarchar(450) NOT NULL,
                    Comments nvarchar(2000) NULL,
                    ActionDateUtc datetime2 NOT NULL CONSTRAINT DF_ProcessReportActions_ActionDateUtc DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_ProcessReportActions_Report FOREIGN KEY(ReportId) REFERENCES dbo.ProcessReports(Id) ON DELETE CASCADE,
                    CONSTRAINT FK_ProcessReportActions_RouteStep FOREIGN KEY(RouteStepId) REFERENCES dbo.ProcessReportRouteSteps(Id),
                    CONSTRAINT FK_ProcessReportActions_ActionType FOREIGN KEY(ActionTypeId) REFERENCES dbo.ProcessWorkflowActionTypes(Id),
                    CONSTRAINT FK_ProcessReportActions_FromStatus FOREIGN KEY(FromStatusId) REFERENCES dbo.ProcessWorkflowStatuses(Id),
                    CONSTRAINT FK_ProcessReportActions_ToStatus FOREIGN KEY(ToStatusId) REFERENCES dbo.ProcessWorkflowStatuses(Id),
                    CONSTRAINT FK_ProcessReportActions_Actor FOREIGN KEY(ActorStaffId) REFERENCES dbo.StaffVacancy(StaffId)
                );
                CREATE INDEX IX_ProcessReportActions_Report ON dbo.ProcessReportActions(ReportId,ActionDateUtc);
            END;
            """);

        migrationBuilder.Sql(
            """
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

                DECLARE @Route TABLE
                (
                    StepOrder int PRIMARY KEY,
                    PersonId uniqueidentifier,
                    StaffId uniqueidentifier,
                    HasCycle bit NOT NULL
                );
                ;WITH ReportingChain AS
                (
                    SELECT 1 AS StepOrder,m.PersonId,m.ReportsToPersonId,
                           CONVERT(nvarchar(max),N'|'+CONVERT(nvarchar(36),@RequesterPersonId)+N'|'+
                               CONVERT(nvarchar(36),m.PersonId)+N'|') AS RoutePath,
                           CONVERT(bit,CASE WHEN m.PersonId=@RequesterPersonId THEN 1 ELSE 0 END) AS HasCycle
                    FROM dbo.Persons requester
                    JOIN dbo.Persons m ON m.PersonId=requester.ReportsToPersonId
                    WHERE requester.PersonId=@RequesterPersonId AND requester.TenantId=@TenantId AND m.IsActive=1
                    UNION ALL
                    SELECT chain.StepOrder+1,m.PersonId,m.ReportsToPersonId,
                           CONVERT(nvarchar(max),chain.RoutePath+CONVERT(nvarchar(36),m.PersonId)+N'|'),
                           CONVERT(bit,CASE WHEN CHARINDEX(
                               N'|'+CONVERT(nvarchar(36),m.PersonId)+N'|',chain.RoutePath
                           )>0 THEN 1 ELSE 0 END)
                    FROM ReportingChain chain
                    JOIN dbo.Persons m ON m.PersonId=chain.ReportsToPersonId
                    WHERE m.TenantId=@TenantId AND m.IsActive=1
                      AND chain.HasCycle=0 AND chain.StepOrder < 50
                )
                INSERT @Route(StepOrder,PersonId,StaffId,HasCycle)
                SELECT chain.StepOrder,chain.PersonId,staff.StaffId,chain.HasCycle
                FROM ReportingChain chain
                JOIN dbo.StaffVacancy staff ON staff.PersonId=chain.PersonId AND staff.TenantId=@TenantId
                OPTION(MAXRECURSION 50);

                IF NOT EXISTS(SELECT 1 FROM @Route) THROW 51203, 'No active reporting manager is configured for this staff member.', 1;
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
            """);

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
                FROM dbo.Persons p JOIN dbo.StaffVacancy s ON s.PersonId=p.PersonId AND s.TenantId=p.TenantId
                WHERE p.TenantId=@TenantId AND p.IdentityUserId=@ActorUserId AND p.IsActive=1;
                IF @StaffId IS NULL THROW 51210, 'No active staff profile is linked to this account.', 1;

                SELECT r.Id,r.RequestNumber,r.Title,r.Description,r.SourceModule,r.SourceRecordId,
                       category.Code AS CategoryCode,category.Name AS CategoryName,
                       priority.Code AS PriorityCode,priority.Name AS PriorityName,priority.ColorCode AS PriorityColor,
                       status.Code AS StatusCode,status.Name AS StatusName,status.ColorCode AS StatusColor,status.IsTerminal,
                       requester.StaffId AS RequesterStaffId,requesterPerson.FullName AS RequesterName,requester.LoginId AS RequesterNumber,
                       subject.StaffId AS SubjectStaffId,subjectPerson.FullName AS SubjectName,subject.LoginId AS SubjectNumber,
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
                             WHERE step.ReportId=r.Id AND step.ApproverStaffId=@StaffId AND step.ActedDateUtc IS NOT NULL))))
                ORDER BY priority.DisplayOrder DESC,r.CreatedDateUtc DESC;
            END
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_ProcessReport_Timeline
                @TenantId int,
                @ActorUserId nvarchar(450),
                @ReportId bigint
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @StaffId uniqueidentifier;
                SELECT TOP(1) @StaffId=s.StaffId
                FROM dbo.Persons p JOIN dbo.StaffVacancy s ON s.PersonId=p.PersonId AND s.TenantId=p.TenantId
                WHERE p.TenantId=@TenantId AND p.IdentityUserId=@ActorUserId AND p.IsActive=1;
                IF NOT EXISTS
                (
                    SELECT 1 FROM dbo.ProcessReports r
                    WHERE r.Id=@ReportId AND r.TenantId=@TenantId AND
                    (r.RequesterStaffId=@StaffId OR r.SubjectStaffId=@StaffId OR r.CurrentApproverStaffId=@StaffId OR
                     EXISTS(SELECT 1 FROM dbo.ProcessReportRouteSteps s WHERE s.ReportId=r.Id AND s.ApproverStaffId=@StaffId))
                ) THROW 51220, 'You cannot view this workflow.', 1;

                SELECT step.Id,step.StepOrder,person.FullName AS ApproverName,staff.LoginId AS ApproverNumber,
                       status.Code AS StatusCode,status.Name AS StatusName,status.ColorCode AS StatusColor,
                       step.AssignedDateUtc,step.ActedDateUtc,step.IsCurrent
                FROM dbo.ProcessReportRouteSteps step
                JOIN dbo.StaffVacancy staff ON staff.StaffId=step.ApproverStaffId
                JOIN dbo.Persons person ON person.PersonId=staff.PersonId
                JOIN dbo.ProcessWorkflowStatuses status ON status.Id=step.StatusId
                WHERE step.ReportId=@ReportId ORDER BY step.StepOrder;

                SELECT action.Id,actionType.Code AS ActionCode,actionType.Name AS ActionName,
                       actor.FullName AS ActorName,action.Comments,action.ActionDateUtc,
                       status.Code AS ToStatusCode,status.Name AS ToStatusName,status.ColorCode AS ToStatusColor
                FROM dbo.ProcessReportActions action
                JOIN dbo.ProcessWorkflowActionTypes actionType ON actionType.Id=action.ActionTypeId
                JOIN dbo.StaffVacancy staff ON staff.StaffId=action.ActorStaffId
                JOIN dbo.Persons actor ON actor.PersonId=staff.PersonId
                JOIN dbo.ProcessWorkflowStatuses status ON status.Id=action.ToStatusId
                WHERE action.ReportId=@ReportId ORDER BY action.ActionDateUtc,action.Id;
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
                DECLARE @ActorStaffId uniqueidentifier,@RequesterStaffId uniqueidentifier,@CurrentApprover uniqueidentifier,
                        @CurrentStatusId int,@CurrentStatusCode nvarchar(40),@ActionTypeId int,@RequiresComments bit,
                        @CurrentStepId bigint,@CurrentStep int,
                        @NextStepId bigint,@NextApprover uniqueidentifier,@NextStatusCode nvarchar(40),@NextStatusId int,
                        @ExpectedRowVersion binary(8)=CONVERT(binary(8),@ExpectedRowVersionHex,2);

                SELECT TOP(1) @ActorStaffId=s.StaffId
                FROM dbo.Persons p JOIN dbo.StaffVacancy s ON s.PersonId=p.PersonId AND s.TenantId=p.TenantId
                WHERE p.TenantId=@TenantId AND p.IdentityUserId=@ActorUserId AND p.IsActive=1;
                SELECT @ActionTypeId=Id,@RequiresComments=RequiresComments
                FROM dbo.ProcessWorkflowActionTypes WHERE Code=@ActionCode AND IsActive=1;
                IF @ActorStaffId IS NULL OR @ActionTypeId IS NULL THROW 51230, 'Invalid workflow actor or action.', 1;
                IF @RequiresComments=1 AND NULLIF(LTRIM(RTRIM(@Comments)),N'') IS NULL
                    THROW 51231, 'Comments are required for this action.', 1;

                BEGIN TRANSACTION;
                SELECT @RequesterStaffId=RequesterStaffId,@CurrentApprover=CurrentApproverStaffId,@CurrentStatusId=StatusId
                FROM dbo.ProcessReports WITH(UPDLOCK,ROWLOCK)
                WHERE Id=@ReportId AND TenantId=@TenantId AND RowVersion=@ExpectedRowVersion;
                IF @CurrentStatusId IS NULL THROW 51232, 'This task changed after it was loaded. Refresh and try again.', 1;
                SELECT @CurrentStatusCode=Code FROM dbo.ProcessWorkflowStatuses WHERE Id=@CurrentStatusId;

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
                    IF @CurrentStepId IS NULL THROW 51235, 'The return route is no longer available.', 1;

                    SET @NextStatusCode=N'PENDING';
                    SELECT @NextStatusId=Id FROM dbo.ProcessWorkflowStatuses
                    WHERE Code=@NextStatusCode AND IsActive=1;

                    UPDATE dbo.ProcessReportRouteSteps SET IsCurrent=0 WHERE ReportId=@ReportId;
                    UPDATE dbo.ProcessReportRouteSteps
                    SET IsCurrent=1,AssignedDateUtc=SYSUTCDATETIME(),ActedDateUtc=NULL,StatusId=@NextStatusId
                    WHERE Id=@CurrentStepId;
                    UPDATE dbo.ProcessReports
                    SET StatusId=@NextStatusId,CurrentApproverStaffId=@NextApprover,
                        ModifiedDateUtc=SYSUTCDATETIME(),CompletedDateUtc=NULL
                    WHERE Id=@ReportId;
                    INSERT dbo.ProcessReportActions
                        (ReportId,RouteStepId,ActionTypeId,FromStatusId,ToStatusId,ActorStaffId,ActorUserId,Comments)
                    VALUES
                        (@ReportId,@CurrentStepId,@ActionTypeId,@CurrentStatusId,@NextStatusId,
                         @ActorStaffId,@ActorUserId,@Comments);
                END
                ELSE
                BEGIN
                    IF @CurrentApprover<>@ActorStaffId THROW 51233, 'This task is not assigned to you.', 1;
                    IF @RequesterStaffId=@ActorStaffId THROW 51234, 'Self-approval is not allowed.', 1;

                    SELECT @CurrentStepId=Id,@CurrentStep=StepOrder
                    FROM dbo.ProcessReportRouteSteps WHERE ReportId=@ReportId AND IsCurrent=1;
                    IF @CurrentStepId IS NULL THROW 51235, 'The active workflow step is missing.', 1;

                    IF @ActionCode IN(N'APPROVE_FORWARD',N'ESCALATE')
                    BEGIN
                        IF @CurrentStatusCode NOT IN(N'PENDING',N'ESCALATED')
                            THROW 51236, 'Approval is not valid in the current workflow state.', 1;
                        SELECT TOP(1) @NextStepId=Id,@NextApprover=ApproverStaffId
                        FROM dbo.ProcessReportRouteSteps
                        WHERE ReportId=@ReportId AND StepOrder>@CurrentStep ORDER BY StepOrder;

                        IF @NextStepId IS NULL
                        BEGIN
                            IF @ActionCode=N'ESCALATE'
                                THROW 51238, 'No higher active approver exists in the saved route.', 1;
                            SET @NextStepId=@CurrentStepId;
                            SET @NextApprover=@ActorStaffId;
                            SET @NextStatusCode=N'PENDING_RESOLUTION';
                        END
                        ELSE SET @NextStatusCode=CASE
                            WHEN @ActionCode=N'ESCALATE' THEN N'ESCALATED' ELSE N'PENDING' END;
                    END
                    ELSE IF @ActionCode=N'RESOLVE'
                    BEGIN
                        IF @CurrentStatusCode<>N'PENDING_RESOLUTION'
                            THROW 51236, 'The report must receive final approval before it can be resolved.', 1;
                        SET @NextStatusCode=N'RESOLVED';
                    END
                    ELSE IF @ActionCode=N'REJECT' SET @NextStatusCode=N'REJECTED';
                    ELSE IF @ActionCode IN(N'RETURN_CORRECTION',N'RETURN_INFORMATION')
                        SET @NextStatusCode=N'RETURNED';
                    ELSE THROW 51236, 'This action is not valid for an assigned task.', 1;

                    SELECT @NextStatusId=Id FROM dbo.ProcessWorkflowStatuses
                    WHERE Code=@NextStatusCode AND IsActive=1;
                    IF @NextStatusId IS NULL THROW 51237, 'The target workflow status is not configured.', 1;

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
                            SET IsCurrent=1,AssignedDateUtc=SYSUTCDATETIME(),ActedDateUtc=NULL,StatusId=@NextStatusId
                            WHERE Id=@NextStepId;
                    END;

                    UPDATE dbo.ProcessReports
                    SET StatusId=@NextStatusId,
                        CurrentApproverStaffId=CASE
                            WHEN @NextStatusCode=N'PENDING_RESOLUTION' THEN @ActorStaffId
                            WHEN @NextStepId IS NOT NULL THEN @NextApprover ELSE NULL END,
                        ModifiedDateUtc=SYSUTCDATETIME(),
                        CompletedDateUtc=CASE
                            WHEN @NextStatusCode IN(N'RESOLVED',N'REJECTED') THEN SYSUTCDATETIME() ELSE NULL END
                    WHERE Id=@ReportId;
                    INSERT dbo.ProcessReportActions
                        (ReportId,RouteStepId,ActionTypeId,FromStatusId,ToStatusId,ActorStaffId,ActorUserId,Comments)
                    VALUES
                        (@ReportId,@CurrentStepId,@ActionTypeId,@CurrentStatusId,@NextStatusId,
                         @ActorStaffId,@ActorUserId,@Comments);
                END;
                COMMIT TRANSACTION;

                SELECT r.Id,r.RequestNumber,status.Code AS StatusCode,status.Name AS StatusName,
                       CONVERT(varchar(16),CONVERT(varbinary(8),r.RowVersion),2) AS RowVersion
                FROM dbo.ProcessReports r JOIN dbo.ProcessWorkflowStatuses status ON status.Id=r.StatusId
                WHERE r.Id=@ReportId;
            END
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_ProcessReport_Lookups
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT N'CATEGORY' AS LookupType,Code,Name,CAST(NULL AS nvarchar(20)) AS ColorCode,DisplayOrder
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
        migrationBuilder.Sql(
            """
            DROP PROCEDURE IF EXISTS dbo.usp_ProcessReport_Lookups;
            DROP PROCEDURE IF EXISTS dbo.usp_ProcessReport_Action;
            DROP PROCEDURE IF EXISTS dbo.usp_ProcessReport_Timeline;
            DROP PROCEDURE IF EXISTS dbo.usp_ProcessReport_List;
            DROP PROCEDURE IF EXISTS dbo.usp_ProcessReport_Submit;
            DROP TABLE IF EXISTS dbo.ProcessReportActions;
            DROP TABLE IF EXISTS dbo.ProcessReportRouteSteps;
            DROP TABLE IF EXISTS dbo.ProcessReports;
            DROP TABLE IF EXISTS dbo.ProcessWorkflowActionTypes;
            DROP TABLE IF EXISTS dbo.ProcessWorkflowStatuses;
            DROP TABLE IF EXISTS dbo.ProcessWorkflowPriorities;
            DROP TABLE IF EXISTS dbo.ProcessWorkflowCategories;
            """);
    }
}
