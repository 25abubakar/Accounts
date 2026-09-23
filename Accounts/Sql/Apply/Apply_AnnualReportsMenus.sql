SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRAN;

DECLARE @ParentId int = (
    SELECT TOP (1) Id
    FROM dbo.Menus
    WHERE ParentId IS NULL AND Title = N'Annual Reports'
    ORDER BY Id
);

IF @ParentId IS NULL
BEGIN
    INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
    VALUES (N'Annual Reports', N'CalendarRange', NULL, NULL, 84, 1);
    SET @ParentId = SCOPE_IDENTITY();
END;
ELSE
    UPDATE dbo.Menus
    SET Title = N'Annual Reports', Icon = N'CalendarRange', Route = NULL,
        ParentId = NULL, SortOrder = 84, IsActive = 1
    WHERE Id = @ParentId;

DECLARE @ExpId int = (
    SELECT TOP (1) Id
    FROM dbo.Menus
    WHERE Route = N'/annual-reports/exp-report'
       OR (ParentId = @ParentId AND Title = N'Annual Exp Report')
    ORDER BY CASE WHEN Route = N'/annual-reports/exp-report' THEN 0 ELSE 1 END, Id
);

IF @ExpId IS NULL
BEGIN
    INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
    VALUES (N'Annual Exp Report', N'DollarSign', N'/annual-reports/exp-report', @ParentId, 1, 1);
    SET @ExpId = SCOPE_IDENTITY();
END;
ELSE
    UPDATE dbo.Menus
    SET Title = N'Annual Exp Report', Icon = N'DollarSign', Route = N'/annual-reports/exp-report',
        ParentId = @ParentId, SortOrder = 1, IsActive = 1
    WHERE Id = @ExpId;

DECLARE @IncomeId int = (
    SELECT TOP (1) Id
    FROM dbo.Menus
    WHERE Route = N'/annual-reports/income-report'
       OR (ParentId = @ParentId AND Title = N'Annual Income Report')
    ORDER BY CASE WHEN Route = N'/annual-reports/income-report' THEN 0 ELSE 1 END, Id
);

IF @IncomeId IS NULL
BEGIN
    INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
    VALUES (N'Annual Income Report', N'LineChart', N'/annual-reports/income-report', @ParentId, 2, 1);
    SET @IncomeId = SCOPE_IDENTITY();
END;
ELSE
    UPDATE dbo.Menus
    SET Title = N'Annual Income Report', Icon = N'LineChart', Route = N'/annual-reports/income-report',
        ParentId = @ParentId, SortOrder = 2, IsActive = 1
    WHERE Id = @IncomeId;

DECLARE @FilterId int = (
    SELECT TOP (1) Id
    FROM dbo.Menus
    WHERE Route = N'/annual-reports/filter'
       OR (ParentId = @ParentId AND Title IN (N'Annual report Filter', N'Annual Report Filter'))
    ORDER BY CASE WHEN Route = N'/annual-reports/filter' THEN 0 ELSE 1 END, Id
);

IF @FilterId IS NULL
BEGIN
    INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
    VALUES (N'Annual report Filter', N'SlidersHorizontal', N'/annual-reports/filter', @ParentId, 3, 1);
    SET @FilterId = SCOPE_IDENTITY();
END;
ELSE
    UPDATE dbo.Menus
    SET Title = N'Annual report Filter', Icon = N'SlidersHorizontal', Route = N'/annual-reports/filter',
        ParentId = @ParentId, SortOrder = 3, IsActive = 1
    WHERE Id = @FilterId;

DECLARE @Targets table (MenuId int NOT NULL, Title nvarchar(100) NOT NULL);
INSERT INTO @Targets (MenuId, Title)
VALUES
    (@ExpId, N'Annual Exp Report'),
    (@IncomeId, N'Annual Income Report'),
    (@FilterId, N'Annual report Filter');

INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
SELECT CONCAT(N'MENU_', target.MenuId), target.Title, N'Menu',
       CONCAT(N'Open the ', target.Title, N' screen.'), SYSUTCDATETIME()
FROM @Targets target
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Features feature
    WHERE feature.FeatureKey = CONCAT(N'MENU_', target.MenuId)
);

INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
SELECT CONCAT(N'MENU_', target.MenuId, N'_VIEW'), CONCAT(target.Title, N' - View'), N'Menu',
       CONCAT(N'View the ', target.Title, N' screen.'), SYSUTCDATETIME()
FROM @Targets target
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Features feature
    WHERE feature.FeatureKey = CONCAT(N'MENU_', target.MenuId, N'_VIEW')
);

INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
SELECT target.MenuId, feature.PermissionId
FROM @Targets target
JOIN dbo.Features feature
  ON feature.FeatureKey IN (
        CONCAT(N'MENU_', target.MenuId),
        CONCAT(N'MENU_', target.MenuId, N'_VIEW'))
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.MenuPermissions existing
    WHERE existing.MenuId = target.MenuId
      AND existing.PermissionId = feature.PermissionId
);

INSERT INTO dbo.TenantMenuPermissions
    (TenantId, MenuId, IsAllow, CanView, CanAdd, CanEdit, CanDelete, GrantedOnUtc, GrantedByUserId)
SELECT tenant.Id, menuGrant.MenuId, 1, 1, 0, 0, 0,
       SYSUTCDATETIME(), N'System: Annual Reports menus'
FROM dbo.Tenants tenant
CROSS JOIN (
    SELECT @ParentId AS MenuId
    UNION ALL
    SELECT MenuId FROM @Targets
) menuGrant
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.TenantMenuPermissions existing
    WHERE existing.TenantId = tenant.Id
      AND existing.MenuId = menuGrant.MenuId
);

IF NOT EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory
    WHERE MigrationId = N'20260921180000_AddAnnualReportsMenus'
)
    INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES (N'20260921180000_AddAnnualReportsMenus', N'9.0.5');

SELECT
    @ParentId AS AnnualReportsParentId,
    @ExpId AS AnnualExpReportId,
    @IncomeId AS AnnualIncomeReportId,
    @FilterId AS AnnualReportFilterId;

COMMIT TRAN;
