SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

DECLARE @RemindersParentId int = (
    SELECT TOP (1) Id
    FROM dbo.Menus
    WHERE ParentId IS NULL AND Title = N'Reminders'
    ORDER BY Id
);

IF @RemindersParentId IS NULL
BEGIN
    INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
    VALUES (N'Reminders', N'Bell', NULL, NULL, 83, 1);
    SET @RemindersParentId = SCOPE_IDENTITY();
END;
ELSE
    UPDATE dbo.Menus
    SET Title = N'Reminders', Icon = N'Bell', Route = NULL,
        ParentId = NULL, SortOrder = 83, IsActive = 1
    WHERE Id = @RemindersParentId;

DECLARE @ReceivableId int = (
    SELECT TOP (1) Id
    FROM dbo.Menus
    WHERE Route = N'/reminders/receivable'
       OR (ParentId = @RemindersParentId AND Title IN (N'Receivable', N'Reciveable'))
    ORDER BY CASE WHEN Route = N'/reminders/receivable' THEN 0 ELSE 1 END, Id
);

IF @ReceivableId IS NULL
BEGIN
    INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
    VALUES (N'Receivable', N'Wallet', N'/reminders/receivable', @RemindersParentId, 1, 1);
    SET @ReceivableId = SCOPE_IDENTITY();
END;
ELSE
    UPDATE dbo.Menus
    SET Title = N'Receivable', Icon = N'Wallet', Route = N'/reminders/receivable',
        ParentId = @RemindersParentId, SortOrder = 1, IsActive = 1
    WHERE Id = @ReceivableId;

DECLARE @PayableId int = (
    SELECT TOP (1) Id
    FROM dbo.Menus
    WHERE Route = N'/reminders/payable'
       OR (ParentId = @RemindersParentId AND Title = N'Payable')
    ORDER BY CASE WHEN Route = N'/reminders/payable' THEN 0 ELSE 1 END, Id
);

IF @PayableId IS NULL
BEGIN
    INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
    VALUES (N'Payable', N'Banknote', N'/reminders/payable', @RemindersParentId, 2, 1);
    SET @PayableId = SCOPE_IDENTITY();
END;
ELSE
    UPDATE dbo.Menus
    SET Title = N'Payable', Icon = N'Banknote', Route = N'/reminders/payable',
        ParentId = @RemindersParentId, SortOrder = 2, IsActive = 1
    WHERE Id = @PayableId;

DECLARE @Targets table (MenuId int NOT NULL, Title nvarchar(100) NOT NULL);
INSERT INTO @Targets (MenuId, Title)
VALUES
    (@ReceivableId, N'Receivable'),
    (@PayableId, N'Payable');

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
       SYSUTCDATETIME(), N'System: Reminders menus'
FROM dbo.Tenants tenant
CROSS JOIN (
    SELECT @RemindersParentId AS MenuId
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
    WHERE MigrationId = N'20260921160000_AddRemindersMenus'
)
    INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES (N'20260921160000_AddRemindersMenus', N'9.0.5');

SELECT
    @RemindersParentId AS RemindersParentId,
    @ReceivableId AS ReceivableId,
    @PayableId AS PayableId;

COMMIT TRAN;
