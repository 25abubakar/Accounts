using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddIsOvertimeBonusActiveToAttendanceRuleSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOvertimeBonusActive",
                table: "AttendanceRuleSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(@"
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
    ruleSetting.ExtremeLateAfterMinutes,
    ruleSetting.PlatformLateStatusId,
    ruleSetting.PlatformExtremeLateStatusId,
    ruleSetting.ExtremeEarlyDepartureAfterMinutes,
    ruleSetting.PlatformEarlyDepartureStatusId,
    ruleSetting.PlatformExtremeEarlyDepartureStatusId,
    ruleSetting.IsApproved,
    ruleSetting.IsActive,
    ruleSetting.IsOvertimeBonusActive,
    ruleSetting.Remarks
FROM dbo.AttendanceRuleSettings AS ruleSetting
INNER JOIN PlatformTypes.AttendanceTypes AS entryType
    ON entryType.Id = ruleSetting.AttendanceEntryTypeId
   AND entryType.TenantId = ruleSetting.TenantId;');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsOvertimeBonusActive",
                table: "AttendanceRuleSettings");
        }
    }
}
