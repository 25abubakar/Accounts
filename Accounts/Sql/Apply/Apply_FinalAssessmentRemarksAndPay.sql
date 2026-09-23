SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

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

IF NOT EXISTS (
    SELECT 1 FROM __EFMigrationsHistory
    WHERE MigrationId = N'20260914190000_FinalAssessmentRemarksAndPay'
)
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES (N'20260914190000_FinalAssessmentRemarksAndPay', N'9.0.0');

SELECT COL_LENGTH(N'dbo.StaffAssessments', N'Remarks') AS RemarksCol;
SELECT CanView, CanEdit FROM TenantMenuPermissions WHERE MenuId = @FinalId;
GO
