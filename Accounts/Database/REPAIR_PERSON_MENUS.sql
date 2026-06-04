-- ══════════════════════════════════════════════════════════════════════════
-- REPAIR_PERSON_MENUS.sql
-- ══════════════════════════════════════════════════════════════════════════
-- PURPOSE: Back-fill PersonMenus rows from existing PersonFeatures rows.
--
-- ROOT CAUSE OF BUG:
--   The admin "bulk-overrides" endpoint was writing PersonFeatures rows
--   (MENU_* entries) but NOT PersonMenus rows.
--   At login, HasPersonGrantsAsync() returns TRUE (PersonFeatures exist),
--   so the code enters the PersonAccess path and calls GetGrantedSidebarAsync(),
--   which ONLY reads PersonMenus → finds 0 rows → returns empty sidebar.
--   Result: user sees "No menu access" even though 84% was granted.
--
-- FIX:
--   This script inserts the missing PersonMenus rows, including
--   parent-menu rows so section headers appear in the sidebar.
--   It is idempotent — safe to run multiple times.
--
-- AFTER RUNNING:
--   Users log out and back in. Granted menus will now appear.
-- ══════════════════════════════════════════════════════════════════════════

SET NOCOUNT ON;

PRINT '════════════════════════════════════════════════════════════════';
PRINT 'REPAIR: Back-fill PersonMenus from PersonFeatures';
PRINT '════════════════════════════════════════════════════════════════';

-- ─── Diagnostics before ───────────────────────────────────────────────────
PRINT '';
PRINT 'Before repair:';
SELECT 'PersonMenus rows'    AS [Table], COUNT(*) AS [Rows] FROM PersonMenus
UNION ALL
SELECT 'PersonFeatures rows' AS [Table], COUNT(*) AS [Rows] FROM PersonFeatures;

-- ─── Identify persons that need repair ────────────────────────────────────
-- These are people who have PersonFeatures with MENU_* keys
-- but are missing the corresponding PersonMenus rows.

DECLARE @repair TABLE (
    PersonId UNIQUEIDENTIFIER NOT NULL,
    MenuId   INT              NOT NULL
);

INSERT INTO @repair (PersonId, MenuId)
SELECT DISTINCT
    pf.PersonId,
    CAST(SUBSTRING(f.FeatureKey, 6, LEN(f.FeatureKey) - 5) AS INT) AS MenuId
FROM PersonFeatures pf
INNER JOIN Features f ON f.PermissionId = pf.PermissionId
WHERE f.FeatureKey LIKE 'MENU_[0-9]%'                     -- bare MENU_{id} only
  AND f.FeatureKey NOT LIKE 'MENU_[0-9]%\_VIEW'  ESCAPE '\'
  AND f.FeatureKey NOT LIKE 'MENU_[0-9]%\_ADD'   ESCAPE '\'
  AND f.FeatureKey NOT LIKE 'MENU_[0-9]%\_EDIT'  ESCAPE '\'
  AND f.FeatureKey NOT LIKE 'MENU_[0-9]%\_DELETE' ESCAPE '\'
  AND ISNUMERIC(SUBSTRING(f.FeatureKey, 6, LEN(f.FeatureKey) - 5)) = 1
  AND m.IsActive = 1
FROM PersonFeatures pf
INNER JOIN Features f ON f.PermissionId = pf.PermissionId
INNER JOIN Menus m ON m.Id = CAST(SUBSTRING(f.FeatureKey, 6, LEN(f.FeatureKey) - 5) AS INT)
WHERE f.FeatureKey LIKE 'MENU_[0-9]%'
  AND f.FeatureKey NOT LIKE '%[_]VIEW' AND f.FeatureKey NOT LIKE '%[_]ADD'
  AND f.FeatureKey NOT LIKE '%[_]EDIT' AND f.FeatureKey NOT LIKE '%[_]DELETE'
  AND ISNUMERIC(SUBSTRING(f.FeatureKey, 6, LEN(f.FeatureKey) - 5)) = 1
  AND m.IsActive = 1;

-- ─── Build the full list including parent menus ───────────────────────────
-- We use a recursive CTE to walk up the parent chain for each menu.

WITH ParentChain AS (
    -- Anchor: the directly granted menus
    SELECT
        r.PersonId,
        m.Id        AS MenuId,
        m.ParentId
    FROM @repair r
    INNER JOIN Menus m ON m.Id = r.MenuId

    UNION ALL

    -- Recursive: climb to parent
    SELECT
        pc.PersonId,
        p.Id        AS MenuId,
        p.ParentId
    FROM ParentChain pc
    INNER JOIN Menus p ON p.Id = pc.ParentId
    WHERE pc.ParentId IS NOT NULL
)
-- Insert missing PersonMenus rows
INSERT INTO PersonMenus (PersonId, MenuId, GrantedBy, GrantedOnUtc)
SELECT DISTINCT
    pc.PersonId,
    pc.MenuId,
    'repair-script',
    GETUTCDATE()
FROM ParentChain pc
WHERE NOT EXISTS (
    SELECT 1
    FROM PersonMenus pm
    WHERE pm.PersonId = pc.PersonId
      AND pm.MenuId   = pc.MenuId
);

DECLARE @inserted INT = @@ROWCOUNT;
PRINT '';
PRINT 'Inserted ' + CAST(@inserted AS VARCHAR(10)) + ' PersonMenus row(s).';

-- ─── Diagnostics after ────────────────────────────────────────────────────
PRINT '';
PRINT 'After repair:';
SELECT 'PersonMenus rows'    AS [Table], COUNT(*) AS [Rows] FROM PersonMenus
UNION ALL
SELECT 'PersonFeatures rows' AS [Table], COUNT(*) AS [Rows] FROM PersonFeatures;

-- ─── Show which persons were repaired ─────────────────────────────────────
PRINT '';
PRINT 'Persons with PersonMenus (should now see sidebar):';
SELECT
    p.FullName,
    p.Email,
    COUNT(pm.MenuId) AS [Menus Granted]
FROM PersonMenus pm
INNER JOIN Persons p ON p.PersonId = pm.PersonId
GROUP BY p.FullName, p.Email
ORDER BY p.FullName;

PRINT '';
PRINT '════════════════════════════════════════════════════════════════';
PRINT 'REPAIR COMPLETE.';
PRINT 'Ask affected users to log out and log back in.';
PRINT 'Their granted menus will now appear in the sidebar.';
PRINT '════════════════════════════════════════════════════════════════';
