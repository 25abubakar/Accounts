SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRAN;

IF OBJECT_ID(N'dbo.ReminderReceivables', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReminderReceivables
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReminderReceivables PRIMARY KEY,
        TenantId INT NOT NULL,
        Ref NVARCHAR(80) NULL,
        FromAccountId INT NULL,
        ToAccountId INT NULL,
        DueDate DATE NULL,
        RemindDate DATE NULL,
        LastPaidDate DATE NULL,
        PaidOn DATE NULL,
        Amount DECIMAL(18,2) NOT NULL CONSTRAINT DF_ReminderReceivables_Amount DEFAULT (0),
        FrequencyTypeId INT NULL,
        InvoiceType NVARCHAR(120) NULL,
        TransTypeId INT NULL,
        Remarks NVARCHAR(2000) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_ReminderReceivables_IsActive DEFAULT (1),
        AlertRoznamcha BIT NOT NULL CONSTRAINT DF_ReminderReceivables_AlertRoznamcha DEFAULT (0),
        CreatedByUserId NVARCHAR(450) NULL,
        CreatedOnUtc DATETIME2 NOT NULL CONSTRAINT DF_ReminderReceivables_CreatedOnUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedByUserId NVARCHAR(450) NULL,
        UpdatedOnUtc DATETIME2 NULL,
        CONSTRAINT FK_ReminderReceivables_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ReminderReceivables_FromAccount FOREIGN KEY (FromAccountId) REFERENCES dbo.AccountsChartAccounts(Id),
        CONSTRAINT FK_ReminderReceivables_ToAccount FOREIGN KEY (ToAccountId) REFERENCES dbo.AccountsChartAccounts(Id),
        CONSTRAINT FK_ReminderReceivables_Frequency FOREIGN KEY (FrequencyTypeId) REFERENCES PlatformTypes.FrequencyTypes(Id),
        CONSTRAINT FK_ReminderReceivables_TransType FOREIGN KEY (TransTypeId) REFERENCES dbo.AccountsTransTypes(Id)
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_ReminderReceivables_Tenant_Ref'
      AND object_id = OBJECT_ID(N'dbo.ReminderReceivables'))
    CREATE UNIQUE INDEX IX_ReminderReceivables_Tenant_Ref
        ON dbo.ReminderReceivables (TenantId, Ref)
        WHERE Ref IS NOT NULL AND Ref <> N'';

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_ReminderReceivables_Tenant_DueDate'
      AND object_id = OBJECT_ID(N'dbo.ReminderReceivables'))
    CREATE INDEX IX_ReminderReceivables_Tenant_DueDate
        ON dbo.ReminderReceivables (TenantId, DueDate);

EXEC('
CREATE OR ALTER PROCEDURE dbo.usp_Reminders_ReceivableList
    @TenantId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        r.Id,
        r.Ref,
        r.FromAccountId,
        fromAcct.AccountName AS FromAcctName,
        fromAcct.AccountNumber AS FromAcctNo,
        r.ToAccountId,
        toAcct.AccountName AS ToAcctName,
        toAcct.AccountNumber AS ToAcctNo,
        r.DueDate,
        r.RemindDate,
        r.LastPaidDate,
        r.PaidOn,
        r.Amount,
        r.FrequencyTypeId,
        freq.Name AS Frequency,
        r.InvoiceType,
        r.TransTypeId,
        transType.Name AS TransactionType,
        r.Remarks,
        r.IsActive,
        r.AlertRoznamcha
    FROM dbo.ReminderReceivables r
    LEFT JOIN dbo.AccountsChartAccounts fromAcct
        ON fromAcct.Id = r.FromAccountId
       AND fromAcct.TenantId = r.TenantId
    LEFT JOIN dbo.AccountsChartAccounts toAcct
        ON toAcct.Id = r.ToAccountId
       AND toAcct.TenantId = r.TenantId
    LEFT JOIN PlatformTypes.FrequencyTypes freq
        ON freq.Id = r.FrequencyTypeId
       AND freq.TenantId = r.TenantId
    LEFT JOIN dbo.AccountsTransTypes transType
        ON transType.Id = r.TransTypeId
       AND (transType.TenantId IS NULL OR transType.TenantId = r.TenantId)
    WHERE r.TenantId = @TenantId
    ORDER BY r.Id;
END
');

DECLARE @ReceivableMenuId int = (
    SELECT TOP (1) Id FROM dbo.Menus WHERE Route = N'/reminders/receivable' ORDER BY Id
);

IF @ReceivableMenuId IS NOT NULL
BEGIN
    INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
    SELECT feature.FeatureKey, feature.FeatureName, N'Menu', feature.Description, SYSUTCDATETIME()
    FROM (VALUES
        (CONCAT(N'MENU_', @ReceivableMenuId, N'_ADD'),    N'Receivable - Add',    N'Create receivable reminders.'),
        (CONCAT(N'MENU_', @ReceivableMenuId, N'_EDIT'),   N'Receivable - Edit',   N'Edit receivable reminders.'),
        (CONCAT(N'MENU_', @ReceivableMenuId, N'_DELETE'), N'Receivable - Delete', N'Delete receivable reminders.')
    ) feature(FeatureKey, FeatureName, Description)
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.Features existing WHERE existing.FeatureKey = feature.FeatureKey
    );

    INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
    SELECT @ReceivableMenuId, f.PermissionId
    FROM dbo.Features f
    WHERE f.FeatureKey IN (
        CONCAT(N'MENU_', @ReceivableMenuId, N'_ADD'),
        CONCAT(N'MENU_', @ReceivableMenuId, N'_EDIT'),
        CONCAT(N'MENU_', @ReceivableMenuId, N'_DELETE'))
      AND NOT EXISTS (
          SELECT 1 FROM dbo.MenuPermissions existing
          WHERE existing.MenuId = @ReceivableMenuId
            AND existing.PermissionId = f.PermissionId
      );

    UPDATE dbo.TenantMenuPermissions
    SET IsAllow = 1, CanView = 1, CanAdd = 1, CanEdit = 1, CanDelete = 1
    WHERE MenuId = @ReceivableMenuId;
END;

IF NOT EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory
    WHERE MigrationId = N'20260921170000_AddReminderReceivables'
)
    INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES (N'20260921170000_AddReminderReceivables', N'9.0.5');

COMMIT TRAN;
