using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260717150000_SeparateEarlyDepartureFromShortLeave")]
public sealed class SeparateEarlyDepartureFromShortLeave : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.AttendancePolicies','EarlyDepartureStatusId') IS NULL
                ALTER TABLE dbo.AttendancePolicies ADD EarlyDepartureStatusId int NULL;
            """);
        migrationBuilder.Sql("""
            DECLARE @ProcessId int=(SELECT Id FROM dbo.Processes WHERE ProcessName=N'Attendance');
            IF NOT EXISTS(SELECT 1 FROM dbo.Statuses WHERE StatusName=N'Early Leaving') INSERT dbo.Statuses(StatusName) VALUES(N'Early Leaving');
            IF NOT EXISTS(SELECT 1 FROM dbo.ColorStyles WHERE ColorName=N'Early Leaving' AND ColorCode=N'#F97316' AND FontColor=N'#FFFFFF' AND FontSize=N'12px')
                INSERT dbo.ColorStyles(ColorName,ColorCode,FontColor,FontSize) VALUES(N'Early Leaving',N'#F97316',N'#FFFFFF',N'12px');
            IF NOT EXISTS(SELECT 1 FROM dbo.ProcessStatusStyles WHERE ProcessId=@ProcessId AND Code=N'EL' AND TenantId IS NULL)
                INSERT dbo.ProcessStatusStyles(ProcessId,StatusId,ColorStyleId,TenantId,IsSystem,Code,Description,DisplayOrder,IsPaid,IsActive,CreatedDate)
                SELECT @ProcessId,s.Id,c.Id,NULL,1,N'EL',N'Checked out without completing the required net working time.',8,1,1,SYSUTCDATETIME()
                FROM dbo.Statuses s CROSS JOIN dbo.ColorStyles c
                WHERE s.StatusName=N'Early Leaving' AND c.ColorName=N'Early Leaving' AND c.ColorCode=N'#F97316' AND c.FontColor=N'#FFFFFF' AND c.FontSize=N'12px';
            UPDATE ap SET EarlyDepartureStatusId=ps.Id
            FROM dbo.AttendancePolicies ap CROSS APPLY(
                SELECT TOP(1) x.Id FROM dbo.ProcessStatusStyles x
                WHERE x.ProcessId=@ProcessId AND x.Code=N'EL' AND (x.TenantId=ap.TenantId OR x.TenantId IS NULL)
                ORDER BY CASE WHEN x.TenantId=ap.TenantId THEN 0 ELSE 1 END) ps
            WHERE ap.EarlyDepartureStatusId IS NULL;
            ALTER TABLE dbo.AttendancePolicies ALTER COLUMN EarlyDepartureStatusId int NOT NULL;
            IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_AttendancePolicies_EarlyDeparture')
                ALTER TABLE dbo.AttendancePolicies ADD CONSTRAINT FK_AttendancePolicies_EarlyDeparture
                FOREIGN KEY(EarlyDepartureStatusId) REFERENCES dbo.ProcessStatusStyles(Id);
            """);
        migrationBuilder.Sql("""
            UPDATE ar SET AttendanceStatusId=policy.EarlyDepartureStatusId,ModifiedDate=SYSUTCDATETIME()
            FROM dbo.AttendanceRecords ar
            JOIN dbo.Persons p ON p.PersonId=ar.PersonId
            CROSS APPLY(
                SELECT TOP(1) ap.ShortLeaveStatusId,ap.EarlyDepartureStatusId
                FROM dbo.AttendancePolicies ap
                WHERE ap.IsActive=1 AND (ap.TenantId=ar.TenantId OR ap.TenantId IS NULL)
                ORDER BY CASE WHEN ap.TenantId=ar.TenantId THEN 0 ELSE 1 END) policy
            WHERE ar.AttendanceStatusId=policy.ShortLeaveStatusId AND ar.CheckInUtc IS NOT NULL AND ar.CheckOutUtc IS NOT NULL
              AND DATEDIFF(minute,ar.CheckInUtc,ar.CheckOutUtc)-ar.TotalBreakMinutes
                  < DATEDIFF(minute,TRY_CONVERT(time,p.ShiftStartTime),TRY_CONVERT(time,p.ShiftEndTime));
            """);
        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EvaluateStatuses
                @TenantId int, @DateFrom date, @DateTo date, @AsOfUtc datetime2
            AS
            BEGIN
                SET NOCOUNT ON; SET XACT_ABORT ON;
                DECLARE @PolicyId int,@TimeZoneId nvarchar(100),@Grace int,@AbsentAfter int,@MissingOutAfter int,@Tolerance int,
                    @Present int,@Late int,@CompletedLate int,@ShortLeave int,@EarlyDeparture int,@Absent int,@NowLocal datetime2;
                SELECT TOP(1) @PolicyId=Id,@TimeZoneId=TimeZoneId,@Grace=OnTimeGraceMinutesAfter,
                    @AbsentAfter=AbsentAfterShiftStartMinutes,@MissingOutAfter=MissingCheckoutAfterShiftEndMinutes,
                    @Tolerance=FullDayToleranceMinutes,@Present=PresentStatusId,@Late=LateStatusId,
                    @CompletedLate=CompletedLateStatusId,@ShortLeave=ShortLeaveStatusId,
                    @EarlyDeparture=EarlyDepartureStatusId,@Absent=AbsentStatusId
                FROM dbo.AttendancePolicies WHERE IsActive=1 AND (TenantId=@TenantId OR TenantId IS NULL)
                ORDER BY CASE WHEN TenantId=@TenantId THEN 0 ELSE 1 END;
                IF @PolicyId IS NULL THROW 51000,'No active attendance policy is configured.',1;
                SET @NowLocal=CONVERT(datetime2,(@AsOfUtc AT TIME ZONE 'UTC') AT TIME ZONE @TimeZoneId);

                ;WITH Dates AS(
                    SELECT @DateFrom D UNION ALL SELECT DATEADD(day,1,D) FROM Dates WHERE D<@DateTo
                ), Missing AS(
                    SELECT p.TenantId,p.PersonId,d.D
                    FROM dbo.Persons p JOIN dbo.StaffVacancy sv ON sv.PersonId=p.PersonId CROSS JOIN Dates d
                    WHERE p.TenantId=@TenantId AND p.IsActive=1 AND DATENAME(weekday,d.D) NOT IN(N'Saturday',N'Sunday')
                      AND DATEADD(minute,@AbsentAfter,DATEADD(minute,DATEDIFF(minute,'00:00',TRY_CONVERT(time,p.ShiftStartTime)),CONVERT(datetime2,d.D)))<=@NowLocal
                      AND NOT EXISTS(SELECT 1 FROM dbo.AttendanceRecords ar WHERE ar.PersonId=p.PersonId AND ar.AttendanceDate=d.D)
                )
                INSERT dbo.AttendanceRecords(TenantId,PersonId,AttendanceDate,AttendanceStatusId,TotalBreakMinutes,CreatedDate,ModifiedDate)
                SELECT TenantId,PersonId,D,@Absent,0,@AsOfUtc,@AsOfUtc FROM Missing OPTION(MAXRECURSION 367);

                UPDATE ar SET AttendanceStatusId=CASE
                    WHEN ar.AttendanceStatusId=@ShortLeave THEN @ShortLeave
                    WHEN ar.CheckInUtc IS NULL THEN @Absent
                    WHEN ar.CheckOutUtc IS NULL AND DATEADD(minute,@MissingOutAfter,DATEADD(minute,DATEDIFF(minute,'00:00',TRY_CONVERT(time,p.ShiftEndTime)),CONVERT(datetime2,ar.AttendanceDate)))<=@NowLocal THEN @Absent
                    WHEN ar.CheckOutUtc IS NULL AND CONVERT(time,(@AsOfUtc AT TIME ZONE 'UTC') AT TIME ZONE @TimeZoneId)>CONVERT(time,DATEADD(minute,@Grace,TRY_CONVERT(time,p.ShiftStartTime))) THEN @Late
                    WHEN ar.CheckOutUtc IS NULL THEN @Present
                    WHEN DATEDIFF(minute,ar.CheckInUtc,ar.CheckOutUtc)-ar.TotalBreakMinutes < DATEDIFF(minute,TRY_CONVERT(time,p.ShiftStartTime),TRY_CONVERT(time,p.ShiftEndTime))-@Tolerance THEN @EarlyDeparture
                    WHEN CONVERT(time,(ar.CheckInUtc AT TIME ZONE 'UTC') AT TIME ZONE @TimeZoneId)>CONVERT(time,DATEADD(minute,@Grace,TRY_CONVERT(time,p.ShiftStartTime))) THEN @CompletedLate
                    ELSE @Present END,
                    ModifiedDate=@AsOfUtc
                FROM dbo.AttendanceRecords ar JOIN dbo.Persons p ON p.PersonId=ar.PersonId
                WHERE ar.TenantId=@TenantId AND ar.AttendanceDate BETWEEN @DateFrom AND @DateTo;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Attendance status history is intentionally preserved.");
}
