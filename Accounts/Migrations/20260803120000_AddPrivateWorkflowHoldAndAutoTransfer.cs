using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260803120000_AddPrivateWorkflowHoldAndAutoTransfer")]
public sealed class AddPrivateWorkflowHoldAndAutoTransfer : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF NOT EXISTS (SELECT 1 FROM dbo.ProcessWorkflowStatuses WHERE Code=N'HELD')
                INSERT dbo.ProcessWorkflowStatuses(Code,Name,ColorCode,IsTerminal,IsActive,DisplayOrder)
                VALUES(N'HELD',N'On Hold',N'#64748B',0,1,45);
            IF NOT EXISTS (SELECT 1 FROM dbo.ProcessWorkflowActionTypes WHERE Code=N'HOLD')
                INSERT dbo.ProcessWorkflowActionTypes(Code,Name,ColorCode,RequiresComments,IsActive,DisplayOrder)
                VALUES(N'HOLD',N'Hold Request',N'#64748B',1,1,65);

            IF COL_LENGTH(N'dbo.ProcessReportRouteSteps',N'OriginalApproverStaffId') IS NULL
                ALTER TABLE dbo.ProcessReportRouteSteps ADD OriginalApproverStaffId uniqueidentifier NULL;
            IF COL_LENGTH(N'dbo.ProcessReportRouteSteps',N'AutoTransferredDateUtc') IS NULL
                ALTER TABLE dbo.ProcessReportRouteSteps ADD AutoTransferredDateUtc datetime2 NULL;
            """);

        var actionSql = FinalizeReportsAtCategoryApprover.ActionProcedureSql.Replace(
            "                IF @ActionCode=N'APPROVE_FORWARD'",
            """
                            IF @ActionCode=N'HOLD'
                            BEGIN
                                IF @CurrentStatusCode NOT IN(N'PENDING',N'FORWARDED',N'ESCALATED')
                                    THROW 51236, 'Only an active pending request can be placed on hold.',1;
                                SET @NextStatusCode=N'HELD';
                                SELECT @NextStatusId=Id FROM dbo.ProcessWorkflowStatuses WHERE Code=@NextStatusCode AND IsActive=1;
                                UPDATE dbo.ProcessReportRouteSteps SET StatusId=@NextStatusId WHERE Id=@CurrentStepId;
                                UPDATE dbo.ProcessReports SET StatusId=@NextStatusId,ModifiedDateUtc=SYSUTCDATETIME() WHERE Id=@ReportId;
                                INSERT dbo.ProcessReportActions(ReportId,RouteStepId,ActionTypeId,FromStatusId,ToStatusId,ActorStaffId,ActorUserId,Comments)
                                VALUES(@ReportId,@CurrentStepId,@ActionTypeId,@CurrentStatusId,@NextStatusId,@ActorStaffId,@ActorUserId,@Comments);
                                COMMIT TRANSACTION;
                                SELECT report.Id,report.RequestNumber,status.Code AS StatusCode,status.Name AS StatusName,
                                       CONVERT(varchar(16),CONVERT(varbinary(8),report.RowVersion),2) AS RowVersion
                                FROM dbo.ProcessReports report JOIN dbo.ProcessWorkflowStatuses status ON status.Id=report.StatusId
                                WHERE report.Id=@ReportId;
                                RETURN;
                            END

                            IF @ActionCode=N'APPROVE_FORWARD'
            """);
        migrationBuilder.Sql(actionSql);

        var listSql = FinalizeReportsAtCategoryApprover.ListProcedureSql
            .Replace("approverPerson.FullName AS CurrentApproverName,",
                "CASE WHEN report.RequesterStaffId=@StaffId THEN NULL ELSE approverPerson.FullName END AS CurrentApproverName,\n                   CONVERT(bit,CASE WHEN report.RequesterStaffId=@StaffId THEN 1 ELSE 0 END) AS IsRequester,");
        migrationBuilder.Sql(listSql);

        var timelineSql = FinalizeReportsAtCategoryApprover.TimelineProcedureSql
            .Replace("DECLARE @StaffId uniqueidentifier;", "DECLARE @StaffId uniqueidentifier,@IsRequester bit=0;")
            .Replace("IF NOT EXISTS\n            (", "SELECT @IsRequester=CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.ProcessReports r WHERE r.Id=@ReportId AND r.TenantId=@TenantId AND r.RequesterStaffId=@StaffId) THEN 1 ELSE 0 END);\n            IF NOT EXISTS\n            (")
            .Replace("WHERE step.ReportId=@ReportId ORDER BY step.StepOrder;", "WHERE step.ReportId=@ReportId AND @IsRequester=0 ORDER BY step.StepOrder;")
            .Replace("WHERE action.ReportId=@ReportId ORDER BY action.ActionDateUtc,action.Id;", "WHERE action.ReportId=@ReportId AND @IsRequester=0 ORDER BY action.ActionDateUtc,action.Id;");
        migrationBuilder.Sql(timelineSql);

        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE dbo.usp_ProcessReport_AutoTransferOverdue
                @TimeoutHours int=24
            AS
            BEGIN
                SET NOCOUNT ON; SET XACT_ABORT ON;
                IF @TimeoutHours<1 SET @TimeoutHours=24;

                ;WITH Due AS
                (
                    SELECT step.Id AS StepId,report.Id AS ReportId,report.TenantId,
                           step.ApproverStaffId,alternativeStaff.StaffId AS AlternativeStaffId
                    FROM dbo.ProcessReports report WITH(UPDLOCK,READPAST,ROWLOCK)
                    JOIN dbo.ProcessWorkflowStatuses status ON status.Id=report.StatusId
                    JOIN dbo.ProcessReportRouteSteps step ON step.ReportId=report.Id AND step.IsCurrent=1
                    JOIN dbo.StaffVacancy currentStaff ON currentStaff.StaffId=step.ApproverStaffId AND currentStaff.TenantId=report.TenantId
                    JOIN dbo.Persons currentPerson ON currentPerson.PersonId=currentStaff.PersonId AND currentPerson.TenantId=report.TenantId
                    JOIN dbo.Persons alternativePerson ON alternativePerson.PersonId=currentPerson.AlternativeReportsToPersonId
                        AND alternativePerson.TenantId=report.TenantId AND alternativePerson.IsActive=1
                    JOIN dbo.StaffVacancy alternativeStaff ON alternativeStaff.PersonId=alternativePerson.PersonId
                        AND alternativeStaff.TenantId=report.TenantId
                    WHERE status.Code IN(N'PENDING',N'FORWARDED',N'ESCALATED')
                      AND step.AssignedDateUtc<=DATEADD(hour,-@TimeoutHours,SYSUTCDATETIME())
                      AND step.AutoTransferredDateUtc IS NULL
                      AND alternativeStaff.StaffId<>step.ApproverStaffId
                )
                UPDATE step SET OriginalApproverStaffId=due.ApproverStaffId,
                                ApproverStaffId=due.AlternativeStaffId,
                                AssignedDateUtc=SYSUTCDATETIME(),AutoTransferredDateUtc=SYSUTCDATETIME()
                FROM dbo.ProcessReportRouteSteps step JOIN Due due ON due.StepId=step.Id;

                UPDATE report SET CurrentApproverStaffId=step.ApproverStaffId,ModifiedDateUtc=SYSUTCDATETIME()
                FROM dbo.ProcessReports report
                JOIN dbo.ProcessReportRouteSteps step ON step.ReportId=report.Id AND step.IsCurrent=1
                WHERE step.AutoTransferredDateUtc IS NOT NULL
                  AND report.CurrentApproverStaffId<>step.ApproverStaffId;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_ProcessReport_AutoTransferOverdue;");
    }
}
