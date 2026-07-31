using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260731019000_AddProcessReportSubmissionCapability")]
public sealed class AddProcessReportSubmissionCapability : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_ProcessReport_SubmissionCapability
                @TenantId int,
                @ActorUserId nvarchar(450)
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE @RequesterPersonId uniqueidentifier;
                DECLARE @ReportingManagerPersonId uniqueidentifier;
                DECLARE @ReportingManagerStaffId uniqueidentifier;
                DECLARE @ReportingManagerName nvarchar(300);

                SELECT TOP (1)
                    @RequesterPersonId = person.PersonId,
                    @ReportingManagerPersonId = person.ReportsToPersonId
                FROM dbo.Persons person
                JOIN dbo.StaffVacancy staff
                  ON staff.PersonId = person.PersonId
                 AND staff.TenantId = person.TenantId
                WHERE person.TenantId = @TenantId
                  AND person.IdentityUserId = @ActorUserId
                  AND person.IsActive = 1;

                IF @RequesterPersonId IS NULL
                BEGIN
                    SELECT
                        CAST(0 AS bit) AS CanSubmit,
                        CAST(N'No active staff profile is linked to this account.' AS nvarchar(300)) AS Reason,
                        CAST(NULL AS uniqueidentifier) AS ReportingManagerStaffId,
                        CAST(NULL AS nvarchar(300)) AS ReportingManagerName;
                    RETURN;
                END;

                SELECT TOP (1)
                    @ReportingManagerStaffId = managerStaff.StaffId,
                    @ReportingManagerName = manager.FullName
                FROM dbo.Persons manager
                JOIN dbo.StaffVacancy managerStaff
                  ON managerStaff.PersonId = manager.PersonId
                 AND managerStaff.TenantId = manager.TenantId
                WHERE manager.PersonId = @ReportingManagerPersonId
                  AND manager.TenantId = @TenantId
                  AND manager.IsActive = 1;

                SELECT
                    CAST(CASE WHEN @ReportingManagerStaffId IS NULL THEN 0 ELSE 1 END AS bit) AS CanSubmit,
                    CAST(
                        CASE WHEN @ReportingManagerStaffId IS NULL
                            THEN N'No active reporting manager is configured for this staff member.'
                            ELSE NULL
                        END
                        AS nvarchar(300)
                    ) AS Reason,
                    @ReportingManagerStaffId AS ReportingManagerStaffId,
                    @ReportingManagerName AS ReportingManagerName;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_ProcessReport_SubmissionCapability;");
    }
}
