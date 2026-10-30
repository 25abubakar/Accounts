using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261030093000_AddEnsureAbsentAttendanceRecordsProcedure")]
public sealed class AddEnsureAbsentAttendanceRecordsProcedure : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
SET ANSI_NULLS ON;
");
        migrationBuilder.Sql(@"
SET QUOTED_IDENTIFIER ON;
");
        migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EnsureAbsentRecords
    @TenantId int,
    @DateFrom date,
    @DateTo date,
    @PersonIds nvarchar(max)=NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @nowUtc datetime2 = SYSUTCDATETIME();
    DECLARE @nowLocal datetime2 = @nowUtc AT TIME ZONE 'UTC' AT TIME ZONE 'Pakistan Standard Time';
    DECLARE @today date = CONVERT(date, @nowLocal);
    DECLARE @monthStart date = DATEFROMPARTS(YEAR(@today), MONTH(@today), 1);
    IF @DateFrom < @monthStart SET @DateFrom = @monthStart;
    IF @DateTo > @today SET @DateTo = @today;
    IF @DateFrom > @DateTo OR NOT EXISTS(SELECT 1 FROM dbo.Tenants WHERE Id=@TenantId AND IsActive=1)
    BEGIN SELECT 0 AS Value; RETURN; END;
    IF @PersonIds IS NOT NULL AND ISJSON(@PersonIds)<>1 THROW 51000, 'Invalid attendance person scope.', 1;

    DECLARE @absentId int = (SELECT Id FROM PlatformSettings.Statuses WHERE Name=N'Absent' AND IsActive=1);
    IF @absentId IS NULL SET @absentId = 5;

    DECLARE @days TABLE(
        PersonId uniqueidentifier NOT NULL,
        AttendanceDate date NOT NULL,
        EntryTypeId int NULL,
        PRIMARY KEY(PersonId, AttendanceDate)
    );

    ;WITH Dates AS
    (
        SELECT @DateFrom AttendanceDate
        UNION ALL SELECT DATEADD(day,1,AttendanceDate) FROM Dates WHERE AttendanceDate<@DateTo
    )
    INSERT INTO @days(PersonId, AttendanceDate, EntryTypeId)
    SELECT person.PersonId, dates.AttendanceDate, entryType.Id
    FROM dbo.StaffVacancy staff
    JOIN dbo.Persons person ON person.PersonId=staff.PersonId AND person.TenantId=@TenantId AND person.IsActive=1
    JOIN dbo.Vacancies vacancy ON vacancy.VacancyId=staff.VacancyId AND vacancy.TenantId=@TenantId
    LEFT JOIN dbo.PersonHrProfiles profile ON profile.PersonId=person.PersonId AND profile.TenantId=@TenantId
    CROSS JOIN Dates dates
    CROSS APPLY(SELECT DATEDIFF(day,CONVERT(date,'19000107'),dates.AttendanceDate)%7 Weekday) calendar
    LEFT JOIN dbo.EmployeeTimingSchedules timing ON timing.TenantId=@TenantId AND timing.StaffId=staff.StaffId AND timing.ScheduleDate=dates.AttendanceDate
    OUTER APPLY(SELECT TOP(1) map.AttendanceEntryTypeId, map.TimeFrom, map.IsOpenAttendance
                FROM dbo.AttendanceMapRules map
                WHERE map.TenantId=@TenantId AND map.StaffId=staff.StaffId ORDER BY map.Id DESC) mapping
    LEFT JOIN dbo.AttendanceRuleSettings ruleSetting
      ON ruleSetting.TenantId=@TenantId
     AND ruleSetting.AttendanceEntryTypeId=mapping.AttendanceEntryTypeId
     AND ruleSetting.IsActive=1 AND ruleSetting.IsApproved=1
    LEFT JOIN PlatformTypes.AttendanceTypes entryType ON entryType.Id=mapping.AttendanceEntryTypeId AND entryType.TenantId=@TenantId AND entryType.IsActive=1
    CROSS APPLY(SELECT
        COALESCE(timing.IsOn, CASE WHEN calendar.Weekday IN(0,6) THEN 0 ELSE 1 END) IsOn,
        COALESCE(TRY_CONVERT(time(0), timing.TimeFrom), TRY_CONVERT(time(0), mapping.TimeFrom), TRY_CONVERT(time(0), person.ShiftStartTime), CONVERT(time(0), '09:00')) ShiftStart,
        ISNULL(ruleSetting.AbsentAfterShiftStartMinutes, 180) AbsentAfterMinutes
    ) effective
    WHERE staff.TenantId=@TenantId
      AND effective.IsOn = 1
      AND ISNULL(mapping.IsOpenAttendance, 0) = 0
      AND ISNULL(entryType.Code, N'') NOT IN (N'NONE', N'NOT_REQUIRED')
      AND (profile.JoiningDate IS NULL OR dates.AttendanceDate>=CONVERT(date,profile.JoiningDate))
      AND (person.TerminationDateUtc IS NULL OR dates.AttendanceDate<=CONVERT(date,person.TerminationDateUtc))
      AND (@PersonIds IS NULL OR EXISTS(SELECT 1 FROM OPENJSON(@PersonIds) scope WHERE TRY_CONVERT(uniqueidentifier,scope.value)=person.PersonId))
      AND (
          dates.AttendanceDate < @today
          OR @nowLocal >= DATEADD(minute, effective.AbsentAfterMinutes,
                            DATEADD(minute, DATEDIFF(minute, CONVERT(time(0), '00:00'), effective.ShiftStart),
                            CONVERT(datetime2, dates.AttendanceDate)))
      )
    OPTION(MAXRECURSION 366);

    DECLARE @changed int = 0;
    BEGIN TRY
        BEGIN TRANSACTION;

        INSERT INTO dbo.AttendanceRecords(
            TenantId, PersonId, AttendanceDate, CanonicalStatusId, AttendanceEntryTypeId,
            TotalBreakMinutes, HasVerificationAnomaly, CreatedDate, ModifiedDate
        )
        SELECT @TenantId, desired.PersonId, desired.AttendanceDate, @absentId, desired.EntryTypeId,
            0, 0, @nowUtc, @nowUtc
        FROM @days desired
        WHERE NOT EXISTS
            (SELECT 1 FROM dbo.AttendanceRecords existing WITH(UPDLOCK,HOLDLOCK)
             WHERE existing.TenantId=@TenantId AND existing.PersonId=desired.PersonId AND existing.AttendanceDate=desired.AttendanceDate);

        SET @changed = @@ROWCOUNT;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;

    SELECT @changed AS Value;
END;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_Attendance_EnsureAbsentRecords;");
    }
}

