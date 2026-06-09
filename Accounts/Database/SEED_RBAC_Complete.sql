-- ═══════════════════════════════════════════════════════════════════════════════
-- COMPLETE RBAC SEED SCRIPT
-- Ensures Features table + MenuPermissions are fully populated.
--
-- WHY THIS IS NEEDED:
--   Without rows in MenuPermissions, the backend treats all menus as "public"
--   (no permission required), so ALL users see ALL menus — breaking RBAC.
--
--   Without rows in Features for MENU_{id} keys, AdminAccessPage cannot grant
--   menu-level permissions because the feature keys don't exist to link to.
--
-- RUN ORDER:
--   1. This script (seed Features + link MenuPermissions)
--   2. Then use AdminAccessPage UI to grant menu access to users
--   3. User logs out + back in → sidebar shows only granted menus
--
-- SAFE: Idempotent — can be run multiple times with no side effects.
-- ═══════════════════════════════════════════════════════════════════════════════

USE [Account];
GO

SET NOCOUNT ON;

PRINT '════════════════════════════════════════════════════════';
PRINT 'STEP 1: Seed Features for each Menu (MENU_{id} pattern)';
PRINT '════════════════════════════════════════════════════════';

-- Insert MENU_{id} feature for each active menu that doesn't already have one
INSERT INTO dbo.Features (FeatureKey, FeatureName, Module)
SELECT 
    'MENU_' + CAST(m.Id AS NVARCHAR(10))  AS FeatureKey,
    m.Title + ' - Access'                  AS FeatureName,
    'Menu'                                 AS Module
FROM dbo.Menus m
WHERE m.IsActive = 1
  AND NOT EXISTS (
      SELECT 1 FROM dbo.Features f 
      WHERE f.FeatureKey = 'MENU_' + CAST(m.Id AS NVARCHAR(10))
  );

PRINT 'MENU_{id} features added: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

-- Insert MENU_{id}_VIEW for each active menu
INSERT INTO dbo.Features (FeatureKey, FeatureName, Module)
SELECT 
    'MENU_' + CAST(m.Id AS NVARCHAR(10)) + '_VIEW' AS FeatureKey,
    m.Title + ' - View'                             AS FeatureName,
    'Menu'                                          AS Module
FROM dbo.Menus m
WHERE m.IsActive = 1
  AND NOT EXISTS (
      SELECT 1 FROM dbo.Features f 
      WHERE f.FeatureKey = 'MENU_' + CAST(m.Id AS NVARCHAR(10)) + '_VIEW'
  );
PRINT 'MENU_{id}_VIEW features added: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

-- Insert MENU_{id}_ADD
INSERT INTO dbo.Features (FeatureKey, FeatureName, Module)
SELECT 
    'MENU_' + CAST(m.Id AS NVARCHAR(10)) + '_ADD' AS FeatureKey,
    m.Title + ' - Add'                             AS FeatureName,
    'Menu'                                         AS Module
FROM dbo.Menus m
WHERE m.IsActive = 1
  AND NOT EXISTS (
      SELECT 1 FROM dbo.Features f 
      WHERE f.FeatureKey = 'MENU_' + CAST(m.Id AS NVARCHAR(10)) + '_ADD'
  );
PRINT 'MENU_{id}_ADD features added: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

-- Insert MENU_{id}_EDIT
INSERT INTO dbo.Features (FeatureKey, FeatureName, Module)
SELECT 
    'MENU_' + CAST(m.Id AS NVARCHAR(10)) + '_EDIT' AS FeatureKey,
    m.Title + ' - Edit'                             AS FeatureName,
    'Menu'                                          AS Module
FROM dbo.Menus m
WHERE m.IsActive = 1
  AND NOT EXISTS (
      SELECT 1 FROM dbo.Features f 
      WHERE f.FeatureKey = 'MENU_' + CAST(m.Id AS NVARCHAR(10)) + '_EDIT'
  );
PRINT 'MENU_{id}_EDIT features added: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

-- Insert MENU_{id}_DELETE
INSERT INTO dbo.Features (FeatureKey, FeatureName, Module)
SELECT 
    'MENU_' + CAST(m.Id AS NVARCHAR(10)) + '_DELETE' AS FeatureKey,
    m.Title + ' - Delete'                             AS FeatureName,
    'Menu'                                            AS Module
FROM dbo.Menus m
WHERE m.IsActive = 1
  AND NOT EXISTS (
      SELECT 1 FROM dbo.Features f 
      WHERE f.FeatureKey = 'MENU_' + CAST(m.Id AS NVARCHAR(10)) + '_DELETE'
  );
PRINT 'MENU_{id}_DELETE features added: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

-- ─────────────────────────────────────────────────────────────────────────────
-- Seed static operational feature keys
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '';
PRINT '════════════════════════════════════════════════════════';
PRINT 'STEP 2: Seed static operational feature keys';
PRINT '════════════════════════════════════════════════════════';

DECLARE @StaticFeatures TABLE (FeatureKey NVARCHAR(100), FeatureName NVARCHAR(150), Module NVARCHAR(100));
INSERT INTO @StaticFeatures VALUES
  ('DEPT_VIEW',             'View Department',              'Organization'),
  ('DEPT_VIEW_ALL',         'View All Departments',         'Organization'),
  ('DEPT_CREATE',           'Create Department',            'Organization'),
  ('DEPT_EDIT',             'Edit Department',              'Organization'),
  ('DEPT_DELETE',           'Delete Department',            'Organization'),
  ('VACANCY_VIEW',          'View Vacancies',               'Vacancy'),
  ('VACANCY_CREATE',        'Create Vacancy',               'Vacancy'),
  ('VACANCY_EDIT',          'Edit Vacancy',                 'Vacancy'),
  ('VACANCY_DELETE',        'Delete Vacancy',               'Vacancy'),
  ('VACANCY_ASSIGN',        'Assign Staff to Vacancy',      'Vacancy'),
  ('EMPLOYEE_VIEW',         'View Employees',               'Employee'),
  ('EMPLOYEE_VIEW_ALL',     'View All Employees',           'Employee'),
  ('EMPLOYEE_EDIT',         'Edit Employee',                'Employee'),
  ('EMPLOYEE_DELETE',       'Delete Employee',              'Employee'),
  ('EMPLOYEE_TRANSFER',     'Transfer Employee',            'Employee'),
  ('PERSON_VIEW',           'View Persons',                 'Person'),
  ('PERSON_VIEW_ALL',       'View All Persons',             'Person'),
  ('PERSON_REGISTER',       'Register Person',              'Person'),
  ('PERSON_EDIT',           'Edit Person',                  'Person'),
  ('PERSON_DELETE',         'Delete Person',                'Person'),
  ('PERSON_RESET_PASSWORD', 'Reset Person Password',        'Person'),
  ('ACCESS_GROUP_VIEW',     'View Access Groups',           'Access'),
  ('ACCESS_GROUP_CREATE',   'Create Access Group',          'Access'),
  ('ACCESS_GROUP_EDIT',     'Edit Access Group',            'Access'),
  ('ACCESS_GROUP_DELETE',   'Delete Access Group',          'Access'),
  ('ACCESS_GROUP_ASSIGN',   'Assign Group to Staff',        'Access'),
  ('LOCATION_VIEW',         'View Locations',               'Location'),
  ('LOCATION_MANAGE',       'Manage Locations',             'Location');

INSERT INTO dbo.Features (FeatureKey, FeatureName, Module)
SELECT sf.FeatureKey, sf.FeatureName, sf.Module
FROM @StaticFeatures sf
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Features f WHERE f.FeatureKey = sf.FeatureKey
);

PRINT 'Static features added: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

-- ─────────────────────────────────────────────────────────────────────────────
-- Link Menus → Features via MenuPermissions
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '';
PRINT '════════════════════════════════════════════════════════';
PRINT 'STEP 3: Link Menus to their MENU_{id} features';
PRINT 'This is what CONTROLS sidebar visibility per user.';
PRINT '════════════════════════════════════════════════════════';

-- Link each active menu to its MENU_{id} feature
INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
SELECT 
    m.Id               AS MenuId,
    f.PermissionId     AS PermissionId
FROM dbo.Menus m
INNER JOIN dbo.Features f ON f.FeatureKey = 'MENU_' + CAST(m.Id AS NVARCHAR(10))
WHERE m.IsActive = 1
  AND NOT EXISTS (
      SELECT 1 FROM dbo.MenuPermissions mp 
      WHERE mp.MenuId = m.Id AND mp.PermissionId = f.PermissionId
  );

PRINT 'MenuPermissions links added: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

-- ─────────────────────────────────────────────────────────────────────────────
-- Also link operational keys to their menus by route
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '';
PRINT 'STEP 3b: Link operational keys to menus by route...';

-- Staff menus → EMPLOYEE_VIEW
INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
SELECT m.Id, f.PermissionId
FROM dbo.Menus m
CROSS JOIN dbo.Features f
WHERE f.FeatureKey = 'EMPLOYEE_VIEW'
  AND m.IsActive = 1
  AND (m.Route LIKE '%/hr/staff%' OR m.Route LIKE '%/staff%' OR m.Title LIKE '%Staff%' OR m.Title LIKE '%Employee%')
  AND NOT EXISTS (SELECT 1 FROM dbo.MenuPermissions mp WHERE mp.MenuId = m.Id AND mp.PermissionId = f.PermissionId);

-- Vacancies/Positions menus → VACANCY_VIEW
INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
SELECT m.Id, f.PermissionId
FROM dbo.Menus m
CROSS JOIN dbo.Features f
WHERE f.FeatureKey = 'VACANCY_VIEW'
  AND m.IsActive = 1
  AND (m.Route LIKE '%vacancies%' OR m.Route LIKE '%positions%' OR m.Title LIKE '%Vacanc%' OR m.Title LIKE '%Position%')
  AND NOT EXISTS (SELECT 1 FROM dbo.MenuPermissions mp WHERE mp.MenuId = m.Id AND mp.PermissionId = f.PermissionId);

-- Organization menus → DEPT_VIEW
INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
SELECT m.Id, f.PermissionId
FROM dbo.Menus m
CROSS JOIN dbo.Features f
WHERE f.FeatureKey = 'DEPT_VIEW'
  AND m.IsActive = 1
  AND (m.Route LIKE '%organization%' OR m.Route LIKE '%org-tree%' OR m.Title LIKE '%Organization%' OR m.Title LIKE '%Org%')
  AND NOT EXISTS (SELECT 1 FROM dbo.MenuPermissions mp WHERE mp.MenuId = m.Id AND mp.PermissionId = f.PermissionId);

-- Person/Register menus → PERSON_VIEW
INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
SELECT m.Id, f.PermissionId
FROM dbo.Menus m
CROSS JOIN dbo.Features f
WHERE f.FeatureKey = 'PERSON_VIEW'
  AND m.IsActive = 1
  AND (m.Route LIKE '%persons%' OR m.Route LIKE '%register%' OR m.Title LIKE '%Person%' OR m.Title LIKE '%Register%')
  AND NOT EXISTS (SELECT 1 FROM dbo.MenuPermissions mp WHERE mp.MenuId = m.Id AND mp.PermissionId = f.PermissionId);

PRINT 'Operational link rows added successfully.';

-- ─────────────────────────────────────────────────────────────────────────────
-- Dashboard is PUBLIC — remove any permission requirement from it
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '';
PRINT 'STEP 4: Making Dashboard public (no permission required)...';

DELETE mp
FROM dbo.MenuPermissions mp
INNER JOIN dbo.Menus m ON m.Id = mp.MenuId
WHERE m.Title = 'Dashboard' OR m.Route = '/dashboard';

PRINT 'Dashboard is now PUBLIC.';

-- ─────────────────────────────────────────────────────────────────────────────
-- Final verification
-- ─────────────────────────────────────────────────────────────────────────────
PRINT '';
PRINT '════════════════════════════════════════════════════════';
PRINT 'VERIFICATION:';
PRINT '════════════════════════════════════════════════════════';

SELECT 
    'Total Features'       AS Metric, COUNT(*) AS Count FROM dbo.Features
UNION ALL SELECT 'Total MenuPermissions', COUNT(*) FROM dbo.MenuPermissions
UNION ALL SELECT 'Total Menus',           COUNT(*) FROM dbo.Menus WHERE IsActive = 1
UNION ALL SELECT 'Menus with Permissions (access-controlled)', COUNT(DISTINCT MenuId) FROM dbo.MenuPermissions
UNION ALL SELECT 'Public Menus (no permission = visible to all)',
    (SELECT COUNT(*) FROM dbo.Menus m WHERE m.IsActive = 1 AND NOT EXISTS (SELECT 1 FROM dbo.MenuPermissions mp WHERE mp.MenuId = m.Id));

PRINT '';
PRINT '════════════════════════════════════════════════════════';
PRINT '✅ SEED COMPLETE!';
PRINT '════════════════════════════════════════════════════════';
PRINT '';
PRINT 'NEXT STEPS:';
PRINT '1. Call POST /api/rbac/seed-features from Swagger or the app';
PRINT '   (This does the same thing via the API — run EITHER this SQL OR the API)';
PRINT '';
PRINT '2. Go to AdminAccessPage in the UI (/access/admin-access)';
PRINT '   - Select a user';
PRINT '   - Check the menus they should see';
PRINT '   - Toggle VIEW/ADD/EDIT/DELETE as needed';
PRINT '   - Click Save';
PRINT '';
PRINT '3. Have the user log out and log back in.';
PRINT '   Their sidebar will now show ONLY the menus you granted.';
PRINT '';
PRINT 'HOW IT WORKS:';
PRINT '  Admin grants MENU_5 → ALLOW saved in UserPermissionOverrides';
PRINT '  User logs in → GET /api/auth/my-menus';
PRINT '  Backend: resolve permissions → allowedIds includes MENU_5 PermissionId';
PRINT '  MenuPermissions: Menu 5 requires MENU_5 → user has it → Menu 5 shown';
PRINT '  Frontend: AuthContext stores menus[] → Sidebar renders from menus[]';
PRINT '════════════════════════════════════════════════════════';
GO
