-- Prefer mapped/scheduled shift duration over AttendanceRuleSettings.WorkingMinutes
-- so WHrs/CR-DB follow each person's Attendance Map times (6h/7h/8h/9h), not a blanket 9h rule.
SET NOCOUNT ON;

DECLARE @def nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_Attendance_EvaluateStatuses'));
IF @def IS NULL
    THROW 50000, 'usp_Attendance_EvaluateStatuses is missing.', 1;

DECLARE @old nvarchar(max) = N'COALESCE(NULLIF(timing.WorkingMinutes, 0), NULLIF(setting.WorkingMinutes, 0), DATEDIFF(minute, windows.ShiftStartLocal, windows.ShiftEndLocal))';
DECLARE @new nvarchar(max) = N'COALESCE(NULLIF(timing.WorkingMinutes, 0), NULLIF(DATEDIFF(minute, windows.ShiftStartLocal, windows.ShiftEndLocal), 0), NULLIF(setting.WorkingMinutes, 0))';

IF CHARINDEX(@new, @def) > 0
BEGIN
    PRINT 'usp_Attendance_EvaluateStatuses already uses shift-first RequiredWorkingMinutes.';
    RETURN;
END

IF CHARINDEX(@old, @def) = 0
    THROW 50001, 'Expected RequiredWorkingMinutes COALESCE pattern was not found.', 1;

SET @def = REPLACE(@def, @old, @new);
SET @def = REPLACE(@def, N'CREATE PROCEDURE', N'CREATE OR ALTER PROCEDURE');
SET @def = REPLACE(@def, N'CREATE   PROCEDURE', N'CREATE OR ALTER PROCEDURE');
SET @def = REPLACE(@def, N'CREATE PROC', N'CREATE OR ALTER PROCEDURE');
EXEC sys.sp_executesql @def;
PRINT 'Patched usp_Attendance_EvaluateStatuses RequiredWorkingMinutes priority (shift before rule).';
