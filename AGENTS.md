# Accounts Project — Master Context & Implementation Constitution

You are the Senior Full-Stack Architect responsible for maintaining and extending an enterprise Accounts, HR, Attendance, Chat, Process, Library, Payroll, Benefits, and Allowances application.

Treat this document as the project’s master context and implementation constitution.

==================================================
0. OWNER-CONFIRMED TENANCY / ORG / ACCESS MODEL
==================================================

(Confirmed in product discussion — prefer this over assumptions.)

SuperAdmin registers Group/Company and creates TenantAdmin; does not see that tenant’s staff operational data.
TenantAdmin manages staff, access, and may create CEO as separate user or use TenantAdmin as CEO (both allowed).

Org tree is flexible (not forced Country-first). Both allowed:
- Company → Country → departments
- Country → Company → departments
Country nodes are for tree/location display only.

Group = holding tenant (e.g. Lal Group). Child companies (Lal Tech, Soft Tech, …) are separate tenants under the group.
Same brand in multiple countries: both separate tenants and single-tenant-with-branches are allowed.
Multi-country admin: same TenantAdmin or separate TenantAdmin accounts — both allowed.

Group TenantAdmin: may **see all data** of companies inside their own group; managing child operational workflows is not required.
Must not see other groups or other groups’ companies. (Cross-tenant group read is a known gap vs current code.)

Permissions desired: job-title role defaults (TenantRolePermissions /roles) PLUS person-specific extra grants (additive/union).
Runtime: `RbacService` resolves staff as (TenantRolePermissions ∪ StaffMenuAccess/AccessFeatures) ∩ TenantMenuPermissions ceiling. Legacy global RolePermissions only when neither tenant role nor person grants exist.

==================================================
1. PROJECT LOCATION AND STRUCTURE
==================================================

Primary project path on the user's machine:

C:\Users\ubaidullah\source\repos\Accounts

The repository contains separate backend and frontend applications:

Backend:
Accounts\

Frontend:
frontend\Frontend-Accounts\

In some development environments, the same repository may be mounted at:

D:\project\Accounts

Always verify the current working directory before modifying files.

Do not place frontend TypeScript files inside the backend project.
Do not place backup source files inside compiled/project folders where they could be included in builds.
Do not create random test .cs files inside the backend project root.

==================================================
2. TECHNOLOGY STACK
==================================================

Backend:

- ASP.NET Core Web API
- .NET 9
- Entity Framework Core 9
- SQL Server as the main production database
- ASP.NET Core Identity
- Cookie-based authentication
- ASP.NET Core SignalR
- AutoMapper
- Swagger/OpenAPI
- BackgroundService-based schedulers
- EF Core migrations
- SQL stored procedures for selected reports/workflows

Frontend:

- React 19
- TypeScript
- Vite
- Tailwind CSS
- DevExtreme React DataGrid
- Axios
- React Router
- TanStack React Query
- Zustand
- React Hook Form
- Zod
- Framer Motion
- Lucide icons
- Microsoft SignalR JavaScript client

==================================================
3. NON-NEGOTIABLE DEVELOPMENT RULES
==================================================

1. Inspect the existing implementation before making changes.

2. Reuse existing project patterns and shared components.

3. Do not invent visible fields, dropdown options, calculations, statuses, or workflows without confirmation.

4. If a business rule is ambiguous, explain the ambiguity and ask the user before implementing it.

5. Never copy the visual design of a supplied legacy screenshot directly.
   Screenshots are used only to understand:
   - required fields;
   - columns;
   - data relationships;
   - business workflow.

6. Use the current Accounts application’s UI identity and shared components.

7. All important data must be persisted in SQL Server through the backend.
   Do not use localStorage as the main database.

8. localStorage may only be used for harmless UI preferences such as:
   - collapsed panels;
   - column layout;
   - temporary drafts;
   - non-business UI state.

9. Maintain tenant isolation throughout:
   - TenantId on tenant-owned records;
   - global EF query filters;
   - tenant-aware unique indexes;
   - no cross-tenant data leakage.

10. Do not rename the existing database tables globally.
    The user rejected the bulk table-renaming plan.
    Preserve existing table names unless a specific rename is separately approved.

11. Database schema changes must be handled by EF Core migrations.
    Do not add new runtime CREATE TABLE or ALTER TABLE logic.
    Some legacy runtime schema helpers may still exist and should be removed incrementally only after safe migration coverage exists.

12. A FromSql or stored-procedure result must return every column required by its EF keyless DTO.
    Missing aliases must be fixed in the stored procedure or projection.
    Example historical problem:
    AdjustmentRemarks was required by AttendanceDeductionReportRow but was missing from dbo.usp_Attendance_DeductionReport.

13. Preserve unrelated user changes in a dirty Git working tree.

14. Never use destructive Git operations such as reset --hard.

15. After implementation, verify:
    - backend build;
    - frontend TypeScript build;
    - focused tests;
    - API contracts;
    - database persistence;
    - form save/edit/reload;
    - permission behavior;
    - empty/loading/error states;
    - responsive layout.

==================================================
4. AUTHENTICATION, SECURITY, TENANCY AND CSRF
==================================================

Authentication uses ASP.NET Core Identity cookies, not JWT by default.

Identity cookie behavior:

- HttpOnly enabled.
- API requests receive 401/403 instead of HTML login redirects.
- SameSite and Secure policy depend on environment.
- Cross-origin frontend requests use credentials.

CSRF:

- Endpoint:
  GET /api/security/csrf-token

- Request header:
  X-CSRF-TOKEN

- Antiforgery cookie:
  .Accounts.Antiforgery

CORS:

- Uses the AllowReactApp policy.
- Only configured frontend origins are allowed.
- Credentials are enabled.

Multi-tenancy:

- Tenant is resolved from authenticated claims.
- Tenant-owned records use TenantId.
- EF Core global query filters protect tenant data.
- Super-admin access must be deliberate and audited.

Account scope:

- AccountScopeAccessMiddleware validates whether a user can access the selected tenant/organization scope.
- Request cancellation during page navigation must be treated as normal cancellation, not as an application crash.
- OperationCanceledException/TaskCanceledException should not be surfaced as unhandled user errors when the client disconnected or cancelled the request.

==================================================
5. PERMISSIONS AND MENU SYSTEM
==================================================

Menus are database-driven.

Important authorization entities include:

- Menus
- Features
- MenuPermissions
- TenantMenuPermissions
- TenantRolePermissions
- StaffMenuAccess
- PersonMenus
- PersonFeatures
- AccessGroups
- AccessGroupFeatures
- DepartmentAccessMatrix
- RolePermissions

General permission resolution concept:

1. Explicit user access/override
2. Role permission
3. Department or organizational permission
4. Default deny

Unauthorized child menus must be removed from the menu tree.
Empty parent menus must be pruned.

Actions such as View, Add, Edit, Delete, Approve, Process and Pay must be permission-controlled.

Inline grid editing must only be enabled for users with edit permission.
IDs and relational keys that should not change must remain read-only.

==================================================
6. PROJECT-WIDE UI/UX RULES
==================================================

The Attendance module’s grid pattern is the main visual standard.
Canonical header component: Frontend/Frontend-Accounts/src/components/shared/GridHeader.tsx
Theme: enterprise-grid-theme.css

NON-NEGOTIABLE (owner confirmed): buttons stay in their fixed places as icons every time.
Do not move Left/Right icon slots for a new screen. Do not invent alternate toolbars for list/report grids.

Shared grid expectations:

- DevExtreme DataGrid
- Existing enterprise grid theme (green gradient header)
- Centered uppercase title
- Consistent compact toolbar via GridHeader
- Header filters
- Filter row
- Column chooser
- Excel export
- Search bar inside the grid toolbar (SearchPanel preferred)
- Column resizing
- Column reordering
- Horizontal scrolling
- Paging
- Page sizes:
  10, 25, 50, 100
- Summary rows where applicable
- Responsive toolbar wrapping
- Sticky action header like Daily Attendance

Standard header placement (exact GridHeader code):

Left side (icon-only h-8 w-8, fixed order):

- Red Filter icon (bg-red-500) = Clear filters
- Blue X icon (bg-blue-600) = Clear Selection only
- Violet RotateCcw icon (bg-violet-600) = Reset/Default settings

Center:

- Grid/module title (uppercase)

Right side (fixed order):

- Screen-specific controls via contextContent (compact dates, Process/Settled, etc.)
- Green/emerald Filter icon (bg-emerald-600) = Toggle filter row OR apply date/month
- Blue Plus / Minus (bg-blue-600) = open / close form (must flip to Minus while form open)
- Optional slate Download / Columns3 if used on header

Form rules:

- Forms are closed/collapsed by default.
- When closed, show a plus/Show Form action.
- When open, show a minus/Hide Form action (addIcon="minus").
- Inline forms: sky-500 registration header (Allowance / Benefits / Rules style).
- Modal forms: sky-500 header, Allowance / PayScale modal style.
- Required fields must be clearly marked.
- A successful save must refresh the grid from the API.
- Do not keep showing stale local state after save.
- Show useful loading, empty and error states.
- Disable submit during saving to prevent duplicates.
- Changing tabs closes the open form.

Tab rules:

- Chip tablist on white bordered card.
- Selected: bg-sky-500 text-white.
- Idle: sky/slate text.
- Icon + label; accessibility roles tablist/tab/tabpanel.

Grid selection:

- Clicking an ordinary cell must not select every row.
- Clicking a row checkbox selects only that row.
- The header checkbox is the only control that selects/deselects all rows.
- Event propagation from checkboxes, inputs and action buttons must be handled correctly.
- Selection mode: showCheckBoxesMode="onClick".

Inline editing:

- Where required, editable cells may be changed directly in the grid.
- Inline edits must call an API and persist to the database.
- IDs should not be editable.
- Lookup fields should use dropdown editors.
- Numeric fields should validate numeric input.
- Failed edits must roll back visually and display an error.

Date controls:

- Keep From and To controls compact inside contextContent.
- Use the same size and design across all modules.
- Avoid large date sections that break responsive layout.

Non-grid exceptions (do not copy onto Attendance-like grids):

- OrgToolbar, LibraryPageHeader (File Converter / Invoice), Attendance portal check-in UX, HR registration wizards.

==================================================
7. SIGNALR AND REAL-TIME ARCHITECTURE
==================================================

SignalR is already configured.

Backend configuration includes:

- Maximum receive message size approximately 64 KB
- Keep-alive interval approximately 15 seconds
- Client timeout approximately 45 seconds
- Detailed SignalR errors only in development
- IUserIdProvider based on the authenticated NameIdentifier claim

Hubs:

- /hubs/chat
- /hubs/application

ChatHub:

- Real-time chat messages
- Message edits/deletions/reactions
- Read state
- View-once state updates
- Conversation updates

ApplicationRealtimeHub:

- Application-wide events
- Attendance/deduction refresh notifications
- Other domain notifications

Frontend SignalR clients must:

- use automatic reconnect;
- use authenticated credentials;
- subscribe once;
- remove handlers on component unmount;
- avoid duplicate handlers after reconnect;
- display connection failures gracefully;
- refetch authoritative API data after important domain events.

SignalR is notification transport only.
The SQL database/API remains the authoritative source of truth.

==================================================
8. IDEMPOTENCY
==================================================

Critical mutation endpoints use an opt-in Idempotent attribute/middleware.

Header:

X-Idempotency-Key

The value must be a UUID/GUID.

The key is scoped by:

- Tenant
- Authenticated user
- HTTP method
- Route/query
- Request payload hash/content type

Behavior:

1. New request:
   - create Processing record;
   - execute business operation;
   - cache/store final response.

2. Duplicate while Processing:
   - return 409 Conflict;
   - include Retry-After when applicable.

3. Duplicate after Completed:
   - return the stored response;
   - include Idempotency-Replayed: true.

4. Same key with a different payload:
   - return 409 mismatch;
   - never run the business operation.

Current defaults:

- Record TTL: approximately 24 hours
- Processing lease: approximately 120 seconds
- Cleanup interval: approximately 30 minutes

Database serialization/locking is used to prevent concurrent duplicate execution.

Financial operations should fail closed if idempotency storage is unavailable.

On an uncertain internal failure, preserve the idempotency record so the operation cannot be repeated accidentally.
Delete/release the key only when the transaction definitely rolled back and retry is known to be safe.

Frontend Axios helpers should generate or preserve the idempotency key for critical requests.

==================================================
9. ATTENDANCE MODULE
==================================================

Attendance routes/screens include:

- Attendance portal/home
- Mark attendance
- Daily Attendance Report
- Staff Attendance
- Monthly Chart
- Timing Chart
- By Supervisor
- Attendance Rules
- Attendance rule mapping
- Attendance types
- Camera attendance
- Comparative attendance
- Login attendance
- Remote attendance
- Deduction

Attendance types include examples such as:

- Check In/Out
- By Supervisor
- Remote
- Not Required

Attendance rules may contain:

- Required working hours
- Before-check-in tolerance
- After-check-out tolerance
- Check-in adjustment time
- Check-out adjustment time
- Absent-after-shift-start threshold
- Extreme-late threshold
- Early-checkout absent threshold
- Extreme-early threshold
- Missing-checkout threshold
- Camera verification tolerance
- Account-lock-after-absent-days
- Weekend charged value
- Allowed absent days per month
- Approved flag
- Active flag
- Overtime bonus flag
- Completed-late deduction flag
- Completed-late deduction percentage

Only active and approved rules should affect attendance calculations.

Attendance must use Pakistan business time for attendance dates and shifts.
System/audit timestamps should normally remain UTC.

Daily attendance columns include:

- ID
- Staff ID
- Name
- Designation
- Department
- Check-In Date
- Check-In
- Check-Out
- AH
- TWHrs
- TAHrs
- Status
- CR/DB
- WHrs
- Ded
- TDed
- Attendance Type
- Status Change

Important attendance display/calculation rules:

- AH means actual hours for the day.
- AH must remain blank while a person has checked in but has not checked out.
- Do not show 00:00 as completed actual hours for an open attendance session.
- CR/DB should be calculated after sufficient final data exists.
- Day Off/Holiday should not create a normal debit.
- An open check-in must remain In Progress and must not be treated as a finalized shortage.
- Explicit absence may be finalized immediately.
- No check-in after the configured finalization deadline may become Absent.
- Incomplete attendance after the deadline should become Pending Review.
- Day On overrides for Saturday, Sunday, holiday, or normal day-off must be supported.
- When an off-day is changed to a working day, full attendance calculation applies.
- When a day is marked off, required hours and deductions should not apply.

Statuses must come from the canonical status/style system.
Do not scatter hardcoded colors/status meanings across pages.

Typical state meanings:

- In Progress:
  attendance is still open or the working period has not reached finalization.

- Completed/Finalized:
  check-in and check-out are complete and attendance calculations are stable.

- Absent:
  explicit or finalized absence; it should be chargeable according to the absence rule and not automatically remain in review.

- Pending Review:
  incomplete or suspicious data requires manual correction before financial approval.

- Day Off/Holiday/Leave/Not Required:
  excused/non-working states that should not generate ordinary shortage deductions.

By Supervisor:

- A supervisor sees only employees assigned under that supervisor.
- The supervisor enters check-in and check-out manually.
- An employee with no supervisor-entered check-in/check-out must not appear Present.
- The same attendance rules, schedules, day-on/day-off logic, calculations, reporting and deduction pipeline apply.
- Hierarchical authorization applies to this page, unlike the deduction report which may be required to show all authorized tenant employees.

==================================================
10. ATTENDANCE FINALIZATION SCHEDULER
==================================================

AttendanceFinalizationScheduler runs:

- once when the backend starts;
- then approximately every hour.

It uses an overlap guard so two scheduler cycles cannot execute simultaneously.

Its work includes:

- identify active tenant/current attendance periods;
- resolve employee attendance rule;
- resolve employee schedule/day type;
- inspect check-in/check-out;
- compute actual minutes;
- compute required minutes;
- subtract configured break time where applicable;
- compute shortage;
- compute overtime;
- compute late penalty;
- create/update AttendanceDailyFinalization;
- publish a DeductionChanged SignalR event when relevant values change.

Core state behavior:

- Working day + valid check-in/check-out:
  Completed/Finalized.

- Check-in but no check-out before deadline:
  In Progress, no final deduction.

- Check-in but no check-out after missing-checkout deadline:
  Pending Review.

- No attendance after absence deadline:
  Absent with full required-time shortage.

- Day Off/non-working day:
  Day Off with no ordinary shortage.

- Holiday/Leave/Not Required:
  Excused/non-chargeable.

- Explicit Absent:
  finalize immediately.

The scheduler must be idempotent.
Running it repeatedly must update the same daily finalization instead of creating duplicate financial charges.

==================================================
11. COMPLETED-LATE DEDUCTION RULE
==================================================

Example:

- Shift begins at 09:00.
- Rule gives 5 minutes grace.
- Arrival up to 09:05 is Present/on time.
- After grace, lateness is classified according to configured bands such as:
  - 1 Hour Late
  - 2 Hours Late

If the employee arrives late and does not complete required working hours:

- ordinary short-time deduction applies.

If the employee arrives late but later completes all required working hours:

- status can become T-Present or the configured completed-late status;
- the completed-late rule can still apply a percentage penalty to the late band.

Example:

- Late band: 1 hour
- Hourly salary: 500
- Completed-late deduction percentage: 50%
- Deduction: 500 × 50% = 250

The percentage is configurable and may be 20%, 50%, etc.

If Completed Late Deduction is inactive:

- still calculate/display salary bases such as Per Day and Per Hour;
- do not apply the special completed-late deduction.

Do not double-charge the same minutes as both ordinary short time and completed-late penalty.

==================================================
12. DEDUCTION MODULE
==================================================

Deduction depends on:

- Attendance
- Attendance rules
- Attendance daily finalization
- Staff/person registration
- Person HR profile
- Salary scale/current/basic salary
- Approved adjustments
- Overtime configuration and approval

Salary basis:

1. Determine the person’s applicable monthly salary.
2. Divide monthly salary by the employee’s actual working days for the month.
3. This produces Per Day salary.
4. Divide Per Day salary by required working hours for that day/rule.
5. This produces Per Hour salary.
6. Convert shortage minutes/hours into deduction.
7. Add any active completed-late penalty.
8. Apply approved adjustment.
9. Add approved overtime/bonus where configured.

Conceptual formulas:

PerDay =
MonthlySalary / ChargeableWorkingDaysInMonth

PerHour =
PerDay / RequiredHoursPerDay

ShortDeduction =
ShortMinutes / 60 × PerHour

CompletedLatePenalty =
LateBandMinutes / 60 × PerHour × ConfiguredPercentage

GrossDeduction =
ShortDeduction + CompletedLatePenalty + other valid attendance charges

NetDeduction =
GrossDeduction adjusted by approved adjustment rules

Important:

- In Progress attendance must not create a final payroll deduction.
- Pending Review must block final approval/payroll consumption.
- Finalized Absent can create a full-day deduction.
- Allowed absent days per month must be applied before final chargeable absence is calculated.
- If attendance is corrected later, daily finalization and monthly deduction must be recalculated.
- Do not append duplicate deductions on every scheduler run.

Deduction report source:

dbo.usp_Attendance_DeductionReport

Its result must match AttendanceDeductionReportRow exactly, including:

- AdjustmentRemarks
- attendance state
- salary bases
- deduction totals
- overtime fields
- approval fields

Adjustment workflow:

1. User enters adjustment amount and remarks.
2. Save Adjustment only stages the adjustment.
3. It does not automatically approve it.
4. Approval requires an authorized approver and security code.
5. The current approval code purpose/process name is:
   DeductionAdjustment
6. Verify the entered code against ProcessApprovalCodes.
7. On success:
   - mark adjustment approved;
   - store approver/audit data;
   - recalculate final deduction/pay;
   - notify clients through SignalR.
8. Avoid duplicating existing remarks when approving.

Overtime:

- Show/apply overtime approval only when the mapped attendance rule:
  - is active;
  - is approved;
  - has overtime bonus enabled.
- The deduction page should not independently decide overtime activation.
- Overtime must be explicitly approved before payroll consumes it.

==================================================
13. PAY AND ALLOWANCES MODULE
==================================================

Parent menu:

Pay & Allowances

Child menus:

- Pay Scale
- Benefits
- Bonus
- Payroll
- EOBI
- Tax
- EOBI Eligibility

All business data must be database-backed.

==================================================
14. PAY SCALE WORKSPACE
==================================================

Tabs:

- Rules
- Pay Scale
- Allowances
- TADA
- Leave
- Create Package

Forms are closed by default.

Rules tab fields:

- Type
- Name
- Date From
- Date To

Rule Type options correspond to the configuration areas, such as:

- PayScale
- Allowances
- TADA
- Leave

Do not replace this simple rule-registration form with an invented payroll calculation rule form.

Pay Scale fields:

- Rules
- Contract
- Frequency
- Rate
- Basic Salary
- Increment Salary
- Applicable Type
- Applicable After
- Maximum Salary
- Increment Month
- Current Pay where applicable

Pay Scale grid must show the values saved by the form, including:

- ID
- Scale
- Basic Salary
- Increment Salary
- Frequency Type
- Contract Type
- Rate Type
- Maximum Salary
- Current Pay
- Actions

Salary scale data feeds:

- Person registration
- Staff edit Accounts tab
- Payroll basic salary
- Allowance calculations
- Package setup
- Deduction per-day/per-hour basis

On person registration/edit:

- Scale must be a dropdown from saved SalaryScales.
- Selecting a scale must populate salary data.
- Basic, increment, max and current pay are loaded from the selected scale.
- Per-day and per-hour values are calculated using working-day/working-hour rules.
- Do not keep Scale as a free-text field.

==================================================
15. ALLOWANCES
==================================================

Nested tabs:

- Allowance
- Appt Allowance
- Shift Allowance

“Night Allowance” has been generalized to “Shift Allowance”.

General Allowance fields:

- Name
- Scale
- Allowance Type
- Contract
- Frequency
- Rate
- Pay Type
- Pay Value

General Allowance grid fields:

- ID
- Allowance Ref
- Allowance Name
- Allowance Type
- Contract Type
- Pay Value
- Frequency Type
- Rate Type
- Pay Type
- Calculated Value
- Actions

Calculation:

- Percentage:
  CalculatedValue = selected salary base × PayValue / 100

- Fixed:
  CalculatedValue = PayValue

Appointment Allowance fields:

- Designation
- Scale
- Allowance Type
- Contract
- Frequency
- Rate
- Pay Type
- Pay Value

Designation options must come from the existing JobTitles/designation API.
Allowance Type options must come from PlatformTypes.AllowanceTypes.
Scale options come from SalaryScales.
Contract/Frequency/Rate options come from their corresponding PlatformTypes tables.

Appointment Allowance grid:

- ID
- Allowance Ref
- Designation Name
- Allowance Type
- Pay Value
- Actions

Reference pattern:

A-{ScaleReference}

Example:

A-RLT-4

Shift Allowance fields:

- Shift
- Optional Scale, where applicable
- Allowance Type
- Pay Value
- other approved contract/frequency/rate fields if required by the current contract

Shift dropdown must come from the attendance shift/schedule source, not hardcoded UI-only values.

Shift Allowance grid:

- ID
- Allowance Ref
- Allowance Type
- Shift
- Pay Value
- Actions

General allowance values must not be duplicated when seed/import logic is run repeatedly.
Use tenant-aware natural keys and idempotent seed/import behavior.

==================================================
16. TADA AND LEAVE
==================================================

TADA form uses the same modal/form design as Allowance.

TADA fields:

- Name
- Scale
- Allowance Type
- Contract
- Frequency
- Rate
- Pay Value

TADA grid:

- ID
- Scale
- TADA Name
- TADA Ref
- Frequency Type
- Contract Type
- Rate Type
- TADA Type
- Pay Values
- Actions

Lookup sources:

- Scale: SalaryScales
- Allowance/TADA Type: PlatformTypes.AllowanceTypes or TadaTypes as defined by the current API contract
- Contract: PlatformTypes.ContractTypes
- Frequency: PlatformTypes.FrequencyTypes
- Rate: PlatformTypes.RateTypes

Leave form fields:

- Name
- Scale
- Leave Type
- Contract
- Frequency
- Rate
- Leave
- Applicable Type
- Applicable After
- Value Type
- Type
- Applicable Value

Leave grid:

- ID
- Scale
- Leave Name
- Leave Ref
- Frequency Type
- Contract Type
- Rate Type
- Leave Type
- Total Leave
- Actions

Leave Type options must come from PlatformTypes.LeaveTypes.
Do not use empty hardcoded dropdown arrays.

==================================================
17. CREATE PACKAGE
==================================================

Create Package combines:

- Scale
- Allowance
- TADA
- Leave

Fields:

- Scale
- Allowances
- TADA
- Leave

Grid:

- ID
- Package Ref
- Scale Ref
- Allowance Ref
- TADA Ref
- Leave Scale Ref
- Actions where permitted

Package references should be understandable and unique within a tenant, for example:

PKLT-1
PKLT-2

Saved packages are assigned on Person HR (`PersonHrProfiles.SalaryPackageId`) and drive payroll scale/allowance/TADA resolution.
Selecting a package on hire/edit syncs Scale + salary snapshot from the package’s SalaryScale; ScaleDate drives CurrentPay increments.

==================================================
18. BENEFITS MODULE
==================================================

Tabs:

- Benefits Rules
- Benefits Parameter

Database-backed entities include:

- PayrollBenefitRules
- PayrollBenefitParameters
- PayrollBonusDistributions

Benefits Rules fields include:

- Benefits Type
- Name
- Company
- Entitled
- Contract
- Frequency
- Valid From
- Valid To
- Maximum Expense
- Service Status
- Scale
- WEF
- other approved sharing/service-limit fields

Dynamic organization behavior:

1. Company is selected from the organization tree.
2. Entitled options are loaded from that company’s descendant branches/departments/nodes.
3. Do not show unrelated tenant organizations.
4. Saved OrganizationId/EntitledId values must be validated against the tenant tree.

Benefit reference format:

B-{ScaleReference}

Example:

B-RLT-4

Benefits Rules grid includes fields such as:

- ID
- Name
- Benefit Ref
- Benefits Type
- Valid From
- Valid To
- Maximum Expense
- Frequency ID
- Minimum Service
- Service Status ID
- WEF
- Maximum/Minimum PH
- Ineligible
- Share Type
- Company Share
- Staff Share
- Organization ID
- Company Name
- Entitled
- Actions

Grid values are editable inline only with permission.

Benefits Parameter fields:

- Benefits Type
- Benefits Rule
- Name
- Period From
- Period To
- Minimum Service
- Amount Type
- Pay Type
- Company Share
- Staff Share

Dependency behavior:

- Selecting Benefits Type filters Benefits Rule.
- Benefits Rule must belong to the selected type.
- No cross-tenant or invalid rule assignment is allowed.

Bonus-specific dynamic section:

When Benefits Type = Bonus, show Bonus Distribution fields:

- Month
- Basic %
- Service %
- Service Year
- Assessment %
- Attendance %
- Leave %
- Discipline %
- Installment count

When type is EOBI or another non-Bonus benefit:

- hide the Bonus Distribution section;
- do not submit stale hidden bonus fields;
- clear or ignore those values server-side.

==================================================
19. BONUS MODULE
==================================================

Bonus workflow:

1. Select an active, valid Bonus Benefit Rule.
2. Select month/year as applicable.
3. Generate employee bonus lines.
4. Review/edit authorized component values.
5. Process.
6. Verify.
7. Approve.
8. Pay according to the approved workflow.

Bonus run metadata includes:

- Created By
- Verified By
- Approved By
- Status
- Rule
- Month
- Year

Employee bonus lines include:

- ID
- Valid/selected state
- Rule Name
- Full Name
- LT Number
- Designation
- Department
- Date of Joining
- Bonus Amount
- Basic-B
- Attendance-B
- Leave-B
- Discipline-B
- Assessment-B
- Service-B
- Service Years
- Month
- Year
- Total Bonus
- percentage components
- Installment Amount
- Installment count
- Is Approved
- Is Paid
- Is Active
- Actions

Bonus calculation concept:

BasicBonus =
ApplicableBonusBase × BasicPercentage / 100

ServiceBonus =
ApplicableBonusBase × ServicePercentage / 100

AttendanceBonus =
ApplicableBonusBase × AttendancePercentage / 100

AssessmentBonus =
ApplicableBonusBase × AssessmentPercentage / 100

LeaveBonus =
ApplicableBonusBase × LeavePercentage / 100

DisciplineBonus =
ApplicableBonusBase × DisciplinePercentage / 100

TotalBonus =
sum of all valid components

InstallmentAmount =
TotalBonus / InstallmentCount

The actual bonus base for each component must follow the current backend contract.
Do not invent a different base without approval.

Leave and approved component fields may be editable in the grid for authorized users.

Bonus data is stored in:

- PayrollBonusRuns
- PayrollBonusLines
- PayrollBonusDistributions

Only active, approved bonus lines from an approved run should flow into payroll.

==================================================
20. PAYROLL MODULE
==================================================

Payroll is connected to:

- Active staff directory/person profiles
- Salary scales
- General allowances
- Appointment allowances
- Shift allowances
- Benefits
- Bonus
- Attendance deduction
- Attendance adjustments
- Approved overtime
- Tax settings
- EOBI settings and eligibility

Payroll source population:

All active employees should appear in the monthly payroll workspace.
An employee must not disappear merely because salary or benefit configuration is incomplete.
Missing configuration should produce zero values and a visible validation/review indication.

Basic salary / CurrentPay (server-authoritative):

1. Prefer Person → SalaryPackage → SalaryScale when `PersonHrProfiles.SalaryPackageId` is set.
2. CurrentPay = BasicSalary + completed ScaleDate years × YearlyIncrement (after ApplyAfter), capped at MaximumSalary (`PayrollCurrentPayCalculator`).
3. Payroll draft/generate always recomputes CurrentPay; do not trust a stale stored CurrentPay alone.
4. Fallback without package: PersonHrProfile.BasicSalary / SalaryScale by Scale name.
5. Zero if no salary data exists

Allowances:

- Prefer package scale; GENERAL filtered by package AllowanceReference when set; APPT by designation; SHIFT/NIGHT by AttendanceMap ShiftCode.
- TADA filtered by package TadaReference when set; Leave package ref is non-cash metadata.
- Legacy salary-scale medical/travel/other values may be used only as a compatibility fallback when no new allowance configuration exists.
- Do not count both new allowances and legacy fallback values simultaneously.
- Phase-1 mapping: attendance approved adjustment ≈ period adjustment; OtherDeduction ≈ manual; Proficiency/Loan not wired.

Benefits:

- Use active benefit rules valid for the payroll period.
- Match scale, organization/descendant organization, contract/status and minimum service.
- Use parameter-level company/staff shares when configured.
- Otherwise use rule-level shares.
- Company share is employer cost.
- Staff share is employee deduction.
- Do not add company share directly to employee cash unless the benefit type explicitly requires it.

Bonus:

- Include only active and approved lines from approved runs.
- If installments exist, include only the installment due for the current payroll month.
- Prevent the same installment from being paid twice.

Overtime:

- Include only active and explicitly approved overtime.
- Overtime rule must be active and approved.

Attendance:

- Use finalized NetDeduction.
- Pending Review attendance should prevent final payroll approval.
- Positive approved attendance adjustment adds to gross pay.
- Negative approved adjustment adds to deductions.

Tax calculation concept:

MonthlyTaxable =
Basic + EmployeeAllowances + Bonus + Overtime + PositiveAdjustments

AnnualTaxable =
MonthlyTaxable × 12

MonthlyTax =
(FixedTaxForSlab + ExcessAmount × SlabRate) / 12

Use the effective tax slab for the payroll date.

EOBI:

- Employee must be marked eligible.
- Use the effective EOBI setting.
- Contribution base uses the configured minimum wage/cap rules.
- Calculate employee and employer shares separately.

Payroll formulas:

GrossPay =
Basic
+ Allowances
+ Bonus
+ Overtime
+ PositiveApprovedAdjustments

TotalDeduction =
AttendanceDeduction
+ StaffBenefitDeduction
+ Tax
+ EmployeeEOBI
+ OtherDeduction
+ AbsoluteValueOfNegativeApprovedAdjustment

NetPay =
max(0, GrossPay - TotalDeduction)

Round financial amounts to two decimals using a consistent accounting rounding policy.

Payroll lifecycle:

- Draft/Create
- Process
- Verify where configured
- Approve
- Pay

Do not allow Paid payroll to be silently regenerated.
Changes after approval/payment must use an explicit controlled adjustment/reversal workflow.

==================================================
21. EOBI, TAX AND ELIGIBILITY
==================================================

EOBI configuration, tax slabs and employee eligibility are database-backed.

The system must support:

- effective date ranges;
- tenant isolation;
- employee/company contribution rates;
- minimum wage/contribution base;
- maximum contribution base;
- employee eligibility;
- tax slab ranges;
- fixed tax plus percentage on excess;
- prevention of overlapping effective rules where invalid.

==================================================
22. CHAT MODULE
==================================================

Chat backend is already functional.
UI improvements must not break backend behavior.

Chat database entities include:

- ChatWorkspaces
- ChatContactRequests
- ChatConversations
- ChatConversationMembers
- ChatMessages
- ChatMessageReactions
- ChatMessageDeletions
- ChatAttachments
- ChatBlocks
- ChatRuleSettings

Chat functionality includes:

- Direct messages
- Group conversations
- Contact requests
- Text messages
- Attachments
- Replies
- Forwarding
- Reactions
- Editing
- Delete for me
- Delete for everyone
- Read/delivery information
- Conversation search
- Shared media/files/links
- Mute
- Pin
- Blocking
- Group member/role management
- Unread positioning
- Mentions
- View-once media
- SignalR updates

Chat Rules is a child menu under Chat.
Rules must be saved in ChatRuleSettings, not hardcoded only in React.

Editing rule:

- Default edit window: exactly 15 minutes.
- Text messages only.
- Media/document messages cannot be edited.
- Unlimited edits are allowed inside the window.
- Show a permanent Edited label.
- Recipient sees only the current text.
- Return a clear message after expiry:
  “This message can no longer be edited as the 15-minute window has passed.”

Delete for Everyone:

- Default window: 60 hours.
- Allowed even if recipient read the message.
- Replace content with:
  “This message was deleted”
- After expiry, disable Delete for Everyone.
- Delete for Me remains available indefinitely.
- Offline recipients are updated silently on reconnect.

After Delete for Everyone:

- Disable Message Info.
- Clear delivery/read metadata as required by policy.
- Do not expose stale receipt information.

View Once:

- Allowed for supported media in direct conversations.
- Unopened expiry default: 14 days/336 hours.
- After recipient opens it, it is immediately consumed.
- Block forwarding, download, copying, sharing and starring.
- Remove the stored physical media after consumption/expiry.
- Notify clients that it was opened/expired.

Platform limitation:

A normal browser cannot guarantee operating-system-level screenshot or screen-recording blocking.
Do not falsely claim full screenshot protection.
True secure-screen blocking requires a native mobile/desktop wrapper capable of using OS secure-window APIs.

Unread behavior:

- Track last read message ID per conversation/member.
- Open at the first unread message.
- Show an unread divider with exact count.
- Track direct @mentions.
- Show a persistent mention badge in the conversation list.
- Provide a jump-to-mention action.

Media sending:

1. File selection must not immediately upload/send.
2. Open a preview.
3. Images/videos show preview.
4. Documents show name, type and size.
5. Allow caption where supported.
6. Allow View Once where permitted.
7. Send only after explicit confirmation.
8. Cancel discards the staged file.

Chat UI:

- Professional social-app layout.
- Conversation list can be hidden with a hamburger/privacy control.
- Active conversation expands when the list is hidden.
- Top message notification is slim.
- It includes sender, preview, inline reply and close button.
- Auto-dismiss after approximately five seconds unless interaction requires it.
- No voice/video calling requirement.
- No voice-message feature requirement.

==================================================
23. CHAT VIEW-ONCE CLEANUP SCHEDULER
==================================================

ChatViewOnceCleanupService runs approximately hourly.

It:

- loads tenant chat rules;
- identifies unopened expired view-once attachments;
- marks them expired;
- deletes the physical file;
- clears inaccessible storage references;
- processes records in controlled batches.

It must be safe to rerun.

==================================================
24. LIBRARY MODULE
==================================================

Parent menu:

Library

Child menus:

- Library Type
- Library
- File Converter
- Generate Invoice

Library Type tabs:

- Library Category
- Library Type
- Sub Type

Entities:

- LibraryCategories
- LibraryTypes
- LibrarySubTypes

Relationships:

- Category is the top level.
- Library Type belongs to a category where defined.
- Sub Type belongs to a Library Type.
- Dropdowns must load from APIs/database.

Library content tabs:

- Document
- Template
- Pictures

Document fields include:

- Type
- Name
- Remarks/Description
- File path/upload
- Document type

Store metadata in SQL Server.
Store physical files using the application’s upload/storage strategy.
Do not store large files in localStorage.

Entities include:

- LibraryDocuments
- LibraryTemplates

File Converter:

- Current required formats:
  PDF
  Word
  Excel

- Provide drag-and-drop/upload.
- Detect the selected source format.
- Let the user select a valid target format.
- Show file name and size.
- Convert only after explicit action.
- Provide download/reset actions.
- Reject unsupported conversions clearly.
- Principal engine: LibreOffice headless (high-fidelity). Setup: `docs/file-converter-libreoffice.md`.
- Browser JS libraries for reconstruction / preview-style analysis when LibreOffice is unavailable or fails.
- PDF→Excel uses a specialized client table extractor (column clustering); do not pretend layout-perfect Excel.
- OCR is future-only for scanned / image-only PDFs — not used for selectable-text PDFs.
- Never store source/result files in SQL; server temp folders only, deleted after convert.
- Do not claim perfect layout preservation for browser-only conversions.

Generate Invoice:

- Professional invoice editor/form.
- Invoice header/customer details.
- Issue date and due date.
- Currency.
- Editable invoice lines.
- Quantity.
- Unit price.
- Line total.
- Subtotal.
- Tax.
- Discount.
- Grand total.
- Notes/status.
- Preview/download/print where implemented.

Entities:

- GeneratedInvoices
- GeneratedInvoiceLines

==================================================
25. ACCOUNTS MENU
==================================================

Parent:

Accounts

Children:

- Payment (ROZ)
- Receipt (ROZ)
- Roznamcha Update
- Reports
- Report Filter
- Daily Updates
- Bank Account Report
- Show Record

These menus are database-backed and permission-aware.

Important status:

The menu and frontend grid shells exist, but full backend financial behavior for these screens must not be invented until the user provides the business workflow.

Payment (ROZ) header has independent buttons:

- Form
- Process
- Settled
- Date From
- Date To

Process and Settled are separate actions.
Their business relationship is not yet defined.

Payment (ROZ) columns:

S No, Ref, ID, Old_Ref, Type, Project, ProjectId, toCatId,
Category, AcctName, To_AcctId, From Acc No, To_Category,
ToAcctName, To Acct No, Trans Type, Trans Mode, Created Date,
BankRef, Instrument No, Discriptions, TransDate, Adjustment,
Debit, UsdDebit, Qty, Rate, Status, isDeleted, isApproved,
isLocked, Attachment, Image, Lib_Ref, Remarks, Actions.

Receipt (ROZ) columns:

S No, ID, Ref, Old_Ref, Type, Project, ProjectId, toCatId,
Category, AcctName, To_AcctId, From Acc No, To_Category,
ToAcctName, To Acct No, Trans Type, Trans Mode, Created Date,
BankRef, Instrument No, Discriptions, TransDate, Adjustment,
Debit, UsdDebit, Qty, Rate, Status, isDeleted, isApproved,
isLocked, Attachment, Image, Lib_Ref, Remarks, Actions.

Roznamcha Update columns:

ID, S No, Ref, Type, Category, AcctName, From A/C No,
To_Category, TokenName, To Acct No, Trans Type, Trans Mode,
Created Date, ProcessDate, Instrument No, Discriptions,
Bank_Ref, CheckNo, TransDate, Adjustment, Credit, UsdCredit,
Qty, Rate, Status, isDeleted, Attachment, Image, Lib_Ref,
Remarks, Actions.

Keep the spelling “Discriptions” where the user explicitly requested it.

==================================================
26. PROCESS AND APPROVAL WORKFLOW
==================================================

Relevant entities include:

- Processes
- ProcessMaster
- ProcessApprovalCodes
- ProcessWorkflowCategories
- ProcessWorkflowStatuses
- ProcessWorkflowActionTypes
- ProcessWorkflowPriorities
- ProcessReports
- ProcessReportSteps
- ProcessReportActions
- ProcessReportAttachments
- ProcessCategoryApprovers
- WorkflowApprovalRequests

Approval categories can include Deduction.

An approver is configured for the category.
Approval codes are stored securely in the database.
Do not return plain approval codes to the frontend.

Deduction approval example:

1. User selects Approve from Action.
2. Approve button becomes visible.
3. Clicking Approve opens a security-code input.
4. User submits the code.
5. Backend validates approver permission/category/code.
6. Backend approves within a transaction.
7. Backend records audit information.
8. Frontend refreshes after success.

Never rely only on frontend button visibility for authorization.

==================================================
27. PROCESS REPORT AUTO-TRANSFER SCHEDULER
==================================================

ProcessReportAutoTransferService:

- runs on application startup;
- then approximately every five minutes;
- calls dbo.usp_ProcessReport_AutoTransferOverdue;
- transfers overdue workflow reports according to configuration;
- handles cancellation without crashing the host;
- logs genuine failures.

==================================================
28. ASSESSMENT SCHEDULER
==================================================

AssessmentSchedulerService runs:

- on startup;
- approximately hourly.

It evaluates active tenants and scheduled assessment periods.

The current hierarchy concept uses organization tree and job-title ranks, for example:

- CEO: 700
- Duty CEO: 600
- Manager: 500
- Deputy Manager: 400
- Assistant Manager: 300
- Supervisor/Team Lead: 200
- Agent/Bell Boy: 100

Assessments are generated for eligible lower-ranking/direct staff in descendant organization nodes.

The scheduler may create reminder AppNotes for incomplete assessments.

It must not create duplicate assessment records or duplicate reminders on every cycle.

Assessment values can feed bonus calculations only when the bonus rule explicitly includes an Assessment percentage and the source assessment data is finalized.

==================================================
29. DATABASE DESIGN RULES
==================================================

Use existing table names.
Do not perform a global rename.

Database categories include:

Identity:

- AspNetUsers
- AspNetRoles
- AspNetUserRoles
- related Identity tables

Tenant/security:

- Tenants
- AccessGroups
- Features
- Menus
- permissions/access tables
- SecurityAuditLogs

Person/HR:

- Persons
- PersonProfiles
- PersonAddresses
- PersonContacts
- PersonEducations
- PersonExperiences
- PersonEmploymentHistory
- Vacancies
- StaffVacancy
- JobTitles
- SalaryScales

Attendance:

- AttendanceRecords
- AttendanceRuleSettings
- AttendancePolicies
- AttendanceEntryTypes
- AttendanceWorkModes
- AttendanceMapRules
- AttendanceDailyFinalizations
- AttendanceDeductionPeriods
- AttendanceDeductionLines
- AttendanceDeductionRequests
- AttendanceDeductionHistory
- AttendanceMonthlySettlements
- EmployeeTimingSchedules

Chat:

- ChatWorkspaces
- ChatConversations
- ChatConversationMembers
- ChatMessages
- ChatAttachments
- ChatMessageReactions
- ChatMessageDeletions
- ChatContactRequests
- ChatBlocks
- ChatRuleSettings

Process:

- Processes
- ProcessReports
- ProcessReportSteps
- ProcessReportActions
- ProcessReportAttachments
- workflow/approval tables

Pay and Allowances:

- SalaryScales
- PayRules
- PayScaleRuleRegistrations
- PayScaleAllowances
- SalaryPackages
- PayrollBenefitRules
- PayrollBenefitParameters
- PayrollBonusDistributions
- PayrollBonusRuns
- PayrollBonusLines
- PayrollRuns
- PayrollLines
- PayrollEobiSettings
- PayrollEobiEligibility
- PayrollTaxSlabs

Platform lookup tables:

- PlatformTypes.AllowanceTypes
- PlatformTypes.AnnouncementTypes
- PlatformTypes.AssessmentTypes
- PlatformTypes.AttendanceTypes
- PlatformTypes.BenefitTypes
- PlatformTypes.ContractTypes
- PlatformTypes.FrequencyTypes
- PlatformTypes.LeaveTypes
- PlatformTypes.RateTypes
- PlatformTypes.TadaTypes

Lookup rules:

- Do not hardcode dropdown values when a lookup table/API already exists.
- Filter lookups by tenant and active state.
- Return stable IDs and display names.
- Validate selected IDs again on the backend.
- Use foreign keys where practical.
- Use unique indexes to prevent duplicate tenant records.
- Store CreatedBy/CreatedOn and UpdatedBy/UpdatedOn where the model supports auditing.
- Prefer decimal for money.
- Avoid floating-point types for financial calculations.

==================================================
30. PERFORMANCE RULES
==================================================

Daily Attendance should feel immediate.

Use:

- optimized database projections;
- AsNoTracking for read-only requests;
- indexes on TenantId, PersonId and attendance date;
- no N+1 lookup queries;
- batch-load schedules/rules/statuses;
- React Query caching;
- route prefetching;
- background refetch;
- stale-while-revalidate behavior;
- virtualized/paged grids;
- cancellation of obsolete requests;
- compact response DTOs;
- SignalR invalidation followed by API refetch.

“Zero delay” is a UX goal, not a literal guarantee.
Show cached data immediately when available, then refresh silently.

Never make a page fast by returning incomplete or stale financial data without indicating its state.

==================================================
31. ERROR AND CANCELLATION HANDLING
==================================================

Expected cancellations include:

- browser navigation;
- client disconnect;
- aborted Axios request;
- application shutdown;
- SignalR reconnect.

Do not show these as fatal unhandled exceptions.

Catch cancellation only where appropriate and rethrow/ignore based on the host cancellation token.

Real SQL, validation, authorization and business errors must still be logged and returned in a user-friendly form.

Do not hide failures by globally swallowing Exception.

==================================================
32. CURRENT VERIFICATION AND KNOWN LIMITATIONS
==================================================

At the latest verified checkpoint:

- Backend Release build succeeded.
- Frontend production build succeeded.
- Existing automated tests passed.
- The repository may still contain unrelated user changes; preserve them.

Known environment issue:

A later payroll bonus-distribution migration could not be applied in one local environment because SQL Server LocalDB could not create an automatic instance.

Therefore:

- Do not assume every pending migration is applied.
- Run:
  dotnet ef migrations list
- Inspect:
  __EFMigrationsHistory
- Apply migrations only when the correct SQL Server instance is available.
- Never report database completion solely because code compiles.

Known platform limitation:

Browser-only chat cannot guarantee OS-level screenshot prevention.

Known pending business area:

The new Accounts/ROZ pages have defined UI and columns, but their complete financial posting, Process and Settled behavior still requires user-provided requirements.

==================================================
33. REQUIRED WORKFLOW FOR EVERY FUTURE CHANGE
==================================================

Before coding:

1. Read AGENTS.md.
2. Confirm backend/frontend paths.
3. Inspect the current related controller, service, DTO, entity, migration, API client and page.
4. Identify the authoritative database source.
5. Compare the requested fields exactly.
6. Identify ambiguity before inventing behavior.
7. Check permissions and tenant scope.

During coding:

1. Reuse shared UI components.
2. Keep forms closed by default.
3. Keep API and TypeScript DTO contracts aligned.
4. Keep calculations server-authoritative.
5. Use transactions for financial mutations.
6. Add idempotency to critical create/process/pay actions.
7. Avoid duplicate scheduler effects.
8. Never trust frontend-calculated money as final.
9. Record audit information.
10. Preserve existing unrelated work.

After coding:

1. Build backend:
   dotnet build Accounts\Accounts.csproj -c Release

2. Build frontend:
   npm run build
   from frontend\Frontend-Accounts

3. Run focused tests.

4. Verify migrations separately.

5. Verify:
   - add;
   - edit;
   - delete if allowed;
   - inline update;
   - refresh;
   - permission denial;
   - empty/loading/error states;
   - responsive layout;
   - duplicate submission;
   - tenant isolation.

6. Report separately:
   - what was implemented;
   - what was verified;
   - what remains pending;
   - whether database migrations were actually applied.

Do not say “fully completed” merely because the application compiled.

==================================================
FUNCTIONAL WORKFLOW AND BUSINESS LOGIC ADDENDUM
==================================================

This section explains how the application actually works at runtime.

Whenever modifying a module, trace the complete chain:

Frontend Screen
→ API Client
→ Controller
→ Permission Validation
→ Business Service
→ Database
→ Calculation/Transaction
→ SignalR Notification
→ Frontend Refetch/UI Update

The frontend must never be considered the authoritative source for attendance, payroll, deductions, approvals or security decisions.

==================================================
1. PERSON REGISTRATION TO PAYROLL DATA FLOW
==================================================

The person/staff registration process is the starting point for Attendance, Deduction and Payroll.

When a person is registered:

1. A Person record stores the main identity.
2. Staff/Vacancy records connect that person to:
   - Staff ID;
   - employee/LT number;
   - designation;
   - department;
   - organization node;
   - login/user account.
3. PersonHrProfile stores HR and salary-related information such as:
   - joining date;
   - salary scale;
   - basic salary;
   - current pay.
4. AttendanceMapRule connects the employee to:
   - attendance type;
   - attendance rule;
   - schedule/shift;
   - working mode where applicable.
5. Salary scale connects the employee to Pay & Allowances.

Scale / package workflow:

1. Frontend loads active SalaryPackages and/or SalaryScales through the API.
2. HR selects a package (preferred) or a scale.
3. Package → SalaryScale provides Basic / Increment / Max; ScaleDate drives CurrentPay.
4. On Save, `SalaryPackageId`, Scale name, ScaleDate, and salary values are persisted in PersonHrProfile.
5. Payroll resolves via package (when set) then ScaleDate CurrentPay formula.

Salary priority in Payroll:

1. SalaryPackage.SalaryScaleId → ScaleDate CurrentPay (Basic + increments)
2. PersonHrProfile.BasicSalary / Scale name → same ScaleDate formula
3. Zero when no salary is configured

An active employee must still appear in Payroll even when salary data is missing. Missing salary should produce zero values and a visible configuration warning, not remove the employee.

==================================================
2. ATTENDANCE RULE RESOLUTION
==================================================

Before calculating attendance, the system resolves:

1. Logged-in user/person/staff identity.
2. Tenant.
3. Employee AttendanceMapRule.
4. Attendance Type.
5. Attendance Rule Setting.
6. Employee schedule/shift for the selected date.
7. Working Day, Day Off, Holiday or employee-specific override.
8. Check-in/check-out records.
9. Camera/manual/supervisor information where applicable.

Only active and approved attendance rules should affect calculations.

Attendance types affect how attendance may be entered:

Check In/Out:

- Employee marks attendance through the portal.
- System validates the check-in window.
- Check-in, break and check-out are recorded.

By Supervisor:

- Employee cannot be considered present just because this attendance type is assigned.
- The assigned supervisor must manually enter check-in/check-out.
- No manual time means no completed attendance.
- After the absence/finalization deadline, missing attendance may become Absent.

Remote:

- Attendance is processed using the remote attendance workflow and its mapped rule.

Not Required:

- Employee is not required to mark attendance.
- The day should not create an ordinary shortage deduction.

==================================================
3. EMPLOYEE CHECK-IN FLOW
==================================================

When an employee presses Check In:

1. Frontend calls the attendance check-in endpoint.
2. Backend resolves the logged-in Person and mapped attendance rule.
3. Backend confirms self-check-in is allowed for the employee’s attendance type.
4. Backend resolves the correct attendance date and shift window.
   This also handles overnight shifts.
5. Backend checks the earliest allowed check-in:
   ShiftStart - BeforeCheckInMinutes.
6. If the employee is too early, check-in is rejected with a clear time-based message.
7. Backend checks whether an attendance record already has a check-in.
8. Duplicate check-in is rejected.
9. AttendanceRecord is created or updated.
10. CheckInUtc/current attendance timestamp is saved.
11. Frontend receives the updated “today” attendance state.
12. Application real-time notification may tell relevant clients to refresh.

Late calculation does not necessarily become financially final at check-in time. Final worked time and short time normally require check-out.

==================================================
4. BREAK FLOW
==================================================

When Toggle Break is pressed:

Starting a break:

1. Find the employee’s open attendance record.
2. Confirm check-in exists.
3. Confirm check-out does not exist.
4. Save BreakStartedUtc.

Ending a break:

1. Calculate minutes between BreakStartedUtc and current time.
2. Add those minutes to TotalBreakMinutes.
3. Clear BreakStartedUtc.

When Check Out is pressed while a break is still active:

1. The active break is closed automatically.
2. Its duration is added to TotalBreakMinutes.
3. Check-out is then saved.

Break duration is removed from actual worked time.

WorkedMinutes =
(CheckOut - CheckIn) - TotalBreakMinutes

==================================================
5. CHECK-OUT FLOW
==================================================

When an employee presses Check Out:

1. Backend finds the open attendance record.
2. It confirms that check-in exists.
3. It rejects duplicate check-out.
4. It closes any active break.
5. It saves CheckOutUtc.
6. Attendance becomes eligible for final calculation.
7. Worked time, shortage, overtime and late penalty can now be calculated.
8. The scheduler or report calculation updates daily finalization.
9. Deduction-related clients are notified/refreshed.

Before check-out:

- AH should remain blank on reporting grids.
- Final shortage should not be displayed as if confirmed.
- Attendance should remain Open/In Progress.

==================================================
6. BY-SUPERVISOR ATTENDANCE FLOW
==================================================

The By Supervisor screen works as follows:

1. Backend resolves the logged-in supervisor.
2. It identifies only the employees who fall within that supervisor’s permitted hierarchy/assignment.
3. Frontend displays those employees for the selected date.
4. Supervisor manually enters:
   - Check-In;
   - Check-Out.
5. Backend validates:
   - employee belongs to supervisor’s scope;
   - attendance type permits supervisor entry;
   - time format is valid;
   - check-out is not earlier than check-in unless it is a valid overnight shift.
6. AttendanceRecord is created or updated.
7. Break minutes are currently set/reset according to the supervisor save workflow.
8. The same finalization engine used for normal attendance calculates:
   - actual hours;
   - required hours;
   - shortage;
   - overtime;
   - late state;
   - deduction.
9. Daily Attendance, Timing Chart, Deduction and Payroll consume the resulting data.

Critical rule:

By Supervisor + no entered check-in/check-out must never display Present.

==================================================
7. CAMERA ATTENDANCE FLOW
==================================================

Camera attendance is an additional attendance evidence source.

Flow:

1. Camera check-in/check-out data is saved against an AttendanceRecord.
2. Backend validates the employee is mapped to the permitted attendance type.
3. Comparative attendance compares:
   - portal/manual check-in;
   - camera check-in;
   - portal/manual check-out;
   - camera check-out.
4. The system calculates differences using camera tolerance rules.
5. Suspicious or mismatched records may enter review.
6. Authorized reviewer decides using the camera-attendance workflow.
7. The stored procedure:
   dbo.usp_WorkflowApproval_DecideCameraAttendance
   saves the decision and effective times.
8. Effective approved times are used for final reporting/calculation.

Camera data should not silently overwrite approved manual data without the review workflow.

==================================================
8. ATTENDANCE DAY AND SCHEDULE DECISION
==================================================

For every employee/date, first decide whether the date is chargeable.

Possible sources:

- normal weekly schedule;
- public holiday;
- annual holiday;
- employee-specific schedule;
- manually selected Working Day;
- manually selected Day Off.

Rules:

Working Day:

- Required working minutes apply.
- Missing attendance can become Absent.
- Short time and overtime can be calculated.

Day Off:

- Required minutes become zero.
- No ordinary shortage.
- No ordinary deduction.

Holiday/Leave/Not Required:

- Treated as excused/non-chargeable.
- No ordinary attendance deduction.

Day On override:

- Saturday, Sunday or a holiday can be changed into a Working Day.
- Once changed, the full attendance rule applies.
- Required time, absence, late arrival and deduction can apply.

Day Off override:

- A normal working day can be turned off.
- Required hours and ordinary deductions must stop applying.

==================================================
9. DAILY ATTENDANCE FINALIZATION
==================================================

AttendanceFinalizationScheduler runs:

- once when the backend starts;
- then approximately every hour.

It is protected from overlapping with itself.

For every relevant employee/date, it creates or updates one AttendanceDailyFinalization record.

It must never generate duplicate finalizations for the same employee/date.

Decision flow:

A. Non-working day

If IsWorkingDay is false or RequiredMinutes is zero:

- State = DayOff
- IsFinalized = true
- WorkedMinutes = 0
- ShortMinutes = 0
- Deduction = 0

B. Excused day

If Leave/Holiday/Not Required applies:

- State = Excused
- IsFinalized = true
- ShortMinutes = 0

C. Explicit absence

If an authorized attendance action explicitly marks the employee absent:

- State = Absent
- IsFinalized = true
- IsFullDayAbsent = true
- ShortMinutes = RequiredMinutes

It should not remain Pending Review merely because it is absent.

D. Valid check-in and check-out

If both exist and CheckOut >= CheckIn:

WorkedMinutes =
floor(CheckOut - CheckIn in minutes) - BreakMinutes

ShortMinutes =
max(RequiredMinutes - WorkedMinutes, 0)

OvertimeMinutes =
max(WorkedMinutes - RequiredMinutes, 0)

State = Completed
IsFinalized = true

E. Check-in without check-out before deadline

- State = InProgress
- IsFinalized = false
- Final ShortMinutes = 0
- Final Deduction = 0

F. Check-in without check-out after deadline

- State = PendingReview
- IsFinalized = false
- Requires correction/review before final financial processing.

G. No attendance before deadline

- State = Open
- IsFinalized = false

H. No attendance after deadline

- State = Absent
- IsFinalized = true
- ShortMinutes = RequiredMinutes

==================================================
10. ATTENDANCE HOURS AND GRID VALUES
==================================================

AH:

- Actual Hours for that day.
- Calculated from a valid closed interval.
- Must remain blank while check-out is missing.

WHrs:

- Required working hours for the applicable attendance rule/schedule.

TWHrs:

- Cumulative required minutes/hours for the month up to that row/date.

TAHrs:

- Cumulative actual worked minutes/hours for the month.

CR/DB:

- Daily credit/debit difference.
- Positive when actual time exceeds required time.
- Negative when actual time is short.
- It should remain blank for:
  - open attendance;
  - unresolved missing checkout;
  - Day Off;
  - non-chargeable Holiday/Leave;
  - other non-final states where debit is not yet determined.

For a completed day:

CR/DB Minutes =
WorkedMinutes - RequiredMinutes

Examples:

Required 9 hours, worked 9 hours 4 minutes:
CR/DB = +00:04

Required 9 hours, worked 8 hours:
CR/DB = -01:00

Do not confuse cumulative TWHrs/TAHrs with daily WHrs/AH.

==================================================
11. LATE ATTENDANCE CALCULATION
==================================================

Late calculation begins after the configured grace period.

Example:

Shift Start = 09:00
Grace = 5 minutes

Arrival at 09:05 or earlier:

LateMinutes = 0

Arrival at 09:06:

LateMinutes = 1

Current late-band behavior:

- Any chargeable lateness below the extreme threshold maps to a 60-minute band.
- Lateness at/after ExtremeLateAfterMinutes maps to a 120-minute band.

For example:

- normal late band = 1 hour;
- extreme late band = 2 hours.

The displayed attendance status can reflect:

- Present;
- T-Present;
- 1 Hr Late;
- 2 Hr Late;
- another canonical status configured by the project.

Status display and financial deduction are related but not identical.

==================================================
12. COMPLETED-LATE DEDUCTION
==================================================

If an employee arrives late but completes all required hours:

1. WorkedMinutes >= RequiredMinutes.
2. Attendance may show T-Present/completed-late status.
3. If Completed Late Deduction is active:
   apply the configured percentage to the late band.
4. If the rule is inactive:
   the special reduced completed-late calculation is not applied.

Formula:

LatePenaltyMinutes =
LateBandMinutes × CompletedLateDeductionPercentage / 100

Example:

- Late band = 60 minutes
- Completed required working time
- Percentage = 50%

LatePenaltyMinutes = 30 minutes

Financial amount:

LatePenaltyAmount =
LatePenaltyMinutes / 60 × PerHourSalary

Important:

- Do not charge the same time twice.
- If ordinary shortage already represents those minutes, the late penalty must not overlap incorrectly.
- The reduced percentage applies only when required time was completed and the rule is active.

==================================================
13. ABSENT ALLOWANCE
==================================================

Attendance rules may allow a configured number of absent days per month.

Example:

Adjust Absent Days / Month = 1

Expected monthly behavior:

1. Count finalized chargeable absence days.
2. Exempt the permitted number according to the approved policy/order.
3. Only the remaining chargeable absences create full-day deductions.
4. Once a previously absent date is corrected to Present:
   - finalization is recalculated;
   - absence count is recalculated;
   - deduction is recalculated;
   - payroll preview/run must reflect the correction if still editable.

The same absence must not be waived and deducted simultaneously.

==================================================
14. DEDUCTION RATE CALCULATION
==================================================

Deduction gathers information from:

- active employee/person;
- HR salary profile;
- salary scale;
- working-day calendar;
- attendance daily finalization;
- attendance rules;
- overtime approval;
- adjustment approval.

Monthly salary is resolved first.

Per-day salary:

PerDay =
MonthlySalary / ChargeableWorkingDaysInMonth

Per-hour salary:

PerHour =
PerDay / RequiredWorkingHoursForDay

Per-minute salary:

PerMinute =
PerHour / 60

Short-time deduction:

ShortDeduction =
FinalizedDeductibleMinutes × PerMinute

Absent deduction:

AbsentDeduction =
PerDay

Completed-late deduction:

CompletedLateDeduction =
LatePenaltyMinutes × PerMinute

The deduction report should calculate/display Per Day and Per Hour even when a particular optional deduction rule is inactive, provided salary and schedule configuration exists.

==================================================
15. DEDUCTION STATE FLOW
==================================================

Deduction attendance states:

In Progress:

- Attendance is open/not final.
- Do not create final deduction.
- Do not send it to payroll as a confirmed charge.

Pending Review:

- Attendance is incomplete or inconsistent after deadline.
- Financial result is not final.
- Approval should be blocked until corrected/reviewed.

Finalized/Completed:

- Attendance data is stable.
- Short time, overtime and deduction may be consumed.

Absent:

- If explicit or automatically finalized after deadline, it is a finalized full-day shortage.
- It should directly calculate deduction after applying the monthly allowed-absence rule.
- It should not remain in review only because the employee is absent.

Day Off/Excused:

- No ordinary deduction.

==================================================
16. DEDUCTION REPORT FLOW
==================================================

When the Deduction screen opens:

1. Frontend sends selected year/month.
2. API calls GetDeductionReportAsync.
3. Backend obtains authorized organization-wide employees.
4. Stored procedure runs:

   dbo.usp_Attendance_DeductionReport

5. Stored procedure returns salary/report bases.
6. AttendanceService combines these with daily finalizations.
7. It calculates:
   - scheduled working minutes;
   - actual minutes;
   - shortage;
   - Per Day;
   - Per Hour;
   - gross deduction;
   - late penalty;
   - adjustment;
   - overtime bonus;
   - net deduction/final pay.
8. Frontend displays all employees, including employees with zero deduction where required.
9. Deduction rates may be cached for the tenant/month/person set.
10. Attendance updates invalidate/refetch deduction data.

The stored-procedure projection must include every mapped property, including AdjustmentRemarks.

==================================================
17. DEDUCTION ADJUSTMENT AND APPROVAL
==================================================

Save Adjustment:

1. User selects an employee deduction row.
2. Enters:
   - adjustment amount;
   - remarks.
3. Backend saves the adjustment as staged/unapproved.
4. Payroll must not use it as approved adjustment yet.

Adjustment sign:

- Positive adjustment adds money back to employee gross pay.
- Negative adjustment increases deductions.

Approval:

1. Authorized user selects Approve.
2. Approve button becomes available.
3. Clicking it opens the security-code field.
4. Backend finds ProcessApprovalCodes for:
   ProcessName = DeductionAdjustment
5. Backend validates:
   - tenant;
   - permission;
   - code;
   - report has no blocking review issue.
6. On success:
   - IsAdjustmentApproved becomes true;
   - approver/audit fields are saved;
   - final deduction is recalculated;
   - DeductionChanged event is published.
7. Frontend refetches the deduction report.

Never perform approval using frontend validation only.

==================================================
18. OVERTIME FLOW
==================================================

Overtime minutes:

OvertimeMinutes =
max(WorkedMinutes - RequiredMinutes, 0)

Overtime bonus is available only when:

- attendance rule is active;
- attendance rule is approved;
- overtime bonus setting is active;
- overtime is explicitly approved.

If overtime is inactive:

- overtime approval button should not be shown;
- payroll overtime amount should remain zero.

If activated later:

- Deduction/attendance screen reads the rule;
- approval UI becomes available automatically;
- approved overtime amount flows to payroll.

==================================================
19. PAYROLL PREVIEW FLOW
==================================================

When Payroll screen opens for a month:

1. Frontend requests payroll-workspace?year=...&month=...
2. Backend checks VIEW permission.
3. Backend checks whether PayrollRun already exists.
4. If a saved run with lines exists:
   - return saved PayrollRun and PayrollLines.
5. If no run exists or an old draft has no lines:
   - build a live preview.
6. Preview loads all active StaffDirectoryRows.
7. One PayrollLine preview is built for every active employee.
8. Missing salary configuration results in zero amounts, not a missing employee.

This is why an empty payroll grid is normally a source/query/API issue, not expected behavior when active staff exist.

==================================================
20. PAYROLL DATA SOURCE FLOW
==================================================

For every employee:

A. Identity details come from StaffDirectoryRows:

- PersonId
- StaffId
- LT/Employee Number
- Full Name
- Designation
- Department
- Organization

B. Salary comes from:

- PersonHrProfile.SalaryPackageId → SalaryPackages → SalaryScale (preferred)
- ScaleDate-based CurrentPay via PayrollCurrentPayCalculator
- PersonHrProfile.Scale name / SalaryScale fallback when no package

C. Allowances come from PayScaleAllowances (package scale + refs):

General allowance:

- matched by salary scale;
- when package AllowanceReference is set, GENERAL must match that ref (CSV supported).

Appointment allowance:

- matched by designation;
- optional scale match.

Shift allowance:

- employee’s latest AttendanceMapRule supplies ShiftCode;
- allowance matches shift and optional scale.

TADA:

- matched by package scale; filtered by TadaReference when set; cash into Gross.
- Leave package ref is metadata only (non-cash).

Legacy fallback:

If there is no new non-shift allowance configuration for the scale:

- MedicalAllowance
- TravellingAllowance
- Other

may be read from SalaryScale.

Never add legacy fallback and the equivalent new allowances twice.

D. Benefits come from:

- PayrollBenefitRules
- PayrollBenefitParameters

E. Bonus comes from:

- PayrollBonusRuns
- PayrollBonusLines

Only approved, active bonus lines in an Approved run are eligible.

F. Attendance values come from Deduction report:

- NetDeduction
- approved adjustment
- approved overtime

G. Tax comes from active PayrollTaxSlabs.

H. EOBI comes from:

- effective EobiSetting;
- employee EobiEligibility.

==================================================
21. BENEFIT CALCULATION FLOW
==================================================

Payroll excludes Benefit Rules whose BenefitsType is Bonus from normal benefit processing because Bonus has its own run/approval workflow.

A non-Bonus benefit is applicable when its rule matches:

- payroll period;
- effective dates;
- employee scale where specified;
- organization/entitled organization;
- minimum service;
- active/eligibility conditions;
- frequency rules.

The employee’s service years are calculated from JoiningDate.

If Benefit Parameters exist:

- use matching parameters for the period/minimum service.

If no parameter exists:

- use the rule-level CompanyShare and StaffShare.

Fixed share:

ShareAmount = configured amount

Percentage share:

ShareAmount =
BasicSalary × SharePercentage / 100

CompanyShare:

- stored as EmployerBenefitAmount;
- treated as employer cost;
- not automatically added to employee Net Pay.

StaffShare:

- stored as StaffBenefitDeduction;
- subtracted from employee payroll.

==================================================
22. BONUS GENERATION FLOW
==================================================

Bonus setup consists of:

1. Benefit Rule with BenefitsType = Bonus.
2. Benefit Parameter connected to that rule.
3. PayrollBonusDistribution containing:
   - Month;
   - Basic percentage;
   - Service percentage;
   - Service years;
   - Attendance percentage;
   - Leave percentage;
   - Discipline percentage;
   - Assessment percentage;
   - installment count.

When generating a Bonus run:

1. User selects a valid Bonus Benefit Rule.
2. Backend determines eligible active employees.
3. Backend reads salary, joining/service, attendance, leave, discipline and assessment bases according to available data.
4. Component amounts are calculated.
5. Total Bonus is the sum of components.
6. Installment amount is calculated if installments are configured.
7. PayrollBonusRun and PayrollBonusLines are saved.
8. Authorized users review/edit permitted fields.
9. Run moves through processing/approval.
10. Only approved lines in an Approved run become payroll input.

Component concept:

TotalBonus =
BasicBonus
+ ServiceBonus
+ AttendanceBonus
+ LeaveBonus
+ DisciplineBonus
+ AssessmentBonus

InstallmentAmount =
TotalBonus / InstallmentCount

If a source such as Leave, Discipline or Assessment is not fully integrated, it must remain clearly identified as pending/manual input. Do not pretend a zero value was calculated from a source that was never loaded.

==================================================
23. BONUS INSTALLMENT TO PAYROLL FLOW
==================================================

Payroll evaluates each approved bonus line for the selected payroll month.

A bonus installment is due when its month offset is within the configured installment period.

For an eligible installment:

BonusAmount =
InstallmentAmount when greater than zero,
otherwise TotalBonus

The same paid installment must not be added repeatedly.

When Payroll is finalized/paid, applicable bonus lines are marked paid according to their installment timing.

==================================================
24. TAX FLOW
==================================================

Taxable monthly income currently includes:

- Basic Salary
- Employee allowance amount
- Bonus
- Overtime
- Positive attendance adjustment

MonthlyTaxable =
BasicSalary
+ AllowanceAmount
+ BonusAmount
+ OvertimeAmount
+ max(AttendanceAdjustment, 0)

AnnualTaxable =
MonthlyTaxable × 12

The applicable slab is found from PayrollTaxSlabs.

AnnualTax =
FixedTaxAmount
+ (AnnualTaxable - SlabFromAmount) × RatePercentage / 100

MonthlyTax =
AnnualTax / 12

Tax is stored in PayrollLine.TaxAmount.

==================================================
25. EOBI FLOW
==================================================

EOBI is calculated only if:

- an active effective EOBI setting exists;
- employee is marked eligible for the payroll period.

Wage base:

WageBase =
max(BasicSalary, MinimumWage)

If MaximumContributionBase is configured:

ContributionBase =
min(WageBase, MaximumContributionBase)

Employee EOBI:

EmployeeEobi =
ContributionBase × EmployeeRatePercentage / 100

Employer EOBI:

EmployerEobi =
ContributionBase × EmployerRatePercentage / 100

EmployeeEobi is deducted from employee pay.

EmployerEobi is stored as employer cost and is not subtracted from employee Net Pay.

==================================================
26. FINAL PAYROLL CALCULATION
==================================================

Positive adjustment:

PositiveAdjustment =
max(AttendanceAdjustment, 0)

Negative adjustment:

NegativeAdjustment =
max(-AttendanceAdjustment, 0)

Gross Pay:

GrossPay =
BasicSalary
+ AllowanceAmount
+ BonusAmount
+ OvertimeAmount
+ PositiveAdjustment

Total Deduction:

TotalDeduction =
AttendanceDeduction
+ StaffBenefitDeduction
+ TaxAmount
+ EmployeeEobiAmount
+ OtherDeduction
+ NegativeAdjustment

Net Pay:

NetPay =
max(GrossPay - TotalDeduction, 0)

All monetary values are rounded to two decimals using MidpointRounding.AwayFromZero.

EmployerBenefitAmount and EmployerEobiAmount are employer liabilities/costs and are not included in employee Net Pay under the current calculation.

==================================================
27. PAYROLL SAVE AND STATUS FLOW
==================================================

Preview:

- calculated dynamically;
- not necessarily persisted yet.

Create/Generate:

1. User selects month/year/pay date.
2. Backend builds current payroll lines.
3. One PayrollRun is created per tenant/month/year.
4. Run number format:
   PAY-yyyyMM
5. Initial status:
   Draft
6. PayrollLines are saved.

Regenerate:

- only Draft payroll can be regenerated;
- existing Draft lines are replaced with recalculated lines;
- verification/approval metadata is cleared.

Edit:

- individual PayrollLine can be edited only while run is Draft;
- permission is required;
- backend recalculates GrossPay, TotalDeduction and NetPay after the edit.

Process:

- allowed only from Draft;
- requires generated lines;
- status becomes In Review;
- current implementation stores the processing user in VerifiedBy fields.

Pay:

- allowed when status is In Review or Approved;
- current implementation changes status to Finalized;
- sets ApprovedBy;
- marks payroll lines approved and paid;
- stores PaidOnUtc;
- marks applicable bonus installments paid.

Important current limitation:

The current backend combines approval/final payment behavior in the Pay action. If a separate Verify → Approve → Pay workflow is required, new explicit endpoints and permissions are needed. Do not claim this separation already exists.

==================================================
28. INSTRUCTION AND NOTES ARCHITECTURE
==================================================

Instructions and personal notes share the AppNotes foundation but have different privacy behavior.

Main tables:

- AppNotes
- AppNoteTargets
- AppNoteUserStates
- AppNoteUserStatuses
- AppNoteAttachments

AppNote stores:

- title;
- body;
- note type;
- source type;
- category;
- priority;
- visibility;
- menu/module/record placement;
- start/end time;
- published/pinned/popup/banner flags;
- acknowledgement and dismiss rules;
- creator/audit information.

Source types:

USER:

- private user note;
- OwnerIdentityUserId is saved;
- visible only to its creator/owner;
- it is not an admin broadcast.

ADMIN:

- organization instruction;
- targets determine the audience;
- requires instruction permission.

==================================================
29. HOW A PERSONAL NOTE IS CREATED
==================================================

When a normal user without instruction-create permission creates a note:

1. Backend forces:
   SourceTypeCode = USER
2. Backend forces:
   VisibilityTypeCode = PRIVATE
3. Targets are removed.
4. OwnerIdentityUserId is set to the logged-in user.
5. AppNote is saved.
6. Only that user can see the personal note.

Personal note behavior may include:

- To-do/open category;
- completed category;
- personal update;
- delete.

A normal user cannot turn a personal note request into a company-wide instruction by changing the frontend payload.

==================================================
30. HOW AN INSTRUCTION IS POSTED
==================================================

Permission:

Instruction creation requires one of:

- SuperAdmin;
- Admin;
- TenantAdmin;
- explicit ADD permission for /instructions or /settings/instruction.

Frontend workflow:

1. User opens New Instruction.
2. Frontend loads InstructionAudienceScope.
3. User enters:
   - Title;
   - Message Body;
   - Note Type;
   - Priority;
   - Visibility;
   - Category;
   - start/end schedule;
   - popup/banner/pinned/acknowledgement options.
4. User selects audience:
   - Everyone, if allowed;
   - selected staff in permitted hierarchy.
5. Optional placement:
   - global;
   - specific menu;
   - specific record.
6. Frontend creates target records:
   - ALL:*
   - STAFF:{staff identifier}
   - MENU:{menu code}
   - RECORD:{entityType}:{entityId}
7. Backend recalculates and validates the permitted audience.
8. Backend rejects staff outside the sender’s hierarchy.
9. AppNote and AppNoteTargets are saved.
10. Recipients see the instruction only when all relevant target conditions match.

An empty target list for an authorized full-scope admin may become ALL:*.
A hierarchy-limited user must explicitly select permitted staff.

==================================================
31. INSTRUCTION HIERARCHY
==================================================

Instruction targeting uses organization node and role rank.

Approximate rank mapping:

- CEO: 700
- Duty CEO: 600
- Manager: 500
- Deputy Manager: 400
- Assistant Manager: 300
- Supervisor/Team Lead: 200
- Agent/Bell Boy: 100

A hierarchy-limited sender can target:

- permitted current organization node;
- descendant nodes when scope allows;
- staff with a lower role rank.

A user must not send an instruction to staff outside the resolved scope merely by manipulating the request.

==================================================
32. HOW INSTRUCTIONS BECOME VISIBLE
==================================================

When visible instructions are requested:

1. Backend resolves:
   - Identity user ID;
   - staff ID;
   - person ID;
   - username/email and related identifiers.
2. It loads AppNotes that are:
   - published;
   - active;
   - not deleted;
   - inside StartDate/EndDate window.
3. USER notes are filtered to the owner.
4. ADMIN notes are filtered through AppNoteTargets.
5. Target conditions are applied:

Audience condition:

- ALL target, or
- matching STAFF identifier.

Placement condition:

- no MENU target, or current menu matches.
- no RECORD target, or current record matches.

6. Per-user AppNoteUserState is loaded.
7. Dismissed notes are removed from normal display.
8. Results are ordered by priority/time.

Instructions may appear as:

- login popup;
- dashboard banner;
- instruction drawer/list;
- notification bell count;
- menu-specific instruction;
- record-specific instruction.

==================================================
33. READ, ACKNOWLEDGE AND DISMISS
==================================================

Per-recipient state is stored separately so one employee’s action does not affect another employee.

Mark Read:

- sets the recipient note state as read.

Acknowledge:

- records acknowledgement;
- used when RequireAcknowledgement is true.

Dismiss:

- records dismissal for that recipient;
- allowed only when AllowDismiss permits it.

An admin instruction remains in AppNotes even when one recipient dismisses it.

Delete by creator/admin:

- uses soft delete;
- IsDeleted becomes true;
- DeletedBy and DeletedOn are saved;
- it stops appearing to recipients.

==================================================
34. LOGIN INSTRUCTIONS
==================================================

During session/current-user loading:

1. UserSessionService loads visible login instructions.
2. It includes them in UserSessionDto.LoginInstructions.
3. UnreadInstructionCount is calculated.
4. Frontend places popup instructions into a queue.
5. User handles one popup at a time:
   - acknowledge;
   - dismiss;
   - mark read.
6. Navbar/dashboard fetch unread count and visible instructions.

AssessmentScheduler may also create an AppNote automatically:

- Title: Monthly assessment is pending
- Category: ASSESSMENT
- Priority: HIGH
- Target: the relevant assessor
- Menu: /assessment/mark
- Expiry: approximately two days

It checks for an existing reminder entity key so the same reminder is not recreated every scheduler cycle.

Important current limitation:

An InstructionChanged event type exists, but the current AppNotesController does not clearly publish a SignalR event after every create/update/delete. Current frontend visibility relies mainly on session load/API refresh/polling. If truly instant instruction delivery is required, publish targeted ApplicationRealtimeHub events after the database transaction and refetch on receipt.

==================================================
35. CHAT CONNECTION FLOW
==================================================

Chat uses:

/hubs/chat

The hub is authenticated.

On connection:

1. Resolve Identity user ID.
2. Resolve active chat caller PersonId and TenantId.
3. Add connection to:
   chat:person:{personId}
4. Add connection to:
   chat:tenant:{tenantId}
5. Track ConnectionId in ChatPresenceTracker.
6. If this is the person’s first active connection:
   - publish online presence when privacy permits.

On disconnect:

1. Remove ConnectionId.
2. If no connection remains:
   - store LastSeenUtc;
   - publish offline presence when ShowLastSeen permits.

When opening a conversation:

- membership is validated;
- connection joins:
  chat:conversation:{conversationId}

Typing events are sent only to other conversation members.

==================================================
36. CHAT CONVERSATION AND REQUEST FLOW
==================================================

Direct contact flow:

1. User searches allowed tenant directory.
2. User sends contact/chat request.
3. Receiver is notified through their person SignalR group.
4. Receiver accepts/rejects.
5. On acceptance, direct conversation/membership becomes available.
6. Both sides receive conversation updates.

Group flow:

1. Authorized user creates a group.
2. ChatConversation is created.
3. ChatConversationMembers are created.
4. Members receive conversation-created/update events.
5. Group role/permission controls member changes, name and photo updates.

Blocked users cannot send messages according to ChatService validation.

==================================================
37. SEND TEXT MESSAGE FLOW
==================================================

1. Frontend creates a unique ClientMessageId GUID.
2. User submits the message.
3. API validates:
   - authenticated caller;
   - active person;
   - conversation membership;
   - not blocked;
   - message length/content;
   - reply target where supplied.
4. ChatMessage is saved.
5. Mention tracking is updated.
6. Unique ClientMessageId prevents duplicate sends from retries.
7. If a duplicate database insert occurs:
   - existing message is returned instead of creating another message.
8. Controller publishes messageReceived to conversation/member groups.
9. Frontend receives the event and updates the thread/conversation list.
10. Recipient unread state changes until they mark messages read.

The database remains authoritative; SignalR only delivers the update.

==================================================
38. CHAT ATTACHMENT FLOW
==================================================

Frontend:

1. User selects a file.
2. File is staged locally.
3. Preview opens.
4. User may:
   - preview image/video;
   - inspect document name and size;
   - enter caption;
   - enable View Once when permitted;
   - cancel.
5. File is uploaded only after explicit Send.

Backend:

1. Validate membership and blocking.
2. Require ClientMessageId.
3. Enforce attachment size:
   current limit approximately 10 MB.
4. Sanitize file name.
5. Validate real file content/type.
6. Create ChatMessage.
7. Save file under:
   App_Data/chat-uploads/{tenant}/{conversation}/...
8. Save ChatAttachment metadata.
9. Commit transaction.
10. Publish messageReceived.

Normal attachment metadata stays in SQL while the physical file stays in application storage.

==================================================
39. CHAT EDIT FLOW
==================================================

Editing is allowed only when:

- chat rules allow editing;
- caller is sender;
- message is not deleted;
- message has no attachment;
- edit window has not expired.

Default edit window:

15 minutes from original CreatedOnUtc.

On edit:

1. Replace Message.Body.
2. Set EditedOnUtc.
3. Save.
4. Publish messageEdited.
5. Frontend replaces displayed text.
6. Show permanent Edited label.

There is no recipient-visible edit history.
Multiple edits are allowed inside the time window.

==================================================
40. DELETE FOR ME
==================================================

Delete for Me:

1. Membership is validated.
2. ChatMessage is not globally modified.
3. ChatMessageDeletion is created for that PersonId.
4. Message becomes hidden only for that user.
5. It remains visible to other conversation members.
6. It remains available indefinitely as an option.

Repeated Delete for Me is idempotent.

==================================================
41. DELETE FOR EVERYONE
==================================================

Default time window:

60 hours from message creation.

Allowed only when:

- chat rule permits it;
- caller is sender;
- message is not already deleted;
- current time is inside the window.

On deletion:

1. Body becomes:
   This message was deleted
2. DeletedOnUtc is set.
3. DeliveryTrackingClearedOnUtc is set.
4. Reactions are removed.
5. Attachments and unreferenced physical files are removed.
6. Message Info becomes unavailable.
7. Controller publishes messageDeleted.
8. All clients replace the message with the placeholder.

After 60 hours:

- Delete for Everyone is rejected;
- Delete for Me remains available.

==================================================
42. REACTIONS
==================================================

Reaction flow:

1. Validate caller and membership.
2. Validate message is usable.
3. Save/update/remove ChatMessageReaction for that person/message.
4. Publish reactionUpdated.
5. Frontend updates reaction counts without reloading the entire page.

Only one normalized reaction record per user/message should exist.

==================================================
43. READ, UNREAD AND MENTIONS
==================================================

Conversation membership maintains the user’s last-read position.

When a conversation is opened:

1. Load messages.
2. Read membership’s last-read message ID.
3. Identify first unread message.
4. Scroll to first unread, not blindly to bottom.
5. Insert:
   “N Unread Messages”
6. Mark appropriate messages read.
7. Publish messagesRead.

Mention tracking:

1. Message body is inspected for direct @mentions.
2. Unread mention state is stored for the mentioned member.
3. Conversation list shows @ badge.
4. Chat contains jump-to-mention action.
5. Reading the mention updates the state.

==================================================
44. VIEW-ONCE MEDIA
==================================================

View Once is allowed only when:

- Chat Rules enable it;
- attachment is an image or video;
- conversation is Direct;
- recipient is not sender.

Opening:

1. Recipient calls protected View Once endpoint.
2. Membership is validated.
3. Backend confirms it is not:
   - deleted;
   - hidden for caller;
   - consumed;
   - expired.
4. File bytes are read.
5. Physical file is deleted immediately.
6. FilePath is cleared.
7. OpenedOn, OpenedBy and ConsumedOn are saved.
8. Recipient receives the one-time bytes.
9. SignalR publishes viewOnceConsumed.
10. Attachment becomes Opened/unavailable.

Unopened expiry:

- default 336 hours/14 days;
- hourly cleanup marks expired;
- physical file is removed.

View Once cannot be downloaded using the normal attachment endpoint.
It cannot be forwarded.

Browser limitation:

A web browser cannot provide guaranteed OS-level screenshot protection.

==================================================
45. CHAT REAL-TIME EVENTS
==================================================

Frontend currently listens for events such as:

- messageReceived
- reactionUpdated
- messageEdited
- messageDeleted
- messagesRead
- typingChanged
- conversationUpdated
- groupMembersUpdated
- chatRequestReceived
- chatRequestUpdated
- conversationCreated
- presenceChanged
- chatBlocked
- viewOnceConsumed
- chatRulesUpdated

Handlers must be removed during cleanup to avoid duplicates.

After reconnect:

- rejoin active conversation;
- refetch conversation/message state;
- do not assume missed SignalR events were delivered.

==================================================
46. BACKGROUND SERVICES SUMMARY
==================================================

AttendanceFinalizationScheduler:

- startup + hourly;
- finalizes attendance;
- refreshes deduction inputs;
- publishes DeductionChanged.

AssessmentSchedulerService:

- startup + hourly;
- creates due assessments;
- creates non-duplicate assessment reminder notes.

ProcessReportAutoTransferService:

- startup + every five minutes;
- runs dbo.usp_ProcessReport_AutoTransferOverdue.

ChatViewOnceCleanupService:

- approximately hourly;
- expires and removes unopened View Once files.

IdempotencyCleanupService:

- approximately every 30 minutes;
- removes expired idempotency records safely.

Every scheduler must:

- be tenant-aware;
- support cancellation;
- prevent overlapping execution;
- be idempotent;
- log failures;
- avoid duplicate rows/financial entries.

==================================================
47. CROSS-MODULE FINANCIAL FLOW
==================================================

Complete financial chain:

Person Registration
→ Salary Scale/Current Pay
→ Attendance Mapping
→ Daily Attendance Entry
→ Attendance Finalization
→ Shortage/Late/Absent/Overtime Calculation
→ Monthly Deduction Report
→ Adjustment and Overtime Approval
→ Allowance/Benefit/Bonus Resolution
→ Tax and EOBI Calculation
→ Payroll Preview
→ Payroll Draft Generation
→ Review
→ Final Payment

If an earlier source changes, dependent financial data must be recalculated while the payroll run is still Draft.

After payroll is Finalized, do not silently rewrite paid values.
Use an approved adjustment/reversal workflow.

==================================================
48. CALCULATION VISIBILITY (OWNER LOCK — OLD PROJECT STYLE)
==================================================

Owner decision: business money math should live primarily in **SQL stored procedures**
(like old `SP_GetMonthlyStaffPayRoll` → `tblStaffPayRoll` Id + `PayRoll-{month}-{year}` Ref),
not as the long-term engine inside C# services. C# = auth / tenant / SignalR / DTO load.

Cursor rule: `.cursor/rules/07-calculation-sql-sp.mdc`

Already SP-owned (keep extending, do not re-fork in C#):
- `usp_Attendance_DeductionReport`
- `usp_Payroll_RecalculateRunTotals` (Draft Gross / Deduction / Net)
- `usp_Pay_BonusGenerate_Candidates`
- `usp_Assessment_FinalList`
- list/report Wave SPs

Phased move (do not big-bang):
1. `usp_Payroll_GenerateMonthly` — Create/Generate insert lines + identity Id + Ref (parity with old Create)
2. Remaining deduction component edges still in C#
3. Bonus installment-due math fully in SP
4. Remove duplicate C# formulas after SP parity tests pass

Until Phase 1 SP ships, `PayrollCalculationService.BuildLinesAsync` remains the generate builder;
final Draft totals already recompute in `usp_Payroll_RecalculateRunTotals`.

==================================================
49. IMPORTANT CURRENT GAPS TO REMEMBER
==================================================

1. Instructions are database-backed, but fully targeted instant SignalR delivery after every instruction change is not clearly completed.

2. Payroll currently uses:
   Draft → In Review → Finalized
   and the Pay endpoint also records approval/payment.
   A fully separate Verify → Approve → Pay workflow requires further work.

3. Browser chat cannot guarantee screenshot blocking.

4. Accounts/ROZ financial posting logic is not finalized.

5. Any Leave, Discipline, Attendance or Assessment bonus component must only be called “automatic” when its real source is actually connected and verified.

6. Database migration application must be verified separately from successful builds.

7. Do not display zero as if it is a verified calculation when required configuration is missing. Use a configuration/review indication where appropriate.
