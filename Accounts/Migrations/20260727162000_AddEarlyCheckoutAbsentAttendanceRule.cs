using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260727162000_AddEarlyCheckoutAbsentAttendanceRule")]
public sealed class AddEarlyCheckoutAbsentAttendanceRule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.AttendanceRuleSettings', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.AttendanceRuleSettings', N'EarlyCheckoutAbsentAfterMinutes') IS NULL
            BEGIN
                ALTER TABLE dbo.AttendanceRuleSettings ADD EarlyCheckoutAbsentAfterMinutes int NOT NULL
                    CONSTRAINT DF_AttendanceRuleSettings_EarlyCheckoutAbsentAfterMinutes DEFAULT(120);
            END;
            """);

        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.AttendanceRuleSettings', N'U') IS NOT NULL
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM sys.check_constraints
                    WHERE name = N'CK_AttendanceRuleSettings_Minutes'
                      AND parent_object_id = OBJECT_ID(N'dbo.AttendanceRuleSettings'))
                    ALTER TABLE dbo.AttendanceRuleSettings DROP CONSTRAINT CK_AttendanceRuleSettings_Minutes;

                ALTER TABLE dbo.AttendanceRuleSettings ADD CONSTRAINT CK_AttendanceRuleSettings_Minutes CHECK
                (
                    WorkingMinutes BETWEEN 0 AND 1440
                    AND BeforeCheckInMinutes BETWEEN 0 AND 720
                    AND AfterCheckOutMinutes BETWEEN 0 AND 720
                    AND CheckInAdjustMinutes BETWEEN 0 AND 720
                    AND CheckOutAdjustMinutes BETWEEN 0 AND 720
                    AND AbsentAfterShiftStartMinutes BETWEEN 1 AND 1440
                    AND EarlyCheckoutAbsentAfterMinutes BETWEEN 1 AND 1440
                    AND MissingCheckoutAfterShiftEndMinutes BETWEEN 1 AND 1440
                );

                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AttendanceRuleSettings_Tenant_ActiveApproved' AND object_id = OBJECT_ID(N'dbo.AttendanceRuleSettings'))
                    DROP INDEX IX_AttendanceRuleSettings_Tenant_ActiveApproved ON dbo.AttendanceRuleSettings;

                CREATE INDEX IX_AttendanceRuleSettings_Tenant_ActiveApproved
                    ON dbo.AttendanceRuleSettings(TenantId, IsActive, IsApproved)
                    INCLUDE (AttendanceEntryTypeId, WorkingMinutes, BeforeCheckInMinutes, CheckInAdjustMinutes, CheckOutAdjustMinutes, AbsentAfterShiftStartMinutes, EarlyCheckoutAbsentAfterMinutes, MissingCheckoutAfterShiftEndMinutes, AccountLockAbsentDays, WeekendChargeValue, AdjustAbsentDays);
            END;
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
                   ruleSetting.EarlyCheckoutAbsentAfterMinutes,
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
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AttendanceRuleSettings_Tenant_ActiveApproved' AND object_id = OBJECT_ID(N'dbo.AttendanceRuleSettings'))
                    DROP INDEX IX_AttendanceRuleSettings_Tenant_ActiveApproved ON dbo.AttendanceRuleSettings;

                IF EXISTS (
                    SELECT 1
                    FROM sys.check_constraints
                    WHERE name = N'CK_AttendanceRuleSettings_Minutes'
                      AND parent_object_id = OBJECT_ID(N'dbo.AttendanceRuleSettings'))
                    ALTER TABLE dbo.AttendanceRuleSettings DROP CONSTRAINT CK_AttendanceRuleSettings_Minutes;

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'EarlyCheckoutAbsentAfterMinutes') IS NOT NULL
                BEGIN
                    DECLARE @defaultName sysname;
                    SELECT @defaultName = defaultConstraint.name
                    FROM sys.default_constraints defaultConstraint
                    JOIN sys.columns columnDefinition
                      ON columnDefinition.default_object_id = defaultConstraint.object_id
                    WHERE defaultConstraint.parent_object_id = OBJECT_ID(N'dbo.AttendanceRuleSettings')
                      AND columnDefinition.name = N'EarlyCheckoutAbsentAfterMinutes';
                    IF @defaultName IS NOT NULL
                        EXEC(N'ALTER TABLE dbo.AttendanceRuleSettings DROP CONSTRAINT ' + QUOTENAME(@defaultName));

                    ALTER TABLE dbo.AttendanceRuleSettings DROP COLUMN EarlyCheckoutAbsentAfterMinutes;
                END;
            END;
            """);
    }
}
