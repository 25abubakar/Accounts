using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Idempotent AppLookup seeds for pay/staff/invoice dropdown masters + Lookup Masters menu.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260903180000_SeedAppLookupBusinessMasters")]
public sealed class SeedAppLookupBusinessMasters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @Lookups TABLE
            (
                LookupTypeCode nvarchar(100) NOT NULL,
                LookupTypeName nvarchar(150) NOT NULL,
                ValueCode nvarchar(100) NOT NULL,
                DisplayText nvarchar(150) NOT NULL,
                SortOrder int NOT NULL,
                IsDefault bit NOT NULL
            );

            INSERT @Lookups (LookupTypeCode, LookupTypeName, ValueCode, DisplayText, SortOrder, IsDefault) VALUES
                (N'PAY_RULE_TYPE', N'Pay Rule Type', N'PayScale', N'PayScale', 10, 1),
                (N'PAY_RULE_TYPE', N'Pay Rule Type', N'Allowances', N'Allowances', 20, 0),
                (N'PAY_RULE_TYPE', N'Pay Rule Type', N'TADA', N'TADA', 30, 0),
                (N'PAY_RULE_TYPE', N'Pay Rule Type', N'Leave', N'Leave', 40, 0),
                (N'CALCULATION_TYPE', N'Calculation Type', N'Fixed', N'Fixed', 10, 1),
                (N'CALCULATION_TYPE', N'Calculation Type', N'Percentage', N'Percentage', 20, 0),
                (N'PAY_FREQUENCY', N'Pay Frequency', N'Monthly', N'Monthly', 10, 1),
                (N'PAY_FREQUENCY', N'Pay Frequency', N'Quarterly', N'Quarterly', 20, 0),
                (N'PAY_FREQUENCY', N'Pay Frequency', N'Annual', N'Annual', 30, 0),
                (N'PAY_FREQUENCY', N'Pay Frequency', N'OneTime', N'OneTime', 40, 0),
                (N'PAYROLL_RUN_STATUS', N'Payroll Run Status', N'Draft', N'Draft', 10, 1),
                (N'PAYROLL_RUN_STATUS', N'Payroll Run Status', N'In Review', N'In Review', 20, 0),
                (N'PAYROLL_RUN_STATUS', N'Payroll Run Status', N'Approved', N'Approved', 30, 0),
                (N'PAYROLL_RUN_STATUS', N'Payroll Run Status', N'Finalized', N'Finalized', 40, 0),
                (N'BONUS_RUN_STATUS', N'Bonus Run Status', N'Generated', N'Generated', 10, 1),
                (N'BONUS_RUN_STATUS', N'Bonus Run Status', N'Verified', N'Verified', 20, 0),
                (N'BONUS_RUN_STATUS', N'Bonus Run Status', N'Approved', N'Approved', 30, 0),
                (N'PAY_TYPE', N'Pay Type / Basis', N'Basic', N'Basic', 10, 1),
                (N'PAY_TYPE', N'Pay Type / Basis', N'Gross', N'Gross', 20, 0),
                (N'PAY_TYPE', N'Pay Type / Basis', N'CurrentPay', N'CurrentPay', 30, 0),
                (N'GENDER', N'Gender', N'Male', N'Male', 10, 0),
                (N'GENDER', N'Gender', N'Female', N'Female', 20, 0),
                (N'GENDER', N'Gender', N'Other', N'Other', 30, 0),
                (N'MARITAL_STATUS', N'Marital Status', N'Single', N'Single', 10, 0),
                (N'MARITAL_STATUS', N'Marital Status', N'Married', N'Married', 20, 0),
                (N'MARITAL_STATUS', N'Marital Status', N'Divorced', N'Divorced', 30, 0),
                (N'MARITAL_STATUS', N'Marital Status', N'Widowed', N'Widowed', 40, 0),
                (N'BLOOD_GROUP', N'Blood Group', N'A+', N'A+', 10, 0),
                (N'BLOOD_GROUP', N'Blood Group', N'A-', N'A-', 20, 0),
                (N'BLOOD_GROUP', N'Blood Group', N'B+', N'B+', 30, 0),
                (N'BLOOD_GROUP', N'Blood Group', N'B-', N'B-', 40, 0),
                (N'BLOOD_GROUP', N'Blood Group', N'AB+', N'AB+', 50, 0),
                (N'BLOOD_GROUP', N'Blood Group', N'AB-', N'AB-', 60, 0),
                (N'BLOOD_GROUP', N'Blood Group', N'O+', N'O+', 70, 0),
                (N'BLOOD_GROUP', N'Blood Group', N'O-', N'O-', 80, 0),
                (N'PAYMENT_MODE', N'Payment Mode', N'Bank Transfer', N'Bank Transfer', 10, 1),
                (N'PAYMENT_MODE', N'Payment Mode', N'Cash', N'Cash', 20, 0),
                (N'PAYMENT_MODE', N'Payment Mode', N'Cheque', N'Cheque', 30, 0),
                (N'NATIONALITY', N'Nationality', N'Pakistani', N'Pakistani', 10, 1),
                (N'INVOICE_STATUS', N'Invoice Status', N'Draft', N'Draft', 10, 1),
                (N'INVOICE_STATUS', N'Invoice Status', N'Issued', N'Issued', 20, 0),
                (N'INVOICE_STATUS', N'Invoice Status', N'Paid', N'Paid', 30, 0),
                (N'INVOICE_STATUS', N'Invoice Status', N'Cancelled', N'Cancelled', 40, 0),
                (N'CURRENCY', N'Currency', N'PKR', N'PKR', 10, 1),
                (N'CURRENCY', N'Currency', N'USD', N'USD', 20, 0),
                (N'CURRENCY', N'Currency', N'EUR', N'EUR', 30, 0),
                (N'CURRENCY', N'Currency', N'GBP', N'GBP', 40, 0),
                (N'CURRENCY', N'Currency', N'AED', N'AED', 50, 0),
                (N'CURRENCY', N'Currency', N'SAR', N'SAR', 60, 0);

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
                    SELECT ValueCode, DisplayText, SortOrder, IsDefault
                    FROM @Lookups
                    WHERE LookupTypeCode = @LookupTypeCode
                ) AS source
                   ON target.LookupTypeId = @LookupTypeId
                  AND target.ValueCode = source.ValueCode
                WHEN MATCHED THEN UPDATE SET
                    DisplayText = source.DisplayText,
                    SortOrder = source.SortOrder,
                    IsDefault = source.IsDefault,
                    IsActive = 1
                WHEN NOT MATCHED THEN INSERT
                    (LookupTypeId, ValueCode, DisplayText, SortOrder, IsDefault, IsActive, CreatedOn)
                VALUES
                    (@LookupTypeId, source.ValueCode, source.DisplayText, source.SortOrder, source.IsDefault, 1, SYSUTCDATETIME());

                SET @LookupTypeId = NULL;
                FETCH NEXT FROM lookup_cursor INTO @LookupTypeCode, @LookupTypeName;
            END
            CLOSE lookup_cursor;
            DEALLOCATE lookup_cursor;

            -- Menu under Platform Settings (RBAC grants unchanged — assign via Access Control).
            DECLARE @PlatformId int = (
                SELECT TOP (1) [Id]
                FROM dbo.Menus
                WHERE [Title] IN (N'Platform Settings', N'Settings')
                  AND [ParentId] IS NULL
                ORDER BY CASE WHEN [Title] = N'Platform Settings' THEN 0 ELSE 1 END, [Id]
            );

            IF @PlatformId IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM dbo.Menus WHERE Route = N'/settings/lookup-masters')
            BEGIN
                INSERT INTO dbo.Menus ([Title], [Icon], [Route], [ParentId], [SortOrder], [IsActive])
                VALUES (N'Lookup Masters', N'ListTree', N'/settings/lookup-masters', @PlatformId, 12, 1);
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM dbo.Menus WHERE Route = N'/settings/lookup-masters';

            DELETE v
            FROM dbo.AppLookupValues v
            INNER JOIN dbo.AppLookupTypes t ON t.LookupTypeId = v.LookupTypeId
            WHERE t.LookupTypeCode IN (
                N'PAY_RULE_TYPE', N'CALCULATION_TYPE', N'PAY_FREQUENCY', N'PAYROLL_RUN_STATUS',
                N'BONUS_RUN_STATUS', N'PAY_TYPE', N'GENDER', N'MARITAL_STATUS', N'BLOOD_GROUP',
                N'PAYMENT_MODE', N'NATIONALITY', N'INVOICE_STATUS', N'CURRENCY'
            );

            DELETE FROM dbo.AppLookupTypes
            WHERE LookupTypeCode IN (
                N'PAY_RULE_TYPE', N'CALCULATION_TYPE', N'PAY_FREQUENCY', N'PAYROLL_RUN_STATUS',
                N'BONUS_RUN_STATUS', N'PAY_TYPE', N'GENDER', N'MARITAL_STATUS', N'BLOOD_GROUP',
                N'PAYMENT_MODE', N'NATIONALITY', N'INVOICE_STATUS', N'CURRENCY'
            );
            """);
    }
}
