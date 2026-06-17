-- ============================================================
-- FIX: Backfill TenantId on AspNetUsers for staff whose
--      Person record has a TenantId but the user account does not.
--
-- This fixes pre-tenant staff who were registered before the
-- multi-tenant migration. Their Person.TenantId is correct but
-- AspNetUsers.TenantId was never populated.
--
-- Run ONCE on the database.
-- ============================================================

UPDATE u
SET    u.TenantId = p.TenantId
FROM   AspNetUsers   u
JOIN   Persons       p ON p.IdentityUserId = u.Id
WHERE  u.TenantId IS NULL
  AND  p.TenantId IS NOT NULL
  AND  u.IsSuperAdmin = 0
  AND  u.IsTenantAdmin = 0;

PRINT CAST(@@ROWCOUNT AS VARCHAR) + ' user(s) backfilled with TenantId from Person record.';

-- Also verify
SELECT u.UserName, u.TenantId, p.TenantId as PersonTenantId, t.TenantName
FROM   AspNetUsers u
JOIN   Persons     p ON p.IdentityUserId = u.Id
LEFT JOIN Tenants  t ON t.Id = u.TenantId
WHERE  u.IsSuperAdmin = 0
  AND  u.IsTenantAdmin = 0
ORDER BY u.UserName;
