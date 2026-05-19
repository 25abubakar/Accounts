# Permission-Filtered Data Access API

## Overview

This API provides endpoints that **automatically filter data based on the logged-in user's permissions**. 

**Key Principle**: If a user doesn't have permission to view certain data, that data is **NOT returned** in the response.

---

## 🔐 How It Works

1. **User logs in** → System identifies their `StaffId`
2. **User requests data** → System checks their permissions via RBAC
3. **System filters data** → Only returns data they have access to
4. **Frontend receives** → Clean, pre-filtered data ready to display

---

## 📋 Permission Keys Used

| Permission Key | Description |
|---|---|
| `DEPT_VIEW` | Can view departments |
| `DEPT_VIEW_ALL` | Can view ALL departments (not just own) |
| `EMPLOYEE_VIEW` | Can view staff/employees |
| `EMPLOYEE_VIEW_ALL` | Can view ALL staff (not just own department) |
| `PERSON_VIEW` | Can view persons |
| `PERSON_VIEW_ALL` | Can view ALL persons (not just own department) |
| `VACANCY_VIEW` | Can view vacancies |
| `VACANCY_VIEW_ALL` | Can view ALL vacancies (not just own department) |
| `ACCESS_GROUP_VIEW` | Can view access groups |

---

## 🚀 API Endpoints

### 1. Get All Accessible Data

**Endpoint**: `GET /api/data/accessible`

**Description**: Returns ALL data the current user has permission to view in one response.

**Authentication**: Required (Bearer token or cookie)

**Response**:
```json
{
  "staffId": "70F6690C-2874-4DD1-AEC3-5EDEF44CF138",
  "permissions": [
    "DEPT_VIEW",
    "EMPLOYEE_VIEW",
    "PERSON_VIEW"
  ],
  "data": {
    "departments": [
      {
        "organizationId": 4,
        "organizationName": "IT Department",
        "organizationType": "Department",
        "parentId": 1,
        "level": 2,
        "flagUrl": null,
        "isActive": true
      }
    ],
    "staff": [
      {
        "staffId": "70F6690C-2874-4DD1-AEC3-5EDEF44CF138",
        "fullName": "John Doe",
        "email": "john@example.com",
        "phoneNumber": "+1234567890",
        "photoUrl": null,
        "personId": 123,
        "loginId": "john.doe",
        "vacancy": {
          "vacancyId": "ABC123",
          "vacancyCode": "IT-001",
          "jobTitle": "Software Engineer",
          "organizationId": 4,
          "organizationName": "IT Department"
        }
      }
    ],
    "persons": [],
    "vacancies": [],
    "accessGroups": []
  }
}
```

**Notes**:
- Empty arrays mean user doesn't have permission for that data type
- Only shows data from user's department unless they have `*_VIEW_ALL` permission

---

### 2. Get My Permissions

**Endpoint**: `GET /api/data/my-permissions`

**Description**: Returns all feature permissions the current user has.

**Response**:
```json
{
  "staffId": "70F6690C-2874-4DD1-AEC3-5EDEF44CF138",
  "permissions": [
    "DEPT_VIEW",
    "EMPLOYEE_EDIT",
    "EMPLOYEE_VIEW",
    "PERSON_REGISTER",
    "PERSON_VIEW"
  ],
  "totalCount": 5
}
```

---

### 3. Check Specific Permission

**Endpoint**: `GET /api/data/can-access/{featureKey}`

**Description**: Check if current user has access to a specific feature.

**Example**: `GET /api/data/can-access/EMPLOYEE_EDIT`

**Response**:
```json
{
  "staffId": "70F6690C-2874-4DD1-AEC3-5EDEF44CF138",
  "featureKey": "EMPLOYEE_EDIT",
  "hasAccess": true
}
```

---

### 4. Get Accessible Departments

**Endpoint**: `GET /api/data/departments`

**Description**: Returns only departments the user can view.

**Response**:
```json
[
  {
    "organizationId": 4,
    "organizationName": "IT Department",
    "organizationType": "Department",
    "parentId": 1,
    "level": 2,
    "flagUrl": null,
    "isActive": true
  }
]
```

**Logic**:
- If user has `DEPT_VIEW_ALL` → returns ALL departments
- If user has only `DEPT_VIEW` → returns ONLY their own department
- If user has neither → returns empty array `[]`

---

### 5. Get Accessible Staff

**Endpoint**: `GET /api/data/staff`

**Description**: Returns only staff members the user can view.

**Response**:
```json
[
  {
    "staffId": "70F6690C-2874-4DD1-AEC3-5EDEF44CF138",
    "fullName": "John Doe",
    "email": "john@example.com",
    "phoneNumber": "+1234567890",
    "photoUrl": null,
    "personId": 123,
    "loginId": "john.doe",
    "vacancy": {
      "vacancyId": "ABC123",
      "vacancyCode": "IT-001",
      "jobTitle": "Software Engineer",
      "organizationId": 4,
      "organizationName": "IT Department"
    }
  }
]
```

**Logic**:
- If user has `EMPLOYEE_VIEW_ALL` → returns ALL staff
- If user has only `EMPLOYEE_VIEW` → returns staff from their department only
- If user has neither → returns empty array `[]`

---

### 6. Get Accessible Persons

**Endpoint**: `GET /api/data/persons`

**Description**: Returns only persons the user can view.

**Response**:
```json
[
  {
    "personId": 123,
    "fullName": "Jane Smith",
    "loginId": "jane.smith",
    "email": "jane@example.com",
    "phoneNumber": "+1234567890",
    "profilePhotoUrl": null,
    "branchId": 4,
    "branchName": "IT Department",
    "isHired": false,
    "staffId": null,
    "jobTitle": null
  }
]
```

**Logic**:
- If user has `PERSON_VIEW_ALL` → returns ALL persons
- If user has only `PERSON_VIEW` → returns persons from their department only
- If user has neither → returns empty array `[]`

---

## 🎯 Frontend Integration

### React Example

```typescript
// Get all accessible data
const response = await fetch('http://localhost:5000/api/data/accessible', {
  credentials: 'include', // Important for cookie auth
  headers: {
    'Content-Type': 'application/json'
  }
});

const data = await response.json();

// data.permissions = array of permission keys user has
// data.data.departments = departments user can see
// data.data.staff = staff user can see
// data.data.persons = persons user can see
```

### Check Permission Before Showing UI

```typescript
// Check if user can edit employees
const canEdit = await fetch('http://localhost:5000/api/data/can-access/EMPLOYEE_EDIT', {
  credentials: 'include'
});

const result = await canEdit.json();

if (result.hasAccess) {
  // Show edit button
} else {
  // Hide edit button
}
```

---

## 🔒 Security Features

1. **Authentication Required**: All endpoints require user to be logged in
2. **SuperAdmin Bypass**: SuperAdmin users should use specific endpoints (not filtered)
3. **Automatic Filtering**: No way for frontend to bypass permission checks
4. **Department Scoping**: Users only see data from their department by default
5. **Empty Arrays**: If no permission, returns `[]` instead of error (cleaner UX)

---

## 🧪 Testing

### Test User with Limited Permissions

```bash
# Login as regular user
POST /api/auth/login
{
  "email": "john@example.com",
  "password": "Password123!"
}

# Get accessible data
GET /api/data/accessible
# Should only return data from user's department
```

### Test User with Full Permissions

```bash
# Login as manager with VIEW_ALL permissions
POST /api/auth/login
{
  "email": "manager@example.com",
  "password": "Password123!"
}

# Get accessible data
GET /api/data/accessible
# Should return ALL departments, staff, persons
```

---

## 📊 Permission Matrix Example

| User | DEPT_VIEW | DEPT_VIEW_ALL | Result |
|---|---|---|---|
| John (IT Staff) | ✅ | ❌ | Sees only IT Department |
| Sarah (Manager) | ✅ | ✅ | Sees ALL Departments |
| Bob (No Permission) | ❌ | ❌ | Sees nothing (empty array) |

---

## 🚨 Error Responses

### 401 Unauthorized
```json
{
  "message": "Cannot resolve user identity."
}
```

### 403 Forbidden
```json
{
  "message": "User is not assigned to a staff position."
}
```

---

## 💡 Best Practices

1. **Use `/api/data/accessible` on app load** to get all data at once
2. **Cache permissions** in frontend state to avoid repeated checks
3. **Show/hide UI elements** based on permissions array
4. **Don't make assumptions** - if data is empty, user doesn't have access
5. **Handle 401/403 errors** gracefully by redirecting to login

---

## 🔄 Comparison: Old vs New

### ❌ Old Way (Insecure)
```typescript
// Frontend requests ALL data
const allStaff = await fetch('/api/staff');
// Frontend tries to filter based on user role
// ⚠️ Security risk: data already sent to client
```

### ✅ New Way (Secure)
```typescript
// Backend filters BEFORE sending
const accessibleStaff = await fetch('/api/data/staff');
// ✅ Only authorized data sent to client
// ✅ No way to bypass permission checks
```

---

## 📝 Summary

This API ensures that:
- ✅ Users only see data they have permission to view
- ✅ No sensitive data leaks to unauthorized users
- ✅ Frontend doesn't need to implement permission logic
- ✅ Clean, simple API for frontend developers
- ✅ Automatic department scoping for regular users
- ✅ Full access for users with `*_VIEW_ALL` permissions
