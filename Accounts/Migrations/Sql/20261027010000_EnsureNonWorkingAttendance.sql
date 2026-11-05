CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EnsureNonWorkingDayRecords
    @TenantId int,
    @DateFrom date,
    @DateTo date,
    @PersonIds nvarchar(max)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @today date=CONVERT(date,SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'Pakistan Standard Time');
    DECLARE @monthStart date=DATEFROMPARTS(YEAR(@today),MONTH(@today),1);
    IF @DateFrom<@monthStart SET @DateFrom=@monthStart;
    IF @DateTo>@today SET @DateTo=@today;
    IF @DateFrom>@DateTo OR NOT EXISTS(SELECT 1 FROM dbo.Tenants WHERE Id=@TenantId AND IsActive=1)
    BEGIN SELECT 0 AS Value; RETURN; END;
    IF @PersonIds IS NOT NULL AND ISJSON(@PersonIds)<>1 THROW 51000,'Invalid attendance person scope.',1;
    DECLARE @dayOffId int=(SELECT Id FROM PlatformSettings.Statuses WHERE Name=N'Day Off' AND IsActive=1);
    DECLARE @holidayId int=(SELECT Id FROM PlatformSettings.Statuses WHERE Name=N'Holiday' AND IsActive=1);
    IF @dayOffId IS NULL OR @holidayId IS NULL THROW 51000,'Active Day Off and Holiday statuses are required.',1;

    DECLARE @days TABLE(PersonId uniqueidentifier NOT NULL, AttendanceDate date NOT NULL,
        StatusId int NULL, EntryTypeId int NULL, PRIMARY KEY(PersonId,AttendanceDate));
    ;WITH Dates AS
    (
        SELECT @DateFrom AttendanceDate
        UNION ALL SELECT DATEADD(day,1,AttendanceDate) FROM Dates WHERE AttendanceDate<@DateTo
    )
    INSERT INTO @days(PersonId,AttendanceDate,StatusId,EntryTypeId)
    SELECT person.PersonId,dates.AttendanceDate,
        CASE WHEN COALESCE(timing.IsOn,CASE WHEN calendar.Weekday IN(0,6) THEN 0 ELSE 1 END)=0
             THEN CASE WHEN timingType.ValueCode IN(N'HOLIDAY',N'ANNUAL_HOLIDAY') OR calendar.Weekday=0
                       THEN @holidayId ELSE @dayOffId END END,
        entryType.Id
    FROM dbo.StaffVacancy staff
    JOIN dbo.Persons person ON person.PersonId=staff.PersonId AND person.TenantId=@TenantId AND person.IsActive=1
    JOIN dbo.Vacancies vacancy ON vacancy.VacancyId=staff.VacancyId AND vacancy.TenantId=@TenantId
    LEFT JOIN dbo.PersonHrProfiles profile ON profile.PersonId=person.PersonId AND profile.TenantId=@TenantId
    CROSS JOIN Dates dates
    CROSS APPLY(SELECT DATEDIFF(day,CONVERT(date,'19000107'),dates.AttendanceDate)%7 Weekday) calendar
    LEFT JOIN dbo.EmployeeTimingSchedules timing ON timing.TenantId=@TenantId AND timing.StaffId=staff.StaffId AND timing.ScheduleDate=dates.AttendanceDate
    LEFT JOIN dbo.AppLookupValues timingType ON timingType.LookupValueId=timing.HolidayTypeId
    OUTER APPLY(SELECT TOP(1) map.AttendanceEntryTypeId FROM dbo.AttendanceMapRules map
                WHERE map.TenantId=@TenantId AND map.StaffId=staff.StaffId ORDER BY map.Id DESC) mapping
    LEFT JOIN PlatformTypes.AttendanceTypes entryType ON entryType.Id=mapping.AttendanceEntryTypeId AND entryType.TenantId=@TenantId AND entryType.IsActive=1
    WHERE staff.TenantId=@TenantId
      AND (profile.JoiningDate IS NULL OR dates.AttendanceDate>=CONVERT(date,profile.JoiningDate))
      AND (person.TerminationDateUtc IS NULL OR dates.AttendanceDate<=CONVERT(date,person.TerminationDateUtc))
      AND (@PersonIds IS NULL OR EXISTS(SELECT 1 FROM OPENJSON(@PersonIds) scope WHERE TRY_CONVERT(uniqueidentifier,scope.value)=person.PersonId))
    OPTION(MAXRECURSION 366);

    DECLARE @changed int=0;
    BEGIN TRY
        BEGIN TRANSACTION;
        -- Only system-derived, unpunched, untouched off-day statuses may follow a changed schedule.
        -- Preserve the row/ID when a Day On override makes it a normal working day.
        UPDATE attendance SET CanonicalStatusId=desired.StatusId,
            IsSystemGeneratedNonWorkingDay=CASE WHEN desired.StatusId IS NULL THEN 0 ELSE 1 END,
            ModifiedDate=SYSUTCDATETIME()
        FROM dbo.AttendanceRecords attendance WITH(UPDLOCK,HOLDLOCK)
        JOIN @days desired ON desired.PersonId=attendance.PersonId AND desired.AttendanceDate=attendance.AttendanceDate
        WHERE attendance.TenantId=@TenantId AND attendance.IsSystemGeneratedNonWorkingDay=1
          AND attendance.CanonicalStatusId IN(@dayOffId,@holidayId)
          AND (desired.StatusId IS NULL OR attendance.CanonicalStatusId<>desired.StatusId)
          AND attendance.CheckInUtc IS NULL AND attendance.CheckOutUtc IS NULL
          AND attendance.EffectiveCheckInUtc IS NULL AND attendance.EffectiveCheckOutUtc IS NULL
          AND attendance.CameraCheckInUtc IS NULL AND attendance.CameraCheckOutUtc IS NULL
          AND attendance.SupervisorRecordedByUserId IS NULL AND attendance.ApprovalRequestId IS NULL;
        SET @changed=@@ROWCOUNT;

        INSERT INTO dbo.AttendanceRecords(TenantId,PersonId,AttendanceDate,CanonicalStatusId,AttendanceEntryTypeId,
            TotalBreakMinutes,HasVerificationAnomaly,CreatedDate,IsSystemGeneratedNonWorkingDay)
        SELECT @TenantId,desired.PersonId,desired.AttendanceDate,desired.StatusId,desired.EntryTypeId,
            0,0,SYSUTCDATETIME(),1
        FROM @days desired
        WHERE desired.StatusId IS NOT NULL AND NOT EXISTS
            (SELECT 1 FROM dbo.AttendanceRecords existing WITH(UPDLOCK,HOLDLOCK)
             WHERE existing.PersonId=desired.PersonId AND existing.AttendanceDate=desired.AttendanceDate);
        SET @changed=@changed+@@ROWCOUNT;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
    SELECT @changed AS Value;
END;
