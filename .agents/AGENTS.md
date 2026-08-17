# Project Rules

## Attendance Module
- **Sunday Logic**: Sunday MUST always be evaluated and mapped as a `Holiday` (not `Day Off`), unless overridden by a specific check-in. When updating stored procedures (`usp_Attendance_EvaluateStatuses`, `usp_Attendance_DailyReport`), ensure the logic `DATENAME(weekday, AttendanceDate) = 'Sunday'` explicitly defaults to the `Holiday` status.
- **Status Evaluation**: Statuses (like 1 Hr Late, Absent, Early Departure) must be mapped dynamically via `AttendanceRuleSettings` rather than hardcoded in C# fallbacks. 
- **C# Fallback**: Do not override the database's `StatusName` (derived from `PlatformActionStatusId`) in C# `AttendanceService` unless it is strictly necessary (e.g., if a status was genuinely missed). Always prefer the SQL engine's determined status.
