using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260723144000_RepairAttendanceRuleSettingColumns")]
public sealed class RepairAttendanceRuleSettingColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.AttendanceRuleSettings', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AccountLockAbsentDays') IS NULL
                    ALTER TABLE dbo.AttendanceRuleSettings ADD AccountLockAbsentDays int NOT NULL
                        CONSTRAINT DF_AttendanceRuleSettings_AccountLockAbsentDays DEFAULT(0);

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'WeekendChargeValue') IS NULL
                    ALTER TABLE dbo.AttendanceRuleSettings ADD WeekendChargeValue decimal(6,2) NOT NULL
                        CONSTRAINT DF_AttendanceRuleSettings_WeekendChargeValue DEFAULT(0);

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AdjustAbsentDays') IS NULL
                    ALTER TABLE dbo.AttendanceRuleSettings ADD AdjustAbsentDays int NOT NULL
                        CONSTRAINT DF_AttendanceRuleSettings_AdjustAbsentDays DEFAULT(0);

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AccountLocked') IS NULL
                    ALTER TABLE dbo.AttendanceRuleSettings ADD AccountLocked AS
                        CONVERT(bit, CASE WHEN AccountLockAbsentDays > 0 THEN 1 ELSE 0 END);

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'WeekendCharged') IS NULL
                    ALTER TABLE dbo.AttendanceRuleSettings ADD WeekendCharged AS
                        CONVERT(bit, CASE WHEN WeekendChargeValue > 0 THEN 1 ELSE 0 END);

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AdjustAbsent') IS NULL
                    ALTER TABLE dbo.AttendanceRuleSettings ADD AdjustAbsent AS
                        CONVERT(bit, CASE WHEN AdjustAbsentDays > 0 THEN 1 ELSE 0 END);
            END
            """);

        migrationBuilder.Sql(
            """
            CREATE OR ALTER VIEW dbo.vw_AttendanceRuleSettings
            AS
            SELECT ruleSetting.Id,
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
                   ruleSetting.MissingCheckoutAfterShiftEndMinutes,
                   ruleSetting.AccountLockAbsentDays,
                   ruleSetting.WeekendChargeValue,
                   ruleSetting.AdjustAbsentDays,
                   ruleSetting.IsApproved,
                   ruleSetting.IsActive,
                   ruleSetting.Remarks
            FROM dbo.AttendanceRuleSettings AS ruleSetting
            JOIN dbo.AttendanceEntryTypes AS entryType
              ON entryType.Id = ruleSetting.AttendanceEntryTypeId;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.AttendanceRuleSettings', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AccountLocked') IS NOT NULL
                    ALTER TABLE dbo.AttendanceRuleSettings DROP COLUMN AccountLocked;
                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'WeekendCharged') IS NOT NULL
                    ALTER TABLE dbo.AttendanceRuleSettings DROP COLUMN WeekendCharged;
                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AdjustAbsent') IS NOT NULL
                    ALTER TABLE dbo.AttendanceRuleSettings DROP COLUMN AdjustAbsent;
            END
            """);
    }
}
