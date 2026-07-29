using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260728103000_StoreBusinessTimesAsPakistanLocal")]
public sealed class StoreBusinessTimesAsPakistanLocal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.AccountsDataFixMarkers', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AccountsDataFixMarkers
                (
                    MarkerKey nvarchar(150) NOT NULL CONSTRAINT PK_AccountsDataFixMarkers PRIMARY KEY,
                    AppliedOn datetime2 NOT NULL CONSTRAINT DF_AccountsDataFixMarkers_AppliedOn DEFAULT(SYSDATETIME())
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM dbo.AccountsDataFixMarkers WHERE MarkerKey = N'20260728103000_StoreBusinessTimesAsPakistanLocal')
            BEGIN
            IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL
            BEGIN
                UPDATE dbo.AttendanceRecords
                   SET CheckInUtc = CASE WHEN CheckInUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (CheckInUtc AT TIME ZONE 'UTC') AT TIME ZONE 'Pakistan Standard Time') END,
                       CheckOutUtc = CASE WHEN CheckOutUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (CheckOutUtc AT TIME ZONE 'UTC') AT TIME ZONE 'Pakistan Standard Time') END,
                       BreakStartedUtc = CASE WHEN BreakStartedUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (BreakStartedUtc AT TIME ZONE 'UTC') AT TIME ZONE 'Pakistan Standard Time') END,
                       CreatedDate = CASE WHEN CreatedDate IS NULL THEN CreatedDate ELSE CONVERT(datetime2, (CreatedDate AT TIME ZONE 'UTC') AT TIME ZONE 'Pakistan Standard Time') END,
                       ModifiedDate = CASE WHEN ModifiedDate IS NULL THEN ModifiedDate ELSE CONVERT(datetime2, (ModifiedDate AT TIME ZONE 'UTC') AT TIME ZONE 'Pakistan Standard Time') END;

                IF COL_LENGTH(N'dbo.AttendanceRecords', N'CameraCheckInUtc') IS NOT NULL
                    EXEC(N'UPDATE dbo.AttendanceRecords SET CameraCheckInUtc = CASE WHEN CameraCheckInUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (CameraCheckInUtc AT TIME ZONE ''UTC'') AT TIME ZONE ''Pakistan Standard Time'') END');

                IF COL_LENGTH(N'dbo.AttendanceRecords', N'CameraCheckOutUtc') IS NOT NULL
                    EXEC(N'UPDATE dbo.AttendanceRecords SET CameraCheckOutUtc = CASE WHEN CameraCheckOutUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (CameraCheckOutUtc AT TIME ZONE ''UTC'') AT TIME ZONE ''Pakistan Standard Time'') END');
            END;

            IF OBJECT_ID(N'dbo.ApplicationLoginSessions', N'U') IS NOT NULL
            BEGIN
                UPDATE dbo.ApplicationLoginSessions
                   SET LoginUtc = CONVERT(datetime2, (LoginUtc AT TIME ZONE 'UTC') AT TIME ZONE 'Pakistan Standard Time'),
                       LogoutUtc = CASE WHEN LogoutUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (LogoutUtc AT TIME ZONE 'UTC') AT TIME ZONE 'Pakistan Standard Time') END,
                       CreatedDate = CASE WHEN CreatedDate IS NULL THEN CreatedDate ELSE CONVERT(datetime2, (CreatedDate AT TIME ZONE 'UTC') AT TIME ZONE 'Pakistan Standard Time') END,
                       ModifiedDate = CASE WHEN ModifiedDate IS NULL THEN ModifiedDate ELSE CONVERT(datetime2, (ModifiedDate AT TIME ZONE 'UTC') AT TIME ZONE 'Pakistan Standard Time') END;
            END;

            IF OBJECT_ID(N'dbo.AppNotes', N'U') IS NOT NULL
            BEGIN
                UPDATE dbo.AppNotes
                   SET CreatedOnUtc = CONVERT(datetime2, (CreatedOnUtc AT TIME ZONE 'UTC') AT TIME ZONE 'Pakistan Standard Time'),
                       UpdatedOnUtc = CASE WHEN UpdatedOnUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (UpdatedOnUtc AT TIME ZONE 'UTC') AT TIME ZONE 'Pakistan Standard Time') END,
                       DeletedOnUtc = CASE WHEN DeletedOnUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (DeletedOnUtc AT TIME ZONE 'UTC') AT TIME ZONE 'Pakistan Standard Time') END,
                       StartDateUtc = CASE WHEN StartDateUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (StartDateUtc AT TIME ZONE 'UTC') AT TIME ZONE 'Pakistan Standard Time') END,
                       EndDateUtc = CASE WHEN EndDateUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (EndDateUtc AT TIME ZONE 'UTC') AT TIME ZONE 'Pakistan Standard Time') END;
            END;

            IF OBJECT_ID(N'dbo.AppNoteUserStates', N'U') IS NOT NULL
            BEGIN
                UPDATE dbo.AppNoteUserStates
                   SET ReadOnUtc = CASE WHEN ReadOnUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (ReadOnUtc AT TIME ZONE 'UTC') AT TIME ZONE 'Pakistan Standard Time') END,
                       AcknowledgedOnUtc = CASE WHEN AcknowledgedOnUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (AcknowledgedOnUtc AT TIME ZONE 'UTC') AT TIME ZONE 'Pakistan Standard Time') END,
                       DismissedOnUtc = CASE WHEN DismissedOnUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (DismissedOnUtc AT TIME ZONE 'UTC') AT TIME ZONE 'Pakistan Standard Time') END;
            END;

                INSERT dbo.AccountsDataFixMarkers(MarkerKey)
                VALUES (N'20260728103000_StoreBusinessTimesAsPakistanLocal');
            END;
            """);

        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.usp_Attendance_EvaluateStatuses', N'P') IS NOT NULL
            BEGIN
                DECLARE @Definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.usp_Attendance_EvaluateStatuses'));
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
                SET @Definition = REPLACE(@Definition,
                    N'SET @NowLocal=CONVERT(datetime2,(@AsOfUtc AT TIME ZONE ''UTC'') AT TIME ZONE @TimeZoneId);',
                    N'SET @NowLocal=@AsOfUtc;');
                SET @Definition = REPLACE(@Definition,
                    N'CONVERT(time,(@AsOfUtc AT TIME ZONE ''UTC'') AT TIME ZONE @TimeZoneId)',
                    N'CONVERT(time,@AsOfUtc)');
                SET @Definition = REPLACE(@Definition,
                    N'CONVERT(time,(attendance.CheckInUtc AT TIME ZONE ''UTC'') AT TIME ZONE @TimeZoneId)',
                    N'CONVERT(time,attendance.CheckInUtc)');
                SET @Definition = REPLACE(@Definition,
                    N'CONVERT(datetime2,(attendance.CheckOutUtc AT TIME ZONE ''UTC'') AT TIME ZONE @TimeZoneId) CheckOutLocal',
                    N'attendance.CheckOutUtc CheckOutLocal');
                EXEC(@Definition);
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL
            BEGIN
                UPDATE dbo.AttendanceRecords
                   SET CheckInUtc = CASE WHEN CheckInUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (CheckInUtc AT TIME ZONE 'Pakistan Standard Time') AT TIME ZONE 'UTC') END,
                       CheckOutUtc = CASE WHEN CheckOutUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (CheckOutUtc AT TIME ZONE 'Pakistan Standard Time') AT TIME ZONE 'UTC') END,
                       BreakStartedUtc = CASE WHEN BreakStartedUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (BreakStartedUtc AT TIME ZONE 'Pakistan Standard Time') AT TIME ZONE 'UTC') END;

                IF COL_LENGTH(N'dbo.AttendanceRecords', N'CameraCheckInUtc') IS NOT NULL
                    EXEC(N'UPDATE dbo.AttendanceRecords SET CameraCheckInUtc = CASE WHEN CameraCheckInUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (CameraCheckInUtc AT TIME ZONE ''Pakistan Standard Time'') AT TIME ZONE ''UTC'') END');

                IF COL_LENGTH(N'dbo.AttendanceRecords', N'CameraCheckOutUtc') IS NOT NULL
                    EXEC(N'UPDATE dbo.AttendanceRecords SET CameraCheckOutUtc = CASE WHEN CameraCheckOutUtc IS NULL THEN NULL ELSE CONVERT(datetime2, (CameraCheckOutUtc AT TIME ZONE ''Pakistan Standard Time'') AT TIME ZONE ''UTC'') END');
            END;
            """);
    }
}
