using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260811180000_AddActionStatusAndStatusValues")]
public sealed class AddActionStatusAndStatusValues : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'PlatformSettings.ActionStatuses', N'U') IS NULL
            CREATE TABLE PlatformSettings.ActionStatuses (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PlatformSettings_ActionStatuses PRIMARY KEY,
                TenantId int NOT NULL,
                ActionId int NOT NULL,
                StatusId int NOT NULL,
                ColorId int NULL,
                CreatedOnUtc datetime2 NOT NULL CONSTRAINT DF_PlatformSettings_ActionStatuses_Created DEFAULT(SYSUTCDATETIME()),
                ModifiedOnUtc datetime2 NULL,
                CreatedByUserId nvarchar(450) NULL,
                ModifiedByUserId nvarchar(450) NULL,
                CONSTRAINT FK_PlatformSettings_ActionStatuses_Tenants FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id),
                CONSTRAINT FK_PlatformSettings_ActionStatuses_Actions FOREIGN KEY(ActionId) REFERENCES PlatformSettings.Actions(Id),
                CONSTRAINT FK_PlatformSettings_ActionStatuses_Statuses FOREIGN KEY(StatusId) REFERENCES PlatformSettings.Statuses(Id),
                CONSTRAINT FK_PlatformSettings_ActionStatuses_Colors FOREIGN KEY(ColorId) REFERENCES PlatformSettings.Colors(Id),
                CONSTRAINT UX_PlatformSettings_ActionStatuses_Tenant_Action_Status UNIQUE(TenantId, ActionId, StatusId)
            );

            IF OBJECT_ID(N'PlatformSettings.StatusCrDbValues', N'U') IS NULL
            CREATE TABLE PlatformSettings.StatusCrDbValues (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PlatformSettings_StatusCrDbValues PRIMARY KEY,
                TenantId int NOT NULL,
                StatusId int NOT NULL,
                CrValue nvarchar(150) NOT NULL,
                DbValue nvarchar(150) NOT NULL,
                CreatedOnUtc datetime2 NOT NULL CONSTRAINT DF_PlatformSettings_StatusCrDbValues_Created DEFAULT(SYSUTCDATETIME()),
                ModifiedOnUtc datetime2 NULL,
                CreatedByUserId nvarchar(450) NULL,
                ModifiedByUserId nvarchar(450) NULL,
                CONSTRAINT FK_PlatformSettings_StatusCrDbValues_Tenants FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id),
                CONSTRAINT FK_PlatformSettings_StatusCrDbValues_Statuses FOREIGN KEY(StatusId) REFERENCES PlatformSettings.Statuses(Id),
                CONSTRAINT UX_PlatformSettings_StatusCrDbValues_Tenant_Status UNIQUE(TenantId, StatusId)
            );

            -- The legacy Action Status list uses these additional statuses.
            INSERT INTO PlatformSettings.Statuses(TenantId, Name, IsActive, CreatedByUserId)
            SELECT tenant.Id, seed.Name, 1, N'System: Action Status import'
            FROM dbo.Tenants tenant
            CROSS APPLY (VALUES
                (N'Canceled'),(N'Pending'),(N'Re Scheduled'),(N'Confirmed'),(N'Short Hrs'),
                (N'Entitled'),(N'Not Entitled'),(N'Annual Holiday'),(N'Leave'),(N'Active'),
                (N'In Active'),(N'Opr'),(N'App'),(N'in-Pro'),(N'Cre'),(N'Re App'),(N'Un-Pro'),(N'OA')
            ) seed(Name)
            WHERE tenant.TenantName LIKE N'%Lal%'
              AND NOT EXISTS (SELECT 1 FROM PlatformSettings.Statuses existing WHERE existing.TenantId=tenant.Id AND existing.Name=seed.Name);

            ;WITH Mapping(ActionName, ColorCode, StatusName) AS (
                SELECT * FROM (VALUES
                    (N'Attendance',N'#1DA505',N'Present'),
                    (N'Attendance',N'#2BFA06',N'T-Present'),
                    (N'Attendance',N'#E50533',N'1 Hr Late'),
                    (N'Attendance',N'#E50533',N'2 Hr Late'),
                    (N'Attendance',N'#F70537',N'1 Hr Early'),
                    (N'Attendance',N'#E50533',N'2 Hr Early'),
                    (N'Attendance',N'#E50533',N'Absent'),
                    (N'Attendance',N'#0EB2ED',N'Day Off'),
                    (N'Attendance',N'#F7F305',N'Not Required'),
                    (N'Attendance',N'#C457F2',N'Holiday'),
                    (N'StaffFinancial',N'#2BFA06',N'Approved'),
                    (N'StaffFinancial',N'#AF05F7',N'Verified'),
                    (N'StaffFinancial',N'#05F8F3',N'Recommend'),
                    (N'StaffFinancial',N'#D30731',N'Not Approved'),
                    (N'ToDo',NULL,N'Completed'),
                    (N'ToDo',NULL,N'in Progress'),
                    (N'ToDo',NULL,N'Canceled'),
                    (N'ToDo',NULL,N'Pending'),
                    (N'ToDo',NULL,N'Re Scheduled'),
                    (N'ToDo',NULL,N'Confirmed'),
                    (N'Attendance',N'#F7F305',N'Short Hrs'),
                    (N'Meal',N'#0EB2ED',N'Entitled'),
                    (N'Meal',N'#C457F2',N'Not Entitled'),
                    (N'Attendance',N'#F7BF05',N'Annual Holiday'),
                    (N'Attendance',N'#C457F2',N'Leave'),
                    (N'Attendance',N'#F7BF05',N'Short Leave'),
                    (N'Reminder',N'#27DE07',N'Active'),
                    (N'Reminder',N'#E5FC72',N'In Active'),
                    (N'Reminder',N'#27DE07',N'Opr'),
                    (N'Reminder',N'#76E8DF',N'App'),
                    (N'Reminder',N'#E5FC72',N'in-Pro'),
                    (N'Reminder',N'#08A127',N'Cre'),
                    (N'Reminder',N'#BBBF37',N'Re App'),
                    (N'Reminder',N'#BFBFB6',N'Un-Pro'),
                    (N'Reminder',N'#C457F2',N'OA')
                ) value(ActionName, ColorCode, StatusName)
            )
            INSERT INTO PlatformSettings.ActionStatuses(TenantId, ActionId, StatusId, ColorId, CreatedByUserId)
            SELECT tenant.Id, actionRow.Id, statusRow.Id, colorRow.Id, N'System: initial Action Status import'
            FROM dbo.Tenants tenant
            CROSS JOIN Mapping mapping
            JOIN PlatformSettings.Actions actionRow ON actionRow.TenantId=tenant.Id AND actionRow.Name=mapping.ActionName
            JOIN PlatformSettings.Statuses statusRow ON statusRow.TenantId=tenant.Id AND statusRow.Name=mapping.StatusName
            LEFT JOIN PlatformSettings.Colors colorRow ON colorRow.TenantId=tenant.Id AND colorRow.ColorCode=mapping.ColorCode
            WHERE tenant.TenantName LIKE N'%Lal%'
              AND (mapping.ColorCode IS NULL OR colorRow.Id IS NOT NULL)
              AND NOT EXISTS (
                  SELECT 1 FROM PlatformSettings.ActionStatuses existing
                  WHERE existing.TenantId=tenant.Id AND existing.ActionId=actionRow.Id AND existing.StatusId=statusRow.Id
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'PlatformSettings.StatusCrDbValues', N'U') IS NOT NULL DROP TABLE PlatformSettings.StatusCrDbValues;
            IF OBJECT_ID(N'PlatformSettings.ActionStatuses', N'U') IS NOT NULL DROP TABLE PlatformSettings.ActionStatuses;
            """);
    }
}
