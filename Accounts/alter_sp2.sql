ALTER PROCEDURE dbo.usp_Attendance_EvaluateStatuses
    @TenantId int,
    @DateFrom date,
    @DateTo date,
    @AsOfUtc datetime2
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE
        @PolicyId int,
        @Grace int,
        @AbsentAfter int,
        @MissingOutAfter int,
        @Tolerance int,
        @Present int,
        @PlatformPresent int,
        @Late int,
        @PlatformLate int,
        @CompletedLate int,
        @PlatformCompletedLate int,
        @ShortLeave int,
        @PlatformShortLeave int,
        @EarlyDeparture int,
        @PlatformEarlyDeparture int,
        @Absent int,
        @PlatformAbsent int,
        @DayOff int,
        @PlatformDayOff int,
        @Holiday int,
        @PlatformHoliday int,
        @ProcessId int;

    

    

    SELECT @ProcessId = Id FROM dbo.Processes WHERE ProcessName = N'Attendance';
    
    DECLARE @PlatformActionId int;
    SELECT TOP(1) @PlatformActionId=Id FROM PlatformSettings.Actions WHERE Name=N'Attendance' AND TenantId=@TenantId;

    -- Dynamic Platform Status Lookups
    SELECT TOP(1) @PlatformPresent = pas.Id
    FROM dbo.ProcessStatusStyles pss
    INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue = pss.Code AND (crdb.TenantId = @TenantId OR crdb.TenantId IS NULL)
    INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = crdb.StatusId AND pas.ActionId=@PlatformActionId
    WHERE pss.ProcessId = @ProcessId AND pss.Code = N'P' AND pss.IsActive = 1 AND (pss.TenantId = @TenantId OR pss.TenantId IS NULL)
    ORDER BY CASE WHEN pss.TenantId = @TenantId THEN 0 ELSE 1 END;

    SELECT TOP(1) @PlatformAbsent = pas.Id
    FROM dbo.ProcessStatusStyles pss
    INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue = pss.Code AND (crdb.TenantId = @TenantId OR crdb.TenantId IS NULL)
    INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = crdb.StatusId AND pas.ActionId=@PlatformActionId
    WHERE pss.ProcessId = @ProcessId AND pss.Code = N'A' AND pss.IsActive = 1 AND (pss.TenantId = @TenantId OR pss.TenantId IS NULL)
    ORDER BY CASE WHEN pss.TenantId = @TenantId THEN 0 ELSE 1 END;

    SELECT TOP(1) @PlatformLate = pas.Id
    FROM dbo.ProcessStatusStyles pss
    INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue = pss.Code AND (crdb.TenantId = @TenantId OR crdb.TenantId IS NULL)
    INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = crdb.StatusId AND pas.ActionId=@PlatformActionId
    WHERE pss.ProcessId = @ProcessId AND pss.Code = N'LT' AND pss.IsActive = 1 AND (pss.TenantId = @TenantId OR pss.TenantId IS NULL)
    ORDER BY CASE WHEN pss.TenantId = @TenantId THEN 0 ELSE 1 END;

    SELECT TOP(1) @PlatformCompletedLate = pas.Id
    FROM dbo.ProcessStatusStyles pss
    INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue = pss.Code AND (crdb.TenantId = @TenantId OR crdb.TenantId IS NULL)
    INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = crdb.StatusId AND pas.ActionId=@PlatformActionId
    WHERE pss.ProcessId = @ProcessId AND pss.Code = N'TP' AND pss.IsActive = 1 AND (pss.TenantId = @TenantId OR pss.TenantId IS NULL)
    ORDER BY CASE WHEN pss.TenantId = @TenantId THEN 0 ELSE 1 END;

    SELECT TOP(1) @PlatformShortLeave = pas.Id
    FROM dbo.ProcessStatusStyles pss
    INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue = pss.Code AND (crdb.TenantId = @TenantId OR crdb.TenantId IS NULL)
    INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = crdb.StatusId AND pas.ActionId=@PlatformActionId
    WHERE pss.ProcessId = @ProcessId AND pss.Code = N'SL' AND pss.IsActive = 1 AND (pss.TenantId = @TenantId OR pss.TenantId IS NULL)
    ORDER BY CASE WHEN pss.TenantId = @TenantId THEN 0 ELSE 1 END;

    SELECT TOP(1) @PlatformEarlyDeparture = pas.Id
    FROM dbo.ProcessStatusStyles pss
    INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue = pss.Code AND (crdb.TenantId = @TenantId OR crdb.TenantId IS NULL)
    INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = crdb.StatusId AND pas.ActionId=@PlatformActionId
    WHERE pss.ProcessId = @ProcessId AND pss.Code = N'EL' AND pss.IsActive = 1 AND (pss.TenantId = @TenantId OR pss.TenantId IS NULL)
    ORDER BY CASE WHEN pss.TenantId = @TenantId THEN 0 ELSE 1 END;

    SELECT TOP (1) @DayOff = pss.Id, @PlatformDayOff = pas.Id
    FROM dbo.ProcessStatusStyles pss
    INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue = pss.Code AND (crdb.TenantId = @TenantId OR crdb.TenantId IS NULL)
    INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = crdb.StatusId AND pas.ActionId=@PlatformActionId
    WHERE pss.ProcessId = @ProcessId AND pss.Code = N'DO' AND pss.IsActive = 1 AND (pss.TenantId = @TenantId OR pss.TenantId IS NULL)
    ORDER BY CASE WHEN pss.TenantId = @TenantId THEN 0 ELSE 1 END;

    SELECT TOP (1) @Holiday = pss.Id, @PlatformHoliday = pas.Id
    FROM dbo.ProcessStatusStyles pss
    INNER JOIN PlatformSettings.StatusCrDbValues crdb ON crdb.DbValue = pss.Code AND (crdb.TenantId = @TenantId OR crdb.TenantId IS NULL)
    INNER JOIN PlatformSettings.ActionStatuses pas ON pas.StatusId = crdb.StatusId AND pas.ActionId=@PlatformActionId
    WHERE pss.ProcessId = @ProcessId AND pss.Code = N'HO' AND pss.IsActive = 1 AND (pss.TenantId = @TenantId OR pss.TenantId IS NULL)
    ORDER BY CASE WHEN pss.TenantId = @TenantId THEN 0 ELSE 1 END;

    ;WITH Dates AS
    (
        SELECT @DateFrom AS AttendanceDate
        UNION ALL
        SELECT DATEADD(day, 1, AttendanceDate) FROM Dates WHERE AttendanceDate < @DateTo
    ),
    EffectiveDays AS
    (
        SELECT
            person.TenantId,
            person.PersonId,
            dates.AttendanceDate,
            ISNULL(mapRule.IsOpenAttendance, 0) AS IsOpenAttendance,
            entryType.Code AS AttendanceTypeCode,
            COALESCE(
                timing.IsOn,
                CASE WHEN DATENAME(weekday, dates.AttendanceDate) IN (N'Saturday', N'Sunday') THEN 0 ELSE 1 END
            ) AS IsOn,
            COALESCE(
                TRY_CONVERT(time(0), timing.TimeFrom),
                TRY_CONVERT(time(0), mapRule.TimeFrom),
                TRY_CONVERT(time(0), person.ShiftStartTime)
            ) AS ShiftStart,
            COALESCE(setting.AbsentAfterShiftStartMinutes, @AbsentAfter) AS AbsentAfterMinutes,
            COALESCE(setting.AdjustAbsentDays, 0) AS AdjustAbsentDays,
            setting.PlatformLateStatusId,
            setting.PlatformExtremeLateStatusId,
            setting.ExtremeLateAfterMinutes,
            setting.PlatformEarlyDepartureStatusId,
            setting.PlatformExtremeEarlyDepartureStatusId,
            setting.ExtremeEarlyDepartureAfterMinutes,
            timingHoliday.ValueCode AS HolidayValueCode,
            (
                SELECT COUNT_BIG(1)
                FROM dbo.AttendanceRecords previousAbsent
                WHERE previousAbsent.TenantId = person.TenantId
                  AND person.PersonId = previousAbsent.PersonId
                  AND previousAbsent.AttendanceStatusId = @Absent
                  AND previousAbsent.AttendanceDate >= DATEFROMPARTS(YEAR(dates.AttendanceDate), MONTH(dates.AttendanceDate), 1)
                  AND previousAbsent.AttendanceDate < dates.AttendanceDate
            ) AS PriorMonthlyAbsentCount
        FROM dbo.Persons person
        JOIN dbo.StaffVacancy staff ON staff.PersonId = person.PersonId
        JOIN dbo.AttendanceMapRules mapRule ON mapRule.StaffId = staff.StaffId AND mapRule.TenantId = @TenantId
        JOIN PlatformTypes.AttendanceTypes entryType ON entryType.Id = mapRule.AttendanceEntryTypeId AND entryType.TenantId = mapRule.TenantId AND entryType.IsActive = 1
        CROSS JOIN Dates dates
        LEFT JOIN dbo.AttendanceRuleSettings setting
          ON setting.TenantId = @TenantId
         AND setting.AttendanceEntryTypeId = mapRule.AttendanceEntryTypeId
         AND setting.IsActive = 1
         AND setting.IsApproved = 1
        LEFT JOIN dbo.EmployeeTimingSchedules timing
          ON timing.StaffId = staff.StaffId
         AND timing.ScheduleDate = dates.AttendanceDate
         AND timing.TenantId = @TenantId
        LEFT JOIN dbo.AppLookupValues timingHoliday
          ON timingHoliday.LookupValueId = timing.HolidayTypeId
        WHERE person.TenantId = @TenantId AND person.IsActive = 1
    ),
    Missing AS
    (
        SELECT TenantId, PersonId, AttendanceDate
        FROM EffectiveDays effective
        WHERE effective.IsOn = 1
          AND effective.IsOpenAttendance = 0
          AND effective.AttendanceTypeCode NOT IN (N'NONE', N'NOT_REQUIRED')
          
          AND @AsOfUtc >= DATEADD(
                minute,
                effective.AbsentAfterMinutes,
                DATEADD(
                    minute,
                    DATEDIFF(minute, CONVERT(time(0), '00:00'), effective.ShiftStart),
                    CONVERT(datetime2, effective.AttendanceDate)
                )
              )
          AND NOT EXISTS
          (
              SELECT 1 FROM dbo.AttendanceRecords attendance
              WHERE attendance.TenantId = effective.TenantId
                AND attendance.PersonId = effective.PersonId
                AND attendance.AttendanceDate = effective.AttendanceDate
          )
    )
    INSERT dbo.AttendanceRecords
        (TenantId, PersonId, AttendanceDate, AttendanceStatusId, PlatformActionStatusId, TotalBreakMinutes, CreatedDate, ModifiedDate)
    SELECT TenantId, PersonId, AttendanceDate, @Absent, @PlatformAbsent, 0, @AsOfUtc, @AsOfUtc
    FROM Missing
    OPTION (MAXRECURSION 367);

    UPDATE attendance
       SET AttendanceStatusId =
           CASE
               WHEN effective.IsOn = 0 AND attendance.CheckInUtc IS NULL THEN
                   CASE
                       WHEN effective.HolidayValueCode IN (N'HOLIDAY', N'ANNUAL_HOLIDAY') OR DATENAME(weekday, attendance.AttendanceDate) = N'Sunday'
                           THEN COALESCE(@Holiday, @DayOff, attendance.AttendanceStatusId)
                       ELSE COALESCE(@DayOff, @Holiday, attendance.AttendanceStatusId)
                   END
               WHEN ISNULL(mapRule.IsOpenAttendance, 0) = 1 AND attendance.CheckInUtc IS NOT NULL THEN @Present
               WHEN attendance.AttendanceStatusId = @ShortLeave THEN @ShortLeave
               WHEN attendance.CheckInUtc IS NULL AND @AsOfUtc > effective.CheckInAbsentDeadline  THEN @Absent
               WHEN attendance.CheckInUtc IS NULL THEN attendance.AttendanceStatusId
               WHEN attendance.CheckOutUtc IS NULL AND @AsOfUtc > effective.MissingCheckoutDeadline THEN @Absent
               WHEN attendance.CheckOutUtc IS NULL AND attendance.CheckInUtc > effective.OnTimeDeadline THEN @Late
               WHEN attendance.CheckOutUtc IS NULL THEN @Present
               WHEN attendance.CheckOutUtc < attendance.CheckInUtc THEN @Absent
               WHEN attendance.CheckOutUtc > effective.MissingCheckoutDeadline THEN @Absent
               WHEN DATEDIFF(minute, attendance.CheckOutUtc, effective.ShiftEndLocal) > effective.EarlyCheckoutAbsentAfterMinutes THEN @Absent
               WHEN DATEDIFF(minute, attendance.CheckInUtc, attendance.CheckOutUtc) - ISNULL(attendance.TotalBreakMinutes, 0) < effective.RequiredWorkingMinutes - effective.CheckOutAdjustMinutes THEN @EarlyDeparture
               WHEN attendance.CheckInUtc > effective.OnTimeDeadline THEN @CompletedLate
               ELSE @Present
           END,
           PlatformActionStatusId =
           CASE
               WHEN effective.IsOn = 0 AND attendance.CheckInUtc IS NULL THEN
                   CASE
                       WHEN effective.HolidayValueCode IN (N'HOLIDAY', N'ANNUAL_HOLIDAY') OR DATENAME(weekday, attendance.AttendanceDate) = N'Sunday'
                           THEN COALESCE(@PlatformHoliday, @PlatformDayOff, attendance.PlatformActionStatusId)
                       ELSE COALESCE(@PlatformDayOff, @PlatformHoliday, attendance.PlatformActionStatusId)
                   END
               WHEN ISNULL(mapRule.IsOpenAttendance, 0) = 1 AND attendance.CheckInUtc IS NOT NULL THEN @PlatformPresent
               WHEN attendance.AttendanceStatusId = @ShortLeave THEN @PlatformShortLeave
               WHEN attendance.CheckInUtc IS NULL AND @AsOfUtc > effective.CheckInAbsentDeadline  THEN @PlatformAbsent
               WHEN attendance.CheckInUtc IS NULL THEN attendance.PlatformActionStatusId
               WHEN attendance.CheckOutUtc IS NULL AND @AsOfUtc > effective.MissingCheckoutDeadline THEN @PlatformAbsent
               WHEN attendance.CheckOutUtc IS NULL AND attendance.CheckInUtc > effective.OnTimeDeadline THEN COALESCE(CASE WHEN DATEDIFF(minute, effective.OnTimeDeadline, attendance.CheckInUtc) >= ISNULL(effective.ExtremeLateAfterMinutes, 120) THEN effective.PlatformExtremeLateStatusId ELSE effective.PlatformLateStatusId END, @PlatformLate)
               WHEN attendance.CheckOutUtc IS NULL THEN @PlatformPresent
               WHEN attendance.CheckOutUtc < attendance.CheckInUtc THEN @PlatformAbsent
               WHEN attendance.CheckOutUtc > effective.MissingCheckoutDeadline THEN @PlatformAbsent
               WHEN DATEDIFF(minute, attendance.CheckOutUtc, effective.ShiftEndLocal) > effective.EarlyCheckoutAbsentAfterMinutes THEN @PlatformAbsent
               WHEN DATEDIFF(minute, attendance.CheckInUtc, attendance.CheckOutUtc) - ISNULL(attendance.TotalBreakMinutes, 0) < effective.RequiredWorkingMinutes - effective.CheckOutAdjustMinutes THEN COALESCE(CASE WHEN DATEDIFF(minute, attendance.CheckOutUtc, effective.ShiftEndLocal) >= ISNULL(effective.ExtremeEarlyDepartureAfterMinutes, 120) THEN effective.PlatformExtremeEarlyDepartureStatusId ELSE effective.PlatformEarlyDepartureStatusId END, @PlatformEarlyDeparture)
               WHEN attendance.CheckInUtc > effective.OnTimeDeadline THEN COALESCE(CASE WHEN DATEDIFF(minute, effective.OnTimeDeadline, attendance.CheckInUtc) >= ISNULL(effective.ExtremeLateAfterMinutes, 120) THEN effective.PlatformExtremeLateStatusId ELSE effective.PlatformLateStatusId END, @PlatformLate, @PlatformCompletedLate)
               ELSE @PlatformPresent
           END,
           CameraPlatformActionStatusId =
           CASE
               WHEN effective.IsOn = 0 AND attendance.CameraCheckInUtc IS NULL THEN
                   CASE
                       WHEN effective.HolidayValueCode IN (N'HOLIDAY', N'ANNUAL_HOLIDAY') OR DATENAME(weekday, attendance.AttendanceDate) = N'Sunday'
                           THEN COALESCE(@PlatformHoliday, @PlatformDayOff, attendance.CameraPlatformActionStatusId)
                       ELSE COALESCE(@PlatformDayOff, @PlatformHoliday, attendance.CameraPlatformActionStatusId)
                   END
               WHEN ISNULL(mapRule.IsOpenAttendance, 0) = 1 AND attendance.CameraCheckInUtc IS NOT NULL THEN @PlatformPresent
               WHEN attendance.AttendanceStatusId = @ShortLeave THEN @PlatformShortLeave
               WHEN attendance.CameraCheckInUtc IS NULL AND @AsOfUtc > effective.CheckInAbsentDeadline  THEN @PlatformAbsent
               WHEN attendance.CameraCheckInUtc IS NULL THEN attendance.CameraPlatformActionStatusId
               WHEN attendance.CameraCheckOutUtc IS NULL AND @AsOfUtc > effective.MissingCheckoutDeadline THEN @PlatformAbsent
               WHEN attendance.CameraCheckOutUtc IS NULL AND attendance.CameraCheckInUtc > effective.OnTimeDeadline THEN COALESCE(CASE WHEN DATEDIFF(minute, effective.OnTimeDeadline, attendance.CameraCheckInUtc) >= ISNULL(effective.ExtremeLateAfterMinutes, 120) THEN effective.PlatformExtremeLateStatusId ELSE effective.PlatformLateStatusId END, @PlatformLate)
               WHEN attendance.CameraCheckOutUtc IS NULL THEN @PlatformPresent
               WHEN attendance.CameraCheckOutUtc < attendance.CameraCheckInUtc THEN @PlatformAbsent
               WHEN attendance.CameraCheckOutUtc > effective.MissingCheckoutDeadline THEN @PlatformAbsent
               WHEN DATEDIFF(minute, attendance.CameraCheckOutUtc, effective.ShiftEndLocal) > effective.EarlyCheckoutAbsentAfterMinutes THEN @PlatformAbsent
               WHEN DATEDIFF(minute, attendance.CameraCheckInUtc, attendance.CameraCheckOutUtc) - ISNULL(attendance.TotalBreakMinutes, 0) < effective.RequiredWorkingMinutes - effective.CheckOutAdjustMinutes THEN COALESCE(CASE WHEN DATEDIFF(minute, attendance.CameraCheckOutUtc, effective.ShiftEndLocal) >= ISNULL(effective.ExtremeEarlyDepartureAfterMinutes, 120) THEN effective.PlatformExtremeEarlyDepartureStatusId ELSE effective.PlatformEarlyDepartureStatusId END, @PlatformEarlyDeparture)
               WHEN attendance.CameraCheckInUtc > effective.OnTimeDeadline THEN COALESCE(CASE WHEN DATEDIFF(minute, effective.OnTimeDeadline, attendance.CameraCheckInUtc) >= ISNULL(effective.ExtremeLateAfterMinutes, 120) THEN effective.PlatformExtremeLateStatusId ELSE effective.PlatformLateStatusId END, @PlatformLate, @PlatformCompletedLate)
               ELSE @PlatformPresent
           END,
           ModifiedDate = @AsOfUtc
    FROM dbo.AttendanceRecords attendance
    JOIN dbo.Persons person ON person.PersonId = attendance.PersonId
    LEFT JOIN dbo.StaffVacancy staff ON staff.PersonId = person.PersonId
    LEFT JOIN dbo.AttendanceMapRules mapRule ON mapRule.StaffId = staff.StaffId AND mapRule.TenantId = @TenantId
    LEFT JOIN dbo.AttendanceRuleSettings setting
      ON setting.TenantId = @TenantId
     AND setting.AttendanceEntryTypeId = mapRule.AttendanceEntryTypeId
     AND setting.IsActive = 1
     AND setting.IsApproved = 1
    LEFT JOIN dbo.EmployeeTimingSchedules timing
      ON timing.StaffId = staff.StaffId
     AND timing.ScheduleDate = attendance.AttendanceDate
     AND timing.TenantId = @TenantId
    LEFT JOIN dbo.AppLookupValues timingHoliday ON timingHoliday.LookupValueId = timing.HolidayTypeId
    CROSS APPLY
    (
        SELECT
            COALESCE(TRY_CONVERT(time(0), timing.TimeFrom), TRY_CONVERT(time(0), mapRule.TimeFrom), TRY_CONVERT(time(0), person.ShiftStartTime)) AS ShiftStart,
            COALESCE(TRY_CONVERT(time(0), timing.TimeTo), TRY_CONVERT(time(0), mapRule.TimeTo), TRY_CONVERT(time(0), person.ShiftEndTime)) AS ShiftEnd,
            COALESCE(timing.IsOn, CASE WHEN DATENAME(weekday, attendance.AttendanceDate) IN (N'Saturday', N'Sunday') THEN 0 ELSE 1 END) AS IsOn
    ) shiftData
    CROSS APPLY
    (
        SELECT
            DATEADD(minute, DATEDIFF(minute, CONVERT(time(0), '00:00'), shiftData.ShiftStart), CONVERT(datetime2, attendance.AttendanceDate)) AS ShiftStartLocal,
            DATEADD(day, CASE WHEN shiftData.ShiftEnd <= shiftData.ShiftStart THEN 1 ELSE 0 END, DATEADD(minute, DATEDIFF(minute, CONVERT(time(0), '00:00'), shiftData.ShiftEnd), CONVERT(datetime2, attendance.AttendanceDate))) AS ShiftEndLocal
    ) windows
    CROSS APPLY
    (
        SELECT
            shiftData.IsOn,
            windows.ShiftStartLocal,
            windows.ShiftEndLocal,
            DATEADD(minute, COALESCE(setting.CheckInAdjustMinutes, @Grace), windows.ShiftStartLocal) AS OnTimeDeadline,
            DATEADD(minute, COALESCE(setting.AbsentAfterShiftStartMinutes, @AbsentAfter), windows.ShiftStartLocal) AS CheckInAbsentDeadline,
            DATEADD(minute, COALESCE(setting.MissingCheckoutAfterShiftEndMinutes, @MissingOutAfter), windows.ShiftEndLocal) AS MissingCheckoutDeadline,
            COALESCE(setting.EarlyCheckoutAbsentAfterMinutes, @MissingOutAfter) AS EarlyCheckoutAbsentAfterMinutes,
            COALESCE(setting.CheckOutAdjustMinutes, @Tolerance) AS CheckOutAdjustMinutes,
            COALESCE(NULLIF(timing.WorkingMinutes, 0), NULLIF(setting.WorkingMinutes, 0), DATEDIFF(minute, windows.ShiftStartLocal, windows.ShiftEndLocal)) AS RequiredWorkingMinutes,
            COALESCE(setting.AdjustAbsentDays, 0) AS AdjustAbsentDays,
            setting.PlatformLateStatusId,
            setting.PlatformExtremeLateStatusId,
            setting.ExtremeLateAfterMinutes,
            setting.PlatformEarlyDepartureStatusId,
            setting.PlatformExtremeEarlyDepartureStatusId,
            setting.ExtremeEarlyDepartureAfterMinutes,
            timingHoliday.ValueCode AS HolidayValueCode,
            (
                SELECT COUNT_BIG(1)
                FROM dbo.AttendanceRecords previousAbsent
                WHERE previousAbsent.TenantId = attendance.TenantId
                  AND previousAbsent.PersonId = attendance.PersonId
                  AND previousAbsent.AttendanceStatusId = @Absent
                  AND previousAbsent.AttendanceDate >= DATEFROMPARTS(YEAR(attendance.AttendanceDate), MONTH(attendance.AttendanceDate), 1)
                  AND previousAbsent.AttendanceDate < attendance.AttendanceDate
            ) AS PriorMonthlyAbsentCount
    ) effective
    WHERE attendance.TenantId = @TenantId
      AND attendance.AttendanceDate BETWEEN @DateFrom AND @DateTo;
END;