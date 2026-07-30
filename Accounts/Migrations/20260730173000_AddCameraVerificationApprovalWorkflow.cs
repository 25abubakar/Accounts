using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260730173000_AddCameraVerificationApprovalWorkflow")]
public sealed class AddCameraVerificationApprovalWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF COL_LENGTH('dbo.AttendanceRuleSettings', 'CameraVerificationToleranceMinutes') IS NULL
                ALTER TABLE dbo.AttendanceRuleSettings
                ADD CameraVerificationToleranceMinutes int NOT NULL
                    CONSTRAINT DF_AttendanceRuleSettings_CameraTolerance DEFAULT (10);

            IF COL_LENGTH('dbo.AttendanceRecords', 'EffectiveCheckInUtc') IS NULL
                ALTER TABLE dbo.AttendanceRecords ADD EffectiveCheckInUtc datetime2 NULL;
            IF COL_LENGTH('dbo.AttendanceRecords', 'EffectiveCheckOutUtc') IS NULL
                ALTER TABLE dbo.AttendanceRecords ADD EffectiveCheckOutUtc datetime2 NULL;
            IF COL_LENGTH('dbo.AttendanceRecords', 'VerificationStatusId') IS NULL
                ALTER TABLE dbo.AttendanceRecords ADD VerificationStatusId int NULL;
            IF COL_LENGTH('dbo.AttendanceRecords', 'HasVerificationAnomaly') IS NULL
                ALTER TABLE dbo.AttendanceRecords ADD HasVerificationAnomaly bit NOT NULL
                    CONSTRAINT DF_AttendanceRecords_HasVerificationAnomaly DEFAULT (0);
            IF COL_LENGTH('dbo.AttendanceRecords', 'VerificationDifferenceMinutes') IS NULL
                ALTER TABLE dbo.AttendanceRecords ADD VerificationDifferenceMinutes int NULL;
            IF COL_LENGTH('dbo.AttendanceRecords', 'ApprovalRequestId') IS NULL
                ALTER TABLE dbo.AttendanceRecords ADD ApprovalRequestId bigint NULL;
            IF COL_LENGTH('dbo.AttendanceRecords', 'CameraRemarks') IS NULL
                ALTER TABLE dbo.AttendanceRecords ADD CameraRemarks nvarchar(1000) NULL;

            IF OBJECT_ID('dbo.WorkflowApprovalRequests', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.WorkflowApprovalRequests
                (
                    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkflowApprovalRequests PRIMARY KEY,
                    TenantId int NOT NULL,
                    ProcessCode nvarchar(80) NOT NULL,
                    EntityType nvarchar(80) NOT NULL,
                    EntityId nvarchar(100) NOT NULL,
                    SubjectPersonId uniqueidentifier NULL,
                    RequestedByUserId nvarchar(450) NOT NULL,
                    StatusCode nvarchar(20) NOT NULL,
                    DecisionCode nvarchar(40) NULL,
                    DecisionByUserId nvarchar(450) NULL,
                    DecisionDate datetime2 NULL,
                    Comments nvarchar(1000) NULL,
                    CreatedDate datetime2 NOT NULL CONSTRAINT DF_WorkflowApprovalRequests_CreatedDate DEFAULT SYSUTCDATETIME(),
                    ModifiedDate datetime2 NULL
                );
                CREATE INDEX IX_WorkflowApprovalRequests_Entity
                    ON dbo.WorkflowApprovalRequests(TenantId, ProcessCode, EntityType, EntityId, StatusCode);
                CREATE INDEX IX_WorkflowApprovalRequests_Status
                    ON dbo.WorkflowApprovalRequests(TenantId, StatusCode, CreatedDate);
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_AttendanceRecords_VerificationStatus')
                ALTER TABLE dbo.AttendanceRecords WITH CHECK
                ADD CONSTRAINT FK_AttendanceRecords_VerificationStatus
                    FOREIGN KEY (VerificationStatusId) REFERENCES dbo.ProcessStatusStyles(Id);
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_AttendanceRecords_ApprovalRequest')
                ALTER TABLE dbo.AttendanceRecords WITH CHECK
                ADD CONSTRAINT FK_AttendanceRecords_ApprovalRequest
                    FOREIGN KEY (ApprovalRequestId) REFERENCES dbo.WorkflowApprovalRequests(Id);

            DECLARE @ProcessId int, @StatusId int, @ColorId int;
            IF NOT EXISTS (SELECT 1 FROM dbo.Processes WHERE ProcessName = N'Attendance Verification')
                INSERT dbo.Processes(ProcessName) VALUES (N'Attendance Verification');
            SELECT @ProcessId = Id FROM dbo.Processes WHERE ProcessName = N'Attendance Verification';

            DECLARE @Definitions TABLE
            (
                Code nvarchar(10), StatusName nvarchar(100), ColorName nvarchar(100),
                ColorCode nvarchar(20), FontColor nvarchar(20), DisplayOrder int
            );
            INSERT @Definitions VALUES
                (N'VERIFIED', N'Camera Verified', N'Verification Success', N'#10B981', N'#FFFFFF', 10),
                (N'PENDING',  N'Pending Review', N'Verification Pending', N'#F59E0B', N'#111827', 20),
                (N'IN-MATCH', N'Check-In Mismatch', N'Verification Danger', N'#EF4444', N'#FFFFFF', 30),
                (N'OUT-MATCH',N'Check-Out Mismatch', N'Verification Danger', N'#EF4444', N'#FFFFFF', 40),
                (N'APPROVED', N'Approved Correction', N'Verification Approved', N'#2563EB', N'#FFFFFF', 50),
                (N'REJECTED', N'System Punch Rejected', N'Verification Rejected', N'#7C3AED', N'#FFFFFF', 60);

            DECLARE definition_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT Code, StatusName, ColorName, ColorCode, FontColor, DisplayOrder FROM @Definitions;
            DECLARE @Code nvarchar(10), @StatusName nvarchar(100), @ColorName nvarchar(100),
                    @ColorCode nvarchar(20), @FontColor nvarchar(20), @DisplayOrder int;
            OPEN definition_cursor;
            FETCH NEXT FROM definition_cursor INTO @Code,@StatusName,@ColorName,@ColorCode,@FontColor,@DisplayOrder;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM dbo.Statuses WHERE StatusName = @StatusName)
                    INSERT dbo.Statuses(StatusName) VALUES (@StatusName);
                SELECT @StatusId = Id FROM dbo.Statuses WHERE StatusName = @StatusName;

                IF NOT EXISTS
                (
                    SELECT 1 FROM dbo.ColorStyles
                    WHERE ColorName = @ColorName AND ColorCode = @ColorCode
                      AND FontColor = @FontColor AND FontSize = N'11px'
                )
                    INSERT dbo.ColorStyles(ColorName,ColorCode,FontColor,FontSize)
                    VALUES (@ColorName,@ColorCode,@FontColor,N'11px');
                SELECT @ColorId = Id FROM dbo.ColorStyles
                WHERE ColorName = @ColorName AND ColorCode = @ColorCode
                  AND FontColor = @FontColor AND FontSize = N'11px';

                IF NOT EXISTS
                (
                    SELECT 1 FROM dbo.ProcessStatusStyles
                    WHERE ProcessId = @ProcessId AND Code = @Code AND TenantId IS NULL
                )
                    INSERT dbo.ProcessStatusStyles
                        (ProcessId,StatusId,ColorStyleId,TenantId,IsSystem,Code,Description,DisplayOrder,IsPaid,IsActive,CreatedDate)
                    VALUES
                        (@ProcessId,@StatusId,@ColorId,NULL,1,@Code,N'Camera versus system attendance verification',@DisplayOrder,0,1,SYSUTCDATETIME());

                FETCH NEXT FROM definition_cursor INTO @Code,@StatusName,@ColorName,@ColorCode,@FontColor,@DisplayOrder;
            END
            CLOSE definition_cursor;
            DEALLOCATE definition_cursor;
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_Attendance_ApplyCameraVerification
                @TenantId int,
                @AttendanceRecordId bigint,
                @ActorUserId nvarchar(450)
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                DECLARE @Tolerance int = 10, @Difference int = 0, @Anomaly bit = 0,
                        @VerificationCode nvarchar(10) = N'VERIFIED',
                        @VerificationStatusId int, @RequestId bigint,
                        @PersonId uniqueidentifier, @SystemIn datetime2, @SystemOut datetime2,
                        @CameraIn datetime2, @CameraOut datetime2, @EntryTypeId int;

                SELECT @PersonId=PersonId,@SystemIn=CheckInUtc,@SystemOut=CheckOutUtc,
                       @CameraIn=CameraCheckInUtc,@CameraOut=CameraCheckOutUtc,
                       @EntryTypeId=AttendanceEntryTypeId
                FROM dbo.AttendanceRecords WITH (UPDLOCK, ROWLOCK)
                WHERE Id=@AttendanceRecordId AND TenantId=@TenantId;
                IF @PersonId IS NULL THROW 51020, 'Attendance record was not found.', 1;

                SELECT TOP (1) @Tolerance=CameraVerificationToleranceMinutes
                FROM dbo.AttendanceRuleSettings
                WHERE TenantId=@TenantId AND AttendanceEntryTypeId=@EntryTypeId
                  AND IsActive=1 AND IsApproved=1
                ORDER BY Id DESC;

                DECLARE @InDifference int =
                    CASE WHEN @SystemIn IS NOT NULL AND @CameraIn IS NOT NULL
                         THEN ABS(DATEDIFF(minute,@SystemIn,@CameraIn)) ELSE 0 END;
                DECLARE @OutDifference int =
                    CASE WHEN @SystemOut IS NOT NULL AND @CameraOut IS NOT NULL
                         THEN ABS(DATEDIFF(minute,@SystemOut,@CameraOut)) ELSE 0 END;
                SET @Difference = CASE WHEN @InDifference >= @OutDifference THEN @InDifference ELSE @OutDifference END;
                SET @Anomaly = CASE WHEN @Difference > ISNULL(@Tolerance,10) THEN 1 ELSE 0 END;
                SET @VerificationCode =
                    CASE WHEN @InDifference > ISNULL(@Tolerance,10) THEN N'IN-MATCH'
                         WHEN @OutDifference > ISNULL(@Tolerance,10) THEN N'OUT-MATCH'
                         ELSE N'VERIFIED' END;

                SELECT TOP (1) @VerificationStatusId=style.Id
                FROM dbo.ProcessStatusStyles style
                JOIN dbo.Processes process ON process.Id=style.ProcessId
                WHERE process.ProcessName=N'Attendance Verification'
                  AND style.Code=@VerificationCode AND style.IsActive=1
                  AND (style.TenantId=@TenantId OR style.TenantId IS NULL)
                ORDER BY CASE WHEN style.TenantId=@TenantId THEN 0 ELSE 1 END;

                UPDATE dbo.AttendanceRecords
                   SET EffectiveCheckInUtc =
                           CASE WHEN @SystemIn IS NULL THEN @CameraIn
                                WHEN @CameraIn IS NULL THEN @SystemIn
                                WHEN @CameraIn>@SystemIn THEN @CameraIn ELSE @SystemIn END,
                       EffectiveCheckOutUtc =
                           CASE WHEN @SystemOut IS NULL THEN @CameraOut
                                WHEN @CameraOut IS NULL THEN @SystemOut
                                WHEN @CameraOut<@SystemOut THEN @CameraOut ELSE @SystemOut END,
                       VerificationStatusId=@VerificationStatusId,
                       HasVerificationAnomaly=@Anomaly,
                       VerificationDifferenceMinutes=@Difference,
                       ModifiedDate=SYSUTCDATETIME()
                 WHERE Id=@AttendanceRecordId AND TenantId=@TenantId;

                IF @Anomaly=1
                BEGIN
                    SELECT TOP (1) @RequestId=Id
                    FROM dbo.WorkflowApprovalRequests WITH (UPDLOCK, ROWLOCK)
                    WHERE TenantId=@TenantId
                      AND ProcessCode=N'CAMERA_ATTENDANCE_VERIFICATION'
                      AND EntityType=N'AttendanceRecord'
                      AND EntityId=CONVERT(nvarchar(100),@AttendanceRecordId)
                      AND StatusCode=N'PENDING'
                    ORDER BY Id DESC;

                    IF @RequestId IS NULL
                    BEGIN
                        INSERT dbo.WorkflowApprovalRequests
                            (TenantId,ProcessCode,EntityType,EntityId,SubjectPersonId,RequestedByUserId,StatusCode,CreatedDate)
                        VALUES
                            (@TenantId,N'CAMERA_ATTENDANCE_VERIFICATION',N'AttendanceRecord',
                             CONVERT(nvarchar(100),@AttendanceRecordId),@PersonId,@ActorUserId,N'PENDING',SYSUTCDATETIME());
                        SET @RequestId=SCOPE_IDENTITY();
                    END;
                    UPDATE dbo.AttendanceRecords SET ApprovalRequestId=@RequestId WHERE Id=@AttendanceRecordId;
                END
                ELSE
                    UPDATE dbo.AttendanceRecords SET ApprovalRequestId=NULL WHERE Id=@AttendanceRecordId;
            END
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE dbo.usp_WorkflowApproval_DecideCameraAttendance
                @TenantId int,
                @RequestId bigint,
                @ReviewerUserId nvarchar(450),
                @DecisionCode nvarchar(40),
                @ManualCheckIn datetime2 = NULL,
                @ManualCheckOut datetime2 = NULL,
                @Comments nvarchar(1000) = NULL
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                DECLARE @RecordId bigint, @SubjectPersonId uniqueidentifier,
                        @RequestedBy nvarchar(450), @SubjectUserId nvarchar(450),
                        @StatusId int, @StatusCode nvarchar(10);

                SELECT @RecordId=TRY_CONVERT(bigint,EntityId),@SubjectPersonId=SubjectPersonId,@RequestedBy=RequestedByUserId
                FROM dbo.WorkflowApprovalRequests WITH (UPDLOCK,ROWLOCK)
                WHERE Id=@RequestId AND TenantId=@TenantId
                  AND ProcessCode=N'CAMERA_ATTENDANCE_VERIFICATION'
                  AND StatusCode=N'PENDING';
                IF @RecordId IS NULL THROW 51021, 'This verification is no longer pending.', 1;

                SELECT @SubjectUserId=IdentityUserId FROM dbo.Persons WHERE PersonId=@SubjectPersonId;
                IF @ReviewerUserId=@RequestedBy OR @ReviewerUserId=@SubjectUserId
                    THROW 51022, 'Self-approval is not allowed. A different authorized approver must review this entry.', 1;

                IF @DecisionCode NOT IN (N'APPROVE_CAMERA',N'APPROVE_SYSTEM',N'MANUAL_CORRECTION',N'REJECT_SYSTEM_PUNCH')
                    THROW 51023, 'Unsupported approval decision.', 1;
                IF @DecisionCode=N'MANUAL_CORRECTION' AND @ManualCheckIn IS NULL AND @ManualCheckOut IS NULL
                    THROW 51024, 'Manual correction requires a check-in or check-out time.', 1;

                SET @StatusCode=CASE WHEN @DecisionCode=N'REJECT_SYSTEM_PUNCH' THEN N'REJECTED' ELSE N'APPROVED' END;
                SELECT TOP (1) @StatusId=style.Id
                FROM dbo.ProcessStatusStyles style
                JOIN dbo.Processes process ON process.Id=style.ProcessId
                WHERE process.ProcessName=N'Attendance Verification' AND style.Code=@StatusCode
                  AND style.IsActive=1 AND (style.TenantId=@TenantId OR style.TenantId IS NULL)
                ORDER BY CASE WHEN style.TenantId=@TenantId THEN 0 ELSE 1 END;

                UPDATE attendance
                   SET EffectiveCheckInUtc =
                       CASE @DecisionCode
                           WHEN N'APPROVE_SYSTEM' THEN attendance.CheckInUtc
                           WHEN N'APPROVE_CAMERA' THEN attendance.CameraCheckInUtc
                           WHEN N'REJECT_SYSTEM_PUNCH' THEN attendance.CameraCheckInUtc
                           WHEN N'MANUAL_CORRECTION' THEN COALESCE(@ManualCheckIn,attendance.EffectiveCheckInUtc) END,
                       EffectiveCheckOutUtc =
                       CASE @DecisionCode
                           WHEN N'APPROVE_SYSTEM' THEN attendance.CheckOutUtc
                           WHEN N'APPROVE_CAMERA' THEN attendance.CameraCheckOutUtc
                           WHEN N'REJECT_SYSTEM_PUNCH' THEN attendance.CameraCheckOutUtc
                           WHEN N'MANUAL_CORRECTION' THEN COALESCE(@ManualCheckOut,attendance.EffectiveCheckOutUtc) END,
                       VerificationStatusId=@StatusId,
                       HasVerificationAnomaly=0,
                       ModifiedDate=SYSUTCDATETIME()
                FROM dbo.AttendanceRecords attendance
                WHERE attendance.Id=@RecordId AND attendance.TenantId=@TenantId;

                UPDATE dbo.WorkflowApprovalRequests
                   SET StatusCode=CASE WHEN @DecisionCode=N'REJECT_SYSTEM_PUNCH' THEN N'REJECTED' ELSE N'APPROVED' END,
                       DecisionCode=@DecisionCode,DecisionByUserId=@ReviewerUserId,
                       DecisionDate=SYSUTCDATETIME(),Comments=@Comments,ModifiedDate=SYSUTCDATETIME()
                 WHERE Id=@RequestId;
                COMMIT TRANSACTION;
            END
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER VIEW dbo.vw_AttendanceRuleSettings
            AS
            SELECT
                attendanceRule.Id, attendanceRule.TenantId, attendanceRule.AttendanceEntryTypeId,
                entryType.Code AS AttendanceTypeCode, entryType.Name AS AttendanceTypeName,
                attendanceRule.Reference, attendanceRule.RuleName, attendanceRule.WorkingMinutes,
                attendanceRule.BeforeCheckInMinutes, attendanceRule.AfterCheckOutMinutes,
                attendanceRule.CheckInAdjustMinutes, attendanceRule.CheckOutAdjustMinutes,
                attendanceRule.AbsentAfterShiftStartMinutes, attendanceRule.EarlyCheckoutAbsentAfterMinutes,
                attendanceRule.MissingCheckoutAfterShiftEndMinutes, attendanceRule.CameraVerificationToleranceMinutes,
                attendanceRule.AccountLockAbsentDays, attendanceRule.WeekendChargeValue, attendanceRule.AdjustAbsentDays,
                attendanceRule.IsApproved, attendanceRule.IsActive, attendanceRule.Remarks
            FROM dbo.AttendanceRuleSettings AS attendanceRule
            JOIN dbo.AttendanceEntryTypes AS entryType
              ON entryType.Id=attendanceRule.AttendanceEntryTypeId;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_WorkflowApproval_DecideCameraAttendance;");
        migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.usp_Attendance_ApplyCameraVerification;");
    }
}
