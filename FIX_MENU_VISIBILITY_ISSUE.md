# 🔧 Fix: User Has Permissions But No Menus Show

**Issue:** Admin grants user 84% access (16 menus), but when user logs in, sidebar is empty.

**Root Cause:** `MenuPermissions` table is empty — no link between Menus and Features.

---

## 🎯 Quick Fix (3 Steps)

### Step 1: Link Menus to Features (Backend)

**Option A: Using API Endpoint (Recommended)**
```bash
# This automatically links all active menus to their MENU_{id} features
curl -X POST https://localhost:7015/api/rbac/link-menus-to-features
```

**Option B: Using SQL Script**
```sql
-- Run this script if backend is not running
:r Accounts\Database\FIX_MENU_PERMISSIONS.sql
```

### Step 2: Verify MenuPermissions Table

```sql
-- Check that menus are now linked to features
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
ORDER BY m.SortOrder;
```

**Expected Result:**
```
Id | Title      | Route         | FeatureKey | FeatureName
---|------------|---------------|------------|-------------
1  | Dashboard  | /dashboard    | MENU_1     | Dashboard Menu
2  | HR         | NULL          | MENU_2     | HR Menu
3  | Staff      | /hr/staff     | MENU_3     | Staff Menu
...
```

### Step 3: User Login Test

1. User logs out (if already logged in)
2. User logs back in
3. **Result:** Sidebar should now show the 16 granted menus

---

## 🔍 Detailed Diagnosis

### Check 1: Does User Have Permissions?

```sql
-- Check user's granted permissions
DECLARE @StaffId UNIQUEIDENTIFIER = 'YOUR-STAFF-GUID-HERE';

SELECT 
    f.FeatureKey,
    f.FeatureName,
    upo.Status,
    upo.SetDate
FROM UserPermissionOverrides upo
INNER JOIN Features f ON f.PermissionId = upo.PermissionId
WHERE upo.StaffId = @StaffId
  AND upo.Status = 'ALLOW'
ORDER BY f.FeatureKey;
```

**Expected:** Should show 16+ features with Status = 'ALLOW'

### Check 2: Are Menus Linked to Features?

```sql
-- Check MenuPermissions table
SELECT COUNT(*) AS [LinkedMenus]
FROM MenuPermissions;
```

**Problem If:** Returns 0 (empty table)  
**Solution:** Run Step 1 above

### Check 3: What Does API Return?

**Test Endpoint:**
```bash
# Replace {staffId} with actual GUID
curl -X GET "https://localhost:7015/api/auth/my-menus" \
  -H "Authorization: Bearer YOUR-JWT-TOKEN"
```

**Check Response:**
```json
{
  "status": true,
  "isFullAccess": false,
  "staffId": "guid-here",
  "menus": [],  // ❌ EMPTY = PROBLEM
  "permissions": ["MENU_1", "MENU_2", ...],  // ✅ HAS PERMISSIONS
  "permissionDetails": [...]
}
```

**If `menus` is empty but `permissions` has values:**
→ MenuPermissions table is not populated → Run Step 1

---

## 🛠️ Permanent Fix (For New Menus)

When adding new menus in the future, ensure they're automatically linked:

### Option 1: Update Seed Endpoint

The seed endpoint now automatically links menus:

```bash
curl -X POST https://localhost:7015/api/rbac/seed-features
```

**Response:**
```json
{
  "message": "Seed complete.",
  "menuFeatures": { "added": 45, "skipped": 0 },
  "staticFeatures": { "added": 28, "skipped": 0 },
  "menuPermissionsLinked": 45,  // ✅ New field
  "totalFeatures": 73
}
```

### Option 2: Automatic Linking in Code

Add this to your menu creation code:

```csharp
// After creating a new menu
var menu = new Menu { Title = "New Menu", ... };
_db.Menus.Add(menu);
await _db.SaveChangesAsync();

// Get the menu's PermissionId from Features table
var feature = await _db.Features
    .FirstOrDefaultAsync(f => f.FeatureKey == $"MENU_{menu.Id}");

if (feature != null)
{
    // Link menu to feature
    _db.MenuPermissions.Add(new MenuPermission
    {
        MenuId = menu.Id,
        PermissionId = feature.PermissionId
    });
    await _db.SaveChangesAsync();
}
```

---

## 📊 Complete Workflow (Admin to User)

### 1. Admin Grants Access

```
Admin opens /access/admin-access
   ↓
Selects user "Muhammad Farooq"
   ↓
Toggles menu permissions:
  ✅ MENU_1 (Dashboard)
  ✅ MENU_2 (HR)
  ✅ MENU_3 (Staff)
  ... (16 total)
   ↓
Clicks "Save Changes"
   ↓
POST /api/rbac/staff/{staffId}/bulk-overrides
   ↓
Writes to UserPermissionOverrides table
```

### 2. Backend Linking (Required)

```
MenuPermissions table links menus to features:
┌────────┬──────────────┐
│ MenuId │ PermissionId │
├────────┼──────────────┤
│ 1      │ 1            │  ← MENU_1
│ 2      │ 2            │  ← MENU_2
│ 3      │ 3            │  ← MENU_3
└────────┴──────────────┘

If this table is EMPTY, menus won't appear!
```

### 3. User Logs In

```
User enters credentials
   ↓
POST /api/auth/login
   ↓
GET /api/auth/my-menus
   ↓
Backend queries:
  1. UserPermissionOverrides → Gets allowed PermissionIds
  2. MenuPermissions → Links MenuIds to PermissionIds
  3. Menus → Filters menus by allowed PermissionIds
   ↓
Returns filtered menu tree
   ↓
Frontend renders sidebar
```

---

## 🚨 Troubleshooting

### Problem: User still sees no menus after Step 1

**Check A: User is actually hired**
```sql
SELECT 
    p.FullName,
    p.Email,
    s.StaffId,
    s.LoginId,
    v.JobTitle
FROM Persons p
LEFT JOIN StaffVacancies s ON s.PersonId = p.PersonId
LEFT JOIN Vacancies v ON v.VacancyId = s.VacancyId
WHERE p.Email = 'user@example.com';
```

**Expected:** StaffId should NOT be NULL

**Check B: User has MENU_* permissions**
```sql
-- Should return 16 rows (matching the 84% / 16 menus in admin UI)
SELECT COUNT(*)
FROM UserPermissionOverrides upo
INNER JOIN Features f ON f.PermissionId = upo.PermissionId
WHERE upo.StaffId = 'staff-guid-here'
  AND upo.Status = 'ALLOW'
  AND f.FeatureKey LIKE 'MENU_%';
```

**Check C: MenuPermissions is populated**
```sql
-- Should return > 0
SELECT COUNT(*) FROM MenuPermissions;
```

**If COUNT = 0:** Run the fix again (Step 1)

---

### Problem: Some menus show, but not all

**Check:** Feature keys might not match

```sql
-- Find granted MENU_* features that have no corresponding menu
SELECT f.FeatureKey, f.FeatureName
FROM Features f
INNER JOIN UserPermissionOverrides upo ON upo.PermissionId = f.PermissionId
WHERE upo.StaffId = 'staff-guid-here'
  AND upo.Status = 'ALLOW'
  AND f.FeatureKey LIKE 'MENU_%'
  AND NOT EXISTS (
      SELECT 1 
      FROM MenuPermissions mp 
      WHERE mp.PermissionId = f.PermissionId
  );
```

**Solution:** These features exist but have no menu link. Either:
1. Delete the orphaned features, OR
2. Create the missing menus

---

### Problem: Dashboard shows but is empty

**This is normal!** Dashboard content is separate from sidebar menus.

**Check:** Does user have permissions for dashboard widgets?

```sql
-- Check if user has permissions for dashboard features
SELECT f.FeatureKey, f.FeatureName
FROM UserPermissionOverrides upo
INNER JOIN Features f ON f.PermissionId = upo.PermissionId
WHERE upo.StaffId = 'staff-guid-here'
  AND upo.Status = 'ALLOW'
  AND (f.FeatureKey LIKE '%_VIEW%' OR f.Module = 'Dashboard');
```

---

## ✅ Verification Checklist

After applying the fix:

- [ ] MenuPermissions table is populated (COUNT > 0)
- [ ] User has permissions in UserPermissionOverrides
- [ ] User is hired (has StaffId in StaffVacancies)
- [ ] Backend responds with menus in `/api/auth/my-menus`
- [ ] Frontend sidebar renders menus
- [ ] Menu items are clickable and routes work
- [ ] User can navigate to granted pages
- [ ] Pages without permission show "Access Denied"

---

## 🔧 Quick SQL Fixes

### Reset User's Permissions (Start Fresh)

```sql
-- Remove all existing permissions for user
DELETE FROM UserPermissionOverrides
WHERE StaffId = 'staff-guid-here';

-- Grant all menus
INSERT INTO UserPermissionOverrides (StaffId, PermissionId, Status, SetBy, SetDate, Reason)
SELECT 
    'staff-guid-here',
    f.PermissionId,
    'ALLOW',
    'admin-user-id-here',
    GETUTCDATE(),
    'Manual grant via SQL'
FROM Features f
WHERE f.FeatureKey LIKE 'MENU_%';
```

### Make Dashboard Public (Visible to All)

```sql
-- Remove permission requirement from Dashboard menu
DELETE mp
FROM MenuPermissions mp
INNER JOIN Menus m ON m.Id = mp.MenuId
WHERE m.Title = 'Dashboard' OR m.Route = '/dashboard';
```

---

## 📞 Still Having Issues?

### Debug Logs

Enable backend logging to see what's happening:

```csharp
// In AuthController.GetMyMenus()
_logger.LogInformation($"User {identityUserId} has {allowedIds.Count} allowed permission IDs");
_logger.LogInformation($"Loaded {allMenus.Count} total menus from database");
_logger.LogInformation($"Filtered sidebar has {sidebar.Count} menu items");
```

### Frontend Debug

```typescript
// In AuthContext.tsx
console.log('my-menus response:', response);
console.log('menus count:', response.menus.length);
console.log('permissions count:', response.permissions.length);
```

### Database Profiler

Run SQL Profiler during user login to see actual queries:

```sql
-- Should see these queries (no loops!):
SELECT * FROM Persons WHERE IdentityUserId = '...'
SELECT * FROM UserPermissionOverrides WHERE StaffId = '...'
SELECT * FROM Features WHERE PermissionId IN (...)
SELECT * FROM Menus WHERE IsActive = 1
SELECT * FROM MenuPermissions WHERE MenuId IN (...)
```

---

## 📚 Related Documentation

- [API_DOCUMENTATION.md](./API_DOCUMENTATION.md) - API reference
- [QUICK_START.md](./QUICK_START.md) - Setup guide
- [FIX_MENU_PERMISSIONS.sql](./Accounts/Database/FIX_MENU_PERMISSIONS.sql) - SQL fix script

---

**Last Updated:** June 4, 2026  
**Status:** ✅ Fix Verified and Tested
