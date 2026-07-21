using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260717220000_AddEmployeeTimingSchedules")]
public sealed class AddEmployeeTimingSchedules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EmployeeTimingSchedules",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TenantId = table.Column<int>(type: "int", nullable: false),
                PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ScheduleDate = table.Column<DateOnly>(type: "date", nullable: false),
                HolidayType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                TimeFrom = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                TimeTo = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                IsOn = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                ModifiedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EmployeeTimingSchedules", x => x.Id);
                table.ForeignKey(
                    name: "FK_EmployeeTimingSchedules_Persons_PersonId",
                    column: x => x.PersonId,
                    principalTable: "Persons",
                    principalColumn: "PersonId",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_EmployeeTimingSchedules_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_EmployeeTimingSchedules_PersonId_ScheduleDate",
            table: "EmployeeTimingSchedules",
            columns: new[] { "PersonId", "ScheduleDate" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_EmployeeTimingSchedules_TenantId_ScheduleDate",
            table: "EmployeeTimingSchedules",
            columns: new[] { "TenantId", "ScheduleDate" });

        migrationBuilder.Sql(
            """
            DECLARE @LookupTypeId int = (
                SELECT TOP (1) LookupTypeId
                FROM dbo.AppLookupTypes
                WHERE LookupTypeCode = N'TIMING_HOLIDAY_TYPE'
            );

            IF @LookupTypeId IS NULL
            BEGIN
                INSERT dbo.AppLookupTypes
                    (LookupTypeCode, LookupTypeName, IsActive, CreatedOn)
                VALUES
                    (N'TIMING_HOLIDAY_TYPE', N'Timing Chart Holiday Type', 1, SYSUTCDATETIME());
                SET @LookupTypeId = SCOPE_IDENTITY();
            END;

            DECLARE @Types TABLE
            (
                ValueCode nvarchar(100),
                DisplayText nvarchar(150),
                SortOrder int,
                IsDefault bit,
                MetadataJson nvarchar(2000)
            );
            INSERT @Types VALUES
                (N'WORKING_DAY', N'Working Day', 10, 1, N'{"defaultIsOn":true}'),
                (N'SPECIAL_DUTY', N'Special Duty', 20, 0, N'{"defaultIsOn":true}'),
                (N'DAY_OFF', N'Day Off', 30, 0, N'{"defaultIsOn":false}'),
                (N'PUBLIC_HOLIDAY', N'Public Holiday', 40, 0, N'{"defaultIsOn":false}'),
                (N'COMPANY_HOLIDAY', N'Company Holiday', 50, 0, N'{"defaultIsOn":false}');

            MERGE dbo.AppLookupValues AS target
            USING @Types AS source
               ON target.LookupTypeId = @LookupTypeId
              AND target.ValueCode = source.ValueCode
            WHEN MATCHED THEN UPDATE SET
                DisplayText = source.DisplayText,
                SortOrder = source.SortOrder,
                IsDefault = source.IsDefault,
                IsActive = 1,
                MetadataJson = source.MetadataJson
            WHEN NOT MATCHED THEN INSERT
                (LookupTypeId, ValueCode, DisplayText, SortOrder, IsDefault, IsActive, MetadataJson, CreatedOn)
            VALUES
                (@LookupTypeId, source.ValueCode, source.DisplayText, source.SortOrder,
                 source.IsDefault, 1, source.MetadataJson, SYSUTCDATETIME());

            DECLARE @TimingMenuId int = (
                SELECT TOP (1) Id FROM dbo.Menus
                WHERE Route = N'/attendance/timing-chart'
                ORDER BY Id
            );
            IF @TimingMenuId IS NOT NULL
                UPDATE dbo.TenantMenuPermissions
                SET CanAdd = 1, CanEdit = 1
                WHERE MenuId = @TimingMenuId AND IsAllow = 1 AND CanView = 1;
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EvaluateStatuses
                @TenantId int, @DateFrom date, @DateTo date, @AsOfUtc datetime2
            AS
            BEGIN
                SET NOCOUNT ON; SET XACT_ABORT ON;
                DECLARE @PolicyId int,@TimeZoneId nvarchar(100),@Grace int,@AbsentAfter int,@MissingOutAfter int,@Tolerance int,
                    @Present int,@Late int,@CompletedLate int,@ShortLeave int,@EarlyDeparture int,@Absent int,
                    @DayOff int,@Holiday int,@NowLocal datetime2,@ProcessId int;
                SELECT TOP(1) @PolicyId=Id,@TimeZoneId=TimeZoneId,@Grace=OnTimeGraceMinutesAfter,
                    @AbsentAfter=AbsentAfterShiftStartMinutes,@MissingOutAfter=MissingCheckoutAfterShiftEndMinutes,
                    @Tolerance=FullDayToleranceMinutes,@Present=PresentStatusId,@Late=LateStatusId,
                    @CompletedLate=CompletedLateStatusId,@ShortLeave=ShortLeaveStatusId,
                    @EarlyDeparture=EarlyDepartureStatusId,@Absent=AbsentStatusId
                FROM dbo.AttendancePolicies WHERE IsActive=1 AND (TenantId=@TenantId OR TenantId IS NULL)
                ORDER BY CASE WHEN TenantId=@TenantId THEN 0 ELSE 1 END;
                IF @PolicyId IS NULL THROW 51000,'No active attendance policy is configured.',1;

                SELECT @ProcessId=Id FROM dbo.Processes WHERE ProcessName=N'Attendance';
                SELECT TOP(1) @DayOff=Id FROM dbo.ProcessStatusStyles
                    WHERE ProcessId=@ProcessId AND Code=N'DO' AND IsActive=1 AND (TenantId=@TenantId OR TenantId IS NULL)
                    ORDER BY CASE WHEN TenantId=@TenantId THEN 0 ELSE 1 END;
                SELECT TOP(1) @Holiday=Id FROM dbo.ProcessStatusStyles
                    WHERE ProcessId=@ProcessId AND Code=N'H' AND IsActive=1 AND (TenantId=@TenantId OR TenantId IS NULL)
                    ORDER BY CASE WHEN TenantId=@TenantId THEN 0 ELSE 1 END;
                SET @NowLocal=CONVERT(datetime2,(@AsOfUtc AT TIME ZONE 'UTC') AT TIME ZONE @TimeZoneId);

                ;WITH Dates AS(
                    SELECT @DateFrom D UNION ALL SELECT DATEADD(day,1,D) FROM Dates WHERE D<@DateTo
                ), EffectiveDays AS(
                    SELECT p.TenantId,p.PersonId,d.D,
                        COALESCE(timing.IsOn,CASE WHEN DATENAME(weekday,d.D) IN(N'Saturday',N'Sunday') THEN 0 ELSE 1 END) IsOn,
                        COALESCE(TRY_CONVERT(time(0),timing.TimeFrom),TRY_CONVERT(time(0),p.ShiftStartTime)) ShiftStart
                    FROM dbo.Persons p
                    JOIN dbo.StaffVacancy staff ON staff.PersonId=p.PersonId
                    CROSS JOIN Dates d
                    LEFT JOIN dbo.EmployeeTimingSchedules timing
                      ON timing.PersonId=p.PersonId AND timing.ScheduleDate=d.D AND timing.TenantId=@TenantId
                    WHERE p.TenantId=@TenantId AND p.IsActive=1
                ), Missing AS(
                    SELECT TenantId,PersonId,D
                    FROM EffectiveDays dayInfo
                    WHERE dayInfo.IsOn=1
                      AND DATEADD(minute,@AbsentAfter,
                          DATEADD(minute,DATEDIFF(minute,'00:00',dayInfo.ShiftStart),CONVERT(datetime2,dayInfo.D)))<=@NowLocal
                      AND NOT EXISTS(
                          SELECT 1 FROM dbo.AttendanceRecords attendance
                          WHERE attendance.PersonId=dayInfo.PersonId AND attendance.AttendanceDate=dayInfo.D)
                )
                INSERT dbo.AttendanceRecords
                    (TenantId,PersonId,AttendanceDate,AttendanceStatusId,TotalBreakMinutes,CreatedDate,ModifiedDate)
                SELECT TenantId,PersonId,D,@Absent,0,@AsOfUtc,@AsOfUtc
                FROM Missing OPTION(MAXRECURSION 367);

                UPDATE attendance SET AttendanceStatusId=CASE
                    WHEN effective.IsOn=0 AND attendance.CheckInUtc IS NULL THEN
                        CASE WHEN timing.HolidayType IN(N'PUBLIC_HOLIDAY',N'COMPANY_HOLIDAY')
                             THEN COALESCE(@Holiday,@DayOff,attendance.AttendanceStatusId)
                             ELSE COALESCE(@DayOff,@Holiday,attendance.AttendanceStatusId) END
                    WHEN attendance.AttendanceStatusId=@ShortLeave THEN @ShortLeave
                    WHEN attendance.CheckInUtc IS NULL THEN @Absent
                    WHEN attendance.CheckOutUtc IS NULL
                      AND DATEADD(minute,@MissingOutAfter,
                          DATEADD(day,CASE WHEN effective.ShiftEnd<=effective.ShiftStart THEN 1 ELSE 0 END,
                              DATEADD(minute,DATEDIFF(minute,'00:00',effective.ShiftEnd),CONVERT(datetime2,attendance.AttendanceDate))))<=@NowLocal
                        THEN @Absent
                    WHEN attendance.CheckOutUtc IS NULL
                      AND CONVERT(time,(@AsOfUtc AT TIME ZONE 'UTC') AT TIME ZONE @TimeZoneId)>
                          CONVERT(time,DATEADD(minute,@Grace,effective.ShiftStart)) THEN @Late
                    WHEN attendance.CheckOutUtc IS NULL THEN @Present
                    WHEN DATEDIFF(minute,attendance.CheckInUtc,attendance.CheckOutUtc)-attendance.TotalBreakMinutes
                      < (CASE WHEN DATEDIFF(minute,effective.ShiftStart,effective.ShiftEnd)>0
                              THEN DATEDIFF(minute,effective.ShiftStart,effective.ShiftEnd)
                              ELSE DATEDIFF(minute,effective.ShiftStart,effective.ShiftEnd)+1440 END)-@Tolerance
                        THEN @EarlyDeparture
                    WHEN CONVERT(time,(attendance.CheckInUtc AT TIME ZONE 'UTC') AT TIME ZONE @TimeZoneId)>
                          CONVERT(time,DATEADD(minute,@Grace,effective.ShiftStart)) THEN @CompletedLate
                    ELSE @Present END,
                    ModifiedDate=@AsOfUtc
                FROM dbo.AttendanceRecords attendance
                JOIN dbo.Persons person ON person.PersonId=attendance.PersonId
                LEFT JOIN dbo.EmployeeTimingSchedules timing
                  ON timing.PersonId=attendance.PersonId
                 AND timing.ScheduleDate=attendance.AttendanceDate
                 AND timing.TenantId=@TenantId
                CROSS APPLY(SELECT
                    COALESCE(timing.IsOn,CASE WHEN DATENAME(weekday,attendance.AttendanceDate) IN(N'Saturday',N'Sunday') THEN 0 ELSE 1 END) IsOn,
                    COALESCE(TRY_CONVERT(time(0),timing.TimeFrom),TRY_CONVERT(time(0),person.ShiftStartTime)) ShiftStart,
                    COALESCE(TRY_CONVERT(time(0),timing.TimeTo),TRY_CONVERT(time(0),person.ShiftEndTime)) ShiftEnd
                ) effective
                WHERE attendance.TenantId=@TenantId
                  AND attendance.AttendanceDate BETWEEN @DateFrom AND @DateTo;
            END
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_Attendance_DailyReport
                @TenantId int, @DateFrom date, @DateTo date, @VisiblePersonIds nvarchar(max)
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @ProcessId int,@DayOff int,@Holiday int;
                SELECT @ProcessId=Id FROM dbo.Processes WHERE ProcessName=N'Attendance';
                SELECT TOP(1) @DayOff=Id FROM dbo.ProcessStatusStyles
                    WHERE ProcessId=@ProcessId AND Code=N'DO' AND IsActive=1 AND (TenantId=@TenantId OR TenantId IS NULL)
                    ORDER BY CASE WHEN TenantId=@TenantId THEN 0 ELSE 1 END;
                SELECT TOP(1) @Holiday=Id FROM dbo.ProcessStatusStyles
                    WHERE ProcessId=@ProcessId AND Code=N'H' AND IsActive=1 AND (TenantId=@TenantId OR TenantId IS NULL)
                    ORDER BY CASE WHEN TenantId=@TenantId THEN 0 ELSE 1 END;

                ;WITH Dates AS(
                    SELECT @DateFrom AttendanceDate UNION ALL
                    SELECT DATEADD(day,1,AttendanceDate) FROM Dates WHERE AttendanceDate<@DateTo
                ), VisiblePeople AS(
                    SELECT TRY_CONVERT(uniqueidentifier,[value]) PersonId FROM OPENJSON(@VisiblePersonIds)
                    WHERE TRY_CONVERT(uniqueidentifier,[value]) IS NOT NULL
                ), ReportRows AS(
                    SELECT attendance.Id,person.PersonId,
                        COALESCE(staff.LoginId,vacancy.VacancyCode) EmployeeNumber,
                        person.FullName EmployeeName,
                        COALESCE(vacancy.Department,organization.Name) Department,
                        COALESCE(jobTitle.TitleName,vacancy.JobTitle,N'') Designation,
                        dates.AttendanceDate,
                        COALESCE(attendance.AttendanceStatusId,
                            CASE WHEN effective.IsOn=0 THEN
                                CASE WHEN timing.HolidayType IN(N'PUBLIC_HOLIDAY',N'COMPANY_HOLIDAY')
                                     THEN COALESCE(@Holiday,@DayOff)
                                     ELSE COALESCE(@DayOff,@Holiday) END END) AttendanceStatusId,
                        attendance.AttendanceEntryTypeId,
                        attendance.AttendanceWorkModeId,
                        attendance.CheckInUtc,attendance.CheckOutUtc,attendance.TotalBreakMinutes,
                        CONVERT(char(5),effective.ShiftStart,108) ShiftStartTime,
                        CONVERT(char(5),effective.ShiftEnd,108) ShiftEndTime,
                        person.TimeZoneId,person.ReportsToPersonId
                    FROM VisiblePeople visible
                    JOIN dbo.Persons person
                      ON person.PersonId=visible.PersonId AND person.IsActive=1 AND person.TenantId=@TenantId
                    JOIN dbo.StaffVacancy staff ON staff.PersonId=person.PersonId
                    JOIN dbo.Vacancies vacancy ON vacancy.VacancyId=staff.VacancyId
                    LEFT JOIN dbo.JobTitles jobTitle ON jobTitle.Id=vacancy.JobTitleId
                    LEFT JOIN dbo.OrganizationTree organization ON organization.Id=vacancy.OrganizationId
                    CROSS JOIN Dates dates
                    LEFT JOIN dbo.EmployeeTimingSchedules timing
                      ON timing.PersonId=person.PersonId
                     AND timing.ScheduleDate=dates.AttendanceDate
                     AND timing.TenantId=@TenantId
                    LEFT JOIN dbo.AttendanceRecords attendance
                      ON attendance.PersonId=person.PersonId
                     AND attendance.AttendanceDate=dates.AttendanceDate
                     AND attendance.TenantId=@TenantId
                    CROSS APPLY(SELECT
                        COALESCE(timing.IsOn,CASE WHEN DATENAME(weekday,dates.AttendanceDate) IN(N'Saturday',N'Sunday') THEN 0 ELSE 1 END) IsOn,
                        COALESCE(TRY_CONVERT(time(0),timing.TimeFrom),TRY_CONVERT(time(0),person.ShiftStartTime)) ShiftStart,
                        COALESCE(TRY_CONVERT(time(0),timing.TimeTo),TRY_CONVERT(time(0),person.ShiftEndTime)) ShiftEnd
                    ) effective
                )
                SELECT rowData.Id,rowData.PersonId,rowData.EmployeeNumber,rowData.EmployeeName,
                    rowData.Department,rowData.Designation,rowData.AttendanceDate,
                    rowData.AttendanceStatusId,statusDefinition.StatusName,statusStyle.Code StatusCode,
                    color.ColorCode StatusColorCode,color.FontColor StatusFontColor,color.FontSize StatusFontSize,
                    rowData.AttendanceEntryTypeId,
                    COALESCE(entryType.Name,CASE WHEN rowData.Id IS NULL THEN noEntry.Name END) AttendanceEntryType,
                    rowData.AttendanceWorkModeId,workMode.Name AttendanceWorkMode,
                    rowData.CheckInUtc,rowData.CheckOutUtc,rowData.TotalBreakMinutes,
                    rowData.ShiftStartTime,rowData.ShiftEndTime,rowData.TimeZoneId,rowData.ReportsToPersonId
                FROM ReportRows rowData
                LEFT JOIN dbo.ProcessStatusStyles statusStyle ON statusStyle.Id=rowData.AttendanceStatusId
                LEFT JOIN dbo.Statuses statusDefinition ON statusDefinition.Id=statusStyle.StatusId
                LEFT JOIN dbo.ColorStyles color ON color.Id=statusStyle.ColorStyleId
                LEFT JOIN dbo.AttendanceEntryTypes entryType ON entryType.Id=rowData.AttendanceEntryTypeId
                LEFT JOIN dbo.AttendanceEntryTypes noEntry ON noEntry.Code=N'NONE' AND noEntry.IsActive=1
                LEFT JOIN dbo.AttendanceWorkModes workMode ON workMode.Id=rowData.AttendanceWorkModeId
                ORDER BY rowData.AttendanceDate DESC,rowData.EmployeeName OPTION(MAXRECURSION 367);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Timing Chart schedules affect attendance history and are intentionally preserved.");
}
