using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Final Assessment: Remarks column, amount override for boss, Pay→Draft payroll.
/// Keep SP in sync with Accounts/Sql/StoredProcedures/usp_Assessment_FinalList.sql.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260914190000_FinalAssessmentRemarksAndPay")]
public sealed class FinalAssessmentRemarksAndPay : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET QUOTED_IDENTIFIER ON;

            IF OBJECT_ID(N'dbo.StaffAssessments', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.StaffAssessments', N'Remarks') IS NULL
                ALTER TABLE dbo.StaffAssessments ADD Remarks nvarchar(500) NULL;

            DECLARE @FinalId int = (SELECT TOP (1) Id FROM Menus WHERE Route = N'/assessment/final' ORDER BY Id);
            IF @FinalId IS NOT NULL
            BEGIN
                DECLARE @EditKey nvarchar(50) = CONCAT(N'MENU_', @FinalId, N'_EDIT');
                IF NOT EXISTS (SELECT 1 FROM Features WHERE FeatureKey = @EditKey)
                    INSERT INTO Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
                    VALUES (@EditKey, N'Final Assessment - Edit', N'Menu',
                            N'Adjust final assessment amounts and pay to payroll.', SYSUTCDATETIME());

                INSERT INTO MenuPermissions (MenuId, PermissionId)
                SELECT @FinalId, f.PermissionId
                FROM Features f
                WHERE f.FeatureKey = @EditKey
                  AND NOT EXISTS (
                      SELECT 1 FROM MenuPermissions mp
                      WHERE mp.MenuId = @FinalId AND mp.PermissionId = f.PermissionId);

                UPDATE TenantMenuPermissions
                SET CanEdit = 1, IsAllow = 1, CanView = 1
                WHERE MenuId = @FinalId;

                INSERT INTO AccessFeatures (StaffMenuAccessId, PermissionId, IsAllow)
                SELECT sma.Id, f.PermissionId, 1
                FROM StaffMenuAccess sma
                CROSS JOIN Features f
                WHERE sma.MenuId = @FinalId
                  AND f.FeatureKey = @EditKey
                  AND NOT EXISTS (
                      SELECT 1 FROM AccessFeatures af
                      WHERE af.StaffMenuAccessId = sma.Id AND af.PermissionId = f.PermissionId);
            END
            """);

        migrationBuilder.Sql(
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
                    COALESCE(
                        CASE WHEN org.Label = N'Department' THEN org.Name END,
                        vac.Department,
                        org.Name,
                        N'—'
                    ) AS Department,
                    COALESCE(des.TitleName, vac.JobTitle, N'—') AS JobTitle,
                    @Year AS AssessmentYear,
                    @Month AS AssessmentMonth,
                    sa.Rating,
                    sa.Amount,
                    sa.Remarks,
                    CAST(CASE WHEN sa.IsLocked = 1 OR sa.Rating IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS IsLocked,
                    sa.SubmittedDateUtc,
                    sa.AssessorPersonId,
                    COALESCE(ap.FullName, N'—') AS AssessorName,
                    CASE
                        WHEN sa.Id IS NULL THEN N'Pending'
                        WHEN sa.IsLocked = 1 OR sa.Rating IS NOT NULL THEN N'Submitted'
                        ELSE N'Open'
                    END AS Status
                FROM dbo.Persons p
                INNER JOIN dbo.StaffVacancy sv
                    ON sv.PersonId = p.PersonId
                   AND sv.TenantId = p.TenantId
                LEFT JOIN dbo.Vacancies vac ON vac.VacancyId = sv.VacancyId
                LEFT JOIN dbo.OrganizationTree org ON org.Id = vac.OrganizationId
                LEFT JOIN dbo.JobTitles des ON des.Id = vac.JobTitleId
                OUTER APPLY
                (
                    SELECT TOP (1)
                        a.Id,
                        a.Rating,
                        a.Amount,
                        a.Remarks,
                        a.IsLocked,
                        a.SubmittedDateUtc,
                        a.AssessorPersonId
                    FROM dbo.StaffAssessments a
                    WHERE a.TenantId = @TenantId
                      AND a.SubjectPersonId = p.PersonId
                      AND a.AssessmentYear = @Year
                      AND a.AssessmentMonth = @Month
                    ORDER BY
                        CASE WHEN a.IsLocked = 1 OR a.Rating IS NOT NULL THEN 0 ELSE 1 END,
                        a.SubmittedDateUtc DESC,
                        a.Id DESC
                ) sa
                LEFT JOIN dbo.Persons ap ON ap.PersonId = sa.AssessorPersonId
                WHERE p.TenantId = @TenantId
                  AND p.IsActive = 1
                ORDER BY Department, p.FullName;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH(N'dbo.StaffAssessments', N'Remarks') IS NOT NULL
                ALTER TABLE dbo.StaffAssessments DROP COLUMN Remarks;
            """);
    }
}
