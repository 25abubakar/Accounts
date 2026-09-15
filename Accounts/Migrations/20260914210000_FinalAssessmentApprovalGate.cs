using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260914210000_FinalAssessmentApprovalGate")]
public sealed class FinalAssessmentApprovalGate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH(N'dbo.StaffAssessments', N'IsFinalApproved') IS NULL
                ALTER TABLE dbo.StaffAssessments ADD IsFinalApproved bit NOT NULL CONSTRAINT DF_StaffAssessments_FinalApproved DEFAULT(0);
            IF COL_LENGTH(N'dbo.StaffAssessments', N'FinalApprovedByUserId') IS NULL
                ALTER TABLE dbo.StaffAssessments ADD FinalApprovedByUserId nvarchar(450) NULL;
            IF COL_LENGTH(N'dbo.StaffAssessments', N'FinalApprovedByName') IS NULL
                ALTER TABLE dbo.StaffAssessments ADD FinalApprovedByName nvarchar(200) NULL;
            IF COL_LENGTH(N'dbo.StaffAssessments', N'FinalApprovedDateUtc') IS NULL
                ALTER TABLE dbo.StaffAssessments ADD FinalApprovedDateUtc datetime2 NULL;
            IF COL_LENGTH(N'dbo.StaffAssessments', N'IsPostedToPayroll') IS NULL
                ALTER TABLE dbo.StaffAssessments ADD IsPostedToPayroll bit NOT NULL CONSTRAINT DF_StaffAssessments_Posted DEFAULT(0);
            IF COL_LENGTH(N'dbo.StaffAssessments', N'PostedPayrollRunId') IS NULL
                ALTER TABLE dbo.StaffAssessments ADD PostedPayrollRunId bigint NULL;
            IF COL_LENGTH(N'dbo.StaffAssessments', N'PostedToPayrollDateUtc') IS NULL
                ALTER TABLE dbo.StaffAssessments ADD PostedToPayrollDateUtc datetime2 NULL;
            """);

        migrationBuilder.Sql(
            """
            DECLARE @FinalId int = (SELECT TOP (1) Id FROM Menus WHERE Route = N'/assessment/final' ORDER BY Id);
            IF @FinalId IS NOT NULL
            BEGIN
                DECLARE @ApproveKey nvarchar(50) = CONCAT(N'MENU_', @FinalId, N'_APPROVE');
                IF NOT EXISTS (SELECT 1 FROM Features WHERE FeatureKey = @ApproveKey)
                    INSERT INTO Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
                    VALUES (@ApproveKey, N'Final Assessment - Approve & Pay', N'Menu',
                            N'Finally approve assessment amounts with a security code and post them to Draft payroll.', SYSUTCDATETIME());

                INSERT INTO MenuPermissions (MenuId, PermissionId)
                SELECT @FinalId, f.PermissionId
                FROM Features f
                WHERE f.FeatureKey = @ApproveKey
                  AND NOT EXISTS (SELECT 1 FROM MenuPermissions mp WHERE mp.MenuId = @FinalId AND mp.PermissionId = f.PermissionId);
            END

            -- Existing tenants can initially use the same higher-authority PIN
            -- already configured for deduction approvals. It is copied, not hard-coded.
            INSERT INTO ProcessApprovalCodes (TenantId, ProcessName, PinCode)
            SELECT source.TenantId, N'FinalAssessment', source.PinCode
            FROM (
                SELECT TenantId, MAX(PinCode) AS PinCode
                FROM ProcessApprovalCodes
                WHERE ProcessName = N'DeductionAdjustment' AND PinCode > 0
                GROUP BY TenantId
            ) source
            WHERE NOT EXISTS (
                  SELECT 1 FROM ProcessApprovalCodes target
                  WHERE target.TenantId = source.TenantId AND target.ProcessName = N'FinalAssessment');
            """);

        // SQL Server requires CREATE/ALTER PROCEDURE to be the first statement in
        // its batch. Execute the definition dynamically so MigrateAsync can apply
        // this safely inside the migration transaction.
        migrationBuilder.Sql("EXEC(N'" + FinalListProcedure.Replace("'", "''") + "')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH(N'dbo.StaffAssessments', N'PostedToPayrollDateUtc') IS NOT NULL ALTER TABLE dbo.StaffAssessments DROP COLUMN PostedToPayrollDateUtc;
            IF COL_LENGTH(N'dbo.StaffAssessments', N'PostedPayrollRunId') IS NOT NULL ALTER TABLE dbo.StaffAssessments DROP COLUMN PostedPayrollRunId;
            IF COL_LENGTH(N'dbo.StaffAssessments', N'IsPostedToPayroll') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.StaffAssessments DROP CONSTRAINT IF EXISTS DF_StaffAssessments_Posted;
                ALTER TABLE dbo.StaffAssessments DROP COLUMN IsPostedToPayroll;
            END
            IF COL_LENGTH(N'dbo.StaffAssessments', N'FinalApprovedDateUtc') IS NOT NULL ALTER TABLE dbo.StaffAssessments DROP COLUMN FinalApprovedDateUtc;
            IF COL_LENGTH(N'dbo.StaffAssessments', N'FinalApprovedByName') IS NOT NULL ALTER TABLE dbo.StaffAssessments DROP COLUMN FinalApprovedByName;
            IF COL_LENGTH(N'dbo.StaffAssessments', N'FinalApprovedByUserId') IS NOT NULL ALTER TABLE dbo.StaffAssessments DROP COLUMN FinalApprovedByUserId;
            IF COL_LENGTH(N'dbo.StaffAssessments', N'IsFinalApproved') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.StaffAssessments DROP CONSTRAINT IF EXISTS DF_StaffAssessments_FinalApproved;
                ALTER TABLE dbo.StaffAssessments DROP COLUMN IsFinalApproved;
            END
            """);
    }

    private const string FinalListProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_Assessment_FinalList
            @TenantId INT,
            @Year INT,
            @Month INT
        AS
        BEGIN
            SET NOCOUNT ON;
            SELECT
                CAST(ROW_NUMBER() OVER (ORDER BY COALESCE(vac.Department, org.Name, N''), p.FullName) AS INT) AS Id,
                sa.Id AS AssessmentId,
                p.PersonId,
                sv.StaffId AS StaffGuid,
                COALESCE(sv.LoginId, CONVERT(NVARCHAR(50), sv.StaffId)) AS StaffId,
                p.FullName,
                COALESCE(CASE WHEN org.Label = N'Department' THEN org.Name END, vac.Department, org.Name, N'-') AS Department,
                COALESCE(des.TitleName, vac.JobTitle, N'-') AS JobTitle,
                @Year AS AssessmentYear,
                @Month AS AssessmentMonth,
                sa.Rating,
                sa.Amount,
                sa.Remarks,
                CAST(CASE WHEN sa.IsLocked = 1 OR sa.Rating IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS IsLocked,
                sa.SubmittedDateUtc,
                sa.AssessorPersonId,
                COALESCE(ap.FullName, N'-') AS AssessorName,
                COALESCE(sa.IsFinalApproved, CAST(0 AS BIT)) AS IsFinalApproved,
                sa.FinalApprovedByName,
                sa.FinalApprovedDateUtc,
                COALESCE(sa.IsPostedToPayroll, CAST(0 AS BIT)) AS IsPostedToPayroll,
                sa.PostedPayrollRunId,
                sa.PostedToPayrollDateUtc,
                CASE
                    WHEN sa.Id IS NULL THEN N'Pending'
                    WHEN sa.IsFinalApproved = 1 AND sa.IsPostedToPayroll = 1 THEN N'Approved & Posted'
                    WHEN sa.IsLocked = 1 OR sa.Rating IS NOT NULL THEN N'Submitted'
                    ELSE N'Open'
                END AS Status
            FROM dbo.Persons p
            INNER JOIN dbo.StaffVacancy sv ON sv.PersonId = p.PersonId AND sv.TenantId = p.TenantId
            LEFT JOIN dbo.Vacancies vac ON vac.VacancyId = sv.VacancyId
            LEFT JOIN dbo.OrganizationTree org ON org.Id = vac.OrganizationId
            LEFT JOIN dbo.JobTitles des ON des.Id = vac.JobTitleId
            OUTER APPLY
            (
                SELECT TOP (1)
                    a.Id, a.Rating, a.Amount, a.Remarks, a.IsLocked, a.SubmittedDateUtc, a.AssessorPersonId,
                    a.IsFinalApproved, a.FinalApprovedByName, a.FinalApprovedDateUtc,
                    a.IsPostedToPayroll, a.PostedPayrollRunId, a.PostedToPayrollDateUtc
                FROM dbo.StaffAssessments a
                WHERE a.TenantId = @TenantId AND a.SubjectPersonId = p.PersonId
                  AND a.AssessmentYear = @Year AND a.AssessmentMonth = @Month
                ORDER BY CASE WHEN a.IsLocked = 1 OR a.Rating IS NOT NULL THEN 0 ELSE 1 END,
                         a.SubmittedDateUtc DESC, a.Id DESC
            ) sa
            LEFT JOIN dbo.Persons ap ON ap.PersonId = sa.AssessorPersonId
            WHERE p.TenantId = @TenantId AND p.IsActive = 1
            ORDER BY Department, p.FullName;
        END
        """;
}
