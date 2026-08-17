using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Repairs attendance-type persistence after PlatformTypes.AttendanceTypes
/// replaced the old dbo.AttendanceEntryTypes master table.
/// This migration remaps existing rows to the tenant-scoped platform table,
/// then recreates the attendance FKs/views against the new source of truth.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260817123000_RepairAttendanceTypePlatformMapping")]
public partial class RepairAttendanceTypePlatformMapping : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            SET XACT_ABORT ON;

            BEGIN TRY
                BEGIN TRANSACTION;

                -- The legacy constraints reject the new PlatformTypes ids, so they
                -- must be removed before any existing values are remapped.
                DECLARE @dropSql nvarchar(max) = N'';

                ;WITH fkTargets AS
                (
                    SELECT
                        fk.name AS FkName,
                        sch.name AS SchemaName,
                        tbl.name AS TableName
                    FROM sys.foreign_keys AS fk
                    INNER JOIN sys.tables AS tbl
                        ON tbl.object_id = fk.parent_object_id
                    INNER JOIN sys.schemas AS sch
                        ON sch.schema_id = tbl.schema_id
                    INNER JOIN sys.foreign_key_columns AS fkc
                        ON fkc.constraint_object_id = fk.object_id
                    INNER JOIN sys.columns AS col
                        ON col.object_id = fkc.parent_object_id
                       AND col.column_id = fkc.parent_column_id
                    WHERE sch.name = N'dbo'
                      AND tbl.name IN (N'AttendanceRecords', N'AttendanceMapRules', N'AttendanceRuleSettings')
                      AND col.name = N'AttendanceEntryTypeId'
                )
                SELECT @dropSql +=
                    N'ALTER TABLE ' + QUOTENAME(SchemaName) + N'.' + QUOTENAME(TableName) +
                    N' DROP CONSTRAINT ' + QUOTENAME(FkName) + N';' + CHAR(13)
                FROM fkTargets;

                IF LEN(@dropSql) > 0
                    EXEC sp_executesql @dropSql;

                -- Remap existing attendance rows/rules/settings from the old global
                -- attendance entry master to the new tenant-scoped platform table.
                IF OBJECT_ID(N'dbo.AttendanceEntryTypes', N'U') IS NOT NULL
                   AND OBJECT_ID(N'PlatformTypes.AttendanceTypes', N'U') IS NOT NULL
                BEGIN
                    -- Keep the migration valid for tenants that still have a referenced
                    -- legacy value which was not copied into their platform master.
                    ;WITH referencedTypes AS
                    (
                        SELECT TenantId, AttendanceEntryTypeId
                        FROM dbo.AttendanceRecords
                        WHERE AttendanceEntryTypeId IS NOT NULL
                        UNION
                        SELECT TenantId, AttendanceEntryTypeId
                        FROM dbo.AttendanceMapRules
                        UNION
                        SELECT TenantId, AttendanceEntryTypeId
                        FROM dbo.AttendanceRuleSettings
                    ), normalizedTypes AS
                    (
                        SELECT
                            reference.TenantId,
                            oldType.Name,
                            CASE oldType.Code
                                WHEN N'CHECK' THEN N'CHECK_IN_OUT'
                                WHEN N'NONE' THEN N'NOT_REQUIRED'
                                ELSE oldType.Code
                            END AS Code,
                            oldType.Id AS DisplayOrder,
                            oldType.IsActive
                        FROM referencedTypes AS reference
                        INNER JOIN dbo.AttendanceEntryTypes AS oldType
                            ON oldType.Id = reference.AttendanceEntryTypeId
                    )
                    INSERT INTO PlatformTypes.AttendanceTypes
                        (TenantId, Name, Code, DisplayOrder, IsActive, CreatedOnUtc)
                    SELECT source.TenantId, source.Name, source.Code,
                           source.DisplayOrder, source.IsActive, SYSUTCDATETIME()
                    FROM normalizedTypes AS source
                    WHERE NOT EXISTS
                    (
                        SELECT 1
                        FROM PlatformTypes.AttendanceTypes AS target
                        WHERE target.TenantId = source.TenantId
                          AND target.Code = source.Code
                    );

                    UPDATE ar
                       SET ar.AttendanceEntryTypeId = newType.Id
                    FROM dbo.AttendanceRecords AS ar
                    INNER JOIN dbo.AttendanceEntryTypes AS oldType
                        ON oldType.Id = ar.AttendanceEntryTypeId
                    INNER JOIN PlatformTypes.AttendanceTypes AS newType
                        ON newType.TenantId = ar.TenantId
                       AND newType.Code = CASE oldType.Code
                           WHEN N'CHECK' THEN N'CHECK_IN_OUT'
                           WHEN N'NONE' THEN N'NOT_REQUIRED'
                           ELSE oldType.Code
                       END
                    WHERE ar.AttendanceEntryTypeId IS NOT NULL
                      AND ar.AttendanceEntryTypeId <> newType.Id;

                    UPDATE mapRule
                       SET mapRule.AttendanceEntryTypeId = newType.Id
                    FROM dbo.AttendanceMapRules AS mapRule
                    INNER JOIN dbo.AttendanceEntryTypes AS oldType
                        ON oldType.Id = mapRule.AttendanceEntryTypeId
                    INNER JOIN PlatformTypes.AttendanceTypes AS newType
                        ON newType.TenantId = mapRule.TenantId
                       AND newType.Code = CASE oldType.Code
                           WHEN N'CHECK' THEN N'CHECK_IN_OUT'
                           WHEN N'NONE' THEN N'NOT_REQUIRED'
                           ELSE oldType.Code
                       END
                    WHERE mapRule.AttendanceEntryTypeId <> newType.Id;

                    UPDATE ruleSetting
                       SET ruleSetting.AttendanceEntryTypeId = newType.Id
                    FROM dbo.AttendanceRuleSettings AS ruleSetting
                    INNER JOIN dbo.AttendanceEntryTypes AS oldType
                        ON oldType.Id = ruleSetting.AttendanceEntryTypeId
                    INNER JOIN PlatformTypes.AttendanceTypes AS newType
                        ON newType.TenantId = ruleSetting.TenantId
                       AND newType.Code = CASE oldType.Code
                           WHEN N'CHECK' THEN N'CHECK_IN_OUT'
                           WHEN N'NONE' THEN N'NOT_REQUIRED'
                           ELSE oldType.Code
                       END
                    WHERE ruleSetting.AttendanceEntryTypeId <> newType.Id;
                END;

                -- Recreate the FKs against the tenant-scoped platform attendance types.
                IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL
                   AND OBJECT_ID(N'PlatformTypes.AttendanceTypes', N'U') IS NOT NULL
                   AND NOT EXISTS
                   (
                       SELECT 1
                       FROM sys.foreign_keys
                       WHERE name = N'FK_AttendanceRecords_AttendanceTypes_AttendanceEntryTypeId'
                         AND parent_object_id = OBJECT_ID(N'dbo.AttendanceRecords')
                   )
                BEGIN
                    ALTER TABLE dbo.AttendanceRecords WITH CHECK
                    ADD CONSTRAINT FK_AttendanceRecords_AttendanceTypes_AttendanceEntryTypeId
                        FOREIGN KEY(AttendanceEntryTypeId) REFERENCES PlatformTypes.AttendanceTypes(Id);
                END;

                IF OBJECT_ID(N'dbo.AttendanceMapRules', N'U') IS NOT NULL
                   AND OBJECT_ID(N'PlatformTypes.AttendanceTypes', N'U') IS NOT NULL
                   AND NOT EXISTS
                   (
                       SELECT 1
                       FROM sys.foreign_keys
                       WHERE name = N'FK_AttendanceMapRules_AttendanceTypes_AttendanceEntryTypeId'
                         AND parent_object_id = OBJECT_ID(N'dbo.AttendanceMapRules')
                   )
                BEGIN
                    ALTER TABLE dbo.AttendanceMapRules WITH CHECK
                    ADD CONSTRAINT FK_AttendanceMapRules_AttendanceTypes_AttendanceEntryTypeId
                        FOREIGN KEY(AttendanceEntryTypeId) REFERENCES PlatformTypes.AttendanceTypes(Id);
                END;

                IF OBJECT_ID(N'dbo.AttendanceRuleSettings', N'U') IS NOT NULL
                   AND OBJECT_ID(N'PlatformTypes.AttendanceTypes', N'U') IS NOT NULL
                   AND NOT EXISTS
                   (
                       SELECT 1
                       FROM sys.foreign_keys
                       WHERE name = N'FK_AttendanceRuleSettings_AttendanceTypes_AttendanceEntryTypeId'
                         AND parent_object_id = OBJECT_ID(N'dbo.AttendanceRuleSettings')
                   )
                BEGIN
                    ALTER TABLE dbo.AttendanceRuleSettings WITH CHECK
                    ADD CONSTRAINT FK_AttendanceRuleSettings_AttendanceTypes_AttendanceEntryTypeId
                        FOREIGN KEY(AttendanceEntryTypeId) REFERENCES PlatformTypes.AttendanceTypes(Id);
                END;

                -- Refresh the read views so the UI resolves attendance labels from the
                -- same final mapping source.
                -- A view definition must be the first statement in its SQL batch.
                -- Execute each definition as its own nested batch while retaining
                -- this migration's all-or-nothing transaction.
                EXEC(N'CREATE OR ALTER VIEW dbo.vw_AttendanceMapRules
                AS
                SELECT
                    mapRule.TenantId,
                    mapRule.Id,
                    mapRule.StaffId,
                    mapRule.AttendanceEntryTypeId,
                    entryType.Code AS AttendanceTypeCode,
                    entryType.Name AS AttendanceTypeName,
                    mapRule.ShiftCode,
                    COALESCE(shiftLookup.DisplayText, mapRule.ShiftCode) AS ShiftName,
                    mapRule.TimeFrom,
                    mapRule.TimeTo,
                    mapRule.IsOpenAttendance
                FROM dbo.AttendanceMapRules AS mapRule
                INNER JOIN PlatformTypes.AttendanceTypes AS entryType
                    ON entryType.Id = mapRule.AttendanceEntryTypeId
                   AND entryType.TenantId = mapRule.TenantId
                LEFT JOIN dbo.AppLookupTypes AS shiftType
                    ON shiftType.LookupTypeCode = N''ATTENDANCE_SHIFT''
                   AND shiftType.IsActive = 1
                LEFT JOIN dbo.AppLookupValues AS shiftLookup
                    ON shiftLookup.LookupTypeId = shiftType.LookupTypeId
                   AND shiftLookup.ValueCode = mapRule.ShiftCode
                   AND shiftLookup.IsActive = 1;');

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

                COMMIT TRANSACTION;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT > 0
                    ROLLBACK TRANSACTION;
                THROW;
            END CATCH
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR ALTER VIEW dbo.vw_AttendanceMapRules
            AS
            SELECT
                mapRule.TenantId,
                mapRule.Id,
                mapRule.StaffId,
                mapRule.AttendanceEntryTypeId,
                entryType.Code AS AttendanceTypeCode,
                entryType.Name AS AttendanceTypeName,
                mapRule.ShiftCode,
                COALESCE(shiftLookup.DisplayText, mapRule.ShiftCode) AS ShiftName,
                mapRule.TimeFrom,
                mapRule.TimeTo,
                mapRule.IsOpenAttendance
            FROM dbo.AttendanceMapRules AS mapRule
            INNER JOIN dbo.AttendanceEntryTypes AS entryType
                ON entryType.Id = mapRule.AttendanceEntryTypeId
            LEFT JOIN dbo.AppLookupTypes AS shiftType
                ON shiftType.LookupTypeCode = N'ATTENDANCE_SHIFT'
               AND shiftType.IsActive = 1
            LEFT JOIN dbo.AppLookupValues AS shiftLookup
                ON shiftLookup.LookupTypeId = shiftType.LookupTypeId
               AND shiftLookup.ValueCode = mapRule.ShiftCode
               AND shiftLookup.IsActive = 1;

            CREATE OR ALTER VIEW dbo.vw_AttendanceRuleSettings
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
                ruleSetting.AccountLockAbsentDays,
                ruleSetting.WeekendChargeValue,
                ruleSetting.AdjustAbsentDays,
                ruleSetting.IsApproved,
                ruleSetting.IsActive,
                ruleSetting.Remarks
            FROM dbo.AttendanceRuleSettings AS ruleSetting
            INNER JOIN dbo.AttendanceEntryTypes AS entryType
                ON entryType.Id = ruleSetting.AttendanceEntryTypeId;

            IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL
               AND OBJECT_ID(N'dbo.AttendanceEntryTypes', N'U') IS NOT NULL
               AND NOT EXISTS
               (
                   SELECT 1
                   FROM sys.foreign_keys
                   WHERE name = N'FK_AttendanceRecords_AttendanceEntryTypes_AttendanceEntryTypeId'
                     AND parent_object_id = OBJECT_ID(N'dbo.AttendanceRecords')
               )
            BEGIN
                ALTER TABLE dbo.AttendanceRecords WITH CHECK
                ADD CONSTRAINT FK_AttendanceRecords_AttendanceEntryTypes_AttendanceEntryTypeId
                    FOREIGN KEY(AttendanceEntryTypeId) REFERENCES dbo.AttendanceEntryTypes(Id);
            END;

            IF OBJECT_ID(N'dbo.AttendanceMapRules', N'U') IS NOT NULL
               AND OBJECT_ID(N'dbo.AttendanceEntryTypes', N'U') IS NOT NULL
               AND NOT EXISTS
               (
                   SELECT 1
                   FROM sys.foreign_keys
                   WHERE name = N'FK_AttendanceMapRules_AttendanceEntryTypes_AttendanceEntryTypeId'
                     AND parent_object_id = OBJECT_ID(N'dbo.AttendanceMapRules')
               )
            BEGIN
                ALTER TABLE dbo.AttendanceMapRules WITH CHECK
                ADD CONSTRAINT FK_AttendanceMapRules_AttendanceEntryTypes_AttendanceEntryTypeId
                    FOREIGN KEY(AttendanceEntryTypeId) REFERENCES dbo.AttendanceEntryTypes(Id);
            END;

            IF OBJECT_ID(N'dbo.AttendanceRuleSettings', N'U') IS NOT NULL
               AND OBJECT_ID(N'dbo.AttendanceEntryTypes', N'U') IS NOT NULL
               AND NOT EXISTS
               (
                   SELECT 1
                   FROM sys.foreign_keys
                   WHERE name = N'FK_AttendanceRuleSettings_AttendanceEntryTypes_AttendanceEntryTypeId'
                     AND parent_object_id = OBJECT_ID(N'dbo.AttendanceRuleSettings')
               )
            BEGIN
                ALTER TABLE dbo.AttendanceRuleSettings WITH CHECK
                ADD CONSTRAINT FK_AttendanceRuleSettings_AttendanceEntryTypes_AttendanceEntryTypeId
                    FOREIGN KEY(AttendanceEntryTypeId) REFERENCES dbo.AttendanceEntryTypes(Id);
            END;
            """);
    }
}
