# 🚀 Quick Start Guide - Accounts System

**Last Updated:** June 4, 2026

---

## ⚡ 5-Minute Setup

### Step 1: Database Setup (2 minutes)

```sql
-- 1. Run migration script
USE YourDatabase;
GO

-- Execute the migration file
:r Accounts\Database\MIGRATION_RBAC_Refactor.sql
GO

-- 2. Verify Features table exists
SELECT COUNT(*) FROM Features;
-- Expected: 0 rows (empty, will be seeded next)
```

### Step 2: Seed Features (1 minute)

```bash
# Call the seed endpoint (Postman or curl)
curl -X POST https://localhost:7015/api/rbac/seed-features

# Expected response:
{
  "message": "Seed complete.",
  "menuFeatures": { "added": 45, "skipped": 0 },
  "staticFeatures": { "added": 28, "skipped": 0 },
  "totalFeatures": 73
}
```

### Step 3: Start Backend (30 seconds)

```bash
cd Accounts
dotnet run

# Backend should start on https://localhost:7015
```

### Step 4: Start Frontend (1.5 minutes)

```bash
cd Frontend/Frontend-Accounts-main

# Install dependencies (first time only)
npm install

# Start dev server
npm run dev

# Frontend should start on http://localhost:5173
```

### Step 5: Login & Test (30 seconds)

1. Open browser: `http://localhost:5173`
2. Login with existing credentials
3. Dashboard should render **instantly** (<0.5s)
4. Sidebar menus appear immediately

---

## 🎯 Common Tasks

### Task 1: Grant User Access (Admin)

```typescript
// 1. Get all users
GET /api/rbac/users

// 2. Get user's current permissions
GET /api/rbac/staff/{staffId}/permissions-summary

// 3. Bulk save permissions
POST /api/rbac/staff/{staffId}/bulk-overrides
{
  "MENU_1": "ALLOW",
  "MENU_1_VIEW": "ALLOW",
  "EMPLOYEE_VIEW": "ALLOW",
  "EMPLOYEE_EDIT": "DENY"
}
```

**Frontend (AdminAccessPage.tsx):**
1. Navigate to `/access/admin-access`
2. Select staff member from dropdown
3. Toggle permissions in grid
4. Click "Save Changes"

---

### Task 2: Check User Permissions

**Backend:**
```csharp
// In any controller
var hasAccess = await _rbac.HasAccessAsync(staffId, "EMPLOYEE_EDIT");
if (!hasAccess)
    return Forbidden(new { message = "Access denied" });
```

**Frontend:**
```typescript
// In any component
const { hasPermission } = useAuth();

if (!hasPermission('EMPLOYEE_EDIT')) {
  return <NoAccessMessage />;
}

// Or before action
const handleEdit = () => {
  if (!hasPermission('EMPLOYEE_EDIT')) {
    toast.error('You do not have permission');
    return;
  }
  // Proceed
};
```

---

### Task 3: Add New Feature Key

**Step 1: Add to Features table**
```sql
INSERT INTO Features (FeatureKey, FeatureName, Module)
VALUES ('REPORTS_VIEW', 'View Reports', 'Reports');
```

**Step 2: Update frontend constants (optional)**
```typescript
// Frontend/src/utils/featureKeys.ts
export const FEATURE_KEYS = {
  // ... existing keys
  REPORTS_VIEW: 'REPORTS_VIEW',
} as const;
```

**Step 3: Use in code**
```typescript
// Check permission
if (hasPermission('REPORTS_VIEW')) {
  // Show reports section
}

// Protect route
<Route 
  path="/reports" 
  element={<RequirePermission feature="REPORTS_VIEW"><ReportsPage /></RequirePermission>} 
/>
```

---

### Task 4: Add New Menu Item

**Step 1: Insert menu**
```sql
INSERT INTO Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
VALUES ('Reports', 'chart-bar', '/reports', NULL, 100, 1);

-- Get the new menu ID
SELECT SCOPE_IDENTITY() AS NewMenuId;
```

**Step 2: Link to feature**
```sql
-- Assuming NewMenuId = 50 and PermissionId for REPORTS_VIEW = 150
INSERT INTO MenuPermissions (MenuId, PermissionId)
VALUES (50, 150);
```

**Step 3: Re-seed to generate MENU_50 keys**
```bash
curl -X POST https://localhost:7015/api/rbac/seed-features
```

**Step 4: Grant to users**
```sql
-- Grant to all Managers
INSERT INTO RolePermissions (JobTitle, PermissionId, IsAllowed)
VALUES ('Manager', 150, 1);

-- Or grant to specific user
POST /api/rbac/staff/{staffId}/bulk-overrides
{ "MENU_50": "ALLOW", "MENU_50_VIEW": "ALLOW" }
```

---

## 🔍 Debugging Checklist

### Problem: User sees no menus after login

```sql
-- 1. Check if user is hired
SELECT p.FullName, s.StaffId, s.LoginId
FROM Persons p
LEFT JOIN StaffVacancies s ON p.PersonId = s.PersonId
WHERE p.Email = 'user@example.com';

-- 2. Check user overrides
SELECT f.FeatureKey, upo.Status
FROM UserPermissionOverrides upo
JOIN Features f ON upo.PermissionId = f.PermissionId
WHERE upo.StaffId = 'staff-guid-here';

-- 3. Check role permissions
SELECT f.FeatureKey, rp.IsAllowed
FROM RolePermissions rp
JOIN Features f ON rp.PermissionId = f.PermissionId
WHERE rp.JobTitle = 'Manager';
```

### Problem: Slow login (>2 seconds)

```sql
-- Check for N+1 queries
-- Enable SQL Profiler and look for:
-- ❌ BAD: 100+ individual SELECT queries
-- ✅ GOOD: 5-8 bulk queries with WHERE IN clauses

-- Verify indexes exist
SELECT 
    i.name AS IndexName,
    t.name AS TableName
FROM sys.indexes i
JOIN sys.tables t ON i.object_id = t.object_id
WHERE t.name IN ('UserPermissionOverrides', 'RolePermissions', 'MenuPermissions')
  AND i.name IS NOT NULL;
```

### Problem: Frontend shows "Network Error"

```bash
# 1. Check backend is running
curl https://localhost:7015/api/health

# 2. Check .env file
cat Frontend/Frontend-Accounts-main/.env
# Should show: VITE_API_URL=https://localhost:7015

# 3. Check browser console for CORS errors
# Open DevTools → Console tab

# 4. Verify JWT token is being sent
# Open DevTools → Network tab → Select request → Headers
# Should see: Authorization: Bearer <token>
```

---

## 📊 Performance Benchmarks

### Expected Performance (After Optimization)

| Metric | Target | Acceptable | Poor |
|--------|--------|------------|------|
| **Login Response** | <0.5s | <1s | >2s |
| **Database Queries** | 5-8 | <15 | >50 |
| **Dashboard Render** | <100ms | <500ms | >1s |
| **Memory Usage** | <50MB | <100MB | >200MB |

### How to Measure

**Backend (C#):**
```csharp
var sw = Stopwatch.StartNew();
var result = await _rbac.GetEffectivePermissionIdsAsync(staffId);
sw.Stop();
_logger.LogInformation($"Permission resolution took {sw.ElapsedMilliseconds}ms");
```

**Frontend (React):**
```typescript
console.time('my-menus');
const response = await authApi.getMyMenus();
console.timeEnd('my-menus');
// Should log: "my-menus: 300-500ms"
```

**Database (SQL Profiler):**
```sql
-- Enable query statistics
SET STATISTICS TIME ON;
SET STATISTICS IO ON;

-- Run your query
EXEC GetEffectivePermissions @StaffId = 'guid-here';

-- Check execution plan (should use indexes)
```

---

## 🎯 Quick Reference - API Endpoints

### Authentication Flow
```
1. POST /api/auth/login          → Get JWT token
2. GET  /api/auth/my-menus       → Get menus + permissions (FAST)
3. GET  /api/auth/session        → Get session metadata (background)
```

### Admin Permission Management
```
1. GET  /api/rbac/users                              → List all staff
2. GET  /api/rbac/staff/{staffId}/permissions-summary → Load permissions
3. POST /api/rbac/staff/{staffId}/bulk-overrides     → Save changes
```

### Runtime Permission Checks
```
✅ Frontend:  hasPermission('EMPLOYEE_EDIT')  // In-memory, instant
❌ Avoid:     GET /api/rbac/has-access        // Extra API call, slow
```

---

## 🔐 Security Best Practices

### 1. Never Store Permissions in Frontend
```typescript
// ❌ BAD: Hardcoded logic
if (user.role === 'Admin') {
  // Show admin features
}

// ✅ GOOD: Check against server-provided permissions
if (hasPermission('ADMIN_PANEL')) {
  // Show admin features
}
```

### 2. Always Validate on Backend
```csharp
// ✅ GOOD: Backend validates EVERY request
[HttpPost("employees/{id}")]
public async Task<IActionResult> UpdateEmployee(Guid id, EmployeeDto dto)
{
    var staffId = GetCurrentStaffId();
    
    // Check permission before processing
    if (!await _rbac.HasAccessAsync(staffId, "EMPLOYEE_EDIT"))
        return Forbid();
    
    // Proceed with update
    return Ok(await _service.UpdateEmployee(id, dto));
}
```

### 3. Use HTTPS in Production
```env
# Production .env
VITE_API_URL=https://accounts-api.yourdomain.com

# Never use HTTP in production (insecure)
```

---

## 📚 Essential Files Reference

### Backend
- **`AuthController.cs`** - Login, my-menus endpoint
- **`RbacController.cs`** - Permission management endpoints
- **`RbacService.cs`** - Core permission resolution logic
- **`Features` table** - Master permission list

### Frontend
- **`AuthContext.tsx`** - Auth state management
- **`rbacApi.ts`** - RBAC API client
- **`featureKeys.ts`** - Permission constants
- **`AdminAccessPage.tsx`** - Admin permission UI

### Database
- **`MIGRATION_RBAC_Refactor.sql`** - Schema migration script
- **`Features`** - Master permission table
- **`UserPermissionOverrides`** - User-specific overrides
- **`RolePermissions`** - Job title defaults

---

## 🆘 Emergency Contacts

**Issue: Login broken for all users**
1. Check backend logs for exceptions
2. Verify database connection string
3. Test `/api/health` endpoint
4. Rollback recent deployments if needed

**Issue: Permission changes not saving**
1. Check admin has correct role (SuperAdmin/Admin)
2. Verify Features table is seeded
3. Check foreign key constraints
4. Review UserPermissionOverrides table

**Issue: Performance degradation**
1. Check SQL Profiler for N+1 queries
2. Verify indexes exist and are used
3. Check server CPU/memory usage
4. Review recent code changes

---

## ✅ Pre-Deployment Checklist

- [ ] Database migration completed successfully
- [ ] Features table seeded (73+ features)
- [ ] Backend runs without errors
- [ ] Frontend builds successfully (`npm run build`)
- [ ] Login flow tested end-to-end
- [ ] Admin permission assignment tested
- [ ] Performance measured (<0.5s login)
- [ ] Database indexes verified
- [ ] CORS configured correctly
- [ ] HTTPS enabled in production
- [ ] Environment variables updated
- [ ] Backup of production database taken

---

## 🎉 Success Indicators

✅ **User logs in** → Dashboard renders in <0.5s  
✅ **Sidebar appears** → No loading spinner, instant render  
✅ **Permission check** → Instant response from hasPermission()  
✅ **Admin saves permissions** → Changes apply immediately  
✅ **Database queries** → 5-8 queries per login (check logs)  
✅ **No errors** → Browser console is clean  
✅ **Build passes** → `npm run build` completes successfully  

---

**Ready to go? Start with Step 1: Database Setup ⬆️**

For detailed documentation, see:
- [API_DOCUMENTATION.md](./API_DOCUMENTATION.md)
- [IMPLEMENTATION_COMPLETE.md](./IMPLEMENTATION_COMPLETE.md)
- [RBAC_REFACTOR_README.md](./Accounts/Database/RBAC_REFACTOR_README.md)
