using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Reverts person/post approval-authority experiments back to the committed
/// category-approver workflow from AddConfigurableProcessRouting.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260807160000_RevertToCategoryApproverWorkflow")]
public sealed class RevertToCategoryApproverWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE dbo.ProcessWorkflowActionTypes
            SET IsActive=0
            WHERE Code IN(N'FINAL_APPROVE',N'AUTO_TRANSFER_ALTERNATIVE',N'AUTO_FORWARD');

            UPDATE dbo.ProcessWorkflowActionTypes
            SET Name=N'Recommend & Forward',IsActive=1
            WHERE Code=N'APPROVE_FORWARD';

            -- Restore final-approve flags that authority migrations cleared.
            UPDATE dbo.ProcessCategoryApprovers SET CanFinalApprove=1,IsActive=1 WHERE IsActive=1 OR CanFinalApprove=0;

            IF OBJECT_ID(N'dbo.fn_ProcessStaffHasFinalApprovalAuthority',N'FN') IS NOT NULL
                DROP FUNCTION dbo.fn_ProcessStaffHasFinalApprovalAuthority;

            IF OBJECT_ID(N'dbo.ProcessCategoryApprovalAuthorities',N'U') IS NOT NULL
                DROP TABLE dbo.ProcessCategoryApprovalAuthorities;
            """);

        // Rebuild submit / action / list / timeline / auto-transfer from the last committed design.
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

        var action = FinalizeReportsAtCategoryApprover.ActionProcedureSql.Replace(
            "IF EXISTS(SELECT 1 FROM dbo.ProcessCategoryApprovers WHERE TenantId=@TenantId AND CategoryId=@CategoryId AND StaffId=@ActorStaffId)",
            "IF EXISTS(SELECT 1 FROM dbo.ProcessCategoryApprovers WHERE TenantId=@TenantId AND CategoryId=@CategoryId AND StaffId=@ActorStaffId AND IsActive=1 AND CanFinalApprove=1) AND NOT EXISTS(SELECT 1 FROM dbo.ProcessReportRouteSteps WHERE ReportId=@ReportId AND StepOrder>@CurrentStep)");
        action = action.Replace("                IF @ActionCode=N'APPROVE_FORWARD'", """
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

        // Clear experimental migration history so future databases do not expect those files.
        migrationBuilder.Sql("""
            DELETE FROM dbo.__EFMigrationsHistory
            WHERE MigrationId IN
            (
                N'20260731140000_CleanReportToWorkflowLogic',
                N'20260807133000_AddPostBasedProcessApprovalAuthority',
                N'20260807143000_EnhancePersonApprovalAndEscalation',
                N'20260807153000_HierarchyTravelWithOptionalApproval'
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally empty — this migration permanently retires the experimental authority model.
    }
}
