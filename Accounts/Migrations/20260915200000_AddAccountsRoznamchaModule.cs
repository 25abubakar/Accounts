using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Phase 1: Accounts / ROZ billing ledger foundation tables (no seed business IDs).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260915200000_AddAccountsRoznamchaModule")]
public sealed class AddAccountsRoznamchaModule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.AccountsCategories', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AccountsCategories
                (
                    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountsCategories PRIMARY KEY,
                    TenantId INT NOT NULL,
                    Code NVARCHAR(40) NOT NULL,
                    Name NVARCHAR(120) NOT NULL,
                    IsActive BIT NOT NULL CONSTRAINT DF_AccountsCategories_IsActive DEFAULT (1),
                    CONSTRAINT FK_AccountsCategories_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id)
                );
                CREATE UNIQUE INDEX IX_AccountsCategories_Tenant_Code ON dbo.AccountsCategories (TenantId, Code);
            END;

            IF OBJECT_ID(N'dbo.AccountsChartAccounts', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AccountsChartAccounts
                (
                    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountsChartAccounts PRIMARY KEY,
                    TenantId INT NOT NULL,
                    AccountNumber NVARCHAR(50) NOT NULL,
                    AccountName NVARCHAR(200) NOT NULL,
                    CategoryId INT NULL,
                    IsActive BIT NOT NULL CONSTRAINT DF_AccountsChartAccounts_IsActive DEFAULT (1),
                    CONSTRAINT FK_AccountsChartAccounts_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_AccountsChartAccounts_Categories FOREIGN KEY (CategoryId) REFERENCES dbo.AccountsCategories(Id)
                );
                CREATE UNIQUE INDEX IX_AccountsChartAccounts_Tenant_AccountNumber
                    ON dbo.AccountsChartAccounts (TenantId, AccountNumber);
                CREATE INDEX IX_AccountsChartAccounts_Tenant_Category
                    ON dbo.AccountsChartAccounts (TenantId, CategoryId);
            END;

            IF OBJECT_ID(N'dbo.AccountsRoznamchaTypes', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AccountsRoznamchaTypes
                (
                    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountsRoznamchaTypes PRIMARY KEY,
                    TenantId INT NULL,
                    Code NVARCHAR(40) NOT NULL,
                    Name NVARCHAR(120) NOT NULL,
                    IsActive BIT NOT NULL CONSTRAINT DF_AccountsRoznamchaTypes_IsActive DEFAULT (1),
                    CONSTRAINT FK_AccountsRoznamchaTypes_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id)
                );
                CREATE UNIQUE INDEX IX_AccountsRoznamchaTypes_Tenant_Code
                    ON dbo.AccountsRoznamchaTypes (TenantId, Code);
            END;

            IF OBJECT_ID(N'dbo.AccountsEntryStatuses', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AccountsEntryStatuses
                (
                    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountsEntryStatuses PRIMARY KEY,
                    TenantId INT NULL,
                    Code NVARCHAR(40) NOT NULL,
                    Name NVARCHAR(120) NOT NULL,
                    ColorCode NVARCHAR(20) NULL,
                    FontColor NVARCHAR(20) NULL,
                    IsActive BIT NOT NULL CONSTRAINT DF_AccountsEntryStatuses_IsActive DEFAULT (1),
                    CONSTRAINT FK_AccountsEntryStatuses_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id)
                );
                CREATE UNIQUE INDEX IX_AccountsEntryStatuses_Tenant_Code
                    ON dbo.AccountsEntryStatuses (TenantId, Code);
            END;

            IF OBJECT_ID(N'dbo.AccountsTransTypes', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AccountsTransTypes
                (
                    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountsTransTypes PRIMARY KEY,
                    TenantId INT NULL,
                    Code NVARCHAR(40) NOT NULL,
                    Name NVARCHAR(120) NOT NULL,
                    IsActive BIT NOT NULL CONSTRAINT DF_AccountsTransTypes_IsActive DEFAULT (1),
                    CONSTRAINT FK_AccountsTransTypes_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id)
                );
                CREATE UNIQUE INDEX IX_AccountsTransTypes_Tenant_Code
                    ON dbo.AccountsTransTypes (TenantId, Code);
            END;

            IF OBJECT_ID(N'dbo.AccountsTransModes', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AccountsTransModes
                (
                    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountsTransModes PRIMARY KEY,
                    TenantId INT NULL,
                    Code NVARCHAR(40) NOT NULL,
                    Name NVARCHAR(120) NOT NULL,
                    IsActive BIT NOT NULL CONSTRAINT DF_AccountsTransModes_IsActive DEFAULT (1),
                    CONSTRAINT FK_AccountsTransModes_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id)
                );
                CREATE UNIQUE INDEX IX_AccountsTransModes_Tenant_Code
                    ON dbo.AccountsTransModes (TenantId, Code);
            END;

            IF OBJECT_ID(N'dbo.AccountsCurrencies', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AccountsCurrencies
                (
                    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountsCurrencies PRIMARY KEY,
                    TenantId INT NULL,
                    Code NVARCHAR(10) NOT NULL,
                    Name NVARCHAR(80) NOT NULL,
                    IsActive BIT NOT NULL CONSTRAINT DF_AccountsCurrencies_IsActive DEFAULT (1),
                    CONSTRAINT FK_AccountsCurrencies_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id)
                );
                CREATE UNIQUE INDEX IX_AccountsCurrencies_Tenant_Code
                    ON dbo.AccountsCurrencies (TenantId, Code);
            END;

            IF OBJECT_ID(N'dbo.AccountsModuleSettings', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AccountsModuleSettings
                (
                    TenantId INT NOT NULL CONSTRAINT PK_AccountsModuleSettings PRIMARY KEY,
                    BillingCategoryId INT NULL,
                    DefaultFromAccountId INT NULL,
                    DefaultToAccountId INT NULL,
                    DefaultCurrencyId INT NULL,
                    PaymentRozTypeId INT NULL,
                    ReceiptRozTypeId INT NULL,
                    CONSTRAINT FK_AccountsModuleSettings_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id) ON DELETE CASCADE,
                    CONSTRAINT FK_AccountsModuleSettings_BillingCategory FOREIGN KEY (BillingCategoryId) REFERENCES dbo.AccountsCategories(Id),
                    CONSTRAINT FK_AccountsModuleSettings_DefaultFromAccount FOREIGN KEY (DefaultFromAccountId) REFERENCES dbo.AccountsChartAccounts(Id),
                    CONSTRAINT FK_AccountsModuleSettings_DefaultToAccount FOREIGN KEY (DefaultToAccountId) REFERENCES dbo.AccountsChartAccounts(Id),
                    CONSTRAINT FK_AccountsModuleSettings_DefaultCurrency FOREIGN KEY (DefaultCurrencyId) REFERENCES dbo.AccountsCurrencies(Id),
                    CONSTRAINT FK_AccountsModuleSettings_PaymentRozType FOREIGN KEY (PaymentRozTypeId) REFERENCES dbo.AccountsRoznamchaTypes(Id),
                    CONSTRAINT FK_AccountsModuleSettings_ReceiptRozType FOREIGN KEY (ReceiptRozTypeId) REFERENCES dbo.AccountsRoznamchaTypes(Id)
                );
            END;

            IF OBJECT_ID(N'dbo.RoznamchaEntries', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.RoznamchaEntries
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RoznamchaEntries PRIMARY KEY,
                    TenantId INT NOT NULL,
                    SNo INT NULL,
                    Ref NVARCHAR(80) NULL,
                    OldRef NVARCHAR(80) NULL,
                    RoznamchaTypeId INT NULL,
                    CategoryId INT NULL,
                    FromAccountId INT NULL,
                    ToAccountId INT NULL,
                    ProjectId INT NULL,
                    CustomerId INT NULL,
                    StaffId UNIQUEIDENTIFIER NULL,
                    TransTypeId INT NULL,
                    TransModeId INT NULL,
                    InstrumentDate DATE NULL,
                    InstrumentNo NVARCHAR(100) NULL,
                    Descriptions NVARCHAR(2000) NULL,
                    CurrencyId INT NULL,
                    TaxTypeId INT NULL,
                    TaxRate DECIMAL(18,4) NULL,
                    TaxAmt DECIMAL(18,2) NULL,
                    Adjustment DECIMAL(18,2) NULL,
                    Amount DECIMAL(18,2) NULL,
                    BalanceAmount DECIMAL(18,2) NULL,
                    Qty DECIMAL(18,4) NULL,
                    Rate DECIMAL(18,4) NULL,
                    EnterStatusId INT NULL,
                    Attachment NVARCHAR(500) NULL,
                    Remarks NVARCHAR(2000) NULL,
                    BankLtRef NVARCHAR(100) NULL,
                    TransDate DATE NOT NULL,
                    IsDeleted BIT NOT NULL CONSTRAINT DF_RoznamchaEntries_IsDeleted DEFAULT (0),
                    BankRef NVARCHAR(100) NULL,
                    UsdAmount DECIMAL(18,2) NULL,
                    IsShow BIT NOT NULL CONSTRAINT DF_RoznamchaEntries_IsShow DEFAULT (1),
                    IsApproved BIT NOT NULL CONSTRAINT DF_RoznamchaEntries_IsApproved DEFAULT (0),
                    IsSettled BIT NOT NULL CONSTRAINT DF_RoznamchaEntries_IsSettled DEFAULT (0),
                    LibRef NVARCHAR(80) NULL,
                    IsLocked BIT NOT NULL CONSTRAINT DF_RoznamchaEntries_IsLocked DEFAULT (0),
                    IsLedger BIT NOT NULL CONSTRAINT DF_RoznamchaEntries_IsLedger DEFAULT (0),
                    IsManual BIT NOT NULL CONSTRAINT DF_RoznamchaEntries_IsManual DEFAULT (0),
                    EobCheckNo NVARCHAR(80) NULL,
                    ClaimNoIcn NVARCHAR(80) NULL,
                    PatientInfo NVARCHAR(500) NULL,
                    PaidCopayAmount DECIMAL(18,2) NULL,
                    ReferralSource NVARCHAR(200) NULL,
                    Reference NVARCHAR(200) NULL,
                    IsMatchedCopay BIT NOT NULL CONSTRAINT DF_RoznamchaEntries_IsMatchedCopay DEFAULT (0),
                    IsClosed BIT NOT NULL CONSTRAINT DF_RoznamchaEntries_IsClosed DEFAULT (0),
                    ImagePath NVARCHAR(500) NULL,
                    CreatedByUserId NVARCHAR(450) NULL,
                    CreatedOnUtc DATETIME2 NOT NULL CONSTRAINT DF_RoznamchaEntries_CreatedOnUtc DEFAULT (SYSUTCDATETIME()),
                    UpdatedByUserId NVARCHAR(450) NULL,
                    UpdatedOnUtc DATETIME2 NULL,
                    CONSTRAINT FK_RoznamchaEntries_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_RoznamchaEntries_Categories FOREIGN KEY (CategoryId) REFERENCES dbo.AccountsCategories(Id),
                    CONSTRAINT FK_RoznamchaEntries_FromAccount FOREIGN KEY (FromAccountId) REFERENCES dbo.AccountsChartAccounts(Id),
                    CONSTRAINT FK_RoznamchaEntries_ToAccount FOREIGN KEY (ToAccountId) REFERENCES dbo.AccountsChartAccounts(Id),
                    CONSTRAINT FK_RoznamchaEntries_Staff FOREIGN KEY (StaffId) REFERENCES dbo.StaffVacancy(StaffId),
                    CONSTRAINT FK_RoznamchaEntries_RozType FOREIGN KEY (RoznamchaTypeId) REFERENCES dbo.AccountsRoznamchaTypes(Id),
                    CONSTRAINT FK_RoznamchaEntries_TransType FOREIGN KEY (TransTypeId) REFERENCES dbo.AccountsTransTypes(Id),
                    CONSTRAINT FK_RoznamchaEntries_TransMode FOREIGN KEY (TransModeId) REFERENCES dbo.AccountsTransModes(Id),
                    CONSTRAINT FK_RoznamchaEntries_Currency FOREIGN KEY (CurrencyId) REFERENCES dbo.AccountsCurrencies(Id),
                    CONSTRAINT FK_RoznamchaEntries_Status FOREIGN KEY (EnterStatusId) REFERENCES dbo.AccountsEntryStatuses(Id)
                );
                CREATE INDEX IX_RoznamchaEntries_Tenant_TransDate ON dbo.RoznamchaEntries (TenantId, TransDate);
                CREATE UNIQUE INDEX IX_RoznamchaEntries_Tenant_Ref
                    ON dbo.RoznamchaEntries (TenantId, Ref)
                    WHERE Ref IS NOT NULL;
                CREATE INDEX IX_RoznamchaEntries_Tenant_Category_TransDate
                    ON dbo.RoznamchaEntries (TenantId, CategoryId, TransDate);
                CREATE INDEX IX_RoznamchaEntries_Tenant_Type_TransDate
                    ON dbo.RoznamchaEntries (TenantId, RoznamchaTypeId, TransDate);
            END;

            IF OBJECT_ID(N'dbo.BillingRoznamchaImports', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.BillingRoznamchaImports
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BillingRoznamchaImports PRIMARY KEY,
                    TenantId INT NOT NULL,
                    FileName NVARCHAR(260) NOT NULL,
                    UploadedByUserId NVARCHAR(450) NULL,
                    UploadedOnUtc DATETIME2 NOT NULL CONSTRAINT DF_BillingRoznamchaImports_UploadedOnUtc DEFAULT (SYSUTCDATETIME()),
                    Status NVARCHAR(40) NOT NULL CONSTRAINT DF_BillingRoznamchaImports_Status DEFAULT (N'Pending'),
                    ImportRowCount INT NOT NULL CONSTRAINT DF_BillingRoznamchaImports_ImportRowCount DEFAULT (0),
                    ErrorMessage NVARCHAR(2000) NULL,
                    ProcessedOnUtc DATETIME2 NULL,
                    CONSTRAINT FK_BillingRoznamchaImports_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id)
                );
                CREATE INDEX IX_BillingRoznamchaImports_Tenant_UploadedOn
                    ON dbo.BillingRoznamchaImports (TenantId, UploadedOnUtc);
            END;

            IF OBJECT_ID(N'dbo.BankStatements', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.BankStatements
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BankStatements PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ChartAccountId INT NULL,
                    AccountNumber NVARCHAR(50) NULL,
                    StatementDate DATE NULL,
                    ValueDate DATE NULL,
                    Description NVARCHAR(1000) NULL,
                    Debit DECIMAL(18,2) NULL,
                    Credit DECIMAL(18,2) NULL,
                    Balance DECIMAL(18,2) NULL,
                    BankRef NVARCHAR(100) NULL,
                    InstrumentNo NVARCHAR(100) NULL,
                    IsMatched BIT NOT NULL CONSTRAINT DF_BankStatements_IsMatched DEFAULT (0),
                    MatchedRoznamchaEntryId BIGINT NULL,
                    RawLine NVARCHAR(2000) NULL,
                    CreatedByUserId NVARCHAR(450) NULL,
                    CreatedOnUtc DATETIME2 NOT NULL CONSTRAINT DF_BankStatements_CreatedOnUtc DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT FK_BankStatements_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_BankStatements_ChartAccount FOREIGN KEY (ChartAccountId) REFERENCES dbo.AccountsChartAccounts(Id),
                    CONSTRAINT FK_BankStatements_MatchedEntry FOREIGN KEY (MatchedRoznamchaEntryId) REFERENCES dbo.RoznamchaEntries(Id)
                );
                CREATE INDEX IX_BankStatements_Tenant_StatementDate ON dbo.BankStatements (TenantId, StatementDate);
                CREATE INDEX IX_BankStatements_Tenant_IsMatched ON dbo.BankStatements (TenantId, IsMatched);
            END;

            IF OBJECT_ID(N'dbo.RoznamchaEntryProcessLogs', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.RoznamchaEntryProcessLogs
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RoznamchaEntryProcessLogs PRIMARY KEY,
                    TenantId INT NOT NULL,
                    RoznamchaEntryId BIGINT NOT NULL,
                    Action NVARCHAR(60) NOT NULL,
                    Notes NVARCHAR(2000) NULL,
                    ProcessedByUserId NVARCHAR(450) NULL,
                    ProcessedOnUtc DATETIME2 NOT NULL CONSTRAINT DF_RoznamchaEntryProcessLogs_ProcessedOnUtc DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT FK_RoznamchaEntryProcessLogs_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_RoznamchaEntryProcessLogs_Entry FOREIGN KEY (RoznamchaEntryId) REFERENCES dbo.RoznamchaEntries(Id) ON DELETE CASCADE
                );
                CREATE INDEX IX_RoznamchaEntryProcessLogs_Tenant_Entry_ProcessedOn
                    ON dbo.RoznamchaEntryProcessLogs (TenantId, RoznamchaEntryId, ProcessedOnUtc);
            END;

            IF OBJECT_ID(N'dbo.RoznamchaEntryAccesses', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.RoznamchaEntryAccesses
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RoznamchaEntryAccesses PRIMARY KEY,
                    TenantId INT NOT NULL,
                    RoznamchaEntryId BIGINT NOT NULL,
                    DepartmentId INT NOT NULL,
                    StaffId UNIQUEIDENTIFIER NULL,
                    CONSTRAINT FK_RoznamchaEntryAccesses_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_RoznamchaEntryAccesses_Entry FOREIGN KEY (RoznamchaEntryId) REFERENCES dbo.RoznamchaEntries(Id) ON DELETE CASCADE,
                    CONSTRAINT FK_RoznamchaEntryAccesses_Staff FOREIGN KEY (StaffId) REFERENCES dbo.StaffVacancy(StaffId)
                );
                CREATE UNIQUE INDEX IX_RoznamchaEntryAccesses_Tenant_Entry_Department
                    ON dbo.RoznamchaEntryAccesses (TenantId, RoznamchaEntryId, DepartmentId);
            END;

            IF OBJECT_ID(N'dbo.AccountsEntryDocuments', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.AccountsEntryDocuments
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AccountsEntryDocuments PRIMARY KEY,
                    TenantId INT NOT NULL,
                    RoznamchaEntryId BIGINT NULL,
                    FileName NVARCHAR(260) NOT NULL,
                    StoredPath NVARCHAR(500) NOT NULL,
                    ContentType NVARCHAR(150) NULL,
                    FileSizeBytes BIGINT NOT NULL CONSTRAINT DF_AccountsEntryDocuments_FileSizeBytes DEFAULT (0),
                    UploadedByUserId NVARCHAR(450) NULL,
                    UploadedOnUtc DATETIME2 NOT NULL CONSTRAINT DF_AccountsEntryDocuments_UploadedOnUtc DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT FK_AccountsEntryDocuments_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_AccountsEntryDocuments_Entry FOREIGN KEY (RoznamchaEntryId) REFERENCES dbo.RoznamchaEntries(Id) ON DELETE SET NULL
                );
                CREATE INDEX IX_AccountsEntryDocuments_Tenant_Entry
                    ON dbo.AccountsEntryDocuments (TenantId, RoznamchaEntryId);
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.AccountsEntryDocuments', N'U') IS NOT NULL DROP TABLE dbo.AccountsEntryDocuments;
            IF OBJECT_ID(N'dbo.RoznamchaEntryAccesses', N'U') IS NOT NULL DROP TABLE dbo.RoznamchaEntryAccesses;
            IF OBJECT_ID(N'dbo.RoznamchaEntryProcessLogs', N'U') IS NOT NULL DROP TABLE dbo.RoznamchaEntryProcessLogs;
            IF OBJECT_ID(N'dbo.BankStatements', N'U') IS NOT NULL DROP TABLE dbo.BankStatements;
            IF OBJECT_ID(N'dbo.BillingRoznamchaImports', N'U') IS NOT NULL DROP TABLE dbo.BillingRoznamchaImports;
            IF OBJECT_ID(N'dbo.RoznamchaEntries', N'U') IS NOT NULL DROP TABLE dbo.RoznamchaEntries;
            IF OBJECT_ID(N'dbo.AccountsModuleSettings', N'U') IS NOT NULL DROP TABLE dbo.AccountsModuleSettings;
            IF OBJECT_ID(N'dbo.AccountsCurrencies', N'U') IS NOT NULL DROP TABLE dbo.AccountsCurrencies;
            IF OBJECT_ID(N'dbo.AccountsTransModes', N'U') IS NOT NULL DROP TABLE dbo.AccountsTransModes;
            IF OBJECT_ID(N'dbo.AccountsTransTypes', N'U') IS NOT NULL DROP TABLE dbo.AccountsTransTypes;
            IF OBJECT_ID(N'dbo.AccountsEntryStatuses', N'U') IS NOT NULL DROP TABLE dbo.AccountsEntryStatuses;
            IF OBJECT_ID(N'dbo.AccountsRoznamchaTypes', N'U') IS NOT NULL DROP TABLE dbo.AccountsRoznamchaTypes;
            IF OBJECT_ID(N'dbo.AccountsChartAccounts', N'U') IS NOT NULL DROP TABLE dbo.AccountsChartAccounts;
            IF OBJECT_ID(N'dbo.AccountsCategories', N'U') IS NOT NULL DROP TABLE dbo.AccountsCategories;
            """);
    }
}
