using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260731101500_UseAlternativeReporterWhenPrimaryIsOnLeave")]
public sealed class UseAlternativeReporterWhenPrimaryIsOnLeave : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(BuildSubmitProcedure(useAlternativeReporter: true));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(BuildSubmitProcedure(useAlternativeReporter: false));
    }

    internal static string BuildSubmitProcedure(bool useAlternativeReporter)
    {
        var routeSql = useAlternativeReporter
            ? AlternativeReporterRouteSql
            : PrimaryReporterRouteSql;

        return $$"""
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
            """;
    }

    // LEFT JOIN is forbidden inside the recursive member of a SQL Server recursive CTE
    // (error 4011: "Outer join is not allowed in the recursive part of a recursive CTE").
    // Both LEFT JOINs for alternativeManager have been replaced with CROSS APPLY
    // (SELECT TOP(1) ...) which SQL Server permits in recursive members and is
    // semantically identical — the subquery returns NULL columns when no row matches,
    // so the CASE expression falls back to primaryManager exactly as a LEFT JOIN would.
    private const string AlternativeReporterRouteSql = """
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
                               WHEN alternativeManager.PersonId IS NOT NULL
                                AND leaveProfile.PersonId IS NOT NULL
                               THEN alternativeManager.PersonId
                               ELSE primaryManager.PersonId
                           END AS EffectiveManagerId
                    FROM dbo.Persons employee
                    JOIN dbo.Persons primaryManager
                      ON primaryManager.PersonId=employee.ReportsToPersonId
                     AND primaryManager.TenantId=@TenantId
                     AND primaryManager.IsActive=1
                    LEFT JOIN dbo.Persons alternativeManager
                      ON alternativeManager.PersonId=employee.AlternativeReportsToPersonId
                     AND alternativeManager.TenantId=@TenantId
                     AND alternativeManager.IsActive=1
                    LEFT JOIN dbo.PersonHrProfiles leaveProfile
                      ON leaveProfile.PersonId=primaryManager.PersonId
                     AND leaveProfile.TenantId=@TenantId
                     AND leaveProfile.LeaveFrom IS NOT NULL
                     AND CONVERT(date,leaveProfile.LeaveFrom)<=@RouteDate
                     AND CONVERT(date,COALESCE(leaveProfile.LeaveTo,leaveProfile.LeaveFrom))>=@RouteDate
                    WHERE employee.TenantId=@TenantId AND employee.IsActive=1
                ),
                ReportingChain AS
                (
                    -- Anchor member: first manager above the requester.
                    SELECT 1 AS StepOrder,effectiveManager.PersonId,effectiveManager.ReportsToPersonId,
                           CONVERT(nvarchar(max),N'|'+CONVERT(nvarchar(36),@RequesterPersonId)+N'|'+
                               CONVERT(nvarchar(36),effectiveManager.PersonId)+N'|') AS RoutePath,
                           CONVERT(bit,CASE WHEN effectiveManager.PersonId=@RequesterPersonId THEN 1 ELSE 0 END) AS HasCycle
                    FROM EffectiveReportingEdges edge
                    JOIN dbo.Persons effectiveManager
                      ON effectiveManager.PersonId=edge.EffectiveManagerId
                     AND effectiveManager.TenantId=@TenantId
                     AND effectiveManager.IsActive=1
                    WHERE edge.PersonId=@RequesterPersonId

                    UNION ALL

                    -- Recursive member: walk up one level at a time.
                    -- CROSS APPLY replaces LEFT JOIN here because SQL Server forbids
                    -- outer joins in the recursive part of a recursive CTE.
                    SELECT chain.StepOrder+1,effectiveManager.PersonId,effectiveManager.ReportsToPersonId,
                           CONVERT(nvarchar(max),chain.RoutePath+CONVERT(nvarchar(36),effectiveManager.PersonId)+N'|'),
                           CONVERT(bit,CASE WHEN CHARINDEX(
                               N'|'+CONVERT(nvarchar(36),effectiveManager.PersonId)+N'|',chain.RoutePath
                           )>0 THEN 1 ELSE 0 END)
                    FROM ReportingChain chain
                    JOIN EffectiveReportingEdges edge ON edge.PersonId=chain.PersonId
                    JOIN dbo.Persons effectiveManager
                      ON effectiveManager.PersonId=edge.EffectiveManagerId
                     AND effectiveManager.TenantId=@TenantId
                     AND effectiveManager.IsActive=1
                    WHERE chain.HasCycle=0 AND chain.StepOrder<50
                )
                INSERT @Route(StepOrder,PersonId,StaffId,HasCycle)
                SELECT chain.StepOrder,chain.PersonId,staff.StaffId,chain.HasCycle
                FROM ReportingChain chain
                JOIN dbo.StaffVacancy staff ON staff.PersonId=chain.PersonId AND staff.TenantId=@TenantId
                OPTION(MAXRECURSION 50);
        """;

    private const string PrimaryReporterRouteSql = """
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
        """;
}
