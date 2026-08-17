using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Restores the camera-verification tolerance column to the attendance-rule
/// read view after the attendance-type source was moved to PlatformTypes.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260817132000_RepairAttendanceRuleCameraToleranceView")]
public sealed class RepairAttendanceRuleCameraToleranceView : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.AttendanceRuleSettings', N'U') IS NULL
                THROW 51000, 'AttendanceRuleSettings table was not found.', 1;

            IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'CameraVerificationToleranceMinutes') IS NULL
            BEGIN
                ALTER TABLE dbo.AttendanceRuleSettings
                ADD CameraVerificationToleranceMinutes int NOT NULL
                    CONSTRAINT DF_AttendanceRuleSettings_CameraTolerance_Repair DEFAULT (10);
            END;

            IF OBJECT_ID(N'PlatformTypes.AttendanceTypes', N'U') IS NULL
                THROW 51000, 'PlatformTypes.AttendanceTypes table was not found.', 1;

            EXEC(N'CREATE OR ALTER VIEW dbo.vw_AttendanceRuleSettings
            AS
            SELECT
                ruleSetting.Id,
                ruleSetting.TenantId,
                ruleSetting.AttendanceEntryTypeId,
                entryType.Code AS AttendanceTypeCode,
                entryType.Name AS AttendanceTypeName,
                ruleSetting.Reference,
                ruleSetting.RuleName,
                ruleSetting.WorkingMinutes,
                ruleSetting.BeforeCheckInMinutes,
                ruleSetting.AfterCheckOutMinutes,
                ruleSetting.CheckInAdjustMinutes,
                ruleSetting.CheckOutAdjustMinutes,
                ruleSetting.AbsentAfterShiftStartMinutes,
                ruleSetting.EarlyCheckoutAbsentAfterMinutes,
                ruleSetting.MissingCheckoutAfterShiftEndMinutes,
                ruleSetting.CameraVerificationToleranceMinutes,
                ruleSetting.AccountLockAbsentDays,
                ruleSetting.WeekendChargeValue,
                ruleSetting.AdjustAbsentDays,
                ruleSetting.IsApproved,
                ruleSetting.IsActive,
                ruleSetting.Remarks
            FROM dbo.AttendanceRuleSettings AS ruleSetting
            INNER JOIN PlatformTypes.AttendanceTypes AS entryType
                ON entryType.Id = ruleSetting.AttendanceEntryTypeId
               AND entryType.TenantId = ruleSetting.TenantId;');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The physical column predates this repair and is required by the model.
        // Keeping the compatible view makes rollback non-destructive.
    }
}
