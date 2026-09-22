using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Persists every field shown on the Reminder / Receivable form and seeds its
/// database-backed dropdown masters without replacing existing tenant data.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260922080000_ExpandReminderReceivableForm")]
public sealed class ExpandReminderReceivableForm : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET XACT_ABORT ON;

            IF COL_LENGTH(N'dbo.ReminderReceivables', N'AccountTypeId') IS NULL
                ALTER TABLE dbo.ReminderReceivables ADD AccountTypeId int NULL;
            IF COL_LENGTH(N'dbo.ReminderReceivables', N'ReminderTypeId') IS NULL
                ALTER TABLE dbo.ReminderReceivables ADD ReminderTypeId int NULL;
            IF COL_LENGTH(N'dbo.ReminderReceivables', N'InvoiceTypeId') IS NULL
                ALTER TABLE dbo.ReminderReceivables ADD InvoiceTypeId int NULL;
            IF COL_LENGTH(N'dbo.ReminderReceivables', N'CategoryId') IS NULL
                ALTER TABLE dbo.ReminderReceivables ADD CategoryId int NULL;
            IF COL_LENGTH(N'dbo.ReminderReceivables', N'ToCategoryId') IS NULL
                ALTER TABLE dbo.ReminderReceivables ADD ToCategoryId int NULL;
            IF COL_LENGTH(N'dbo.ReminderReceivables', N'CreatedOn') IS NULL
                ALTER TABLE dbo.ReminderReceivables ADD CreatedOn date NULL;
            IF COL_LENGTH(N'dbo.ReminderReceivables', N'RemindDay') IS NULL
                ALTER TABLE dbo.ReminderReceivables ADD RemindDay int NULL;
            IF COL_LENGTH(N'dbo.ReminderReceivables', N'ReceivedOn') IS NULL
                ALTER TABLE dbo.ReminderReceivables ADD ReceivedOn date NULL;
            """);

        migrationBuilder.Sql(
            """

            UPDATE reminder
            SET CategoryId = accountRow.CategoryId
            FROM dbo.ReminderReceivables reminder
            JOIN dbo.AccountsChartAccounts accountRow ON accountRow.Id = reminder.FromAccountId
            WHERE reminder.CategoryId IS NULL;

            UPDATE reminder
            SET ToCategoryId = accountRow.CategoryId
            FROM dbo.ReminderReceivables reminder
            JOIN dbo.AccountsChartAccounts accountRow ON accountRow.Id = reminder.ToAccountId
            WHERE reminder.ToCategoryId IS NULL;

            UPDATE dbo.ReminderReceivables
            SET CreatedOn = CONVERT(date, DATEADD(hour, 5, CreatedOnUtc))
            WHERE CreatedOn IS NULL;

            UPDATE dbo.ReminderReceivables
            SET ReceivedOn = PaidOn
            WHERE ReceivedOn IS NULL AND PaidOn IS NOT NULL;

            DECLARE @LookupSeed TABLE
            (
                LookupTypeCode nvarchar(100) NOT NULL,
                LookupTypeName nvarchar(150) NOT NULL,
                ValueCode nvarchar(100) NOT NULL,
                DisplayText nvarchar(150) NOT NULL,
                SortOrder int NOT NULL
            );

            INSERT @LookupSeed VALUES
                (N'REMINDER_ACCOUNT_TYPE', N'Reminder Account Type', N'COY', N'Coy', 10),
                (N'REMINDER_ACCOUNT_TYPE', N'Reminder Account Type', N'PRIVATE', N'Private', 20),
                (N'REMINDER_RECEIVABLE_TYPE', N'Receivable Type', N'BILL_INVOICE', N'Bill/Invoice', 10),
                (N'REMINDER_RECEIVABLE_TYPE', N'Receivable Type', N'FEE', N'Fee', 20),
                (N'REMINDER_RECEIVABLE_TYPE', N'Receivable Type', N'SUBSCRIPTION', N'Subscription', 30),
                (N'REMINDER_RECEIVABLE_TYPE', N'Receivable Type', N'RENEWAL', N'Renewal', 40),
                (N'REMINDER_RECEIVABLE_TYPE', N'Receivable Type', N'OTHER', N'Other', 50),
                (N'REMINDER_INVOICE_TYPE', N'Reminder Invoice Type', N'FIXED', N'Fixed', 10),
                (N'REMINDER_INVOICE_TYPE', N'Reminder Invoice Type', N'AS_BILLED', N'As Billed', 20),
                (N'REMINDER_INVOICE_TYPE', N'Reminder Invoice Type', N'VARIABLE', N'Variable', 30),
                (N'REMINDER_INVOICE_TYPE', N'Reminder Invoice Type', N'SERVICE_CHARGES', N'Service Charges', 40),
                (N'REMINDER_INVOICE_TYPE', N'Reminder Invoice Type', N'BILLING', N'Billing', 50);

            MERGE dbo.AppLookupTypes AS target
            USING (SELECT DISTINCT LookupTypeCode, LookupTypeName FROM @LookupSeed) AS source
               ON target.LookupTypeCode = source.LookupTypeCode
            WHEN MATCHED THEN UPDATE SET LookupTypeName = source.LookupTypeName, IsActive = 1
            WHEN NOT MATCHED THEN INSERT (LookupTypeCode, LookupTypeName, IsActive, CreatedOn)
                VALUES (source.LookupTypeCode, source.LookupTypeName, 1, SYSUTCDATETIME());

            MERGE dbo.AppLookupValues AS target
            USING (
                SELECT typeRow.LookupTypeId, seed.ValueCode, seed.DisplayText, seed.SortOrder
                FROM @LookupSeed seed
                JOIN dbo.AppLookupTypes typeRow ON typeRow.LookupTypeCode = seed.LookupTypeCode
            ) AS source
               ON target.LookupTypeId = source.LookupTypeId AND target.ValueCode = source.ValueCode
            WHEN MATCHED THEN UPDATE SET DisplayText = source.DisplayText, SortOrder = source.SortOrder, IsActive = 1
            WHEN NOT MATCHED THEN INSERT
                (LookupTypeId, ValueCode, DisplayText, SortOrder, IsDefault, IsActive, CreatedOn)
                VALUES (source.LookupTypeId, source.ValueCode, source.DisplayText, source.SortOrder, 0, 1, SYSUTCDATETIME());

            IF NOT EXISTS (SELECT 1 FROM dbo.AccountsTransTypes WHERE TenantId IS NULL AND Code = N'PAYMENT')
                INSERT dbo.AccountsTransTypes (TenantId, Code, Name, IsActive) VALUES (NULL, N'PAYMENT', N'Payment', 1);
            ELSE
                UPDATE dbo.AccountsTransTypes SET Name = N'Payment', IsActive = 1 WHERE TenantId IS NULL AND Code = N'PAYMENT';

            IF NOT EXISTS (SELECT 1 FROM dbo.AccountsTransTypes WHERE TenantId IS NULL AND Code = N'RECEIPT')
                INSERT dbo.AccountsTransTypes (TenantId, Code, Name, IsActive) VALUES (NULL, N'RECEIPT', N'Receipt', 1);
            ELSE
                UPDATE dbo.AccountsTransTypes SET Name = N'Receipt', IsActive = 1 WHERE TenantId IS NULL AND Code = N'RECEIPT';

            DECLARE @CategorySeed TABLE (Code nvarchar(40), Name nvarchar(120));
            INSERT @CategorySeed VALUES
                (N'BANK', N'Bank'), (N'CASH_IN_HAND', N'Cash in Hand'), (N'EXPENSE', N'Expense'),
                (N'FEE_SUBSCRIPTIONS', N'Fee & Subscriptions'), (N'PRINTING_STATIONARY', N'Printing & Stationary'),
                (N'REPAIR_MAINTENANCE', N'Repair & Maintenance'), (N'TRAVEL_CONVEYANCE', N'Travel & Conveyance'),
                (N'DONATIONS', N'Donations'), (N'CLIENTS', N'Clients'), (N'PROJECTS', N'Projects'),
                (N'SALARY_BENEFITS', N'Salary & Benefits'), (N'ASSETS_INVENTORY', N'Assets & inventory'),
                (N'LOAN_ADVANCE', N'Loan & Advance'), (N'STAFF', N'Staff'),
                (N'SUPPLIERS_SERVICES', N'Suppliers & Services'), (N'PARTNERS', N'Partners'),
                (N'ACCOUNT_RECONCILIATION', N'Account & Reconciliation'), (N'AMAZON_H', N'Amazon (H)'),
                (N'CONSTRUCTIONS', N'Constructions');

            INSERT dbo.AccountsCategories (TenantId, Code, Name, IsActive)
            SELECT tenant.Id, seed.Code, seed.Name, 1
            FROM dbo.Tenants tenant
            CROSS JOIN @CategorySeed seed
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.AccountsCategories existing
                WHERE existing.TenantId = tenant.Id
                  AND (existing.Code = seed.Code OR existing.Name = seed.Name));

            DECLARE @FrequencySeed TABLE (Code nvarchar(100), Name nvarchar(150), DisplayOrder int);
            INSERT @FrequencySeed VALUES
                (N'PY', N'PY', 10), (N'PM', N'PM', 20), (N'PD', N'PD', 30),
                (N'ONE_TIME', N'One Time', 40), (N'ON_OCCURRENCE', N'On Occurrence', 50),
                (N'ON_JOINING', N'On Joining', 60), (N'WEEKLY', N'Weekly', 70),
                (N'MONTHLY', N'Monthly', 80), (N'QUARTERLY', N'Quarterly', 90),
                (N'ANNUALLY', N'Annually', 100), (N'ON_RETIREMENT', N'On Retirement', 110),
                (N'ON_DEMAND', N'On Demand', 120), (N'ON_TRAVEL', N'On Travel', 130),
                (N'SEMI_ANNUALLY', N'Semi Annually', 140), (N'ON_EXPIRY', N'On Expiry', 150);

            INSERT PlatformTypes.FrequencyTypes
                (TenantId, Name, Code, DisplayOrder, IsActive, CreatedOnUtc)
            SELECT tenant.Id, seed.Name, seed.Code, seed.DisplayOrder, 1, SYSUTCDATETIME()
            FROM dbo.Tenants tenant
            CROSS JOIN @FrequencySeed seed
            WHERE NOT EXISTS (
                SELECT 1 FROM PlatformTypes.FrequencyTypes existing
                WHERE existing.TenantId = tenant.Id AND existing.Code = seed.Code);

            UPDATE reminder
            SET InvoiceTypeId = value.LookupValueId
            FROM dbo.ReminderReceivables reminder
            JOIN dbo.AppLookupTypes typeRow ON typeRow.LookupTypeCode = N'REMINDER_INVOICE_TYPE'
            JOIN dbo.AppLookupValues value ON value.LookupTypeId = typeRow.LookupTypeId
                AND UPPER(value.DisplayText) = UPPER(LTRIM(RTRIM(reminder.InvoiceType)))
            WHERE reminder.InvoiceTypeId IS NULL AND reminder.InvoiceType IS NOT NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ReminderReceivables_AccountType')
                ALTER TABLE dbo.ReminderReceivables ADD CONSTRAINT FK_ReminderReceivables_AccountType
                    FOREIGN KEY (AccountTypeId) REFERENCES dbo.AppLookupValues(LookupValueId);
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ReminderReceivables_ReminderType')
                ALTER TABLE dbo.ReminderReceivables ADD CONSTRAINT FK_ReminderReceivables_ReminderType
                    FOREIGN KEY (ReminderTypeId) REFERENCES dbo.AppLookupValues(LookupValueId);
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ReminderReceivables_InvoiceType')
                ALTER TABLE dbo.ReminderReceivables ADD CONSTRAINT FK_ReminderReceivables_InvoiceType
                    FOREIGN KEY (InvoiceTypeId) REFERENCES dbo.AppLookupValues(LookupValueId);
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ReminderReceivables_Category')
                ALTER TABLE dbo.ReminderReceivables ADD CONSTRAINT FK_ReminderReceivables_Category
                    FOREIGN KEY (CategoryId) REFERENCES dbo.AccountsCategories(Id);
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ReminderReceivables_ToCategory')
                ALTER TABLE dbo.ReminderReceivables ADD CONSTRAINT FK_ReminderReceivables_ToCategory
                    FOREIGN KEY (ToCategoryId) REFERENCES dbo.AccountsCategories(Id);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ReminderReceivables_AccountTypeId' AND object_id = OBJECT_ID(N'dbo.ReminderReceivables'))
                CREATE INDEX IX_ReminderReceivables_AccountTypeId ON dbo.ReminderReceivables(AccountTypeId);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ReminderReceivables_ReminderTypeId' AND object_id = OBJECT_ID(N'dbo.ReminderReceivables'))
                CREATE INDEX IX_ReminderReceivables_ReminderTypeId ON dbo.ReminderReceivables(ReminderTypeId);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ReminderReceivables_InvoiceTypeId' AND object_id = OBJECT_ID(N'dbo.ReminderReceivables'))
                CREATE INDEX IX_ReminderReceivables_InvoiceTypeId ON dbo.ReminderReceivables(InvoiceTypeId);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ReminderReceivables_CategoryId' AND object_id = OBJECT_ID(N'dbo.ReminderReceivables'))
                CREATE INDEX IX_ReminderReceivables_CategoryId ON dbo.ReminderReceivables(CategoryId);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ReminderReceivables_ToCategoryId' AND object_id = OBJECT_ID(N'dbo.ReminderReceivables'))
                CREATE INDEX IX_ReminderReceivables_ToCategoryId ON dbo.ReminderReceivables(ToCategoryId);
            """);

        migrationBuilder.Sql(ReceivableListProcedure);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.ReminderReceivables', N'U') IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ReminderReceivables_AccountType') ALTER TABLE dbo.ReminderReceivables DROP CONSTRAINT FK_ReminderReceivables_AccountType;
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ReminderReceivables_ReminderType') ALTER TABLE dbo.ReminderReceivables DROP CONSTRAINT FK_ReminderReceivables_ReminderType;
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ReminderReceivables_InvoiceType') ALTER TABLE dbo.ReminderReceivables DROP CONSTRAINT FK_ReminderReceivables_InvoiceType;
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ReminderReceivables_Category') ALTER TABLE dbo.ReminderReceivables DROP CONSTRAINT FK_ReminderReceivables_Category;
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ReminderReceivables_ToCategory') ALTER TABLE dbo.ReminderReceivables DROP CONSTRAINT FK_ReminderReceivables_ToCategory;

                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ReminderReceivables_AccountTypeId' AND object_id = OBJECT_ID(N'dbo.ReminderReceivables')) DROP INDEX IX_ReminderReceivables_AccountTypeId ON dbo.ReminderReceivables;
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ReminderReceivables_ReminderTypeId' AND object_id = OBJECT_ID(N'dbo.ReminderReceivables')) DROP INDEX IX_ReminderReceivables_ReminderTypeId ON dbo.ReminderReceivables;
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ReminderReceivables_InvoiceTypeId' AND object_id = OBJECT_ID(N'dbo.ReminderReceivables')) DROP INDEX IX_ReminderReceivables_InvoiceTypeId ON dbo.ReminderReceivables;
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ReminderReceivables_CategoryId' AND object_id = OBJECT_ID(N'dbo.ReminderReceivables')) DROP INDEX IX_ReminderReceivables_CategoryId ON dbo.ReminderReceivables;
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ReminderReceivables_ToCategoryId' AND object_id = OBJECT_ID(N'dbo.ReminderReceivables')) DROP INDEX IX_ReminderReceivables_ToCategoryId ON dbo.ReminderReceivables;

                IF COL_LENGTH(N'dbo.ReminderReceivables', N'AccountTypeId') IS NOT NULL ALTER TABLE dbo.ReminderReceivables DROP COLUMN AccountTypeId;
                IF COL_LENGTH(N'dbo.ReminderReceivables', N'ReminderTypeId') IS NOT NULL ALTER TABLE dbo.ReminderReceivables DROP COLUMN ReminderTypeId;
                IF COL_LENGTH(N'dbo.ReminderReceivables', N'InvoiceTypeId') IS NOT NULL ALTER TABLE dbo.ReminderReceivables DROP COLUMN InvoiceTypeId;
                IF COL_LENGTH(N'dbo.ReminderReceivables', N'CategoryId') IS NOT NULL ALTER TABLE dbo.ReminderReceivables DROP COLUMN CategoryId;
                IF COL_LENGTH(N'dbo.ReminderReceivables', N'ToCategoryId') IS NOT NULL ALTER TABLE dbo.ReminderReceivables DROP COLUMN ToCategoryId;
                IF COL_LENGTH(N'dbo.ReminderReceivables', N'CreatedOn') IS NOT NULL ALTER TABLE dbo.ReminderReceivables DROP COLUMN CreatedOn;
                IF COL_LENGTH(N'dbo.ReminderReceivables', N'RemindDay') IS NOT NULL ALTER TABLE dbo.ReminderReceivables DROP COLUMN RemindDay;
                IF COL_LENGTH(N'dbo.ReminderReceivables', N'ReceivedOn') IS NOT NULL ALTER TABLE dbo.ReminderReceivables DROP COLUMN ReceivedOn;
            END;
            """);
    }

    private const string ReceivableListProcedure =
        """
        CREATE OR ALTER PROCEDURE dbo.usp_Reminders_ReceivableList
            @TenantId INT
        AS
        BEGIN
            SET NOCOUNT ON;

            SELECT
                r.Id,
                r.Ref,
                r.AccountTypeId,
                accountType.DisplayText AS AccountType,
                r.ReminderTypeId,
                reminderType.DisplayText AS ReminderType,
                r.InvoiceTypeId,
                r.CategoryId,
                category.Name AS Category,
                r.FromAccountId,
                fromAcct.AccountName AS FromAcctName,
                fromAcct.AccountNumber AS FromAcctNo,
                r.ToCategoryId,
                toCategory.Name AS ToCategory,
                r.ToAccountId,
                toAcct.AccountName AS ToAcctName,
                toAcct.AccountNumber AS ToAcctNo,
                r.CreatedOn,
                r.DueDate,
                r.RemindDay,
                r.RemindDate,
                r.ReceivedOn,
                r.LastPaidDate,
                r.PaidOn,
                r.Amount,
                r.FrequencyTypeId,
                freq.Name AS Frequency,
                COALESCE(invoiceType.DisplayText, r.InvoiceType) AS InvoiceType,
                r.TransTypeId,
                transType.Name AS TransactionType,
                r.Remarks,
                r.IsActive,
                r.AlertRoznamcha
            FROM dbo.ReminderReceivables r
            LEFT JOIN dbo.AppLookupValues accountType ON accountType.LookupValueId = r.AccountTypeId
            LEFT JOIN dbo.AppLookupValues reminderType ON reminderType.LookupValueId = r.ReminderTypeId
            LEFT JOIN dbo.AppLookupValues invoiceType ON invoiceType.LookupValueId = r.InvoiceTypeId
            LEFT JOIN dbo.AccountsCategories category ON category.Id = r.CategoryId AND category.TenantId = r.TenantId
            LEFT JOIN dbo.AccountsChartAccounts fromAcct ON fromAcct.Id = r.FromAccountId AND fromAcct.TenantId = r.TenantId
            LEFT JOIN dbo.AccountsCategories toCategory ON toCategory.Id = r.ToCategoryId AND toCategory.TenantId = r.TenantId
            LEFT JOIN dbo.AccountsChartAccounts toAcct ON toAcct.Id = r.ToAccountId AND toAcct.TenantId = r.TenantId
            LEFT JOIN PlatformTypes.FrequencyTypes freq ON freq.Id = r.FrequencyTypeId AND freq.TenantId = r.TenantId
            LEFT JOIN dbo.AccountsTransTypes transType ON transType.Id = r.TransTypeId AND (transType.TenantId IS NULL OR transType.TenantId = r.TenantId)
            WHERE r.TenantId = @TenantId
            ORDER BY r.Id DESC;
        END
        """;
}
