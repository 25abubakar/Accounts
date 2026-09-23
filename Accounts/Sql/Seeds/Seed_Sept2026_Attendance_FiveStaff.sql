/*
  Seed attendance 2026-09-01 .. 2026-09-15 for:
    Aftab Hussain, Muhammad Farooq, Muhammad Abubakar, M Arsalan, Kishwar Hanif
  Rules:
    - Sat/Sun off (Saturday Day Off, Sunday Holiday)
    - 2026-09-07 Monday company holiday
    - Persons 1-4: Present on working days (shift times from AttendanceMapRules)
    - Kishwar: T-Present on 4 working days (1,3,8,10); Present on remaining working days
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @TenantId int = 2007;
DECLARE @EntryTypeId int = 203;
DECLARE @AsOf datetime2 = '2026-09-16T06:00:00'; -- after last evening shift on 15th
DECLARE @DayOffHolidayTypeId int = 2;      -- DAY_OFF
DECLARE @SundayHolidayTypeId int = 12;     -- HOLIDAY
DECLARE @CompanyHolidayTypeId int = 12;    -- HOLIDAY (company chutti → Holiday status)
DECLARE @WorkingDayTypeId int = 5;         -- WORKING_DAY

IF OBJECT_ID('tempdb..#Staff') IS NOT NULL DROP TABLE #Staff;
CREATE TABLE #Staff
(
    FullName nvarchar(200) NOT NULL,
    PersonId uniqueidentifier NOT NULL,
    StaffId uniqueidentifier NOT NULL,
    ShiftStart time(0) NOT NULL,
    ShiftEnd time(0) NOT NULL,
    RequiredMinutes int NOT NULL
);

INSERT INTO #Staff (FullName, PersonId, StaffId, ShiftStart, ShiftEnd, RequiredMinutes)
VALUES
    (N'Aftab Hussain',      'FE93664A-3F85-4C9D-8EE1-9BF2029AFDDE', 'AA5A81D7-67E7-4C77-A98E-5309D570F139', '15:00', '22:00', 420),
    (N'Muhammad Farooq',    '4FF16FB7-C89B-458C-933F-5F1922DC4A80', 'D810751E-19A1-4709-BE27-37F8C0B0918D', '08:50', '17:50', 540),
    (N'Muhammad Abubakar',  '2E6C5197-8D1C-4D36-A83C-AD0B32EF66D0', 'F8D6F85F-4421-4E72-AF4D-816E853CD6EB', '09:00', '18:00', 540),
    (N'M Arsalan',          'C347B48A-C8F4-4334-B12B-EAA75C6A034E', '449189CB-204D-4916-A4C3-6F401AF7725A', '09:00', '18:00', 540),
    (N'Kishwar Hanif',      'F6B203AC-8752-49C3-8BAD-62F37F138ABF', 'EE21EE32-03F9-4C0C-8EC0-CF05CFDDB1C3', '11:00', '17:00', 360);

IF OBJECT_ID('tempdb..#Dates') IS NOT NULL DROP TABLE #Dates;
CREATE TABLE #Dates
(
    AttendanceDate date NOT NULL PRIMARY KEY,
    Dow nvarchar(20) NOT NULL,
    IsWorkingDay bit NOT NULL,
    IsCompanyHoliday bit NOT NULL
);

;WITH D AS
(
    SELECT CAST('2026-09-01' AS date) AS AttendanceDate
    UNION ALL
    SELECT DATEADD(day, 1, AttendanceDate) FROM D WHERE AttendanceDate < '2026-09-15'
)
INSERT INTO #Dates (AttendanceDate, Dow, IsWorkingDay, IsCompanyHoliday)
SELECT
    AttendanceDate,
    DATENAME(weekday, AttendanceDate),
    CASE
        WHEN AttendanceDate = '2026-09-07' THEN 0
        WHEN DATENAME(weekday, AttendanceDate) IN (N'Saturday', N'Sunday') THEN 0
        ELSE 1
    END,
    CASE WHEN AttendanceDate = '2026-09-07' THEN 1 ELSE 0 END
FROM D
OPTION (MAXRECURSION 31);

BEGIN TRAN;

-- 1) Timing schedules for every day in range
MERGE dbo.EmployeeTimingSchedules AS tgt
USING
(
    SELECT
        s.StaffId,
        d.AttendanceDate,
        LEFT(CONVERT(nvarchar(8), s.ShiftStart, 108), 5) AS TimeFrom,
        LEFT(CONVERT(nvarchar(8), s.ShiftEnd, 108), 5) AS TimeTo,
        CASE
            WHEN d.IsWorkingDay = 1 THEN CAST(1 AS bit)
            ELSE CAST(0 AS bit)
        END AS IsOn,
        CASE
            WHEN d.IsCompanyHoliday = 1 THEN @CompanyHolidayTypeId
            WHEN d.Dow = N'Sunday' THEN @SundayHolidayTypeId
            WHEN d.Dow = N'Saturday' THEN @DayOffHolidayTypeId
            ELSE @WorkingDayTypeId
        END AS HolidayTypeId,
        CASE WHEN d.IsWorkingDay = 1 THEN s.RequiredMinutes ELSE 0 END AS WorkingMinutes,
        MONTH(d.AttendanceDate) AS ScheduleMonth,
        YEAR(d.AttendanceDate) AS ScheduleYear
    FROM #Staff s
    CROSS JOIN #Dates d
) AS src
ON tgt.TenantId = @TenantId
   AND tgt.StaffId = src.StaffId
   AND tgt.ScheduleDate = src.AttendanceDate
WHEN MATCHED THEN
    UPDATE SET
        TimeFrom = src.TimeFrom,
        TimeTo = src.TimeTo,
        IsOn = src.IsOn,
        HolidayTypeId = src.HolidayTypeId,
        WorkingMinutes = src.WorkingMinutes,
        ScheduleMonth = src.ScheduleMonth,
        ScheduleYear = src.ScheduleYear,
        ModifiedDate = @AsOf
WHEN NOT MATCHED THEN
    INSERT
    (
        TenantId, StaffId, ScheduleDate, TimeFrom, TimeTo, IsOn, HolidayTypeId,
        WorkingMinutes, ScheduleMonth, ScheduleYear, CreatedDate
    )
    VALUES
    (
        @TenantId, src.StaffId, src.AttendanceDate, src.TimeFrom, src.TimeTo, src.IsOn, src.HolidayTypeId,
        src.WorkingMinutes, src.ScheduleMonth, src.ScheduleYear, @AsOf
    );

-- 2) Attendance punch times (working days only)
;WITH Punch AS
(
    SELECT
        s.PersonId,
        s.FullName,
        d.AttendanceDate,
        -- Present: on-time / slight early; Kishwar T-Present days: late but full hours
        CASE
            WHEN s.FullName = N'Kishwar Hanif'
             AND d.AttendanceDate IN ('2026-09-01', '2026-09-03', '2026-09-08', '2026-09-10')
                THEN DATEADD(minute, 35, DATEADD(minute, DATEDIFF(minute, 0, s.ShiftStart), CAST(d.AttendanceDate AS datetime2)))
            WHEN s.FullName = N'Aftab Hussain'
                THEN DATEADD(minute, -2, DATEADD(minute, DATEDIFF(minute, 0, s.ShiftStart), CAST(d.AttendanceDate AS datetime2)))
            WHEN s.FullName = N'Muhammad Farooq'
                THEN DATEADD(minute, -3, DATEADD(minute, DATEDIFF(minute, 0, s.ShiftStart), CAST(d.AttendanceDate AS datetime2)))
            ELSE
                DATEADD(minute, -1, DATEADD(minute, DATEDIFF(minute, 0, s.ShiftStart), CAST(d.AttendanceDate AS datetime2)))
        END AS CheckInLocal,
        CASE
            WHEN s.FullName = N'Kishwar Hanif'
             AND d.AttendanceDate IN ('2026-09-01', '2026-09-03', '2026-09-08', '2026-09-10')
                -- 11:35 + 360 min = 17:35 (completes required minutes)
                THEN DATEADD(minute, 35 + s.RequiredMinutes, DATEADD(minute, DATEDIFF(minute, 0, s.ShiftStart), CAST(d.AttendanceDate AS datetime2)))
            ELSE
                DATEADD(minute, 8, DATEADD(minute, DATEDIFF(minute, 0, s.ShiftEnd), CAST(d.AttendanceDate AS datetime2)))
        END AS CheckOutLocal
    FROM #Staff s
    CROSS JOIN #Dates d
    WHERE d.IsWorkingDay = 1
)
MERGE dbo.AttendanceRecords AS tgt
USING Punch AS src
ON tgt.TenantId = @TenantId
   AND tgt.PersonId = src.PersonId
   AND tgt.AttendanceDate = src.AttendanceDate
WHEN MATCHED THEN
    UPDATE SET
        CheckInUtc = src.CheckInLocal,
        CheckOutUtc = src.CheckOutLocal,
        EffectiveCheckInUtc = src.CheckInLocal,
        EffectiveCheckOutUtc = src.CheckOutLocal,
        AttendanceEntryTypeId = @EntryTypeId,
        TotalBreakMinutes = 0,
        BreakStartedUtc = NULL,
        ModifiedDate = @AsOf
WHEN NOT MATCHED THEN
    INSERT
    (
        TenantId, PersonId, AttendanceDate, AttendanceEntryTypeId,
        CheckInUtc, CheckOutUtc, EffectiveCheckInUtc, EffectiveCheckOutUtc,
        TotalBreakMinutes, CreatedDate, ModifiedDate
    )
    VALUES
    (
        @TenantId, src.PersonId, src.AttendanceDate, @EntryTypeId,
        src.CheckInLocal, src.CheckOutLocal, src.CheckInLocal, src.CheckOutLocal,
        0, @AsOf, @AsOf
    );

-- Clear punches on off / holiday days (status comes from evaluator)
UPDATE ar
SET
    CheckInUtc = NULL,
    CheckOutUtc = NULL,
    EffectiveCheckInUtc = NULL,
    EffectiveCheckOutUtc = NULL,
    BreakStartedUtc = NULL,
    TotalBreakMinutes = 0,
    ModifiedDate = @AsOf
FROM dbo.AttendanceRecords ar
INNER JOIN #Staff s ON s.PersonId = ar.PersonId
INNER JOIN #Dates d ON d.AttendanceDate = ar.AttendanceDate
WHERE ar.TenantId = @TenantId
  AND d.IsWorkingDay = 0;

-- Ensure off/holiday shell rows exist so evaluator can stamp Day Off / Holiday
MERGE dbo.AttendanceRecords AS tgt
USING
(
    SELECT s.PersonId, d.AttendanceDate
    FROM #Staff s
    CROSS JOIN #Dates d
    WHERE d.IsWorkingDay = 0
) AS src
ON tgt.TenantId = @TenantId
   AND tgt.PersonId = src.PersonId
   AND tgt.AttendanceDate = src.AttendanceDate
WHEN NOT MATCHED THEN
    INSERT (TenantId, PersonId, AttendanceDate, AttendanceEntryTypeId, TotalBreakMinutes, CreatedDate, ModifiedDate)
    VALUES (@TenantId, src.PersonId, src.AttendanceDate, @EntryTypeId, 0, @AsOf, @AsOf);

COMMIT TRAN;

EXEC dbo.usp_Attendance_EvaluateStatuses
    @TenantId = @TenantId,
    @DateFrom = '2026-09-01',
    @DateTo = '2026-09-15',
    @AsOfUtc = @AsOf;

-- Summary
SELECT
    p.FullName,
    ar.AttendanceDate,
    DATENAME(weekday, ar.AttendanceDate) AS Dow,
    CONVERT(varchar(8), ar.CheckInUtc, 108) AS CheckIn,
    CONVERT(varchar(8), ar.CheckOutUtc, 108) AS CheckOut,
    st.Name AS StatusName,
    ar.PlatformActionStatusId,
    ar.AttendanceStatusId
FROM dbo.AttendanceRecords ar
JOIN dbo.Persons p ON p.PersonId = ar.PersonId
LEFT JOIN PlatformSettings.ActionStatuses pas ON pas.Id = ar.PlatformActionStatusId
LEFT JOIN PlatformSettings.Statuses st ON st.Id = pas.StatusId
WHERE ar.TenantId = @TenantId
  AND ar.AttendanceDate BETWEEN '2026-09-01' AND '2026-09-15'
  AND p.FullName IN
  (
      N'Aftab Hussain', N'Muhammad Farooq', N'Muhammad Abubakar',
      N'M Arsalan', N'Kishwar Hanif'
  )
ORDER BY p.FullName, ar.AttendanceDate;
