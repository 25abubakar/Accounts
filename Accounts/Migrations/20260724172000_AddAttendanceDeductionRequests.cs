using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    public partial class AddAttendanceDeductionRequests : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'dbo.AttendanceDeductionRequests', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.AttendanceDeductionRequests
                    (
                        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttendanceDeductionRequests PRIMARY KEY,
                        TenantId int NOT NULL,
                        RegNo nvarchar(50) NULL,
                        Name nvarchar(200) NOT NULL,
                        UserId nvarchar(100) NOT NULL,
                        DateOfBirth date NULL,
                        Phone nvarchar(50) NULL,
                        Email nvarchar(256) NULL,
                        Office nvarchar(150) NULL,
                        Department nvarchar(150) NULL,
                        Designation nvarchar(150) NULL,
                        Classification nvarchar(100) NULL,
                        Routing nvarchar(150) NULL,
                        Authority nvarchar(150) NULL,
                        Subject nvarchar(250) NULL,
                        DocumentName nvarchar(260) NULL,
                        DeductionMonth int NOT NULL,
                        DeductionYear int NOT NULL,
                        ActionRouting nvarchar(150) NULL,
                        ActionName nvarchar(100) NULL,
                        Comments nvarchar(1000) NULL,
                        CreatedByUserId nvarchar(450) NULL,
                        CreatedDate datetime2 NOT NULL CONSTRAINT DF_AttendanceDeductionRequests_CreatedDate DEFAULT(SYSUTCDATETIME()),
                        CONSTRAINT FK_AttendanceDeductionRequests_Tenants FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id),
                        CONSTRAINT CK_AttendanceDeductionRequests_Month CHECK(DeductionMonth BETWEEN 1 AND 12),
                        CONSTRAINT CK_AttendanceDeductionRequests_Year CHECK(DeductionYear BETWEEN 2000 AND 2100)
                    );
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AttendanceDeductionRequests_Tenant_Period' AND object_id = OBJECT_ID(N'dbo.AttendanceDeductionRequests'))
                    CREATE INDEX IX_AttendanceDeductionRequests_Tenant_Period ON dbo.AttendanceDeductionRequests(TenantId, DeductionYear, DeductionMonth);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS dbo.AttendanceDeductionRequests;");
        }
    }
}
