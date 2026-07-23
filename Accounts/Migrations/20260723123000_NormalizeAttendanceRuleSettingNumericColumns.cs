using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260723123000_NormalizeAttendanceRuleSettingNumericColumns")]
public sealed class NormalizeAttendanceRuleSettingNumericColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.AttendanceRuleSettings', N'U') IS NOT NULL
            BEGIN
                DROP VIEW IF EXISTS dbo.vw_AttendanceRuleSettings;

                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AttendanceRuleSettings_Tenant_ActiveApproved' AND object_id = OBJECT_ID(N'dbo.AttendanceRuleSettings'))
                    DROP INDEX IX_AttendanceRuleSettings_Tenant_ActiveApproved ON dbo.AttendanceRuleSettings;

                IF EXISTS (
                    SELECT 1
                    FROM sys.check_constraints
                    WHERE name = N'CK_AttendanceRuleSettings_Minutes'
                      AND parent_object_id = OBJECT_ID(N'dbo.AttendanceRuleSettings'))
                    ALTER TABLE dbo.AttendanceRuleSettings DROP CONSTRAINT CK_AttendanceRuleSettings_Minutes;

                DECLARE @constraintName sysname;
                DECLARE @dropDefaultSql nvarchar(max);

                DECLARE defaultCursor CURSOR LOCAL FAST_FORWARD FOR
                    SELECT defaultConstraint.name
                    FROM sys.default_constraints defaultConstraint
                    JOIN sys.columns columnInfo
                      ON columnInfo.object_id = defaultConstraint.parent_object_id
                     AND columnInfo.column_id = defaultConstraint.parent_column_id
                    WHERE defaultConstraint.parent_object_id = OBJECT_ID(N'dbo.AttendanceRuleSettings')
                      AND columnInfo.name IN (
                          N'AccountLocked', N'WeekendCharged', N'AdjustAbsent',
                          N'AccountLockAbsentDays', N'WeekendChargeValue', N'AdjustAbsentDays');

                OPEN defaultCursor;
                FETCH NEXT FROM defaultCursor INTO @constraintName;
                WHILE @@FETCH_STATUS = 0
                BEGIN
                    SET @dropDefaultSql = N'ALTER TABLE dbo.AttendanceRuleSettings DROP CONSTRAINT ' + QUOTENAME(@constraintName);
                    EXEC sys.sp_executesql @dropDefaultSql;
                    FETCH NEXT FROM defaultCursor INTO @constraintName;
                END
                CLOSE defaultCursor;
                DEALLOCATE defaultCursor;

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AccountLockAbsentDays') IS NULL
                    ALTER TABLE dbo.AttendanceRuleSettings ADD AccountLockAbsentDays int NULL;

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'WeekendChargeValue') IS NULL
                    ALTER TABLE dbo.AttendanceRuleSettings ADD WeekendChargeValue decimal(6,2) NULL;

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AdjustAbsentDays') IS NULL
                    ALTER TABLE dbo.AttendanceRuleSettings ADD AdjustAbsentDays int NULL;

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AccountLocked') IS NOT NULL
                    EXEC(N'UPDATE dbo.AttendanceRuleSettings
                           SET AccountLockAbsentDays = CASE WHEN AccountLocked = 1 THEN 1 ELSE 0 END
                           WHERE AccountLockAbsentDays IS NULL');

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'WeekendCharged') IS NOT NULL
                    EXEC(N'UPDATE dbo.AttendanceRuleSettings
                           SET WeekendChargeValue = CASE WHEN WeekendCharged = 1 THEN 1 ELSE 0 END
                           WHERE WeekendChargeValue IS NULL');

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AdjustAbsent') IS NOT NULL
                    EXEC(N'UPDATE dbo.AttendanceRuleSettings
                           SET AdjustAbsentDays = CASE WHEN AdjustAbsent = 1 THEN 1 ELSE 0 END
                           WHERE AdjustAbsentDays IS NULL');

                EXEC(N'UPDATE dbo.AttendanceRuleSettings
                       SET AccountLockAbsentDays = COALESCE(AccountLockAbsentDays, 0),
                           WeekendChargeValue = COALESCE(WeekendChargeValue, 0),
                           AdjustAbsentDays = COALESCE(AdjustAbsentDays, 0)');

                EXEC(N'ALTER TABLE dbo.AttendanceRuleSettings ALTER COLUMN AccountLockAbsentDays int NOT NULL');
                EXEC(N'ALTER TABLE dbo.AttendanceRuleSettings ALTER COLUMN WeekendChargeValue decimal(6,2) NOT NULL');
                EXEC(N'ALTER TABLE dbo.AttendanceRuleSettings ALTER COLUMN AdjustAbsentDays int NOT NULL');

                EXEC(N'ALTER TABLE dbo.AttendanceRuleSettings ADD CONSTRAINT DF_AttendanceRuleSettings_AccountLockAbsentDays DEFAULT(0) FOR AccountLockAbsentDays');
                EXEC(N'ALTER TABLE dbo.AttendanceRuleSettings ADD CONSTRAINT DF_AttendanceRuleSettings_WeekendChargeValue DEFAULT(0) FOR WeekendChargeValue');
                EXEC(N'ALTER TABLE dbo.AttendanceRuleSettings ADD CONSTRAINT DF_AttendanceRuleSettings_AdjustAbsentDays DEFAULT(0) FOR AdjustAbsentDays');

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AccountLocked') IS NOT NULL
                    ALTER TABLE dbo.AttendanceRuleSettings DROP COLUMN AccountLocked;

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'WeekendCharged') IS NOT NULL
                    ALTER TABLE dbo.AttendanceRuleSettings DROP COLUMN WeekendCharged;

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AdjustAbsent') IS NOT NULL
                    ALTER TABLE dbo.AttendanceRuleSettings DROP COLUMN AdjustAbsent;

                EXEC(N'ALTER TABLE dbo.AttendanceRuleSettings ADD CONSTRAINT CK_AttendanceRuleSettings_Minutes CHECK
                (
                    WorkingMinutes BETWEEN 0 AND 1440
                    AND BeforeCheckInMinutes BETWEEN 0 AND 720
                    AND AfterCheckOutMinutes BETWEEN 0 AND 720
                    AND CheckInAdjustMinutes BETWEEN 0 AND 720
                    AND CheckOutAdjustMinutes BETWEEN 0 AND 720
                    AND AbsentAfterShiftStartMinutes BETWEEN 1 AND 1440
                    AND MissingCheckoutAfterShiftEndMinutes BETWEEN 1 AND 1440
                    AND AccountLockAbsentDays BETWEEN 0 AND 31
                    AND WeekendChargeValue BETWEEN 0 AND 31
                    AND AdjustAbsentDays BETWEEN 0 AND 31
                )');

                EXEC(N'CREATE INDEX IX_AttendanceRuleSettings_Tenant_ActiveApproved
                ON dbo.AttendanceRuleSettings(TenantId, IsActive, IsApproved)
                INCLUDE (AttendanceEntryTypeId, WorkingMinutes, BeforeCheckInMinutes, CheckInAdjustMinutes, CheckOutAdjustMinutes, AbsentAfterShiftStartMinutes, MissingCheckoutAfterShiftEndMinutes, AccountLockAbsentDays, WeekendChargeValue, AdjustAbsentDays)');
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
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AttendanceRuleSettings_Tenant_ActiveApproved' AND object_id = OBJECT_ID(N'dbo.AttendanceRuleSettings'))
                    DROP INDEX IX_AttendanceRuleSettings_Tenant_ActiveApproved ON dbo.AttendanceRuleSettings;

                IF EXISTS (
                    SELECT 1
                    FROM sys.check_constraints
                    WHERE name = N'CK_AttendanceRuleSettings_Minutes'
                      AND parent_object_id = OBJECT_ID(N'dbo.AttendanceRuleSettings'))
                    ALTER TABLE dbo.AttendanceRuleSettings DROP CONSTRAINT CK_AttendanceRuleSettings_Minutes;

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AccountLockAbsentDays') IS NOT NULL
                    ALTER TABLE dbo.AttendanceRuleSettings DROP COLUMN AccountLockAbsentDays;

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'WeekendChargeValue') IS NOT NULL
                    ALTER TABLE dbo.AttendanceRuleSettings DROP COLUMN WeekendChargeValue;

                IF COL_LENGTH(N'dbo.AttendanceRuleSettings', N'AdjustAbsentDays') IS NOT NULL
                    ALTER TABLE dbo.AttendanceRuleSettings DROP COLUMN AdjustAbsentDays;
            END
            """);
    }
}
