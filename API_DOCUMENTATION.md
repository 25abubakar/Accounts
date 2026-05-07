# API Documentation - Complete CRUD Operations

**Base URL:** `https://your-domain.com/api`  
**Content-Type:** `application/json` (except file uploads)

---

## 📋 Table of Contents

1. [Organization Tree API](#organization-tree-api)
2. [Positions (Vacancies) API](#positions-vacancies-api)
3. [Staff API](#staff-api)
4. [Authentication API](#authentication-api)

---

## 🌳 Organization Tree API

**Base Route:** `/api/organizationtree`

### Country Lookup Helpers

#### 🔍 Lookup Country Info
```http
GET /api/organizationtree/country-lookup?name=Pakistan
```

**Response:**
```json
{
  "name": "Pakistan",
  "code": "PK",
  "code3": "PAK",
  "flagUrl": "https://flagcdn.com/pk.svg",
  "flagPng": "https://flagcdn.com/w320/pk.png",
  "region": "Asia",
  "capital": "Islamabad"
}
```

**Use case:** Call this before creating a Country node to auto-fill code and flag.

---

#### 🔍 Search Countries (Autocomplete)
```http
GET /api/organizationtree/country-search?q=pak
```

**Response:** Array of `CountryLookupDto` (max 10 results)

---

### Tree / Hierarchy Views

#### 📊 Get Full Nested Tree
```http
GET /api/organizationtree/tree
```

**Response:** Nested JSON tree structure
```json
[
  {
    "id": 1,
    "name": "Pakistan",
    "code": "PK",
    "label": "Country",
    "parentId": null,
    "level": 0,
    "treePath": "[PK] Pakistan",
    "flagUrl": "https://flagcdn.com/pk.svg",
    "children": [
      {
        "id": 2,
        "name": "TechSoft",
        "code": "TS",
        "label": "Company",
        "parentId": 1,
        "level": 1,
        "treePath": "[PK] Pakistan → [TS] TechSoft",
        "flagUrl": null,
        "children": [...]
      }
    ]
  }
]
```

---

#### 📊 Get Subtree from Node
```http
GET /api/organizationtree/tree/{startId}
```

**Example:** `GET /api/organizationtree/tree/2` returns TechSoft and all its children.

---

#### 📊 Get Flat Tree (with indentation)
```http
GET /api/organizationtree/flat-tree
```

**Response:**
```json
[
  {
    "id": 1,
    "name": "Pakistan",
    "code": "PK",
    "label": "Country",
    "parentId": null,
    "level": 0,
    "treePath": "[PK] Pakistan",
    "treeStructure": "[PK] Pakistan",
    "flagUrl": "https://flagcdn.com/pk.svg"
  },
  {
    "id": 2,
    "name": "TechSoft",
    "code": "TS",
    "label": "Company",
    "parentId": 1,
    "level": 1,
    "treePath": "[PK] Pakistan → [TS] TechSoft",
    "treeStructure": "   [TS] TechSoft",
    "flagUrl": null
  }
]
```

---

### CRUD Operations

#### 📖 Get All Nodes (Flat List)
```http
GET /api/organizationtree
```

**Response:** Array of `OrgNodeDto`

---

#### 📖 Get Node by ID
```http
GET /api/organizationtree/{id}
```

**Response:**
```json
{
  "id": 2,
  "name": "TechSoft",
  "code": "TS",
  "label": "Company",
  "parentId": 1,
  "parentName": "Pakistan",
  "flagUrl": null
}
```

**Error (404):**
```json
{
  "message": "Node 999 not found."
}
```

---

#### 📖 Get Direct Children
```http
GET /api/organizationtree/{id}/children
```

**Example:** `GET /api/organizationtree/2/children` returns all branches under TechSoft.

---

#### 📖 Filter by Label
```http
GET /api/organizationtree/by-label/{label}
```

**Examples:**
- `GET /api/organizationtree/by-label/Country` → All countries
- `GET /api/organizationtree/by-label/Company` → All companies
- `GET /api/organizationtree/by-label/Branch` → All branches

---

#### 🔍 Search Nodes
```http
GET /api/organizationtree/search?q=tech
```

**Response:** Array of matching nodes

---

#### ➕ Create Node
```http
POST /api/organizationtree
Content-Type: application/json
```

**Request Body:**
```json
{
  "name": "TechSoft",
  "code": "TS",
  "label": "Company",
  "parentId": 1,
  "flagUrl": null
}
```

**Notes:**
- `label` can be any value: Country, Group, Company, Division, Region, Branch, Department, Team, etc.
- For `label: "Country"`, if `flagUrl` is empty, it auto-fetches from restcountries.com
- `code` is optional for non-Country nodes

**Response (201):** Created node

**Errors:**
- `400`: Parent node doesn't exist
- `400`: Validation errors

---

#### ✏️ Update Node
```http
PUT /api/organizationtree/{id}
Content-Type: application/json
```

**Request Body:**
```json
{
  "name": "TechSoft International",
  "code": "TSI",
  "label": "Company",
  "parentId": 1,
  "flagUrl": null
}
```

**Errors:**
- `404`: Node not found
- `400`: A node cannot be its own parent
- `400`: Parent node doesn't exist

---

#### 🗑️ Delete Node
```http
DELETE /api/organizationtree/{id}
```

**Success (200):**
```json
{
  "message": "Node 'TechSoft' (ID: 2) deleted."
}
```

**Errors:**
- `404`: Node not found
- `400`: Cannot delete — this node has children
- `400`: Cannot delete — this node has vacancies

---

## 💼 Positions (Vacancies) API

**Base Route:** `/api/positions`

### Read Operations

#### 📖 Get All Positions
```http
GET /api/positions
```

**Response:**
```json
[
  {
    "vacancyId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "organizationId": 3,
    "branchName": "Karachi Office",
    "companyName": "TechSoft",
    "countryName": "Pakistan",
    "nodeLabel": "Branch",
    "vacancyCode": "TS-KHI-MGR-01",
    "jobTitle": "Manager",
    "department": "Operations",
    "isFilled": true,
    "createdDate": "2026-05-04T10:30:00Z",
    "employee": {
      "staffId": "...",
      "fullName": "Ali Khan",
      "email": "ali@example.com",
      "phone": "+92-300-1234567",
      "photoUrl": "/uploads/staff/staff_abc123.jpg",
      "vacancyId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "vacancyCode": "TS-KHI-MGR-01",
      "jobTitle": "Manager",
      "branchName": "Karachi Office",
      "companyName": "TechSoft",
      "countryName": "Pakistan",
      "joiningDate": "2026-01-15T08:00:00Z"
    }
  }
]
```

---

#### 📖 Get Position by ID
```http
GET /api/positions/{id}
```

**Error (404):**
```json
{
  "message": "Position 3fa85f64-5717-4562-b3fc-2c963f66afa6 not found."
}
```

---

#### 📖 Get Vacant Positions
```http
GET /api/positions/vacant
```

**Use case:** Show available positions for hiring

---

#### 📖 Get Filled Positions
```http
GET /api/positions/filled
```

---

#### 📖 Get Positions by Organization Node
```http
GET /api/positions/by-node/{orgId}
```

**Example:** `GET /api/positions/by-node/3` returns all positions in Karachi Office

---

#### 📊 Get Full Report
```http
GET /api/positions/report
```

**Response:**
```json
[
  {
    "country": "Pakistan",
    "company": "TechSoft",
    "branch": "Karachi Office",
    "vacancyCode": "TS-KHI-MGR-01",
    "jobTitle": "Manager",
    "department": "Operations",
    "isFilled": true,
    "status": "Filled",
    "employeeName": "Ali Khan",
    "employeeEmail": "ali@example.com",
    "joiningDate": "2026-01-15T08:00:00Z"
  }
]
```

**Use case:** Export to Excel, generate reports

---

#### 🔍 Preview Vacancy Code
```http
GET /api/positions/preview-code?organizationId=3&jobTitle=Manager
```

**Response:**
```json
{
  "vacancyCode": "TS-KHI-MGR-02"
}
```

**Use case:** Show the user what code will be generated before creating the position

---

### Create, Update, Delete

#### ➕ Create Position
```http
POST /api/positions
Content-Type: application/json
```

**Request Body:**
```json
{
  "organizationId": 3,
  "jobTitle": "Manager",
  "department": "Operations"
}
```

**Notes:**
- `vacancyCode` is **auto-generated** — do NOT send it
- Format: `{CompanyCode}-{CityCode}-{JobCode}-{NN}` (e.g., `TS-KHI-MGR-01`)

**Response (201):** Created position

**Errors:**
- `400`: Organization node not found
- `400`: Validation errors

---

#### ✏️ Update Position
```http
PUT /api/positions/{id}
Content-Type: application/json
```

**Request Body:**
```json
{
  "jobTitle": "Senior Manager",
  "department": "Operations",
  "organizationId": 3
}
```

**Notes:**
- If `jobTitle` or `organizationId` changes, `vacancyCode` is **auto-regenerated**

**Errors:**
- `404`: Position not found
- `400`: Organization node not found

---

#### 🗑️ Delete Position
```http
DELETE /api/positions/{id}
```

**Success (200):**
```json
{
  "message": "Position 'TS-KHI-MGR-01' deleted."
}
```

**Errors:**
- `404`: Position not found
- `400`: Cannot delete a filled position. Remove the employee first.

---

## 👥 Staff API

**Base Route:** `/api/staff`

### Read Operations

#### 📖 Get All Staff
```http
GET /api/staff
```

**Response:** Array of `StaffDto`

---

#### 📖 Get Staff by ID
```http
GET /api/staff/{id}
```

**Error (404):**
```json
{
  "message": "Staff 3fa85f64-5717-4562-b3fc-2c963f66afa6 not found."
}
```

---

#### 🔍 Search Staff
```http
GET /api/staff/search?q=ali
```

**Searches:** Full name and email

**Error (400):**
```json
{
  "message": "Query 'q' is required."
}
```

---

### Hire (Create)

#### ➕ Hire Staff to Vacancy
```http
POST /api/staff/hire/{vacancyId}
Content-Type: application/json
```

**Request Body:**
```json
{
  "fullName": "Ali Khan",
  "email": "ali@example.com",
  "phone": "+92-300-1234567"
}
```

**Response (201):** Created staff record with full details

**Errors:**
- `404`: Vacancy not found
- `400`: Vacancy is already filled
- `400`: Validation errors (invalid email, etc.)

---

### Update

#### ✏️ Update Staff Details
```http
PUT /api/staff/{id}
Content-Type: application/json
```

**Request Body:**
```json
{
  "fullName": "Ali Ahmed Khan",
  "email": "ali.khan@example.com",
  "phone": "+92-300-7654321"
}
```

**Notes:**
- This endpoint updates **personal details only**
- To change position, use the Transfer endpoint

**Error (404):**
```json
{
  "message": "Staff 3fa85f64-5717-4562-b3fc-2c963f66afa6 not found."
}
```

---

### Transfer

#### 🔄 Transfer Staff to New Position
```http
PUT /api/staff/{id}/transfer
Content-Type: application/json
```

**Request Body:**
```json
{
  "newVacancyId": "7b8c9d0e-1234-5678-90ab-cdef12345678"
}
```

**Success (200):** Updated staff record

**Errors:**
- `404`: Staff not found
- `404`: Current vacancy not found
- `404`: Target vacancy not found
- `400`: Staff member is not assigned to any vacancy
- `400`: Vacancy is already filled
- `400`: **Transfers are strictly limited to roles within the same Company and Country.** ⚠️

**Important:** The backend now validates that transfers can only happen within the same Company and Country. Handle this error in your UI.

---

### Photo Management

#### 📤 Upload Photo
```http
POST /api/staff/{id}/upload-photo
Content-Type: multipart/form-data
```

**Form Data:**
- `photo`: File (jpg, jpeg, png, webp)

**Constraints:**
- Max size: 5MB
- Allowed formats: `.jpg`, `.jpeg`, `.png`, `.webp`

**Response (200):**
```json
{
  "message": "Photo uploaded successfully.",
  "photoUrl": "/uploads/staff/staff_abc123_xyz789.jpg",
  "fullUrl": "https://your-domain.com/uploads/staff/staff_abc123_xyz789.jpg"
}
```

**Errors:**
- `404`: Staff not found
- `400`: No file uploaded
- `400`: Only jpg, jpeg, png, webp files are allowed
- `400`: File size must be under 5MB

---

#### 🗑️ Delete Photo
```http
DELETE /api/staff/{id}/photo
```

**Success (200):**
```json
{
  "message": "Photo removed."
}
```

**Errors:**
- `404`: Staff not found
- `400`: No photo to delete

---

### Delete

#### 🗑️ Delete Staff
```http
DELETE /api/staff/{id}
```

**Success (200):**
```json
{
  "message": "Employee 'Ali Khan' removed. Vacancy is now vacant."
}
```

**Notes:**
- Automatically frees the vacancy (sets `isFilled = false`)
- Deletes the staff photo file if it exists

**Error (404):**
```json
{
  "message": "Staff 3fa85f64-5717-4562-b3fc-2c963f66afa6 not found."
}
```

---

## 🔐 Authentication API

**Base Route:** `/api/auth`

### Register
```http
POST /api/auth/register
Content-Type: application/json
```

**Request Body:**
```json
{
  "email": "user@example.com",
  "password": "SecurePass123!",
  "confirmPassword": "SecurePass123!",
  "role": "Manager"
}
```

**Allowed Roles:** `Manager`, `Developer`, `AssistantManager`

**Response (200):**
```json
{
  "success": true,
  "message": "User registered successfully.",
  "email": "user@example.com",
  "roles": ["Manager"]
}
```

**Errors:**
- `400`: Passwords do not match
- `400`: Email already exists
- `400`: Invalid role

---

### Login
```http
POST /api/auth/login
Content-Type: application/json
```

**Request Body:**
```json
{
  "email": "user@example.com",
  "password": "SecurePass123!",
  "rememberMe": false
}
```

**Response (200):**
```json
{
  "success": true,
  "message": "Login successful.",
  "email": "user@example.com",
  "roles": ["Manager"]
}
```

**Error (401):**
```json
{
  "success": false,
  "message": "Invalid email or password."
}
```

---

### Logout
```http
POST /api/auth/logout
```

**Response (200):**
```json
{
  "success": true,
  "message": "Logged out successfully."
}
```

---

### Assign Role (Admin Only)
```http
POST /api/auth/assign-role
Content-Type: application/json
```

**Request Body:**
```json
{
  "email": "user@example.com",
  "role": "Developer"
}
```

**Response (200):**
```json
{
  "success": true,
  "message": "Role 'Developer' assigned to user@example.com."
}
```

**Errors:**
- `404`: User not found
- `400`: Invalid role

---

## 📝 Common Response Patterns

### Success Responses
- `200 OK` - Successful GET, PUT, DELETE
- `201 Created` - Successful POST (returns created resource)

### Error Responses
- `400 Bad Request` - Validation errors, business rule violations
- `404 Not Found` - Resource doesn't exist
- `401 Unauthorized` - Authentication required
- `500 Internal Server Error` - Server error

### Error Format
```json
{
  "message": "Descriptive error message"
}
```

Or for validation errors:
```json
{
  "errors": {
    "Email": ["The Email field is not a valid e-mail address."],
    "Password": ["The Password field is required."]
  }
}
```

---

## 🎯 Frontend Integration Tips

### 1. Organization Tree Dropdown
```typescript
// Fetch all branches for a company
const branches = await fetch(`/api/organizationtree/by-label/Branch`);

// Or get children of a specific company
const branches = await fetch(`/api/organizationtree/2/children`);
```

### 2. Vacancy Code Preview
```typescript
// Show user the code before creating
const preview = await fetch(
  `/api/positions/preview-code?organizationId=3&jobTitle=Manager`
);
// Display: "This will create vacancy: TS-KHI-MGR-02"
```

### 3. Transfer Validation
```typescript
try {
  await fetch(`/api/staff/${staffId}/transfer`, {
    method: 'PUT',
    body: JSON.stringify({ newVacancyId })
  });
} catch (error) {
  const message = error?.response?.data?.message;
  if (message?.includes('same Company and Country')) {
    alert('You can only transfer within the same company and country!');
  }
}
```

### 4. Photo Upload
```typescript
const formData = new FormData();
formData.append('photo', fileInput.files[0]);

await fetch(`/api/staff/${staffId}/upload-photo`, {
  method: 'POST',
  body: formData
  // Don't set Content-Type header - browser sets it automatically
});
```

### 5. Full Report Export
```typescript
// Fetch report data
const report = await fetch('/api/positions/report');

// Convert to CSV or Excel
// Use libraries like: xlsx, papaparse, etc.
```

---

## 🔄 CRUD Summary

| Entity | Create | Read | Update | Delete |
|--------|--------|------|--------|--------|
| **Organization Node** | `POST /api/organizationtree` | `GET /api/organizationtree` | `PUT /api/organizationtree/{id}` | `DELETE /api/organizationtree/{id}` |
| **Position** | `POST /api/positions` | `GET /api/positions` | `PUT /api/positions/{id}` | `DELETE /api/positions/{id}` |
| **Staff** | `POST /api/staff/hire/{vacancyId}` | `GET /api/staff` | `PUT /api/staff/{id}` | `DELETE /api/staff/{id}` |
| **Transfer** | - | - | `PUT /api/staff/{id}/transfer` | - |
| **Photo** | `POST /api/staff/{id}/upload-photo` | (included in staff response) | (upload new = replace) | `DELETE /api/staff/{id}/photo` |

---

## 🚀 Quick Start Checklist

- [ ] Set up base API URL in your config
- [ ] Implement error handling for all endpoints
- [ ] Add loading states for async operations
- [ ] Handle 404 and 400 errors gracefully
- [ ] Implement file upload with progress indicator
- [ ] Add confirmation dialogs for delete operations
- [ ] Cache organization tree data (changes infrequently)
- [ ] Implement search debouncing (300ms delay)
- [ ] Show vacancy code preview before creating positions
- [ ] Validate transfer restrictions on frontend (same company/country)

---

**Last Updated:** May 7, 2026  
**API Version:** 1.0  
**Backend Framework:** ASP.NET Core (.NET 10)
