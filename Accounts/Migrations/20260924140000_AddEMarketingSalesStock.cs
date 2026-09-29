using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// E-Marketing sales roznamcha table + Stock Info / Sales Roz list SPs.
/// Sources: Sql/StoredProcedures/usp_EMarketing_StockInfoList.sql,
/// Sql/StoredProcedures/usp_EMarketing_SalesRozList.sql
/// Apply: Sql/Apply/Apply_EMarketingSalesStock.sql
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260924140000_AddEMarketingSalesStock")]
public sealed class AddEMarketingSalesStock : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            SET NOCOUNT ON;
            SET ANSI_NULLS ON;
            SET QUOTED_IDENTIFIER ON;

            IF OBJECT_ID(N'dbo.EMarketingSalesRozEntries', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.EMarketingSalesRozEntries
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_EMarketingSalesRozEntries PRIMARY KEY,
                    TenantId INT NOT NULL,
                    Ref NVARCHAR(80) NULL,
                    AccountId INT NOT NULL,
                    PlatformName NVARCHAR(120) NULL,
                    TransTypeId INT NULL,
                    TransTypeName NVARCHAR(120) NULL,
                    SalesTypeId INT NULL,
                    SalesTypeName NVARCHAR(120) NULL,
                    Qty DECIMAL(18,4) NOT NULL CONSTRAINT DF_EMarketingSalesRoz_Qty DEFAULT (0),
                    AmzProRef NVARCHAR(120) NULL,
                    TotProCharges DECIMAL(18,2) NOT NULL CONSTRAINT DF_EMarketingSalesRoz_TotProCharges DEFAULT (0),
                    TotPromotion DECIMAL(18,2) NOT NULL CONSTRAINT DF_EMarketingSalesRoz_TotPromotion DEFAULT (0),
                    TotProRebate DECIMAL(18,2) NOT NULL CONSTRAINT DF_EMarketingSalesRoz_TotProRebate DEFAULT (0),
                    AmazonFee DECIMAL(18,2) NOT NULL CONSTRAINT DF_EMarketingSalesRoz_AmazonFee DEFAULT (0),
                    Descriptions NVARCHAR(2000) NULL,
                    OrderId NVARCHAR(120) NULL,
                    Other DECIMAL(18,2) NOT NULL CONSTRAINT DF_EMarketingSalesRoz_Other DEFAULT (0),
                    Amount DECIMAL(18,2) NOT NULL CONSTRAINT DF_EMarketingSalesRoz_Amount DEFAULT (0),
                    PurchaseAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_EMarketingSalesRoz_PurchaseAmount DEFAULT (0),
                    SaleAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_EMarketingSalesRoz_SaleAmount DEFAULT (0),
                    QtyPurchase DECIMAL(18,4) NOT NULL CONSTRAINT DF_EMarketingSalesRoz_QtyPurchase DEFAULT (0),
                    QtySale DECIMAL(18,4) NOT NULL CONSTRAINT DF_EMarketingSalesRoz_QtySale DEFAULT (0),
                    StatusId INT NULL,
                    StatusName NVARCHAR(120) NULL,
                    TransDate DATE NOT NULL,
                    DocName NVARCHAR(200) NULL,
                    AttachmentPath NVARCHAR(500) NULL,
                    Remarks NVARCHAR(2000) NULL,
                    IsDeleted BIT NOT NULL CONSTRAINT DF_EMarketingSalesRoz_IsDeleted DEFAULT (0),
                    CreatedByUserId NVARCHAR(450) NULL,
                    CreatedOnUtc DATETIME2 NOT NULL CONSTRAINT DF_EMarketingSalesRoz_CreatedOnUtc DEFAULT (SYSUTCDATETIME()),
                    UpdatedByUserId NVARCHAR(450) NULL,
                    UpdatedOnUtc DATETIME2 NULL,
                    CONSTRAINT FK_EMarketingSalesRoz_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_EMarketingSalesRoz_Account FOREIGN KEY (AccountId) REFERENCES dbo.AccountsChartAccounts(Id)
                );

                CREATE INDEX IX_EMarketingSalesRoz_Tenant_Account_TransDate
                    ON dbo.EMarketingSalesRozEntries (TenantId, AccountId, TransDate)
                    WHERE IsDeleted = 0;

                CREATE INDEX IX_EMarketingSalesRoz_Tenant_TransDate
                    ON dbo.EMarketingSalesRozEntries (TenantId, TransDate)
                    WHERE IsDeleted = 0;
            END;

            EXEC(N'
            CREATE OR ALTER PROCEDURE dbo.usp_EMarketing_StockInfoList
                @TenantId INT,
                @AccountId INT,
                @DateFrom DATE = NULL,
                @DateTo DATE = NULL
            AS
            BEGIN
                SET NOCOUNT ON;

                SELECT
                    s.Id,
                    s.Ref,
                    a.AccountName AS AcctName,
                    s.Descriptions AS RDiscriptions,
                    s.PurchaseAmount,
                    s.TotProCharges,
                    s.SaleAmount,
                    s.QtyPurchase,
                    s.QtySale,
                    CAST(s.QtyPurchase - s.QtySale AS DECIMAL(18, 4)) AS Balance,
                    s.StatusName,
                    s.TransDate
                FROM dbo.EMarketingSalesRozEntries s
                INNER JOIN dbo.AccountsChartAccounts a
                    ON a.Id = s.AccountId
                   AND a.TenantId = s.TenantId
                WHERE s.TenantId = @TenantId
                  AND s.IsDeleted = 0
                  AND s.AccountId = @AccountId
                  AND (@DateFrom IS NULL OR s.TransDate >= @DateFrom)
                  AND (@DateTo IS NULL OR s.TransDate <= @DateTo)
                ORDER BY s.TransDate DESC, s.Id DESC;
            END
            ');

            EXEC(N'
            CREATE OR ALTER PROCEDURE dbo.usp_EMarketing_SalesRozList
                @TenantId INT,
                @AccountId INT = NULL,
                @DateFrom DATE = NULL,
                @DateTo DATE = NULL
            AS
            BEGIN
                SET NOCOUNT ON;

                SELECT
                    s.Id,
                    s.Ref,
                    s.AccountId,
                    a.AccountName AS AcctName,
                    s.PlatformName,
                    s.TransTypeId,
                    s.TransTypeName,
                    s.SalesTypeId,
                    s.SalesTypeName,
                    s.Qty,
                    s.AmzProRef,
                    s.TotProCharges,
                    s.TotPromotion,
                    s.TotProRebate,
                    s.AmazonFee,
                    s.Descriptions,
                    s.OrderId,
                    s.Other,
                    s.Amount,
                    s.PurchaseAmount,
                    s.SaleAmount,
                    s.QtyPurchase,
                    s.QtySale,
                    s.StatusId,
                    s.StatusName,
                    s.TransDate,
                    s.DocName,
                    s.AttachmentPath,
                    s.Remarks
                FROM dbo.EMarketingSalesRozEntries s
                INNER JOIN dbo.AccountsChartAccounts a
                    ON a.Id = s.AccountId
                   AND a.TenantId = s.TenantId
                WHERE s.TenantId = @TenantId
                  AND s.IsDeleted = 0
                  AND (@AccountId IS NULL OR s.AccountId = @AccountId)
                  AND (@DateFrom IS NULL OR s.TransDate >= @DateFrom)
                  AND (@DateTo IS NULL OR s.TransDate <= @DateTo)
                ORDER BY s.TransDate DESC, s.Id DESC;
            END
            ');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.usp_EMarketing_StockInfoList', N'P') IS NOT NULL
                DROP PROCEDURE dbo.usp_EMarketing_StockInfoList;
            IF OBJECT_ID(N'dbo.usp_EMarketing_SalesRozList', N'P') IS NOT NULL
                DROP PROCEDURE dbo.usp_EMarketing_SalesRozList;
            IF OBJECT_ID(N'dbo.EMarketingSalesRozEntries', N'U') IS NOT NULL
                DROP TABLE dbo.EMarketingSalesRozEntries;
            """);
    }
}
