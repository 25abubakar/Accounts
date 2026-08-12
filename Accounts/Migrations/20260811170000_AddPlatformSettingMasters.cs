using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260811170000_AddPlatformSettingMasters")]
public sealed class AddPlatformSettingMasters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF SCHEMA_ID(N'PlatformSettings') IS NULL EXEC(N'CREATE SCHEMA PlatformSettings');

            IF OBJECT_ID(N'PlatformSettings.Actions', N'U') IS NULL
            CREATE TABLE PlatformSettings.Actions (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PlatformSettings_Actions PRIMARY KEY,
                TenantId int NOT NULL,
                Name nvarchar(150) NOT NULL,
                IsActive bit NOT NULL CONSTRAINT DF_PlatformSettings_Actions_IsActive DEFAULT(1),
                CreatedOnUtc datetime2 NOT NULL CONSTRAINT DF_PlatformSettings_Actions_Created DEFAULT(SYSUTCDATETIME()),
                ModifiedOnUtc datetime2 NULL,
                CreatedByUserId nvarchar(450) NULL,
                ModifiedByUserId nvarchar(450) NULL,
                CONSTRAINT FK_PlatformSettings_Actions_Tenants FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id),
                CONSTRAINT UX_PlatformSettings_Actions_Tenant_Name UNIQUE(TenantId, Name)
            );

            IF OBJECT_ID(N'PlatformSettings.Statuses', N'U') IS NULL
            CREATE TABLE PlatformSettings.Statuses (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PlatformSettings_Statuses PRIMARY KEY,
                TenantId int NOT NULL,
                Name nvarchar(150) NOT NULL,
                IsActive bit NOT NULL CONSTRAINT DF_PlatformSettings_Statuses_IsActive DEFAULT(1),
                CreatedOnUtc datetime2 NOT NULL CONSTRAINT DF_PlatformSettings_Statuses_Created DEFAULT(SYSUTCDATETIME()),
                ModifiedOnUtc datetime2 NULL,
                CreatedByUserId nvarchar(450) NULL,
                ModifiedByUserId nvarchar(450) NULL,
                CONSTRAINT FK_PlatformSettings_Statuses_Tenants FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id),
                CONSTRAINT UX_PlatformSettings_Statuses_Tenant_Name UNIQUE(TenantId, Name)
            );

            IF OBJECT_ID(N'PlatformSettings.Colors', N'U') IS NULL
            CREATE TABLE PlatformSettings.Colors (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PlatformSettings_Colors PRIMARY KEY,
                TenantId int NOT NULL,
                ColorCode nvarchar(9) NOT NULL,
                FontColor nvarchar(9) NULL,
                IsActive bit NOT NULL CONSTRAINT DF_PlatformSettings_Colors_IsActive DEFAULT(1),
                CreatedOnUtc datetime2 NOT NULL CONSTRAINT DF_PlatformSettings_Colors_Created DEFAULT(SYSUTCDATETIME()),
                ModifiedOnUtc datetime2 NULL,
                CreatedByUserId nvarchar(450) NULL,
                ModifiedByUserId nvarchar(450) NULL,
                CONSTRAINT FK_PlatformSettings_Colors_Tenants FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id),
                CONSTRAINT UX_PlatformSettings_Colors_Tenant_Code UNIQUE(TenantId, ColorCode)
            );

            -- Seed only the existing LAL companies. Every tenant owns and may
            -- subsequently edit its own independent master data.
            INSERT INTO PlatformSettings.Actions(TenantId, Name, IsActive, CreatedByUserId)
            SELECT tenant.Id, seed.Name, 1, N'System: initial settings import'
            FROM dbo.Tenants tenant
            CROSS APPLY (VALUES
                (N'Attendance'),(N'test6'),(N'test1'),(N'StaffFinancial'),
                (N'ToDo'),(N'Meal'),(N'Reminder')
            ) seed(Name)
            WHERE tenant.TenantName LIKE N'%Lal%'
              AND NOT EXISTS (SELECT 1 FROM PlatformSettings.Actions existing WHERE existing.TenantId=tenant.Id AND existing.Name=seed.Name);

            INSERT INTO PlatformSettings.Statuses(TenantId, Name, IsActive, CreatedByUserId)
            SELECT tenant.Id, seed.Name, seed.IsActive, N'System: initial settings import'
            FROM dbo.Tenants tenant
            CROSS APPLY (VALUES
                (N'Present',0),(N'T-Present',0),(N'1 Hr Late',0),(N'2 Hr Late',0),(N'Absent',0),
                (N'1 Hr Early',0),(N'2 Hr Early',0),(N'Holiday',0),(N'Day Off',0),(N'test',1),
                (N'Working Day',0),(N'Not Required',0),(N'Meeting',1),(N'Short Leave',1),
                (N'Approved',1),(N'Verified',1),(N'Recommend',1),(N'Not Approved',1),
                (N'Completed',1),(N'in Progress',1)
            ) seed(Name, IsActive)
            WHERE tenant.TenantName LIKE N'%Lal%'
              AND NOT EXISTS (SELECT 1 FROM PlatformSettings.Statuses existing WHERE existing.TenantId=tenant.Id AND existing.Name=seed.Name);

            INSERT INTO PlatformSettings.Colors(TenantId, ColorCode, FontColor, IsActive, CreatedByUserId)
            SELECT tenant.Id, seed.ColorCode, seed.FontColor, seed.IsActive, N'System: initial settings import'
            FROM dbo.Tenants tenant
            CROSS APPLY (VALUES
                (N'#C0062C',N'#FFFFFF',0),(N'#D30731',N'#FFFFFF',0),(N'#E50533',N'#FFFFFF',0),(N'#F70537',N'#FFFFFF',0),
                (N'#1DA505',N'#FFFFFF',0),(N'#23C008',N'#FFFFFF',0),(N'#27DE07',NULL,0),(N'#2BFA06',N'#000000',1),
                (N'#0F07F2',N'#FFFFFF',1),(N'#377BF0',N'#FFFFFF',1),(N'#0EB2ED',N'#FFFFFF',1),(N'#05F8F3',NULL,1),
                (N'#F7BF05',NULL,1),(N'#F7F305',NULL,1),(N'#F2F07E',NULL,1),(N'#E5FC72',NULL,1),
                (N'#AF05F7',N'#FFFFFF',1),(N'#C457F2',N'#FFFFFF',1),(N'#D388F2',N'#FFFFFF',1),(N'#E4BCF5',NULL,1),
                (N'#BFBFB6',N'#FFFFFF',1),(N'#D1CFC9',N'#FFFFFF',1),(N'#E3E1DC',N'#000000',1),(N'#AAF2EC',NULL,1),
                (N'#76E8DF',NULL,1),(N'#1AE60B',N'#FFFFFF',1),(N'#08A127',N'#FFFFFF',1),(N'#BBBF37',NULL,1)
            ) seed(ColorCode, FontColor, IsActive)
            WHERE tenant.TenantName LIKE N'%Lal%'
              AND NOT EXISTS (SELECT 1 FROM PlatformSettings.Colors existing WHERE existing.TenantId=tenant.Id AND existing.ColorCode=seed.ColorCode);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'PlatformSettings.Colors', N'U') IS NOT NULL DROP TABLE PlatformSettings.Colors;
            IF OBJECT_ID(N'PlatformSettings.Statuses', N'U') IS NOT NULL DROP TABLE PlatformSettings.Statuses;
            IF OBJECT_ID(N'PlatformSettings.Actions', N'U') IS NOT NULL DROP TABLE PlatformSettings.Actions;
            """);
    }
}
