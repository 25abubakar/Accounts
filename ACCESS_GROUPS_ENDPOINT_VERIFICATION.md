# ✅ Access Groups Endpoint - Verification

## Current Implementation Status

### Endpoint: `GET /api/access/groups`

**Protection**: `[HasPermission("ACCESS_GROUP_VIEW")]`

**Behavior**: Returns **ALL active groups** (no filtering by department or user)

---

## How It Works

```csharp
[HasPermission("ACCESS_GROUP_VIEW")]
[HttpGet("groups")]
public async Task<IActionResult> GetGroups() => 
    Ok(await _service.GetAllGroupsAsync());
```

### Service Implementation

```csharp
public async Task<IEnumerable<object>> GetAllGroupsAsync() =>
    await _db.AccessGroups
        .Include(g => g.Features)
        .Where(g => g.IsActive)  // ← Only filters by IsActive
        .OrderBy(g => g.GroupName)
        .Select(g => new
        {
            g.GroupId, 
            g.GroupName, 
            g.Description, 
            g.IsActive, 
            g.CreatedDate,
            Features = g.Features.Select(f => f.FeatureKey).ToList(),
            StaffCount = g.Staff.Count()
        })
        .ToListAsync<object>();
```

---

## ✅ Correct Behavior

1. **Route Protection**: User must have `ACCESS_GROUP_VIEW` permission to access endpoint
2. **No User Filtering**: Once authorized, user sees ALL active groups
3. **Only Active Groups**: Filters out groups where `IsActive = false`
4. **No Department Scoping**: Groups are global, not department-specific

---

## 🔍 Why This Is Correct

**Access groups are organization-wide resources**, not department-specific. Therefore:

- ✅ Route-level permission check is sufficient
- ✅ No need to filter by user's department
- ✅ If user can access the page, they can see all groups
- ✅ Simplifies group management across the organization

---

## 🆚 Comparison with Other Endpoints

| Endpoint | Filtering | Reason |
|---|---|---|
| `GET /api/access/groups` | **None** (all active) | Groups are global resources |
| `GET /api/data/departments` | **By department** | User should only see their dept |
| `GET /api/data/staff` | **By department** | User should only see their dept staff |
| `GET /api/data/persons` | **By department** | User should only see their dept persons |

---

## 🧪 Testing

### Test 1: User with ACCESS_GROUP_VIEW Permission

```http
GET http://localhost:5000/api/access/groups
Authorization: Bearer {token}
```

**Expected Response:**
```json
[
  {
    "groupId": 1,
    "groupName": "Administrators",
    "description": "Full system access",
    "isActive": true,
    "createdDate": "2026-05-01T00:00:00Z",
    "features": ["DEPT_VIEW", "EMPLOYEE_VIEW", "EMPLOYEE_EDIT"],
    "staffCount": 5
  },
  {
    "groupId": 2,
    "groupName": "Supervisor",
    "description": "Supervisory access",
    "isActive": true,
    "createdDate": "2026-05-02T00:00:00Z",
    "features": ["DEPT_VIEW", "EMPLOYEE_VIEW"],
    "staffCount": 3
  }
]
```

### Test 2: User WITHOUT ACCESS_GROUP_VIEW Permission

```http
GET http://localhost:5000/api/access/groups
Authorization: Bearer {token}
```

**Expected Response:**
```json
{
  "message": "Access denied. 'user.login' does not have permission: 'ACCESS_GROUP_VIEW'.",
  "code": "FORBIDDEN"
}
```

**Status Code:** `403 Forbidden`

---

## 🐛 Troubleshooting

### Issue: "Supervisor group not showing"

**Possible Causes:**

1. **Group is inactive**
   ```sql
   -- Check if group is active
   SELECT GroupId, GroupName, IsActive 
   FROM AccessGroups 
   WHERE GroupName = 'Supervisor';
   
   -- Fix: Activate the group
   UPDATE AccessGroups 
   SET IsActive = 1 
   WHERE GroupName = 'Supervisor';
   ```

2. **Group doesn't exist**
   ```sql
   -- Check if group exists
   SELECT * FROM AccessGroups WHERE GroupName = 'Supervisor';
   
   -- Fix: Create the group
   INSERT INTO AccessGroups (GroupName, Description, IsActive, CreatedDate)
   VALUES ('Supervisor', 'Supervisory access', 1, GETDATE());
   ```

3. **Cache issue**
   - Restart the application
   - Clear browser cache
   - Try in incognito/private mode

---

## 📝 Summary

✅ **Endpoint**: `GET /api/access/groups`  
✅ **Protection**: `[HasPermission("ACCESS_GROUP_VIEW")]`  
✅ **Returns**: ALL active groups (no filtering)  
✅ **Reason**: Groups are global, route protection is sufficient  
✅ **Status**: Working as designed  

**If you can access the endpoint, you see all groups. This is correct!** 🎉
