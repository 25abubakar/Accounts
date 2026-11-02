using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261030170000_AddBillingModuleDomainAndProcedures")]
public sealed class AddBillingModuleDomainAndProcedures : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.tblPerson', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPerson (
        PersonId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblPerson PRIMARY KEY,
        TenantId INT NULL,
        FirstName NVARCHAR(200) NULL,
        LastName NVARCHAR(200) NULL,
        FullName NVARCHAR(400) NULL,
        PhoneNumber NVARCHAR(100) NULL,
        Email NVARCHAR(200) NULL,
        DOB DATETIME NULL,
        Age INT NULL,
        Gender BIT NULL,
        CreatedDate DATETIME NULL CONSTRAINT DF_tblPerson_CreatedDate DEFAULT (GETUTCDATE())
    );
END;

IF OBJECT_ID(N'dbo.tblPatient', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPatient (
        PatientId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblPatient PRIMARY KEY,
        TenantId INT NULL,
        OrgId INT NULL,
        OfficeId INT NULL,
        PatientNo NVARCHAR(200) NULL,
        PersonId INT NULL,
        PatientName NVARCHAR(200) NULL,
        CreatedDate DATETIME NULL CONSTRAINT DF_tblPatient_CreatedDate DEFAULT (GETUTCDATE()),
        CreatedBy NVARCHAR(200) NULL
    );
END;

IF OBJECT_ID(N'dbo.tblAppointment', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblAppointment (
        AppointmentId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblAppointment PRIMARY KEY,
        TenantId INT NULL,
        PatientId INT NULL,
        OrgId INT NULL,
        AppointmentDate NVARCHAR(50) NULL,
        DOS DATETIME NULL,
        StatusId INT NULL,
        AppStatusId INT NULL,
        OfficeId INT NULL,
        PtTypeId INT NULL,
        ProviderName NVARCHAR(200) NULL,
        InsuranceName NVARCHAR(200) NULL,
        IsClaimed BIT NULL CONSTRAINT DF_tblAppointment_IsClaimed DEFAULT (0),
        IsCreatedFromEob BIT NULL CONSTRAINT DF_tblAppointment_IsCreatedFromEob DEFAULT (0),
        CreatedDate DATETIME NULL CONSTRAINT DF_tblAppointment_CreatedDate DEFAULT (GETUTCDATE()),
        CreatedByUserId NVARCHAR(450) NULL
    );
END;

IF OBJECT_ID(N'dbo.tblInsPTClaims', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblInsPTClaims (
        ClaimId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblInsPTClaims PRIMARY KEY,
        TenantId INT NULL,
        AppointmentId INT NULL,
        PatientId INT NULL,
        OrgId INT NULL,
        EobCheckNo NVARCHAR(200) NULL,
        Attachment NVARCHAR(500) NULL,
        PatientResponsibility DECIMAL(18,2) NULL,
        TotalAllowed DECIMAL(18,2) NULL,
        TotalBilled DECIMAL(18,2) NULL,
        TotalPaid DECIMAL(18,2) NULL,
        StatusId INT NULL,
        EobStatusId INT NULL,
        IsCreatedFromEob BIT NULL,
        SubmittedDate DATETIME NULL,
        Remarks NVARCHAR(1000) NULL,
        CarrierId INT NULL,
        PolicyNumber NVARCHAR(200) NULL,
        CreatedDate DATETIME NULL CONSTRAINT DF_tblInsPTClaims_CreatedDate DEFAULT (GETUTCDATE()),
        CreatedByUserId NVARCHAR(450) NULL
    );
END;

IF OBJECT_ID(N'dbo.tblApptCodes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblApptCodes (
        ApptCodeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblApptCodes PRIMARY KEY,
        TenantId INT NULL,
        AppointmentId INT NULL,
        ClaimId INT NULL,
        PatientId INT NULL,
        OrgId INT NULL,
        ProcedureCodeId INT NULL,
        ProcedureCode NVARCHAR(50) NULL,
        DateOfService NVARCHAR(50) NULL,
        PolicyNo NVARCHAR(200) NULL,
        ClaimAmount DECIMAL(18,2) NULL,
        TotalClaimAmount DECIMAL(18,2) NULL,
        InsuranceRate DECIMAL(18,2) NULL,
        InsuranceAmount DECIMAL(18,2) NULL,
        EobAllowed DECIMAL(18,2) NULL,
        Paid DECIMAL(18,2) NULL,
        PatientResponsibility DECIMAL(18,2) NULL,
        CoInsurance DECIMAL(18,2) NULL,
        Deductible DECIMAL(18,2) NULL,
        Modifiers NVARCHAR(100) NULL,
        PolicyNumber NVARCHAR(200) NULL,
        FillSource NVARCHAR(100) NULL,
        IsUpload BIT NULL,
        IsCreatedFromEobs BIT NULL,
        LineEobStatusId INT NULL,
        Balance DECIMAL(18,2) NULL,
        CarrierId INT NULL,
        InsuranceCompanyId INT NULL,
        CreatedDate DATETIME NULL CONSTRAINT DF_tblApptCodes_CreatedDate DEFAULT (GETUTCDATE()),
        CreatedByUserId NVARCHAR(450) NULL
    );
END;

IF OBJECT_ID(N'dbo.tblClaimCodeAmountDetails', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblClaimCodeAmountDetails (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblClaimCodeAmountDetails PRIMARY KEY,
        TenantId INT NULL,
        ApptCodeId INT NULL,
        ClaimId INT NULL,
        OrgId INT NULL,
        EobCheckNo NVARCHAR(200) NULL,
        PaidAmount DECIMAL(18,2) NULL,
        PatientResponsibility DECIMAL(18,2) NULL,
        Remark NVARCHAR(500) NULL,
        IsClosed BIT NULL,
        IsReProcess BIT NULL,
        CreatedDate DATETIME NULL CONSTRAINT DF_tblClaimCodeAmountDetails_CreatedDate DEFAULT (GETUTCDATE())
    );
END;

IF OBJECT_ID(N'dbo.tblBillingRoznamcha', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblBillingRoznamcha (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblBillingRoznamcha PRIMARY KEY,
        TenantId INT NULL,
        OrganizationId INT NULL,
        Practice INT NULL,
        Ins_Name NVARCHAR(200) NULL,
        ChequeNo NVARCHAR(100) NULL,
        Amount DECIMAL(18,2) NULL,
        Trans_Date DATETIME NULL,
        StatusId INT NULL,
        Acc_Ref INT NULL,
        Ref NVARCHAR(100) NULL,
        isSettled BIT NULL CONSTRAINT DF_tblBillingRoznamcha_isSettled DEFAULT (0),
        CreatedBy NVARCHAR(200) NULL,
        CreatedDate DATETIME NULL CONSTRAINT DF_tblBillingRoznamcha_CreatedDate DEFAULT (GETUTCDATE()),
        UpdatedBy NVARCHAR(200) NULL,
        UpdatedDate DATETIME NULL
    );
END;

IF OBJECT_ID(N'dbo.tblOffices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblOffices (
        OfficeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblOffices PRIMARY KEY,
        TenantId INT NULL,
        OfficeName NVARCHAR(200) NULL
    );
END;

IF OBJECT_ID(N'dbo.tblPatientType', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPatientType (
        PtTypeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblPatientType PRIMARY KEY,
        TenantId INT NULL,
        PtType NVARCHAR(200) NULL
    );
END;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}

