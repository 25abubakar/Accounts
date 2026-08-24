using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Restores policy values removed from some deployed evaluator definitions and
/// resolves the tenant-owned Action Status ids used by attendance reports.
/// Legacy policy codes LT/EL map to the platform's 1-L/1-E defaults.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260824090000_RepairAttendanceStatusEvaluation")]
public sealed class RepairAttendanceStatusEvaluation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.usp_Attendance_EvaluateStatuses', N'P') IS NULL
                THROW 51000, 'Attendance status evaluator procedure was not found.', 1;

            DECLARE @Definition nvarchar(max) =
                OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_Attendance_EvaluateStatuses'));

            SET @Definition = REPLACE(@Definition,
                N'CREATE PROCEDURE dbo.usp_Attendance_EvaluateStatuses',
                N'CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EvaluateStatuses');
            SET @Definition = REPLACE(@Definition,
                N'CREATE   PROCEDURE dbo.usp_Attendance_EvaluateStatuses',
                N'CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EvaluateStatuses');
            SET @Definition = REPLACE(@Definition,
                N'CREATE PROC dbo.usp_Attendance_EvaluateStatuses',
                N'CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EvaluateStatuses');
            SET @Definition = REPLACE(@Definition,
                N'CREATE   PROC dbo.usp_Attendance_EvaluateStatuses',
                N'CREATE OR ALTER PROCEDURE dbo.usp_Attendance_EvaluateStatuses');

            IF @Definition NOT LIKE N'%Attendance evaluator policy/status repair v1%'
            BEGIN
                DECLARE @ProcessAnchor nvarchar(max) =
                    N'SELECT @ProcessId = Id FROM dbo.Processes WHERE ProcessName = N''Attendance'';';
                DECLARE @AnchorPosition int = CHARINDEX(@ProcessAnchor, @Definition);

                IF @AnchorPosition = 0
                BEGIN
                    SET @ProcessAnchor =
                        N'SELECT @ProcessId = Id
                FROM dbo.Processes
                WHERE ProcessName = N''Attendance'';';
                    SET @AnchorPosition = CHARINDEX(@ProcessAnchor, @Definition);
                END;

                IF @AnchorPosition = 0
                    THROW 51000, 'Attendance evaluator process-status anchor was not found.', 1;

                DECLARE @RepairBlock nvarchar(max) = N'

                -- Attendance evaluator policy/status repair v1
                SELECT TOP (1)
                    @PolicyId = Id,
                    @Grace = OnTimeGraceMinutesAfter,
                    @AbsentAfter = AbsentAfterShiftStartMinutes,
                    @MissingOutAfter = MissingCheckoutAfterShiftEndMinutes,
                    @Tolerance = FullDayToleranceMinutes,
                    @Present = PresentStatusId,
                    @Late = LateStatusId,
                    @CompletedLate = CompletedLateStatusId,
                    @ShortLeave = ShortLeaveStatusId,
                    @EarlyDeparture = EarlyDepartureStatusId,
                    @Absent = AbsentStatusId
                FROM dbo.AttendancePolicies
                WHERE IsActive = 1
                  AND (TenantId = @TenantId OR TenantId IS NULL)
                ORDER BY CASE WHEN TenantId = @TenantId THEN 0 ELSE 1 END;

                IF @PolicyId IS NULL
                    THROW 51000, ''No active attendance policy is configured.'', 1;

                DECLARE @ResolvedAttendanceActionId int;
                SELECT TOP (1) @ResolvedAttendanceActionId = Id
                FROM PlatformSettings.Actions
                WHERE Name = N''Attendance''
                  AND TenantId = @TenantId
                  AND IsActive = 1;

                SELECT TOP (1) @PlatformPresent = actionStatus.Id
                FROM PlatformSettings.ActionStatuses actionStatus
                INNER JOIN PlatformSettings.StatusCrDbValues crDb
                    ON crDb.StatusId = actionStatus.StatusId
                   AND (crDb.TenantId = @TenantId OR crDb.TenantId IS NULL)
                WHERE actionStatus.ActionId = @ResolvedAttendanceActionId
                  AND actionStatus.TenantId = @TenantId
                  AND crDb.DbValue = N''P'';

                SELECT TOP (1) @PlatformAbsent = actionStatus.Id
                FROM PlatformSettings.ActionStatuses actionStatus
                INNER JOIN PlatformSettings.StatusCrDbValues crDb
                    ON crDb.StatusId = actionStatus.StatusId
                   AND (crDb.TenantId = @TenantId OR crDb.TenantId IS NULL)
                WHERE actionStatus.ActionId = @ResolvedAttendanceActionId
                  AND actionStatus.TenantId = @TenantId
                  AND crDb.DbValue = N''A'';

                SELECT TOP (1) @PlatformLate = actionStatus.Id
                FROM PlatformSettings.ActionStatuses actionStatus
                INNER JOIN PlatformSettings.StatusCrDbValues crDb
                    ON crDb.StatusId = actionStatus.StatusId
                   AND (crDb.TenantId = @TenantId OR crDb.TenantId IS NULL)
                WHERE actionStatus.ActionId = @ResolvedAttendanceActionId
                  AND actionStatus.TenantId = @TenantId
                  AND crDb.DbValue IN (N''LT'', N''1-L'')
                ORDER BY CASE crDb.DbValue WHEN N''LT'' THEN 0 ELSE 1 END;

                SELECT TOP (1) @PlatformCompletedLate = actionStatus.Id
                FROM PlatformSettings.ActionStatuses actionStatus
                INNER JOIN PlatformSettings.StatusCrDbValues crDb
                    ON crDb.StatusId = actionStatus.StatusId
                   AND (crDb.TenantId = @TenantId OR crDb.TenantId IS NULL)
                WHERE actionStatus.ActionId = @ResolvedAttendanceActionId
                  AND actionStatus.TenantId = @TenantId
                  AND crDb.DbValue = N''TP'';

                SELECT TOP (1) @PlatformShortLeave = actionStatus.Id
                FROM PlatformSettings.ActionStatuses actionStatus
                INNER JOIN PlatformSettings.StatusCrDbValues crDb
                    ON crDb.StatusId = actionStatus.StatusId
                   AND (crDb.TenantId = @TenantId OR crDb.TenantId IS NULL)
                WHERE actionStatus.ActionId = @ResolvedAttendanceActionId
                  AND actionStatus.TenantId = @TenantId
                  AND crDb.DbValue = N''SL'';

                SELECT TOP (1) @PlatformEarlyDeparture = actionStatus.Id
                FROM PlatformSettings.ActionStatuses actionStatus
                INNER JOIN PlatformSettings.StatusCrDbValues crDb
                    ON crDb.StatusId = actionStatus.StatusId
                   AND (crDb.TenantId = @TenantId OR crDb.TenantId IS NULL)
                WHERE actionStatus.ActionId = @ResolvedAttendanceActionId
                  AND actionStatus.TenantId = @TenantId
                  AND crDb.DbValue IN (N''EL'', N''1-E'')
                ORDER BY CASE crDb.DbValue WHEN N''EL'' THEN 0 ELSE 1 END;

                SELECT TOP (1) @PlatformDayOff = actionStatus.Id
                FROM PlatformSettings.ActionStatuses actionStatus
                INNER JOIN PlatformSettings.StatusCrDbValues crDb
                    ON crDb.StatusId = actionStatus.StatusId
                   AND (crDb.TenantId = @TenantId OR crDb.TenantId IS NULL)
                WHERE actionStatus.ActionId = @ResolvedAttendanceActionId
                  AND actionStatus.TenantId = @TenantId
                  AND crDb.DbValue = N''DO'';

                SELECT TOP (1) @PlatformHoliday = actionStatus.Id
                FROM PlatformSettings.ActionStatuses actionStatus
                INNER JOIN PlatformSettings.StatusCrDbValues crDb
                    ON crDb.StatusId = actionStatus.StatusId
                   AND (crDb.TenantId = @TenantId OR crDb.TenantId IS NULL)
                WHERE actionStatus.ActionId = @ResolvedAttendanceActionId
                  AND actionStatus.TenantId = @TenantId
                  AND crDb.DbValue IN (N''H'', N''HO'')
                ORDER BY CASE crDb.DbValue WHEN N''HO'' THEN 0 ELSE 1 END;
                ';

                SET @Definition = STUFF(
                    @Definition,
                    @AnchorPosition + LEN(@ProcessAnchor),
                    0,
                    @RepairBlock);
            END;

            EXEC sys.sp_executesql @Definition;

            ;WITH StatusMappings AS
            (
                SELECT
                    actionStatus.TenantId,
                    MAX(CASE WHEN crDb.DbValue IN (N'LT', N'1-L') THEN actionStatus.Id END) AS LateStatusId,
                    MAX(CASE WHEN crDb.DbValue = N'2-L' THEN actionStatus.Id END) AS ExtremeLateStatusId,
                    MAX(CASE WHEN crDb.DbValue IN (N'EL', N'1-E') THEN actionStatus.Id END) AS EarlyStatusId,
                    MAX(CASE WHEN crDb.DbValue = N'2-E' THEN actionStatus.Id END) AS ExtremeEarlyStatusId
                FROM PlatformSettings.ActionStatuses actionStatus
                INNER JOIN PlatformSettings.Actions action
                    ON action.Id = actionStatus.ActionId
                   AND action.TenantId = actionStatus.TenantId
                INNER JOIN PlatformSettings.StatusCrDbValues crDb
                    ON crDb.StatusId = actionStatus.StatusId
                   AND (crDb.TenantId = actionStatus.TenantId OR crDb.TenantId IS NULL)
                WHERE action.Name = N'Attendance'
                GROUP BY actionStatus.TenantId
            )
            UPDATE rule
               SET PlatformLateStatusId = COALESCE(rule.PlatformLateStatusId, mapping.LateStatusId),
                   PlatformExtremeLateStatusId = COALESCE(rule.PlatformExtremeLateStatusId, mapping.ExtremeLateStatusId),
                   PlatformEarlyDepartureStatusId = COALESCE(rule.PlatformEarlyDepartureStatusId, mapping.EarlyStatusId),
                   PlatformExtremeEarlyDepartureStatusId = COALESCE(rule.PlatformExtremeEarlyDepartureStatusId, mapping.ExtremeEarlyStatusId)
            FROM dbo.AttendanceRuleSettings rule
            INNER JOIN StatusMappings mapping ON mapping.TenantId = rule.TenantId
            WHERE rule.PlatformLateStatusId IS NULL
               OR rule.PlatformExtremeLateStatusId IS NULL
               OR rule.PlatformEarlyDepartureStatusId IS NULL
               OR rule.PlatformExtremeEarlyDepartureStatusId IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The restored evaluator policy and tenant status mapping are required
        // for correct attendance calculation and must not be removed.
    }
}
