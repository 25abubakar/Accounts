-- Apply Final Assessment menu + SP (same as migration 20260914180000)
SET NOCOUNT ON;

DECLARE @AssessmentId int = (
    SELECT TOP (1) Id FROM Menus
    WHERE ParentId IS NULL AND Title IN (N'Assessment', N'Assesment')
    ORDER BY CASE WHEN Title = N'Assessment' THEN 0 ELSE 1 END, Id
);

IF @AssessmentId IS NULL
BEGIN
    INSERT INTO Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
    VALUES (N'Assessment', N'ClipboardCheck', NULL, NULL, 5, 1);
    SET @AssessmentId = SCOPE_IDENTITY();
END;

DECLARE @MarkId int = (
    SELECT TOP (1) Id FROM Menus
    WHERE Route = N'/assessment/mark'
       OR (ParentId = @AssessmentId AND Title = N'Mark Assessment')
    ORDER BY CASE WHEN Route = N'/assessment/mark' THEN 0 ELSE 1 END, Id
);

DECLARE @FinalId int = (
    SELECT TOP (1) Id FROM Menus
    WHERE Route = N'/assessment/final'
       OR (ParentId = @AssessmentId AND Title IN (N'Final Assessment', N'Final Assesment'))
    ORDER BY CASE WHEN Route = N'/assessment/final' THEN 0 ELSE 1 END, Id
);

IF @FinalId IS NULL AND @AssessmentId IS NOT NULL
BEGIN
    INSERT INTO Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
    VALUES (N'Final Assessment', N'ClipboardList', N'/assessment/final', @AssessmentId, 3, 1);
    SET @FinalId = SCOPE_IDENTITY();
END;

IF @FinalId IS NOT NULL
    UPDATE Menus
    SET Title = N'Final Assessment',
        Icon = N'ClipboardList',
        Route = N'/assessment/final',
        ParentId = @AssessmentId,
        SortOrder = 3,
        IsActive = 1
    WHERE Id = @FinalId;

IF @FinalId IS NOT NULL
BEGIN
    DECLARE @FeaturePrefix nvarchar(50) = CONCAT(N'MENU_', @FinalId);

    IF NOT EXISTS (SELECT 1 FROM Features WHERE FeatureKey = @FeaturePrefix)
        INSERT INTO Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
        VALUES (@FeaturePrefix, N'Final Assessment', N'Menu',
                N'Open the final staff assessment overview.', SYSUTCDATETIME());

    IF NOT EXISTS (SELECT 1 FROM Features WHERE FeatureKey = CONCAT(@FeaturePrefix, N'_VIEW'))
        INSERT INTO Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
        VALUES (CONCAT(@FeaturePrefix, N'_VIEW'), N'Final Assessment - View', N'Menu',
                N'View all staff assessments for the selected period.', SYSUTCDATETIME());

    INSERT INTO MenuPermissions (MenuId, PermissionId)
    SELECT @FinalId, featureRow.PermissionId
    FROM Features featureRow
    WHERE featureRow.FeatureKey IN (@FeaturePrefix, CONCAT(@FeaturePrefix, N'_VIEW'))
      AND NOT EXISTS (
          SELECT 1
          FROM MenuPermissions existingPermission
          WHERE existingPermission.MenuId = @FinalId
            AND existingPermission.PermissionId = featureRow.PermissionId
      );
END;

IF @FinalId IS NOT NULL AND @MarkId IS NOT NULL
BEGIN
    INSERT INTO StaffMenuAccess (StaffId, MenuId, IsAllow, GrantedBy, GrantedDate)
    SELECT sourceAccess.StaffId, @FinalId, sourceAccess.IsAllow,
           sourceAccess.GrantedBy, sourceAccess.GrantedDate
    FROM StaffMenuAccess sourceAccess
    WHERE sourceAccess.MenuId = @MarkId
      AND NOT EXISTS (
          SELECT 1 FROM StaffMenuAccess existingAccess
          WHERE existingAccess.StaffId = sourceAccess.StaffId
            AND existingAccess.MenuId = @FinalId);

    INSERT INTO AccessFeatures (StaffMenuAccessId, PermissionId, IsAllow)
    SELECT targetAccess.Id, finalFeature.PermissionId, 1
    FROM StaffMenuAccess sourceAccess
    JOIN StaffMenuAccess targetAccess
      ON targetAccess.StaffId = sourceAccess.StaffId
     AND targetAccess.MenuId = @FinalId
    CROSS JOIN Features finalFeature
    WHERE sourceAccess.MenuId = @MarkId
      AND finalFeature.FeatureKey IN (
            CONCAT(N'MENU_', @FinalId),
            CONCAT(N'MENU_', @FinalId, N'_VIEW'))
      AND NOT EXISTS (
          SELECT 1 FROM AccessFeatures existingFeature
          WHERE existingFeature.StaffMenuAccessId = targetAccess.Id
            AND existingFeature.PermissionId = finalFeature.PermissionId);

    INSERT INTO TenantMenuPermissions
        (TenantId, MenuId, IsAllow, CanView, CanAdd, CanEdit, CanDelete, GrantedOnUtc, GrantedByUserId)
    SELECT tenant.Id, @FinalId,
           COALESCE(sourcePermission.IsAllow, 1),
           COALESCE(sourcePermission.CanView, 1),
           0, 0, 0,
           SYSUTCDATETIME(), N'System: Final Assessment Menu'
    FROM Tenants tenant
    LEFT JOIN TenantMenuPermissions sourcePermission
      ON sourcePermission.TenantId = tenant.Id
     AND sourcePermission.MenuId = @MarkId
    WHERE NOT EXISTS (
        SELECT 1 FROM TenantMenuPermissions existingPermission
        WHERE existingPermission.TenantId = tenant.Id
          AND existingPermission.MenuId = @FinalId);

    INSERT INTO TenantRolePermissions
        (TenantId, JobTitle, DeptId, PermissionId, IsAllowed, CreatedOnUtc, SetByUserId)
    SELECT sourceRole.TenantId, sourceRole.JobTitle, sourceRole.DeptId,
           finalFeature.PermissionId, sourceRole.IsAllowed,
           SYSUTCDATETIME(), N'System: Final Assessment Menu'
    FROM TenantRolePermissions sourceRole
    INNER JOIN Features markFeature ON markFeature.PermissionId = sourceRole.PermissionId
    INNER JOIN Features finalFeature ON
        (
            (markFeature.FeatureKey = CONCAT(N'MENU_', @MarkId)
             AND finalFeature.FeatureKey = CONCAT(N'MENU_', @FinalId))
         OR (markFeature.FeatureKey = CONCAT(N'MENU_', @MarkId, N'_VIEW')
             AND finalFeature.FeatureKey = CONCAT(N'MENU_', @FinalId, N'_VIEW'))
        )
    WHERE sourceRole.IsAllowed = 1
      AND NOT EXISTS (
          SELECT 1 FROM TenantRolePermissions existingRole
          WHERE existingRole.TenantId = sourceRole.TenantId
            AND existingRole.JobTitle = sourceRole.JobTitle
            AND existingRole.PermissionId = finalFeature.PermissionId
            AND (
                  (existingRole.DeptId IS NULL AND sourceRole.DeptId IS NULL)
               OR existingRole.DeptId = sourceRole.DeptId
            ));
END;

IF NOT EXISTS (
    SELECT 1 FROM __EFMigrationsHistory
    WHERE MigrationId = N'20260914180000_AddFinalAssessmentMenuAndListSp'
)
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES (N'20260914180000_AddFinalAssessmentMenuAndListSp', N'9.0.0');

SELECT Id, Title, Route, ParentId, SortOrder, IsActive
FROM Menus
WHERE Title LIKE N'%Assessment%' OR Route LIKE N'/assessment%'
ORDER BY ParentId, SortOrder, Id;

SELECT COUNT(*) AS TenantGrants
FROM TenantMenuPermissions tmp
JOIN Menus m ON m.Id = tmp.MenuId
WHERE m.Route = N'/assessment/final' AND tmp.CanView = 1;
GO

CREATE OR ALTER PROCEDURE dbo.usp_Assessment_FinalList
    @TenantId INT,
    @Year INT,
    @Month INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        CAST(ROW_NUMBER() OVER (ORDER BY COALESCE(vac.Department, org.Name, N''), p.FullName) AS INT) AS Id,
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
GO
