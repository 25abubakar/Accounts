-- Final Assessment grid: all active tenant staff with month/year assessment status.
-- Keep in sync with migration 20260914210000_FinalAssessmentApprovalGate.
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
        COALESCE(sa.IsFinalApproved, CAST(0 AS BIT)) AS IsFinalApproved,
        sa.FinalApprovedByName,
        sa.FinalApprovedDateUtc,
        COALESCE(sa.IsPostedToPayroll, CAST(0 AS BIT)) AS IsPostedToPayroll,
        sa.PostedPayrollRunId,
        sa.PostedToPayrollDateUtc,
        COALESCE(ap.FullName, N'—') AS AssessorName,
        CASE
            WHEN sa.Id IS NULL THEN N'Pending'
            WHEN sa.IsFinalApproved = 1 AND sa.IsPostedToPayroll = 1 THEN N'Approved & Posted'
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
            a.AssessorPersonId,
            a.IsFinalApproved,
            a.FinalApprovedByName,
            a.FinalApprovedDateUtc,
            a.IsPostedToPayroll,
            a.PostedPayrollRunId,
            a.PostedToPayrollDateUtc
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
GO
