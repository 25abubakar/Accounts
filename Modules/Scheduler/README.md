# Accounts Scheduler module

This module is hosted by the existing Accounts API and uses the existing React application, Identity cookie, CSRF protection, sidebar and tenant menu ceiling. It does not run a second login/server.

Sidebar routes:

- `/scheduler/jobs` — register/edit, enable/pause, preview and run a registered job.
- `/scheduler/upcoming` — persisted pending triggers.
- `/scheduler/history` — latest 500 executions, with result, attempt and errors.

## Scope and existing calculations

The existing `AttendanceFinalizationScheduler`, `AssessmentSchedulerService`, attendance formulas and assessment formulas are unchanged. The starter Attendance and Assessment jobs **only read/check existing data**. They do not change statuses, create assessments, post payroll or replace the old timers. No recurring jobs are enabled automatically.

A later handover to the new engine must explicitly disable the corresponding old timer and supply a tenant-scoped calculation handler. In particular, the existing assessment `RunNowAsync` scans all tenants; it is deliberately not called from a tenant-owned scheduled job.

## Persistence and scheduling

`AccountsScheduler` is a separate database on the same configured SQL Server by default. Override `ConnectionStrings:SchedulerConnection` if needed. Business access uses the existing `DefaultConnection`. Runtime table creation is not used: apply EF migrations for `SchedulerDbContext` before starting the API. Quartz schema is a creation-only copy of the official v3.22.0 Apache-2.0 SQL Server script; its destructive DROP/USE section was removed.

Calendar schedules use Pakistan time. The interval option is anchored to its chosen local start and repeats every N 24-hour days; it is different from two fixed monthly dates. Monthly 29/30/31 skips months without that day. One-time schedules support future module deadline tasks. Preview the next execution times before enabling.

Quartz persists triggers, coordinates nodes, prevents overlap per definition, and applies the selected missed-execution policy. A SQL application lock also prevents simultaneous executions of the same tenant/handler across different definitions. Failures have bounded retries (1, 2, 3 minute delays), cancellation and a 15–600 second timeout. The dispatcher keeps a unique run record per occurrence. At-least-once recovery is not a guarantee of exactly-once business side effects: write handlers must use their own transactional/idempotent business key.

The lightweight reconciler inspects scheduler metadata once a minute; it does not poll attendance/assessment tables every second. The Accounts process must be running for jobs to execute. Process restarts retain the persisted definitions and triggers.

## Add a module job

1. Implement `IModuleJob` in the new module, with a unique key and a clear description.
2. Register it with `services.AddScoped<IModuleJob, YourJob>()` in `SchedulerModule.cs`.
3. Scope every business query by the provided tenant ID. Use the cancellation token and short SQL command timeouts.
4. Do not execute arbitrary SQL supplied by users. The UI accepts only registered handler keys.
5. For a writing/financial handler, make the operation idempotent and document recovery before enabling it. Do not call a legacy all-tenant loop from a tenant job.

The job then appears in the existing Jobs form without a new sidebar route. All starter jobs are read-only. Run Now uses a UUID command key to avoid duplicate submissions. Create requests also have a unique tenant/create key; edits use SQL row versions.

## Access

Tenant Admin only, with current database role/tenant validation and the Scheduler menu ceiling. New menu grants are ceilings, not extra staff grants. Other tenant records are filtered by EF and controller scope; worker access uses explicit tenant/job identities.

## Wiring

Existing files changed only to connect the requested module:

- `Accounts/Accounts.csproj`: module project reference.
- `Accounts/Program.cs`: one registration call and its namespace import.
- `Frontend/Frontend-Accounts/src/App.tsx`: lazy page and scheduler routes.

The menu migration, engine, UI, migrations and verification files are additions. Existing business calculation source files are unchanged.
