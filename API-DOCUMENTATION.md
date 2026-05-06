# HR Management System — API Documentation
**For Frontend Developers**

---

## Base URL
```
http://localhost:5098
```
> Check `Accounts/Properties/launchSettings.json` for the exact port.

## Swagger UI (Test APIs in Browser)
```
http://localhost:5098/swagger
```

## Important — Axios Setup
All requests must include credentials (cookies). Set this once globally:
```js
// src/api/axios.js
import axios from 'axios';

const api = axios.create({
  baseURL: 'http://localhost:5098',
  withCredentials: true,          // REQUIRED — sends auth cookies
  headers: { 'Content-Type': 'application/json' }
});

export default api;
```

---

## Standard Response Format
All APIs return JSON. Errors always include a `message` field:
```json
{ "message": "Something went wrong." }
```

---

---

# 1. AUTH APIs
**Base path:** `/api/auth`

---

### Register a New User
`POST /api/auth/register`

Creates a new system user with a role.

**Request Body:**
```json
{
  "email": "ali@company.com",
  "password": "Pass123!",
  "confirmPassword": "Pass123!",
  "role": "Manager"
}
```
> Allowed roles: `Manager`, `Developer`, `AssistantManager`

**Success Response (200):**
```json
{
  "success": true,
  "message": "User registered successfully.",
  "email": "ali@company.com",
  "roles": ["Manager"]
}
```
**Errors:** `400` validation, `409` email already exists

---

### Login
`POST /api/auth/login`

**Request Body:**
```json
{
  "email": "ali@company.com",
  "password": "Pass123!",
  "rememberMe": false
}
```

**Success Response (200):**
```json
{
  "success": true,
  "message": "Login successful.",
  "email": "ali@company.com",
  "roles": ["Manager"]
}
```
**Errors:** `401` wrong credentials, `423` account locked

---

### Logout
`POST /api/auth/logout`

No body needed. User must be logged in.

**Response (200):**
```json
{ "success": true, "message": "Logged out successfully." }
```

---

### Assign Role to User
`POST /api/auth/assign-role`

Changes a user's role (replaces existing role).

**Request Body:**
```json
{
  "email": "ali@company.com",
  "role": "Developer"
}
```

**Response (200):**
```json
{
  "success": true,
  "message": "Role 'Developer' assigned to ali@company.com.",
  "email": "ali@company.com",
  "roles": ["Developer"]
}
```

---

### Get All System Users
`GET /api/auth/users`

Returns all registered users with their roles.

**Response (200):**
```json
[
  {
    "id": "abc123",
    "email": "ali@company.com",
    "userName": "ali@company.com",
    "roles": ["Manager"]
  }
]
```

---

---

# 2. ORGANIZATION APIs
**Base path:** `/api/organization`

The organization is a **flexible hierarchy tree**. Each node has:
- `id` — unique number
- `name` — display name (e.g. "Pakistan", "TechSoft", "Lahore Branch")
- `code` — short code (e.g. "PK", "TS") — optional
- `label` — type of node. Can be **any value** you want: `Country`, `Group`, `Company`, `Division`, `Region`, `Branch`, `Department`, `Team`, etc.
- `parentId` — ID of parent node (null for root nodes like Country)
- `flagUrl` — flag image URL (auto-fetched for Country nodes)

**Example hierarchy:**
```
Pakistan (Country, id=1)
  └── TechSoft (Company, id=2)
        └── Lahore Branch (Branch, id=4)
              └── Ali - Manager (Staff, id=7)
```

---

### Get All Nodes (Flat List)
`GET /api/organization`

Returns all nodes as a flat array. Good for dropdowns.

**Response:**
```json
[
  { "id": 1, "name": "Pakistan", "code": "PK", "label": "Country", "parentId": null, "parentName": null, "flagUrl": "https://flagcdn.com/pk.svg" },
  { "id": 2, "name": "TechSoft", "code": "TS", "label": "Company", "parentId": 1, "parentName": "Pakistan", "flagUrl": null },
  { "id": 4, "name": "Lahore Branch", "code": null, "label": "Branch", "parentId": 2, "parentName": "TechSoft", "flagUrl": null }
]
```

---

### Get Single Node
`GET /api/organization/{id}`

**Response:** Single node object (same shape as above)

---

### Get Full Nested Tree
`GET /api/organization/tree`

Returns the full hierarchy as nested JSON. **Use this for tree view UI.**

**Response:**
```json
[
  {
    "id": 1,
    "name": "Pakistan",
    "code": "PK",
    "label": "Country",
    "level": 0,
    "treePath": "[PK] Pakistan",
    "flagUrl": "https://flagcdn.com/pk.svg",
    "children": [
      {
        "id": 2,
        "name": "TechSoft",
        "code": "TS",
        "label": "Company",
        "level": 1,
        "treePath": "[PK] Pakistan → [TS] TechSoft",
        "flagUrl": null,
        "children": [
          {
            "id": 4,
            "name": "Lahore Branch",
            "label": "Branch",
            "level": 2,
            "children": [...]
          }
        ]
      }
    ]
  }
]
```

---

### Get Subtree from Any Node
`GET /api/organization/tree/{nodeId}`

Returns only the subtree starting from a specific node.

**Example:** `GET /api/organization/tree/2` → returns TechSoft + all its branches and staff

---

### Get Flat Tree (with Levels)
`GET /api/organization/flat-tree`

Returns flat list with `level`, `treePath`, and `treeStructure` (indented text).
Good for `<select>` dropdowns showing hierarchy.

**Response:**
```json
[
  { "id": 1, "name": "Pakistan", "label": "Country", "level": 0, "treeStructure": "[PK] Pakistan", "treePath": "[PK] Pakistan" },
  { "id": 2, "name": "TechSoft", "label": "Company", "level": 1, "treeStructure": "   [TS] TechSoft", "treePath": "[PK] Pakistan → [TS] TechSoft" },
  { "id": 4, "name": "Lahore Branch", "label": "Branch", "level": 2, "treeStructure": "      Lahore Branch" }
]
```

---

### Get Children of a Node
`GET /api/organization/{id}/children`

Returns direct children only (one level down).

**Example:** `GET /api/organization/2/children` → returns branches of TechSoft

---

### Filter by Label
`GET /api/organization/by-label/{label}`

Returns all nodes with a specific label.

**Examples:**
- `GET /api/organization/by-label/Country` → all countries
- `GET /api/organization/by-label/Company` → all companies
- `GET /api/organization/by-label/Branch` → all branches
- `GET /api/organization/by-label/Group` → all groups (if you created any)

---

### Search Nodes by Name
`GET /api/organization/search?q=lahore`

Partial, case-insensitive search across all nodes.

---

### Country Lookup (Auto-fetch Flag + Code)
`GET /api/organization/country-lookup?name=Pakistan`

Call this **before creating a country** to get the flag and code automatically.

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

---

### Country Search Autocomplete
`GET /api/organization/country-search?q=pak`

Returns up to 10 matching countries. Use for autocomplete input.

---

### Add Any Node (Country / Company / Group / Branch / etc.)
`POST /api/organization`

**This one API creates ALL types of nodes.** Just change the `label`.

**Request Body:**
```json
{
  "name": "Pakistan",
  "code": "PK",
  "label": "Country",
  "parentId": null,
  "flagUrl": null
}
```
> If `label` is `"Country"` and `flagUrl` is empty, the flag is **auto-fetched** from restcountries.com.

**More examples:**

Add a Company under Pakistan (id=1):
```json
{ "name": "TechSoft", "code": "TS", "label": "Company", "parentId": 1 }
```

Add a Group under Pakistan:
```json
{ "name": "Tech Group", "code": "TG", "label": "Group", "parentId": 1 }
```

Add a Branch under TechSoft (id=2):
```json
{ "name": "Lahore Branch", "code": null, "label": "Branch", "parentId": 2 }
```

**Response (201):** The created node object

---

### Edit a Node
`PUT /api/organization/{id}`

Update name, code, label, parent, or flagUrl.

**Request Body:**
```json
{
  "name": "TechSoft Pvt Ltd",
  "code": "TS",
  "label": "Company",
  "parentId": 1,
  "flagUrl": null
}
```

**Response (200):** Updated node object

---

### Delete a Node
`DELETE /api/organization/{id}`

**Response (200):**
```json
{ "message": "Node 'Lahore Branch' (ID: 4) deleted." }
```
**Blocked if:** node has children OR has vacancies linked to it.

---

---

# 3. POSITION (VACANCY) APIs
**Base path:** `/api/positions`

A **Position** is a job seat/slot inside a branch. It can be empty or filled by an employee.

**Position object:**
```json
{
  "vacancyId": 1,
  "organizationId": 4,
  "branchName": "Lahore Branch",
  "companyName": "TechSoft",
  "countryName": "Pakistan",
  "vacancyCode": "TS-LHR-DEV-01",
  "jobTitle": "Developer",
  "department": "IT",
  "isFilled": false,
  "createdDate": "2025-01-01T00:00:00Z",
  "employee": null
}
```
When filled, `employee` contains the employee details.

---

### Get All Positions
`GET /api/positions`

---

### Get Single Position
`GET /api/positions/{id}`

---

### Get Vacant Positions (Empty Seats)
`GET /api/positions/vacant`

---

### Get Filled Positions (With Employees)
`GET /api/positions/filled`

---

### Get Positions by Branch
`GET /api/positions/by-branch/{organizationId}`

**Example:** `GET /api/positions/by-branch/4` → all positions in Lahore Branch (id=4)

---

### Full Report
`GET /api/positions/report`

Returns all positions with full org path and employee info. Good for reports/tables.

**Response:**
```json
[
  {
    "country": "Pakistan",
    "company": "TechSoft",
    "branch": "Lahore Branch",
    "vacancyCode": "TS-LHR-DEV-01",
    "jobTitle": "Developer",
    "department": "IT",
    "isFilled": true,
    "status": "Filled",
    "employeeName": "Ali Khan",
    "employeeEmail": "ali@company.com",
    "joiningDate": "2025-01-15T00:00:00Z"
  }
]
```

---

### Create a Position (Empty Seat)
`POST /api/positions`

**Request Body:**
```json
{
  "organizationId": 4,
  "vacancyCode": "TS-LHR-DEV-01",
  "jobTitle": "Developer",
  "department": "IT"
}
```
> `organizationId` must be a **Branch** node in the organization tree.

**Response (201):** Created position object

---

### Update a Position
`PUT /api/positions/{id}`

**Request Body:**
```json
{
  "organizationId": 4,
  "vacancyCode": "TS-LHR-DEV-01",
  "jobTitle": "Senior Developer",
  "department": "IT"
}
```

---

### Delete a Position
`DELETE /api/positions/{id}`

**Blocked if:** position is filled (has an employee). Remove employee first.

---

---

# 4. EMPLOYEE APIs
**Base path:** `/api/employees`

---

### Get All Employees
`GET /api/employees`

Returns all employees with their position and org info.

**Employee object:**
```json
{
  "staffId": 1,
  "fullName": "Ali Khan",
  "email": "ali@company.com",
  "phone": "0300-1234567",
  "photoUrl": "/uploads/staff/staff_1_abc123.jpg",
  "vacancyId": 1,
  "vacancyCode": "TS-LHR-DEV-01",
  "jobTitle": "Developer",
  "branchName": "Lahore Branch",
  "companyName": "TechSoft",
  "countryName": "Pakistan",
  "joiningDate": "2025-01-15T00:00:00Z"
}
```

---

### Get Single Employee
`GET /api/employees/{id}`

---

### Search Employees
`GET /api/employees/search?q=ali`

Searches by name or email.

---

### Hire an Employee (Fill a Position)
`POST /api/employees/hire/{positionId}`

Assigns an employee to a vacant position. Automatically marks the position as filled.

**Request Body:**
```json
{
  "fullName": "Ali Khan",
  "email": "ali@company.com",
  "phone": "0300-1234567"
}
```

**Response (201):** Created employee object with full details

**Error:** `400` if position is already filled

---

### Update Employee Info
`PUT /api/employees/{id}`

Update name, email, phone only.

**Request Body:**
```json
{
  "fullName": "Ali Khan Updated",
  "email": "ali.new@company.com",
  "phone": "0300-9999999"
}
```

---

### Upload Employee Photo
`POST /api/employees/{id}/upload-photo`

Send as `multipart/form-data`. Field name must be `photo`.

- Allowed formats: `jpg`, `jpeg`, `png`, `webp`
- Max size: `5MB`

**Example (JavaScript/Axios):**
```js
const formData = new FormData();
formData.append('photo', file); // file = File object from input

await api.post(`/api/employees/${staffId}/upload-photo`, formData, {
  headers: { 'Content-Type': 'multipart/form-data' }
});
```

**Response (200):**
```json
{
  "message": "Photo uploaded successfully.",
  "photoUrl": "/uploads/staff/staff_1_abc123.jpg",
  "fullUrl": "http://localhost:5098/uploads/staff/staff_1_abc123.jpg"
}
```

---

### Delete Employee Photo
`DELETE /api/employees/{id}/photo`

Removes the photo file and clears the photoUrl.

---

### Transfer Employee to Another Position
`PUT /api/employees/{id}/transfer`

Moves employee to a different vacant position. Old position becomes vacant automatically.

**Request Body:**
```json
{ "newVacancyId": 3 }
```

**Error:** `400` if target position is already filled

---

### Remove Employee
`DELETE /api/employees/{id}`

Removes the employee record. Their position automatically becomes vacant again.

---

---

# 5. COMPLETE WORKFLOW EXAMPLE

Here is the full flow from start to finish:

### Step 1 — Lookup country before adding
```
GET /api/organization/country-lookup?name=Pakistan
→ Returns: { code: "PK", flagUrl: "https://flagcdn.com/pk.svg", ... }
```

### Step 2 — Add Country
```
POST /api/organization
Body: { "name": "Pakistan", "label": "Country", "parentId": null }
→ Flag auto-fetched. Returns: { id: 1, name: "Pakistan", code: "PK", flagUrl: "..." }
```

### Step 3 — Add Company under Pakistan
```
POST /api/organization
Body: { "name": "TechSoft", "code": "TS", "label": "Company", "parentId": 1 }
→ Returns: { id: 2, name: "TechSoft", parentId: 1 }
```

### Step 4 — Add Branch under TechSoft
```
POST /api/organization
Body: { "name": "Lahore Branch", "label": "Branch", "parentId": 2 }
→ Returns: { id: 4, name: "Lahore Branch", parentId: 2 }
```

### Step 5 — Create a Position in Lahore Branch
```
POST /api/positions
Body: { "organizationId": 4, "vacancyCode": "TS-LHR-DEV-01", "jobTitle": "Developer", "department": "IT" }
→ Returns: { vacancyId: 1, isFilled: false, ... }
```

### Step 6 — Hire an Employee for that Position
```
POST /api/employees/hire/1
Body: { "fullName": "Ali Khan", "email": "ali@company.com", "phone": "0300-1234567" }
→ Returns: { staffId: 1, fullName: "Ali Khan", jobTitle: "Developer", branchName: "Lahore Branch", ... }
```

### Step 7 — Upload Employee Photo
```
POST /api/employees/1/upload-photo
Body: FormData with field "photo" = image file
→ Returns: { photoUrl: "/uploads/staff/staff_1_xxx.jpg" }
```

### Step 8 — View Full Report
```
GET /api/positions/report
→ Returns table: Country | Company | Branch | Position | Status | Employee
```

---

# 6. CORS — Important for React

The backend allows requests from:
- `http://localhost:5173` (Vite default)
- `https://localhost:5173`

If your React app runs on a different port, ask the backend developer to add it.

---

# 7. TypeScript Types

```typescript
// Organization node
interface OrgNode {
  id: number;
  name: string;
  code: string | null;
  label: string;           // "Country" | "Company" | "Branch" | "Group" | any custom
  parentId: number | null;
  parentName: string | null;
  flagUrl: string | null;
}

// Nested tree node
interface OrgTreeNode extends OrgNode {
  level: number;
  treePath: string;
  treeStructure: string;
  children: OrgTreeNode[];
}

// Position (Vacancy)
interface Position {
  vacancyId: number;
  organizationId: number;
  branchName: string;
  companyName: string;
  countryName: string;
  vacancyCode: string;
  jobTitle: string;
  department: string | null;
  isFilled: boolean;
  createdDate: string;
  employee: Employee | null;
}

// Employee (Staff)
interface Employee {
  staffId: number;
  fullName: string;
  email: string | null;
  phone: string | null;
  photoUrl: string | null;
  vacancyId: number | null;
  vacancyCode: string | null;
  jobTitle: string | null;
  branchName: string | null;
  companyName: string | null;
  countryName: string | null;
  joiningDate: string;
}

// Country Lookup
interface CountryLookup {
  name: string;
  code: string;       // "PK"
  code3: string;      // "PAK"
  flagUrl: string;    // SVG URL
  flagPng: string;    // PNG URL
  region: string;
  capital: string;
}

// Auth
interface AuthResponse {
  success: boolean;
  message: string;
  email?: string;
  roles?: string[];
}
```

---

# 8. Quick API Reference Table

| What you want to do | Method | URL |
|---|---|---|
| Login | POST | `/api/auth/login` |
| Register user | POST | `/api/auth/register` |
| Logout | POST | `/api/auth/logout` |
| Get all users | GET | `/api/auth/users` |
| Assign role | POST | `/api/auth/assign-role` |
| Get full org tree | GET | `/api/organization/tree` |
| Get flat tree (for dropdown) | GET | `/api/organization/flat-tree` |
| Get children of a node | GET | `/api/organization/{id}/children` |
| Search org nodes | GET | `/api/organization/search?q=...` |
| Lookup country flag | GET | `/api/organization/country-lookup?name=...` |
| Add country/company/branch/group | POST | `/api/organization` |
| Edit any node | PUT | `/api/organization/{id}` |
| Delete any node | DELETE | `/api/organization/{id}` |
| Get all positions | GET | `/api/positions` |
| Get vacant positions | GET | `/api/positions/vacant` |
| Get filled positions | GET | `/api/positions/filled` |
| Get positions by branch | GET | `/api/positions/by-branch/{orgId}` |
| Full org+position+employee report | GET | `/api/positions/report` |
| Create position | POST | `/api/positions` |
| Edit position | PUT | `/api/positions/{id}` |
| Delete position | DELETE | `/api/positions/{id}` |
| Get all employees | GET | `/api/employees` |
| Search employees | GET | `/api/employees/search?q=...` |
| Hire employee | POST | `/api/employees/hire/{positionId}` |
| Edit employee | PUT | `/api/employees/{id}` |
| Upload employee photo | POST | `/api/employees/{id}/upload-photo` |
| Delete employee photo | DELETE | `/api/employees/{id}/photo` |
| Transfer employee | PUT | `/api/employees/{id}/transfer` |
| Remove employee | DELETE | `/api/employees/{id}` |
