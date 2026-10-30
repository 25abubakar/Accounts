using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddOcrEobModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblUploadedOcrEobRecords')
BEGIN
    CREATE TABLE [dbo].[tblUploadedOcrEobRecords] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [OrgId] INT NULL,
        [Eob_Date] NVARCHAR(100) NULL,
        [Attachment] NVARCHAR(500) NULL,
        [Check_EFT] NVARCHAR(200) NULL,
        [Tot_PaidAmt] DECIMAL(18,2) NULL,
        [StatusId] INT NULL,
        [Source] NVARCHAR(100) NULL,
        [CreatedDate] DATETIME2 NULL,
        [CreatedByUserId] NVARCHAR(450) NULL,
        CONSTRAINT [PK_tblUploadedOcrEobRecords] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblOCRDocHeaderInfo')
BEGIN
    CREATE TABLE [dbo].[tblOCRDocHeaderInfo] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [EobAttId] INT NULL,
        [OrgId] INT NULL,
        [Address] NVARCHAR(1000) NULL,
        [Phone] NVARCHAR(100) NULL,
        [Fax] NVARCHAR(100) NULL,
        [DocTitle] NVARCHAR(500) NULL,
        [InsuranceName] NVARCHAR(500) NULL,
        [InsuranceAddress] NVARCHAR(1000) NULL,
        [ProviderName] NVARCHAR(500) NULL,
        [ProviderAddress] NVARCHAR(1000) NULL,
        [ProviderId] NVARCHAR(100) NULL,
        [NPI] NVARCHAR(100) NULL,
        [Check_EFT] NVARCHAR(200) NULL,
        [StatementDate] NVARCHAR(100) NULL,
        [TaxId] NVARCHAR(100) NULL,
        [PaymentWeek] NVARCHAR(100) NULL,
        [Source] NVARCHAR(100) NULL,
        [CreatedDate] DATETIME2 NULL,
        CONSTRAINT [PK_tblOCRDocHeaderInfo] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblOcrPatientDetail')
BEGIN
    CREATE TABLE [dbo].[tblOcrPatientDetail] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [OcrHeaderId] INT NULL,
        [OrgId] INT NULL,
        [PatientName] NVARCHAR(500) NULL,
        [MID] NVARCHAR(200) NULL,
        [PatientNo] NVARCHAR(200) NULL,
        [ICN] NVARCHAR(200) NULL,
        [Billed] DECIMAL(18,2) NULL,
        [Allowed] DECIMAL(18,2) NULL,
        [Deductible] DECIMAL(18,2) NULL,
        [CoInsurance] DECIMAL(18,2) NULL,
        [RcAmount] DECIMAL(18,2) NULL,
        [ProviderPaid] DECIMAL(18,2) NULL,
        [PatientResponsibility] DECIMAL(18,2) NULL,
        [AdjustmentTotal] DECIMAL(18,2) NULL,
        [Interest] DECIMAL(18,2) NULL,
        [LateFiling] DECIMAL(18,2) NULL,
        [Net] DECIMAL(18,2) NULL,
        [ProviderDiscount] DECIMAL(18,2) NULL,
        [OtherPlanPayment] DECIMAL(18,2) NULL,
        [OtherAdjustments] DECIMAL(18,2) NULL,
        [NonCovered] DECIMAL(18,2) NULL,
        [Source] NVARCHAR(500) NULL,
        [CreatedDate] DATETIME2 NULL,
        CONSTRAINT [PK_tblOcrPatientDetail] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblOcrProcedureDetails')
BEGIN
    CREATE TABLE [dbo].[tblOcrProcedureDetails] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [OcrPatientId] INT NULL,
        [OrgId] INT NULL,
        [RenderingProviderId] NVARCHAR(100) NULL,
        [DayMonth] NVARCHAR(50) NULL,
        [DateOfService] NVARCHAR(50) NULL,
        [POS] NVARCHAR(10) NULL,
        [NOS] NVARCHAR(10) NULL,
        [ProcedureCode] NVARCHAR(50) NULL,
        [Modifiers] NVARCHAR(100) NULL,
        [Billed] DECIMAL(18,2) NULL,
        [Allowed] DECIMAL(18,2) NULL,
        [Deductible] DECIMAL(18,2) NULL,
        [CoInsurance] DECIMAL(18,2) NULL,
        [NOS_Val] DECIMAL(18,2) NULL,
        [ProviderPaid] DECIMAL(18,2) NULL,
        [SubNOS] NVARCHAR(50) NULL,
        [ProviderDiscount] DECIMAL(18,2) NULL,
        [OtherPlan] DECIMAL(18,2) NULL,
        [OtherAdjustments] DECIMAL(18,2) NULL,
        [Copay] DECIMAL(18,2) NULL,
        [NonCovered] DECIMAL(18,2) NULL,
        [PatientResponsibility] DECIMAL(18,2) NULL,
        [FillSource] NVARCHAR(100) NULL,
        [CreatedDate] DATETIME2 NULL,
        CONSTRAINT [PK_tblOcrProcedureDetails] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblOcrGrpAmtDetails')
BEGIN
    CREATE TABLE [dbo].[tblOcrGrpAmtDetails] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [PatientId] INT NULL,
        [ProcCodeId] INT NULL,
        [GRPCode] NVARCHAR(50) NULL,
        [Rc_Amt] DECIMAL(18,2) NULL,
        [REM] NVARCHAR(500) NULL,
        [CreatedDate] DATETIME2 NULL,
        CONSTRAINT [PK_tblOcrGrpAmtDetails] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblOcrProREMDetails')
BEGIN
    CREATE TABLE [dbo].[tblOcrProREMDetails] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [OcrPatientId] INT NULL,
        [OcrProcCodeId] INT NULL,
        [REM] NVARCHAR(500) NULL,
        [CreatedDate] DATETIME2 NULL,
        CONSTRAINT [PK_tblOcrProREMDetails] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblOcrTotalClaimInfo')
BEGIN
    CREATE TABLE [dbo].[tblOcrTotalClaimInfo] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [OcrHeaderId] INT NULL,
        [OrgId] INT NULL,
        [ClaimCount] INT NULL,
        [Billed] DECIMAL(18,2) NULL,
        [Allowed] DECIMAL(18,2) NULL,
        [Deductible] DECIMAL(18,2) NULL,
        [CoInsurance] DECIMAL(18,2) NULL,
        [RC] DECIMAL(18,2) NULL,
        [ProviderPaid] DECIMAL(18,2) NULL,
        [Adjustment] DECIMAL(18,2) NULL,
        [CheckAmount] DECIMAL(18,2) NULL,
        [ProviderDiscount] DECIMAL(18,2) NULL,
        [OtherPlanPayment] DECIMAL(18,2) NULL,
        [CreatedDate] DATETIME2 NULL,
        CONSTRAINT [PK_tblOcrTotalClaimInfo] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'tblEOBGrpCodeRules')
BEGIN
    CREATE TABLE [dbo].[tblEOBGrpCodeRules] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [OrgId] INT NULL,
        [ExplanationCodeKey] NVARCHAR(200) NULL,
        [Description] NVARCHAR(1000) NULL,
        [Type] NVARCHAR(50) NULL,
        [CreatedDate] DATETIME2 NULL,
        CONSTRAINT [PK_tblEOBGrpCodeRules] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
END;
");

            // Create or alter view
            migrationBuilder.Sql(@"
CREATE OR ALTER VIEW [dbo].[vgetOcrUploadedDocsDetails] AS
SELECT 
    u.Id,
    u.OrgId,
    u.Check_EFT,
    u.Eob_Date,
    u.Source,
    u.CreatedDate,
    u.StatusId,
    u.Attachment,
    u.Tot_PaidAmt,
    s.Name AS StatusName,
    COALESCE(c.ColorCode, CASE WHEN u.StatusId = 1064 THEN '#dcfce7' WHEN u.StatusId = 1065 THEN '#fee2e2' ELSE '#f1f5f9' END) AS ColorCode,
    COALESCE(c.FontColor, CASE WHEN u.StatusId = 1064 THEN '#166534' WHEN u.StatusId = 1065 THEN '#991b1b' ELSE '#334155' END) AS FontColor
FROM [dbo].[tblUploadedOcrEobRecords] u
LEFT JOIN [PlatformSettings].[Statuses] s ON s.Id = u.StatusId
LEFT JOIN [PlatformSettings].[ProcessStatuses] ps ON ps.StatusId = u.StatusId
LEFT JOIN [PlatformSettings].[Colors] c ON c.Id = ps.ColorId;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP VIEW IF EXISTS [dbo].[vgetOcrUploadedDocsDetails];
DROP TABLE IF EXISTS [dbo].[tblEOBGrpCodeRules];
DROP TABLE IF EXISTS [dbo].[tblOcrTotalClaimInfo];
DROP TABLE IF EXISTS [dbo].[tblOcrProREMDetails];
DROP TABLE IF EXISTS [dbo].[tblOcrGrpAmtDetails];
DROP TABLE IF EXISTS [dbo].[tblOcrProcedureDetails];
DROP TABLE IF EXISTS [dbo].[tblOcrPatientDetail];
DROP TABLE IF EXISTS [dbo].[tblOCRDocHeaderInfo];
DROP TABLE IF EXISTS [dbo].[tblUploadedOcrEobRecords];
");
        }
    }
}
