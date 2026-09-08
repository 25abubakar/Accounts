using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Staff Tax calculation rows + Tax Parameter settings for the Tax workspace.
/// Tax brackets continue to use PayrollTaxSlabs.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260908130000_AddPayrollStaffTaxAndTaxParameters")]
public sealed class AddPayrollStaffTaxAndTaxParameters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.PayrollStaffTaxes', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.PayrollStaffTaxes
                (
                    Id              bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PayrollStaffTaxes PRIMARY KEY,
                    TenantId        int NOT NULL,
                    PersonId        uniqueidentifier NOT NULL,
                    StaffId         uniqueidentifier NOT NULL,
                    StaffNumber     nvarchar(50) NULL,
                    TaxRef          nvarchar(80) NOT NULL,
                    FullName        nvarchar(200) NOT NULL,
                    Department      nvarchar(200) NULL,
                    Designation     nvarchar(200) NULL,
                    DateFrom        date NOT NULL,
                    DateTo          date NOT NULL,
                    Frequency       nvarchar(30) NOT NULL CONSTRAINT DF_PayrollStaffTaxes_Frequency DEFAULT (N'Monthly'),
                    MonthlyPay      decimal(18,2) NOT NULL CONSTRAINT DF_PayrollStaffTaxes_MonthlyPay DEFAULT (0),
                    TotMonth        int NOT NULL CONSTRAINT DF_PayrollStaffTaxes_TotMonth DEFAULT (12),
                    IncomePay       decimal(18,2) NOT NULL CONSTRAINT DF_PayrollStaffTaxes_IncomePay DEFAULT (0),
                    ExtraAmount     decimal(18,2) NOT NULL CONSTRAINT DF_PayrollStaffTaxes_ExtraAmount DEFAULT (0),
                    TaxableIncome   decimal(18,2) NOT NULL CONSTRAINT DF_PayrollStaffTaxes_TaxableIncome DEFAULT (0),
                    TaxAmount       decimal(18,2) NOT NULL CONSTRAINT DF_PayrollStaffTaxes_TaxAmount DEFAULT (0),
                    TaxAdjustment   decimal(18,2) NOT NULL CONSTRAINT DF_PayrollStaffTaxes_TaxAdjustment DEFAULT (0),
                    NetTax          decimal(18,2) NOT NULL CONSTRAINT DF_PayrollStaffTaxes_NetTax DEFAULT (0),
                    PayMonth        int NOT NULL CONSTRAINT DF_PayrollStaffTaxes_PayMonth DEFAULT (12),
                    MonthlyTaxAmt   decimal(18,2) NOT NULL CONSTRAINT DF_PayrollStaffTaxes_MonthlyTaxAmt DEFAULT (0),
                    MonthlyNetTax   decimal(18,2) NOT NULL CONSTRAINT DF_PayrollStaffTaxes_MonthlyNetTax DEFAULT (0),
                    DedPercentage   decimal(9,4) NOT NULL CONSTRAINT DF_PayrollStaffTaxes_DedPercentage DEFAULT (100),
                    IsActive        bit NOT NULL CONSTRAINT DF_PayrollStaffTaxes_IsActive DEFAULT (1),
                    CreatedOnUtc    datetime2 NOT NULL CONSTRAINT DF_PayrollStaffTaxes_CreatedOnUtc DEFAULT (SYSUTCDATETIME()),
                    UpdatedOnUtc    datetime2 NULL,
                    CONSTRAINT FK_PayrollStaffTaxes_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_PayrollStaffTaxes_Persons FOREIGN KEY (PersonId) REFERENCES dbo.Persons(PersonId)
                );

                CREATE UNIQUE INDEX IX_PayrollStaffTaxes_TenantId_TaxRef
                    ON dbo.PayrollStaffTaxes (TenantId, TaxRef);

                CREATE INDEX IX_PayrollStaffTaxes_TenantId_PersonId_DateFrom
                    ON dbo.PayrollStaffTaxes (TenantId, PersonId, DateFrom);
            END;

            IF OBJECT_ID(N'dbo.PayrollTaxParameters', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.PayrollTaxParameters
                (
                    Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PayrollTaxParameters PRIMARY KEY,
                    TenantId        int NOT NULL,
                    MinTaxAmt       decimal(18,2) NOT NULL CONSTRAINT DF_PayrollTaxParameters_MinTaxAmt DEFAULT (0),
                    DedPercentage   decimal(9,4) NOT NULL CONSTRAINT DF_PayrollTaxParameters_DedPercentage DEFAULT (100),
                    IsActive        bit NOT NULL CONSTRAINT DF_PayrollTaxParameters_IsActive DEFAULT (1),
                    CreatedOnUtc    datetime2 NOT NULL CONSTRAINT DF_PayrollTaxParameters_CreatedOnUtc DEFAULT (SYSUTCDATETIME()),
                    UpdatedOnUtc    datetime2 NULL,
                    CONSTRAINT FK_PayrollTaxParameters_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id)
                );

                CREATE INDEX IX_PayrollTaxParameters_TenantId
                    ON dbo.PayrollTaxParameters (TenantId);
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.PayrollStaffTaxes', N'U') IS NOT NULL
                DROP TABLE dbo.PayrollStaffTaxes;

            IF OBJECT_ID(N'dbo.PayrollTaxParameters', N'U') IS NOT NULL
                DROP TABLE dbo.PayrollTaxParameters;
            """);
    }
}
