using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Restores hierarchy-based approval:
/// 1. Reports travel the full Report-To chain (no truncation at a category approver).
/// 2. Submit is not blocked when a category approver is missing from the chain.
/// 3. People on the route can Recommend &amp; Forward / return / escalate / reject.
/// 4. Only ProcessCategoryApprovers for that category can Approve (complete the case).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260807170000_RestoreHierarchyApprovalWorkflow")]
public sealed class RestoreHierarchyApprovalWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var submit = UseAlternativeReporterWhenPrimaryIsOnLeave.BuildSubmitProcedure(true)
            .Replace(
                "IF NOT EXISTS(SELECT 1 FROM @Route) THROW 51203, 'No active reporting manager is configured for this staff member.', 1;",
                """
                DECLARE @RoutingMode nvarchar(30)=N'REPORTING_HIERARCHY',@SlaHours int=24;
                SELECT @RoutingMode=RoutingMode,@SlaHours=SlaHours
                FROM dbo.ProcessCategoryRoutingConfigurations
                WHERE TenantId=@TenantId AND CategoryId=@CategoryId AND IsActive=1;

                -- Optional functional routes still use configured approvers as the path.
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

                -- REPORTING_HIERARCHY keeps the full Report-To route. Do not truncate at a category approver.
                IF NOT EXISTS(SELECT 1 FROM @Route)
                    THROW 51203, 'No active reporting manager is configured for this staff member.',1;
                """);
        submit = submit
            .Replace("StatusId,Title,Description,SourceModule,SourceRecordId,CreatedByUserId)",
                "StatusId,Title,Description,SourceModule,SourceRecordId,CreatedByUserId,RoutingModeSnapshot,SlaHoursSnapshot)")
            .Replace("@PendingId,@Title,@Description,@SourceModule,@SourceRecordId,@ActorUserId);",
                "@PendingId,@Title,@Description,@SourceModule,@SourceRecordId,@ActorUserId,@RoutingMode,@SlaHours);");
        migrationBuilder.Sql(submit);

        var action = FinalizeReportsAtCategoryApprover.ActionProcedureSql.Replace(
            "IF EXISTS(SELECT 1 FROM dbo.ProcessCategoryApprovers WHERE TenantId=@TenantId AND CategoryId=@CategoryId AND StaffId=@ActorStaffId)",
            "IF EXISTS(SELECT 1 FROM dbo.ProcessCategoryApprovers WHERE TenantId=@TenantId AND CategoryId=@CategoryId AND StaffId=@ActorStaffId AND IsActive=1 AND CanFinalApprove=1)");
        action = action.Replace(
            "IF @NextStepId IS NULL THROW 51239, 'A configured category approver must give the final approval.',1;",
            "IF @NextStepId IS NULL THROW 51239, 'No upper post remains. A category approver must Approve to complete this case.',1;");
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
            .Replace(
                "finalApprover.StaffId=@StaffId)",
                "finalApprover.StaffId=@StaffId AND finalApprover.IsActive=1 AND finalApprover.CanFinalApprove=1)");
        migrationBuilder.Sql(list);

        var timeline = FinalizeReportsAtCategoryApprover.TimelineProcedureSql
            .Replace("DECLARE @StaffId uniqueidentifier;", "DECLARE @StaffId uniqueidentifier,@IsRequester bit=0;")
            .Replace("IF NOT EXISTS\n            (", "SELECT @IsRequester=CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.ProcessReports r WHERE r.Id=@ReportId AND r.TenantId=@TenantId AND r.RequesterStaffId=@StaffId) THEN 1 ELSE 0 END);\n            IF NOT EXISTS\n            (")
            .Replace("WHERE step.ReportId=@ReportId ORDER BY step.StepOrder;", "WHERE step.ReportId=@ReportId AND @IsRequester=0 ORDER BY step.StepOrder;")
            .Replace("WHERE action.ReportId=@ReportId ORDER BY action.ActionDateUtc,action.Id;", "WHERE action.ReportId=@ReportId AND @IsRequester=0 ORDER BY action.ActionDateUtc,action.Id;")
            .Replace(
                "AND categoryApprover.StaffId=action.ActorStaffId",
                "AND categoryApprover.StaffId=action.ActorStaffId AND categoryApprover.IsActive=1 AND categoryApprover.CanFinalApprove=1");
        migrationBuilder.Sql(timeline);

        migrationBuilder.Sql("""
            UPDATE dbo.ProcessWorkflowActionTypes
            SET Name=N'Recommend & Forward',IsActive=1
            WHERE Code=N'APPROVE_FORWARD';

            UPDATE dbo.ProcessWorkflowActionTypes
            SET IsActive=0
            WHERE Code=N'FINAL_APPROVE';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep hierarchy behavior; use RevertToCategoryApproverWorkflow if truncation is required again.
    }
}
