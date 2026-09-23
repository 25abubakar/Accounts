-- Idempotent deploy: Create Accounts parent + Account Type / Create Category / Accounts List.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRAN;

DECLARE @ParentId int = (
    SELECT TOP (1) Id
    FROM dbo.Menus
    WHERE ParentId IS NULL AND Title = N'Create Accounts'
    ORDER BY Id
);

IF @ParentId IS NULL
BEGIN
    INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
    VALUES (N'Create Accounts', N'Users', NULL, NULL, 85, 1);
    SET @ParentId = SCOPE_IDENTITY();
END;
ELSE
    UPDATE dbo.Menus
    SET Title = N'Create Accounts', Icon = N'Users', Route = NULL,
        ParentId = NULL, SortOrder = 85, IsActive = 1
    WHERE Id = @ParentId;

DECLARE @TypeId int = (
    SELECT TOP (1) Id
    FROM dbo.Menus
    WHERE Route = N'/create-accounts/account-type'
       OR (ParentId = @ParentId AND Title = N'Account Type')
    ORDER BY CASE WHEN Route = N'/create-accounts/account-type' THEN 0 ELSE 1 END, Id
);

IF @TypeId IS NULL
BEGIN
    INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
    VALUES (N'Account Type', N'Tags', N'/create-accounts/account-type', @ParentId, 1, 1);
    SET @TypeId = SCOPE_IDENTITY();
END;
ELSE
    UPDATE dbo.Menus
    SET Title = N'Account Type', Icon = N'Tags', Route = N'/create-accounts/account-type',
        ParentId = @ParentId, SortOrder = 1, IsActive = 1
    WHERE Id = @TypeId;

DECLARE @CategoryId int = (
    SELECT TOP (1) Id
    FROM dbo.Menus
    WHERE Route = N'/create-accounts/create-category'
       OR (ParentId = @ParentId AND Title = N'Create Category')
    ORDER BY CASE WHEN Route = N'/create-accounts/create-category' THEN 0 ELSE 1 END, Id
);

IF @CategoryId IS NULL
BEGIN
    INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
    VALUES (N'Create Category', N'FolderTree', N'/create-accounts/create-category', @ParentId, 2, 1);
    SET @CategoryId = SCOPE_IDENTITY();
END;
ELSE
    UPDATE dbo.Menus
    SET Title = N'Create Category', Icon = N'FolderTree', Route = N'/create-accounts/create-category',
        ParentId = @ParentId, SortOrder = 2, IsActive = 1
    WHERE Id = @CategoryId;

DECLARE @ListId int = (
    SELECT TOP (1) Id
    FROM dbo.Menus
    WHERE Route = N'/create-accounts/accounts-list'
       OR (ParentId = @ParentId AND Title = N'Accounts List')
    ORDER BY CASE WHEN Route = N'/create-accounts/accounts-list' THEN 0 ELSE 1 END, Id
);

IF @ListId IS NULL
BEGIN
    INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
    VALUES (N'Accounts List', N'LayoutGrid', N'/create-accounts/accounts-list', @ParentId, 3, 1);
    SET @ListId = SCOPE_IDENTITY();
END;
ELSE
    UPDATE dbo.Menus
    SET Title = N'Accounts List', Icon = N'LayoutGrid', Route = N'/create-accounts/accounts-list',
        ParentId = @ParentId, SortOrder = 3, IsActive = 1
    WHERE Id = @ListId;

DECLARE @Targets table (MenuId int NOT NULL, Title nvarchar(100) NOT NULL);
INSERT INTO @Targets (MenuId, Title)
VALUES
    (@TypeId, N'Account Type'),
    (@CategoryId, N'Create Category'),
    (@ListId, N'Accounts List');

INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
SELECT CONCAT(N'MENU_', target.MenuId), target.Title, N'Menu',
       CONCAT(N'Open the ', target.Title, N' screen.'), SYSUTCDATETIME()
FROM @Targets target
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Features feature
    WHERE feature.FeatureKey = CONCAT(N'MENU_', target.MenuId)
);

INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
SELECT feature.FeatureKey, feature.FeatureName, N'Menu', feature.Description, SYSUTCDATETIME()
FROM @Targets target
CROSS APPLY (VALUES
    (CONCAT(N'MENU_', target.MenuId, N'_VIEW'),   CONCAT(target.Title, N' - View'),   CONCAT(N'View the ', target.Title, N' screen.')),
    (CONCAT(N'MENU_', target.MenuId, N'_ADD'),    CONCAT(target.Title, N' - Add'),    CONCAT(N'Create ', target.Title, N' records.')),
    (CONCAT(N'MENU_', target.MenuId, N'_EDIT'),   CONCAT(target.Title, N' - Edit'),   CONCAT(N'Edit ', target.Title, N' records.')),
    (CONCAT(N'MENU_', target.MenuId, N'_DELETE'), CONCAT(target.Title, N' - Delete'), CONCAT(N'Delete ', target.Title, N' records.'))
) feature(FeatureKey, FeatureName, Description)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Features existing WHERE existing.FeatureKey = feature.FeatureKey
);

INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
SELECT target.MenuId, feature.PermissionId
FROM @Targets target
JOIN dbo.Features feature
  ON feature.FeatureKey IN (
        CONCAT(N'MENU_', target.MenuId),
        CONCAT(N'MENU_', target.MenuId, N'_VIEW'),
        CONCAT(N'MENU_', target.MenuId, N'_ADD'),
        CONCAT(N'MENU_', target.MenuId, N'_EDIT'),
        CONCAT(N'MENU_', target.MenuId, N'_DELETE'))
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.MenuPermissions existing
    WHERE existing.MenuId = target.MenuId
      AND existing.PermissionId = feature.PermissionId
);

INSERT INTO dbo.TenantMenuPermissions
    (TenantId, MenuId, IsAllow, CanView, CanAdd, CanEdit, CanDelete, GrantedOnUtc, GrantedByUserId)
SELECT tenant.Id, menuGrant.MenuId, 1, 1, 1, 1, 1,
       SYSUTCDATETIME(), N'System: Create Accounts menus'
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

IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20260923120000_AddCreateAccountsMenus')
    INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES (N'20260923120000_AddCreateAccountsMenus', N'9.0.5');

COMMIT TRAN;

SELECT m.Id, m.Title, m.Route, m.ParentId, m.SortOrder, m.IsActive
FROM dbo.Menus m
WHERE m.Title = N'Create Accounts'
   OR m.Route IN (
        N'/create-accounts/account-type',
        N'/create-accounts/create-category',
        N'/create-accounts/accounts-list')
ORDER BY CASE WHEN m.ParentId IS NULL THEN 0 ELSE 1 END, m.SortOrder, m.Id;
