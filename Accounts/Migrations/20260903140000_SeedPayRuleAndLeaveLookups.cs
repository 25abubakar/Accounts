using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Idempotent seed: default PayRule per tenant (Create Package dependency) +
/// Leave Applicable/Value/Calc AppLookup values for Pay Scale Leave form.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260903140000_SeedPayRuleAndLeaveLookups")]
public sealed class SeedPayRuleAndLeaveLookups : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- Default active PayRule per tenant when none exist (Create Package needs PayRuleId).
            INSERT INTO dbo.PayRules
            (
                TenantId, Code, Name, RuleType, WorkingDaysBasis, FixedWorkingDays,
                WorkingHoursPerDay, OvertimeMultiplier, RoundingMode, IsActive, Description, CreatedOnUtc
            )
            SELECT
                t.Id,
                N'DEFAULT',
                N'Default Pay Rule',
                N'Standard',
                N'Scheduled',
                26,
                9.00,
                1.50,
                N'Nearest',
                1,
                N'Auto-seeded default rule for salary packages.',
                SYSUTCDATETIME()
            FROM dbo.Tenants t
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.PayRules pr WHERE pr.TenantId = t.Id AND pr.IsActive = 1
            );

            -- Leave form lookups (replace FE hardcodes).
            DECLARE @Lookups TABLE
            (
                LookupTypeCode nvarchar(100) NOT NULL,
                LookupTypeName nvarchar(150) NOT NULL,
                ValueCode nvarchar(100) NOT NULL,
                DisplayText nvarchar(150) NOT NULL,
                SortOrder int NOT NULL
            );

            INSERT @Lookups (LookupTypeCode, LookupTypeName, ValueCode, DisplayText, SortOrder) VALUES
                (N'LEAVE_APPLICABLE_TYPE', N'Leave Applicable Type', N'BASIC', N'Basic', 10),
                (N'LEAVE_APPLICABLE_TYPE', N'Leave Applicable Type', N'GROSS', N'Gross', 20),
                (N'LEAVE_APPLICABLE_TYPE', N'Leave Applicable Type', N'CURRENT_PAY', N'CurrentPay', 30),
                (N'LEAVE_VALUE_TYPE', N'Leave Value Type', N'AMOUNT', N'Amount', 10),
                (N'LEAVE_VALUE_TYPE', N'Leave Value Type', N'PERCENTAGE', N'Percentage', 20),
                (N'LEAVE_CALC_TYPE', N'Leave Calc Type', N'FIXED', N'Fixed', 10),
                (N'LEAVE_CALC_TYPE', N'Leave Calc Type', N'VARIABLE', N'Variable', 20);

            DECLARE lookup_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT DISTINCT LookupTypeCode, LookupTypeName FROM @Lookups;

            DECLARE @LookupTypeCode nvarchar(100);
            DECLARE @LookupTypeName nvarchar(150);
            DECLARE @LookupTypeId int;

            OPEN lookup_cursor;
            FETCH NEXT FROM lookup_cursor INTO @LookupTypeCode, @LookupTypeName;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SELECT TOP (1) @LookupTypeId = LookupTypeId
                FROM dbo.AppLookupTypes
                WHERE LookupTypeCode = @LookupTypeCode;

                IF @LookupTypeId IS NULL
                BEGIN
                    INSERT dbo.AppLookupTypes (LookupTypeCode, LookupTypeName, IsActive, CreatedOn)
                    VALUES (@LookupTypeCode, @LookupTypeName, 1, SYSUTCDATETIME());
                    SET @LookupTypeId = SCOPE_IDENTITY();
                END
                ELSE
                BEGIN
                    UPDATE dbo.AppLookupTypes
                    SET LookupTypeName = @LookupTypeName, IsActive = 1
                    WHERE LookupTypeId = @LookupTypeId;
                END;

                MERGE dbo.AppLookupValues AS target
                USING (
                    SELECT ValueCode, DisplayText, SortOrder
                    FROM @Lookups
                    WHERE LookupTypeCode = @LookupTypeCode
                ) AS source
                   ON target.LookupTypeId = @LookupTypeId
                  AND target.ValueCode = source.ValueCode
                WHEN MATCHED THEN UPDATE SET
                    DisplayText = source.DisplayText,
                    SortOrder = source.SortOrder,
                    IsActive = 1
                WHEN NOT MATCHED THEN INSERT
                    (LookupTypeId, ValueCode, DisplayText, SortOrder, IsActive, CreatedOn)
                    VALUES (@LookupTypeId, source.ValueCode, source.DisplayText, source.SortOrder, 1, SYSUTCDATETIME());

                SET @LookupTypeId = NULL;
                FETCH NEXT FROM lookup_cursor INTO @LookupTypeCode, @LookupTypeName;
            END;

            CLOSE lookup_cursor;
            DEALLOCATE lookup_cursor;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE v
            FROM dbo.AppLookupValues v
            INNER JOIN dbo.AppLookupTypes t ON t.LookupTypeId = v.LookupTypeId
            WHERE t.LookupTypeCode IN (N'LEAVE_APPLICABLE_TYPE', N'LEAVE_VALUE_TYPE', N'LEAVE_CALC_TYPE');

            DELETE FROM dbo.AppLookupTypes
            WHERE LookupTypeCode IN (N'LEAVE_APPLICABLE_TYPE', N'LEAVE_VALUE_TYPE', N'LEAVE_CALC_TYPE');

            DELETE FROM dbo.PayRules
            WHERE Code = N'DEFAULT'
              AND Description = N'Auto-seeded default rule for salary packages.';
            """);
    }
}
