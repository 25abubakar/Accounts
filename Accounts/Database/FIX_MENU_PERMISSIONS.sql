-- ══════════════════════════════════════════════════════════════════════════
-- FIX MENU PERMISSIONS - Link Menus to Features
-- ══════════════════════════════════════════════════════════════════════════
-- 
-- PROBLEM: Admin grants permissions to users, but menus don't appear
-- CAUSE: MenuPermissions table is empty - no link between Menus and Features
-- 
-- SOLUTION: Auto-link each menu to its corresponding MENU_{id} feature
--
-- USAGE: Run this script after running MIGRATION_RBAC_Refactor.sql
-- ══════════════════════════════════════════════════════════════════════════

USE [YourDatabaseName]; -- UPDATE THIS
GO

PRINT '════════════════════════════════════════════════════════════════';
PRINT 'FIX: Linking Menus to Features via MenuPermissions';
PRINT '════════════════════════════════════════════════════════════════';
PRINT '';

-- ══════════════════════════════════════════════════════════════════════════
-- STEP 1: Check current state
-- ══════════════════════════════════════════════════════════════════════════

PRINT '── Step 1: Current State ──';
PRINT 'Menus count: ' + CAST((SELECT COUNT(*) FROM Menus) AS VARCHAR(10));
PRINT 'Features count: ' + CAST((SELECT COUNT(*) FROM Features) AS VARCHAR(10));
PRINT 'MenuPermissions count: ' + CAST((SELECT COUNT(*) FROM MenuPermissions) AS VARCHAR(10));
PRINT '';

-- Show menus without permissions linked
SELECT 
    'Menus WITHOUT permissions linked:' AS [Info],
    COUNT(*) AS [Count]
FROM Menus m
WHERE NOT EXISTS (
    SELECT 1 FROM MenuPermissions mp WHERE mp.MenuId = m.Id
);

PRINT '';

-- ══════════════════════════════════════════════════════════════════════════
-- STEP 2: Link each menu to its MENU_{id} feature
-- ══════════════════════════════════════════════════════════════════════════

PRINT '── Step 2: Linking Menus to Features ──';

-- For each active menu, link it to its MENU_{id} feature in the Features table
INSERT INTO MenuPermissions (MenuId, PermissionId)
SELECT 
    m.Id AS MenuId,
    f.PermissionId
FROM Menus m
INNER JOIN Features f ON f.FeatureKey = 'MENU_' + CAST(m.Id AS NVARCHAR(10))
WHERE m.IsActive = 1
  AND NOT EXISTS (
      SELECT 1 
      FROM MenuPermissions mp 
      WHERE mp.MenuId = m.Id 
        AND mp.PermissionId = f.PermissionId
  );

PRINT 'Linked ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' menus to their features';
PRINT '';

-- ══════════════════════════════════════════════════════════════════════════
-- STEP 3: Verify the fix
-- ══════════════════════════════════════════════════════════════════════════

PRINT '── Step 3: Verification ──';

-- Show all menus with their linked features
SELECT 
    m.Id,
    m.Title,
    m.Route,
    f.FeatureKey,
    f.FeatureName
FROM Menus m
INNER JOIN MenuPermissions mp ON mp.MenuId = m.Id
INNER JOIN Features f ON f.PermissionId = mp.PermissionId
WHERE m.IsActive = 1
ORDER BY m.SortOrder, m.Id;

PRINT '';
PRINT '✅ Menu permissions linked successfully!';
PRINT '';

-- ══════════════════════════════════════════════════════════════════════════
-- STEP 4: Optional - Make some menus public (visible to all users)
-- ══════════════════════════════════════════════════════════════════════════

PRINT '── Step 4: Optional - Mark Dashboard as Public ──';
PRINT 'Removing permission requirement from Dashboard (if exists)';
PRINT 'This makes Dashboard visible to ALL logged-in users';
PRINT '';

-- Find Dashboard menu and remove its permission requirement
DELETE mp
FROM MenuPermissions mp
INNER JOIN Menus m ON m.Id = mp.MenuId
WHERE m.Title = 'Dashboard' OR m.Route = '/dashboard';

PRINT 'Dashboard is now PUBLIC (visible to all authenticated users)';
PRINT '';

-- ══════════════════════════════════════════════════════════════════════════
-- FINAL CHECK
-- ══════════════════════════════════════════════════════════════════════════

PRINT '════════════════════════════════════════════════════════════════';
PRINT 'FINAL STATE:';
PRINT '════════════════════════════════════════════════════════════════';

SELECT 
    'Total Menus' AS [Metric],
    COUNT(*) AS [Count]
FROM Menus
UNION ALL
SELECT 
    'Menus with Permissions' AS [Metric],
    COUNT(DISTINCT mp.MenuId) AS [Count]
FROM MenuPermissions mp
UNION ALL
SELECT 
    'Public Menus (no permission required)' AS [Metric],
    COUNT(*) AS [Count]
FROM Menus m
WHERE NOT EXISTS (SELECT 1 FROM MenuPermissions mp WHERE mp.MenuId = m.Id)
  AND m.IsActive = 1;

PRINT '';
PRINT '✅ FIX COMPLETE!';
PRINT '';
PRINT '════════════════════════════════════════════════════════════════';
PRINT 'NEXT STEPS:';
PRINT '════════════════════════════════════════════════════════════════';
PRINT '1. Test user login - menus should now appear';
PRINT '2. If user still sees no menus:';
PRINT '   - Check UserPermissionOverrides table for their StaffId';
PRINT '   - Verify they have MENU_* features granted';
PRINT '   - Run: SELECT * FROM UserPermissionOverrides WHERE StaffId = ''user-guid''';
PRINT '';
PRINT '3. Grant permissions via Admin UI:';
PRINT '   - Navigate to /access/admin-access';
PRINT '   - Select the user';
PRINT '   - Toggle menu permissions (MENU_1, MENU_2, etc.)';
PRINT '   - Click Save';
PRINT '';
PRINT '════════════════════════════════════════════════════════════════';

GO
