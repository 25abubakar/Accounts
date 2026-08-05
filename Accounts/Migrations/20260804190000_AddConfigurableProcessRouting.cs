using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260804190000_AddConfigurableProcessRouting")]
public sealed class AddConfigurableProcessRouting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.ProcessCategoryRoutingConfigurations',N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProcessCategoryRoutingConfigurations
                (
                    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProcessCategoryRoutingConfigurations PRIMARY KEY,
                    TenantId int NOT NULL,
                    CategoryId int NOT NULL,
                    RoutingMode nvarchar(30) NOT NULL CONSTRAINT DF_ProcessRouting_Mode DEFAULT N'REPORTING_HIERARCHY',
                    SlaHours int NOT NULL CONSTRAINT DF_ProcessRouting_Sla DEFAULT 24,
                    AutoEscalate bit NOT NULL CONSTRAINT DF_ProcessRouting_AutoEscalate DEFAULT 1,
                    AllowReturn bit NOT NULL CONSTRAINT DF_ProcessRouting_AllowReturn DEFAULT 1,
                    AllowHold bit NOT NULL CONSTRAINT DF_ProcessRouting_AllowHold DEFAULT 1,
                    RequiresAttachment bit NOT NULL CONSTRAINT DF_ProcessRouting_Attachment DEFAULT 0,
                    IsActive bit NOT NULL CONSTRAINT DF_ProcessRouting_Active DEFAULT 1,
                    ModifiedDateUtc datetime2 NOT NULL CONSTRAINT DF_ProcessRouting_Modified DEFAULT SYSUTCDATETIME(),
                    ModifiedByUserId nvarchar(450) NOT NULL,
                    CONSTRAINT UQ_ProcessCategoryRouting_TenantCategory UNIQUE(TenantId,CategoryId),
                    CONSTRAINT CK_ProcessCategoryRouting_Mode CHECK(RoutingMode IN(N'REPORTING_HIERARCHY',N'DIRECT_FUNCTIONAL',N'BRANCH_FIRST')),
                    CONSTRAINT CK_ProcessCategoryRouting_Sla CHECK(SlaHours BETWEEN 1 AND 8760),
                    CONSTRAINT FK_ProcessCategoryRouting_Tenant FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_ProcessCategoryRouting_Category FOREIGN KEY(CategoryId) REFERENCES dbo.ProcessWorkflowCategories(Id)
                );
            END;

            IF COL_LENGTH(N'dbo.ProcessCategoryApprovers',N'LevelOrder') IS NULL
                ALTER TABLE dbo.ProcessCategoryApprovers ADD LevelOrder int NOT NULL CONSTRAINT DF_ProcessCategoryApprovers_Level DEFAULT 1;
            IF COL_LENGTH(N'dbo.ProcessCategoryApprovers',N'CanFinalApprove') IS NULL
                ALTER TABLE dbo.ProcessCategoryApprovers ADD CanFinalApprove bit NOT NULL CONSTRAINT DF_ProcessCategoryApprovers_Final DEFAULT 1;
            IF COL_LENGTH(N'dbo.ProcessCategoryApprovers',N'IsActive') IS NULL
                ALTER TABLE dbo.ProcessCategoryApprovers ADD IsActive bit NOT NULL CONSTRAINT DF_ProcessCategoryApprovers_Active DEFAULT 1;
            IF COL_LENGTH(N'dbo.ProcessReports',N'RoutingModeSnapshot') IS NULL
                ALTER TABLE dbo.ProcessReports ADD RoutingModeSnapshot nvarchar(30) NOT NULL CONSTRAINT DF_ProcessReports_RoutingMode DEFAULT N'REPORTING_HIERARCHY';
            IF COL_LENGTH(N'dbo.ProcessReports',N'SlaHoursSnapshot') IS NULL
                ALTER TABLE dbo.ProcessReports ADD SlaHoursSnapshot int NOT NULL CONSTRAINT DF_ProcessReports_SlaHours DEFAULT 24;
            IF COL_LENGTH(N'dbo.ProcessReports',N'ReviewDateUtc') IS NULL
                ALTER TABLE dbo.ProcessReports ADD ReviewDateUtc datetime2 NULL;

            IF OBJECT_ID(N'dbo.ProcessReportAttachments',N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProcessReportAttachments
                (
                    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProcessReportAttachments PRIMARY KEY,
                    ReportId bigint NOT NULL,
                    FileName nvarchar(260) NOT NULL,
                    StorageKey nvarchar(500) NOT NULL,
                    ContentType nvarchar(120) NULL,
                    FileSize bigint NOT NULL,
                    UploadedByUserId nvarchar(450) NOT NULL,
                    CreatedDateUtc datetime2 NOT NULL CONSTRAINT DF_ProcessReportAttachments_Created DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_ProcessReportAttachments_Report FOREIGN KEY(ReportId) REFERENCES dbo.ProcessReports(Id) ON DELETE CASCADE
                );
            END;

            INSERT dbo.ProcessCategoryRoutingConfigurations(TenantId,CategoryId,RoutingMode,SlaHours,ModifiedByUserId)
            SELECT tenant.Id,category.Id,N'REPORTING_HIERARCHY',24,N'migration'
            FROM dbo.Tenants tenant CROSS JOIN dbo.ProcessWorkflowCategories category
            WHERE category.IsActive=1 AND NOT EXISTS
              (SELECT 1 FROM dbo.ProcessCategoryRoutingConfigurations c WHERE c.TenantId=tenant.Id AND c.CategoryId=category.Id);

            IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.ProcessCategoryApprovers') AND name=N'IX_ProcessCategoryApprovers_Route')
                CREATE INDEX IX_ProcessCategoryApprovers_Route ON dbo.ProcessCategoryApprovers(TenantId,CategoryId,LevelOrder,IsActive);
            """);

        var submit = UseAlternativeReporterWhenPrimaryIsOnLeave.BuildSubmitProcedure(true)
            .Replace(
                "IF NOT EXISTS(SELECT 1 FROM @Route) THROW 51203, 'No active reporting manager is configured for this staff member.', 1;",
                """
                DECLARE @RoutingMode nvarchar(30)=N'REPORTING_HIERARCHY';
                SELECT @RoutingMode=RoutingMode FROM dbo.ProcessCategoryRoutingConfigurations
                WHERE TenantId=@TenantId AND CategoryId=@CategoryId AND IsActive=1;

                IF @RoutingMode IN(N'DIRECT_FUNCTIONAL',N'BRANCH_FIRST')
                BEGIN
                    DECLARE @BranchApprover uniqueidentifier=NULL,@BranchApproverPerson uniqueidentifier=NULL;
                    IF @RoutingMode=N'BRANCH_FIRST'
                        SELECT TOP(1) @BranchApprover=StaffId,@BranchApproverPerson=PersonId FROM @Route ORDER BY StepOrder;
                    DELETE FROM @Route;
                    IF @BranchApprover IS NOT NULL AND @BranchApprover<>@RequesterStaffId
                        INSERT @Route(StepOrder,PersonId,StaffId,HasCycle) VALUES(1,@BranchApproverPerson,@BranchApprover,0);
                    INSERT @Route(StepOrder,PersonId,StaffId,HasCycle)
                    SELECT ROW_NUMBER() OVER(ORDER BY pca.LevelOrder,pca.Id)+CASE WHEN @BranchApprover IS NULL THEN 0 ELSE 1 END,
                           staff.PersonId,pca.StaffId,0
                    FROM dbo.ProcessCategoryApprovers pca
                    JOIN dbo.StaffVacancy staff ON staff.StaffId=pca.StaffId AND staff.TenantId=@TenantId
                    JOIN dbo.Persons person ON person.PersonId=staff.PersonId AND person.IsActive=1
                    WHERE pca.TenantId=@TenantId AND pca.CategoryId=@CategoryId AND pca.IsActive=1
                      AND pca.StaffId<>@RequesterStaffId AND (pca.StaffId<>@BranchApprover OR @BranchApprover IS NULL);
                END;

                IF NOT EXISTS(SELECT 1 FROM @Route)
                    THROW 51203, 'No active approval route is configured for this category.', 1;

                IF @RoutingMode=N'REPORTING_HIERARCHY'
                BEGIN
                    IF NOT EXISTS(SELECT 1 FROM dbo.ProcessCategoryApprovers WHERE TenantId=@TenantId AND CategoryId=@CategoryId AND IsActive=1)
                        THROW 51205, 'No final approver is configured for the selected category.',1;
                    DECLARE @FinalStep int;
                    SELECT @FinalStep=MIN(route.StepOrder) FROM @Route route
                    JOIN dbo.ProcessCategoryApprovers pca ON pca.TenantId=@TenantId AND pca.CategoryId=@CategoryId
                         AND pca.StaffId=route.StaffId AND pca.IsActive=1;
                    IF @FinalStep IS NULL THROW 51206, 'The configured final approver is not present in this reporting route.',1;
                    DELETE FROM @Route WHERE StepOrder>@FinalStep;
                END;
                """);
        submit = submit
            .Replace("DECLARE @RoutingMode nvarchar(30)=N'REPORTING_HIERARCHY';", "DECLARE @RoutingMode nvarchar(30)=N'REPORTING_HIERARCHY',@SlaHours int=24;")
            .Replace("SELECT @RoutingMode=RoutingMode FROM dbo.ProcessCategoryRoutingConfigurations", "SELECT @RoutingMode=RoutingMode,@SlaHours=SlaHours FROM dbo.ProcessCategoryRoutingConfigurations")
            .Replace("StatusId,Title,Description,SourceModule,SourceRecordId,CreatedByUserId)", "StatusId,Title,Description,SourceModule,SourceRecordId,CreatedByUserId,RoutingModeSnapshot,SlaHoursSnapshot)")
            .Replace("@PendingId,@Title,@Description,@SourceModule,@SourceRecordId,@ActorUserId);", "@PendingId,@Title,@Description,@SourceModule,@SourceRecordId,@ActorUserId,@RoutingMode,@SlaHours);");
        migrationBuilder.Sql(submit);

        var action = AddPrivateWorkflowHoldAndAutoTransferAction();
        migrationBuilder.Sql(action);
        var list = FinalizeReportsAtCategoryApprover.ListProcedureSql
            .Replace("approverPerson.FullName AS CurrentApproverName,",
                "CASE WHEN report.RequesterStaffId=@StaffId THEN NULL ELSE approverPerson.FullName END AS CurrentApproverName,\n                   CONVERT(bit,CASE WHEN report.RequesterStaffId=@StaffId THEN 1 ELSE 0 END) AS IsRequester,")
            .Replace("finalApprover.StaffId=@StaffId)",
                "finalApprover.StaffId=@StaffId AND finalApprover.IsActive=1 AND finalApprover.CanFinalApprove=1 AND NOT EXISTS (SELECT 1 FROM dbo.ProcessReportRouteSteps currentRoute JOIN dbo.ProcessReportRouteSteps laterRoute ON laterRoute.ReportId=currentRoute.ReportId AND laterRoute.StepOrder>currentRoute.StepOrder WHERE currentRoute.ReportId=report.Id AND currentRoute.IsCurrent=1))");
        migrationBuilder.Sql(list);

        var timeline = FinalizeReportsAtCategoryApprover.TimelineProcedureSql
            .Replace("DECLARE @StaffId uniqueidentifier;", "DECLARE @StaffId uniqueidentifier,@IsRequester bit=0;")
            .Replace("IF NOT EXISTS\n            (", "SELECT @IsRequester=CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.ProcessReports r WHERE r.Id=@ReportId AND r.TenantId=@TenantId AND r.RequesterStaffId=@StaffId) THEN 1 ELSE 0 END);\n            IF NOT EXISTS\n            (")
            .Replace("WHERE step.ReportId=@ReportId ORDER BY step.StepOrder;", "WHERE step.ReportId=@ReportId AND @IsRequester=0 ORDER BY step.StepOrder;")
            .Replace("WHERE action.ReportId=@ReportId ORDER BY action.ActionDateUtc,action.Id;", "WHERE action.ReportId=@ReportId AND @IsRequester=0 ORDER BY action.ActionDateUtc,action.Id;");
        migrationBuilder.Sql(timeline);

        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE dbo.usp_ProcessReport_AutoTransferOverdue
            AS
            BEGIN
                SET NOCOUNT ON; SET XACT_ABORT ON;
                ;WITH Due AS
                (
                    SELECT step.Id StepId,alternativeStaff.StaffId AlternativeStaffId
                    FROM dbo.ProcessReports report WITH(UPDLOCK,READPAST,ROWLOCK)
                    JOIN dbo.ProcessWorkflowStatuses status ON status.Id=report.StatusId
                    JOIN dbo.ProcessReportRouteSteps step ON step.ReportId=report.Id AND step.IsCurrent=1
                    JOIN dbo.ProcessCategoryRoutingConfigurations config ON config.TenantId=report.TenantId AND config.CategoryId=report.CategoryId AND config.AutoEscalate=1
                    JOIN dbo.StaffVacancy currentStaff ON currentStaff.StaffId=step.ApproverStaffId
                    JOIN dbo.Persons currentPerson ON currentPerson.PersonId=currentStaff.PersonId
                    JOIN dbo.Persons alternativePerson ON alternativePerson.PersonId=currentPerson.AlternativeReportsToPersonId AND alternativePerson.IsActive=1
                    JOIN dbo.StaffVacancy alternativeStaff ON alternativeStaff.PersonId=alternativePerson.PersonId AND alternativeStaff.TenantId=report.TenantId
                    WHERE status.Code IN(N'PENDING',N'FORWARDED',N'ESCALATED')
                      AND step.AssignedDateUtc<=DATEADD(hour,-report.SlaHoursSnapshot,SYSUTCDATETIME())
                      AND step.AutoTransferredDateUtc IS NULL AND alternativeStaff.StaffId<>step.ApproverStaffId
                )
                UPDATE step SET OriginalApproverStaffId=step.ApproverStaffId,ApproverStaffId=due.AlternativeStaffId,
                    AssignedDateUtc=SYSUTCDATETIME(),AutoTransferredDateUtc=SYSUTCDATETIME()
                FROM dbo.ProcessReportRouteSteps step JOIN Due due ON due.StepId=step.Id;
                UPDATE report SET CurrentApproverStaffId=step.ApproverStaffId,ModifiedDateUtc=SYSUTCDATETIME()
                FROM dbo.ProcessReports report JOIN dbo.ProcessReportRouteSteps step ON step.ReportId=report.Id AND step.IsCurrent=1
                WHERE step.AutoTransferredDateUtc IS NOT NULL AND report.CurrentApproverStaffId<>step.ApproverStaffId;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS dbo.ProcessCategoryRoutingConfigurations;");
    }

    private static string AddPrivateWorkflowHoldAndAutoTransferAction()
    {
        var sql = FinalizeReportsAtCategoryApprover.ActionProcedureSql.Replace(
            "IF EXISTS(SELECT 1 FROM dbo.ProcessCategoryApprovers WHERE TenantId=@TenantId AND CategoryId=@CategoryId AND StaffId=@ActorStaffId)",
            "IF EXISTS(SELECT 1 FROM dbo.ProcessCategoryApprovers WHERE TenantId=@TenantId AND CategoryId=@CategoryId AND StaffId=@ActorStaffId AND IsActive=1 AND CanFinalApprove=1) AND NOT EXISTS(SELECT 1 FROM dbo.ProcessReportRouteSteps WHERE ReportId=@ReportId AND StepOrder>@CurrentStep)");
        return sql.Replace("                IF @ActionCode=N'APPROVE_FORWARD'", """
                IF @ActionCode=N'HOLD'
                BEGIN
                    IF NOT EXISTS(SELECT 1 FROM dbo.ProcessCategoryRoutingConfigurations WHERE TenantId=@TenantId AND CategoryId=@CategoryId AND AllowHold=1)
                        THROW 51236, 'Holding is disabled for this category.',1;
                    SET @NextStatusCode=N'HELD';
                    SELECT @NextStatusId=Id FROM dbo.ProcessWorkflowStatuses WHERE Code=@NextStatusCode AND IsActive=1;
                    UPDATE dbo.ProcessReportRouteSteps SET StatusId=@NextStatusId WHERE Id=@CurrentStepId;
                    UPDATE dbo.ProcessReports SET StatusId=@NextStatusId,ModifiedDateUtc=SYSUTCDATETIME() WHERE Id=@ReportId;
                    INSERT dbo.ProcessReportActions(ReportId,RouteStepId,ActionTypeId,FromStatusId,ToStatusId,ActorStaffId,ActorUserId,Comments)
                    VALUES(@ReportId,@CurrentStepId,@ActionTypeId,@CurrentStatusId,@NextStatusId,@ActorStaffId,@ActorUserId,@Comments);
                    COMMIT TRANSACTION;
                    SELECT report.Id,report.RequestNumber,status.Code StatusCode,status.Name StatusName,CONVERT(varchar(16),CONVERT(varbinary(8),report.RowVersion),2) RowVersion
                    FROM dbo.ProcessReports report JOIN dbo.ProcessWorkflowStatuses status ON status.Id=report.StatusId WHERE report.Id=@ReportId;
                    RETURN;
                END
                IF @ActionCode IN(N'RETURN_CORRECTION',N'RETURN_INFORMATION') AND NOT EXISTS
                   (SELECT 1 FROM dbo.ProcessCategoryRoutingConfigurations WHERE TenantId=@TenantId AND CategoryId=@CategoryId AND AllowReturn=1)
                    THROW 51236, 'Returning is disabled for this category.',1;

                IF @ActionCode=N'APPROVE_FORWARD'
                """);
    }
}
