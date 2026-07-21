using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260717140000_AddDatabaseDrivenAttendancePolicy")]
public sealed class AddDatabaseDrivenAttendancePolicy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DECLARE @ProcessId int=(SELECT TOP(1) Id FROM dbo.Processes WHERE ProcessName=N'Attendance');
            IF @ProcessId IS NULL BEGIN INSERT dbo.Processes(ProcessName) VALUES(N'Attendance'); SET @ProcessId=SCOPE_IDENTITY(); END;
            IF NOT EXISTS(SELECT 1 FROM dbo.Statuses WHERE StatusName=N'T-Present') INSERT dbo.Statuses(StatusName) VALUES(N'T-Present');
            IF NOT EXISTS(SELECT 1 FROM dbo.Statuses WHERE StatusName=N'Short Leave') INSERT dbo.Statuses(StatusName) VALUES(N'Short Leave');
            IF NOT EXISTS(SELECT 1 FROM dbo.ColorStyles WHERE ColorName=N'T-Present' AND ColorCode=N'#10B981' AND FontColor=N'#FFFFFF' AND FontSize=N'12px')
                INSERT dbo.ColorStyles(ColorName,ColorCode,FontColor,FontSize) VALUES(N'T-Present',N'#10B981',N'#FFFFFF',N'12px');
            IF NOT EXISTS(SELECT 1 FROM dbo.ColorStyles WHERE ColorName=N'Short Leave' AND ColorCode=N'#F59E0B' AND FontColor=N'#FFFFFF' AND FontSize=N'12px')
                INSERT dbo.ColorStyles(ColorName,ColorCode,FontColor,FontSize) VALUES(N'Short Leave',N'#F59E0B',N'#FFFFFF',N'12px');
            IF NOT EXISTS(SELECT 1 FROM dbo.ProcessStatusStyles WHERE ProcessId=@ProcessId AND Code=N'TP' AND TenantId IS NULL)
                INSERT dbo.ProcessStatusStyles(ProcessId,StatusId,ColorStyleId,TenantId,IsSystem,Code,Description,DisplayOrder,IsPaid,IsActive,CreatedDate)
                SELECT @ProcessId,s.Id,c.Id,NULL,1,N'TP',N'Late arrival with complete required net working time.',6,1,1,SYSUTCDATETIME()
                FROM dbo.Statuses s CROSS JOIN dbo.ColorStyles c WHERE s.StatusName=N'T-Present' AND c.ColorName=N'T-Present' AND c.ColorCode=N'#10B981' AND c.FontColor=N'#FFFFFF' AND c.FontSize=N'12px';
            IF NOT EXISTS(SELECT 1 FROM dbo.ProcessStatusStyles WHERE ProcessId=@ProcessId AND Code=N'SL' AND TenantId IS NULL)
                INSERT dbo.ProcessStatusStyles(ProcessId,StatusId,ColorStyleId,TenantId,IsSystem,Code,Description,DisplayOrder,IsPaid,IsActive,CreatedDate)
                SELECT @ProcessId,s.Id,c.Id,NULL,1,N'SL',N'Checked out before completing required net working time.',7,0,1,SYSUTCDATETIME()
                FROM dbo.Statuses s CROSS JOIN dbo.ColorStyles c WHERE s.StatusName=N'Short Leave' AND c.ColorName=N'Short Leave' AND c.ColorCode=N'#F59E0B' AND c.FontColor=N'#FFFFFF' AND c.FontSize=N'12px';
            """);

        migrationBuilder.Sql("""
            CREATE TABLE dbo.AttendancePolicies(
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttendancePolicies PRIMARY KEY,
                TenantId int NULL, PolicyName nvarchar(100) NOT NULL, TimeZoneId nvarchar(100) NOT NULL,
                EarliestCheckInMinutesBefore int NOT NULL, OnTimeGraceMinutesAfter int NOT NULL,
                AbsentAfterShiftStartMinutes int NOT NULL, MissingCheckoutAfterShiftEndMinutes int NOT NULL,
                FullDayToleranceMinutes int NOT NULL,
                PresentStatusId int NOT NULL, LateStatusId int NOT NULL, CompletedLateStatusId int NOT NULL,
                ShortLeaveStatusId int NOT NULL, AbsentStatusId int NOT NULL,
                IsActive bit NOT NULL, CreatedDate datetime2 NOT NULL, ModifiedDate datetime2 NULL,
                CONSTRAINT FK_AttendancePolicies_Tenants FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id),
                CONSTRAINT FK_AttendancePolicies_Present FOREIGN KEY(PresentStatusId) REFERENCES dbo.ProcessStatusStyles(Id),
                CONSTRAINT FK_AttendancePolicies_Late FOREIGN KEY(LateStatusId) REFERENCES dbo.ProcessStatusStyles(Id),
                CONSTRAINT FK_AttendancePolicies_CompletedLate FOREIGN KEY(CompletedLateStatusId) REFERENCES dbo.ProcessStatusStyles(Id),
                CONSTRAINT FK_AttendancePolicies_ShortLeave FOREIGN KEY(ShortLeaveStatusId) REFERENCES dbo.ProcessStatusStyles(Id),
                CONSTRAINT FK_AttendancePolicies_Absent FOREIGN KEY(AbsentStatusId) REFERENCES dbo.ProcessStatusStyles(Id));
            CREATE UNIQUE INDEX IX_AttendancePolicies_ActiveTenant ON dbo.AttendancePolicies(TenantId) WHERE IsActive=1;
            DECLARE @ProcessId int=(SELECT Id FROM dbo.Processes WHERE ProcessName=N'Attendance');
            INSERT dbo.AttendancePolicies(TenantId,PolicyName,TimeZoneId,EarliestCheckInMinutesBefore,OnTimeGraceMinutesAfter,
                AbsentAfterShiftStartMinutes,MissingCheckoutAfterShiftEndMinutes,FullDayToleranceMinutes,
                PresentStatusId,LateStatusId,CompletedLateStatusId,ShortLeaveStatusId,AbsentStatusId,IsActive,CreatedDate)
            SELECT NULL,N'Default attendance policy',N'Pakistan Standard Time',5,5,120,120,0,p.Id,lt.Id,tp.Id,sl.Id,a.Id,1,SYSUTCDATETIME()
            FROM dbo.ProcessStatusStyles p,dbo.ProcessStatusStyles lt,dbo.ProcessStatusStyles tp,dbo.ProcessStatusStyles sl,dbo.ProcessStatusStyles a
            WHERE p.ProcessId=@ProcessId AND p.Code=N'P' AND p.TenantId IS NULL
              AND lt.ProcessId=@ProcessId AND lt.Code=N'LT' AND lt.TenantId IS NULL
              AND tp.ProcessId=@ProcessId AND tp.Code=N'TP' AND tp.TenantId IS NULL
              AND sl.ProcessId=@ProcessId AND sl.Code=N'SL' AND sl.TenantId IS NULL
              AND a.ProcessId=@ProcessId AND a.Code=N'A' AND a.TenantId IS NULL;
            """);

        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EvaluateStatuses
                @TenantId int, @DateFrom date, @DateTo date, @AsOfUtc datetime2
            AS
            BEGIN
                SET NOCOUNT ON; SET XACT_ABORT ON;
                DECLARE @PolicyId int,@TimeZoneId nvarchar(100),@Grace int,@AbsentAfter int,@MissingOutAfter int,@Tolerance int,
                    @Present int,@Late int,@CompletedLate int,@ShortLeave int,@Absent int,@NowLocal datetime2;
                SELECT TOP(1) @PolicyId=Id,@TimeZoneId=TimeZoneId,@Grace=OnTimeGraceMinutesAfter,
                    @AbsentAfter=AbsentAfterShiftStartMinutes,@MissingOutAfter=MissingCheckoutAfterShiftEndMinutes,
                    @Tolerance=FullDayToleranceMinutes,@Present=PresentStatusId,@Late=LateStatusId,
                    @CompletedLate=CompletedLateStatusId,@ShortLeave=ShortLeaveStatusId,@Absent=AbsentStatusId
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
                    WHEN ar.CheckInUtc IS NULL THEN @Absent
                    WHEN ar.CheckOutUtc IS NULL AND DATEADD(minute,@MissingOutAfter,DATEADD(minute,DATEDIFF(minute,'00:00',TRY_CONVERT(time,p.ShiftEndTime)),CONVERT(datetime2,ar.AttendanceDate)))<=@NowLocal THEN @Absent
                    WHEN ar.CheckOutUtc IS NULL AND CONVERT(time,(@AsOfUtc AT TIME ZONE 'UTC') AT TIME ZONE @TimeZoneId)>CONVERT(time,DATEADD(minute,@Grace,TRY_CONVERT(time,p.ShiftStartTime))) THEN @Late
                    WHEN ar.CheckOutUtc IS NULL THEN @Present
                    WHEN DATEDIFF(minute,ar.CheckInUtc,ar.CheckOutUtc)-ar.TotalBreakMinutes < DATEDIFF(minute,TRY_CONVERT(time,p.ShiftStartTime),TRY_CONVERT(time,p.ShiftEndTime))-@Tolerance THEN @ShortLeave
                    WHEN CONVERT(time,(ar.CheckInUtc AT TIME ZONE 'UTC') AT TIME ZONE @TimeZoneId)>CONVERT(time,DATEADD(minute,@Grace,TRY_CONVERT(time,p.ShiftStartTime))) THEN @CompletedLate
                    ELSE @Present END,
                    ModifiedDate=@AsOfUtc
                FROM dbo.AttendanceRecords ar JOIN dbo.Persons p ON p.PersonId=ar.PersonId
                WHERE ar.TenantId=@TenantId AND ar.AttendanceDate BETWEEN @DateFrom AND @DateTo;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Attendance policy history is intentionally retained.");
}
