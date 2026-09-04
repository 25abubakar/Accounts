using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Staff monthly EOBI contribution table + EOBI Settings menu
/// (monthly workspace stays on /pay-allowances/eobi).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260904140000_AddStaffMonthlyEobi")]
public sealed class AddStaffMonthlyEobi : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.StaffMonthlyEobis', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.StaffMonthlyEobis
                (
                    Id              bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_StaffMonthlyEobis PRIMARY KEY,
                    TenantId        int NOT NULL,
                    PersonId        uniqueidentifier NOT NULL,
                    StaffId         uniqueidentifier NOT NULL,
                    StaffNumber     nvarchar(50) NULL,
                    FullName        nvarchar(200) NOT NULL,
                    Department      nvarchar(200) NULL,
                    DateOfJoining   date NULL,
                    EobiRef         nvarchar(80) NULL,
                    SalaryBase      decimal(18,2) NOT NULL CONSTRAINT DF_StaffMonthlyEobis_SalaryBase DEFAULT (0),
                    CompanyShare    decimal(18,2) NOT NULL CONSTRAINT DF_StaffMonthlyEobis_CompanyShare DEFAULT (0),
                    StaffShare      decimal(18,2) NOT NULL CONSTRAINT DF_StaffMonthlyEobis_StaffShare DEFAULT (0),
                    TotalAmount     decimal(18,2) NOT NULL CONSTRAINT DF_StaffMonthlyEobis_TotalAmount DEFAULT (0),
                    [Month]         int NOT NULL,
                    [Year]          int NOT NULL,
                    Remarks         nvarchar(500) NULL,
                    IsApproved      bit NOT NULL CONSTRAINT DF_StaffMonthlyEobis_IsApproved DEFAULT (0),
                    IsPaid          bit NOT NULL CONSTRAINT DF_StaffMonthlyEobis_IsPaid DEFAULT (0),
                    CreatedOnUtc    datetime2 NOT NULL CONSTRAINT DF_StaffMonthlyEobis_CreatedOnUtc DEFAULT (SYSUTCDATETIME()),
                    UpdatedOnUtc    datetime2 NULL,
                    CONSTRAINT FK_StaffMonthlyEobis_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_StaffMonthlyEobis_Persons FOREIGN KEY (PersonId) REFERENCES dbo.Persons(PersonId)
                );

                CREATE UNIQUE INDEX IX_StaffMonthlyEobis_TenantId_PersonId_Year_Month
                    ON dbo.StaffMonthlyEobis (TenantId, PersonId, [Year], [Month]);

                CREATE INDEX IX_StaffMonthlyEobis_TenantId_Year_Month
                    ON dbo.StaffMonthlyEobis (TenantId, [Year], [Month]);
            END;
            """);

        migrationBuilder.Sql(
            """
            DECLARE @ParentId int =
            (
                SELECT TOP (1) Id
                FROM dbo.Menus
                WHERE ParentId IS NULL
                  AND Title IN (N'Pay & Allowances', N'Pay And Allowances')
                ORDER BY Id
            );

            IF @ParentId IS NULL
                RETURN;

            DECLARE @SettingsMenuId int =
            (
                SELECT TOP (1) Id FROM dbo.Menus WHERE Route = N'/pay-allowances/eobi-settings' ORDER BY Id
            );

            IF @SettingsMenuId IS NULL
            BEGIN
                DECLARE @SortOrder int =
                (
                    SELECT ISNULL(MAX(SortOrder), 5) + 1
                    FROM dbo.Menus
                    WHERE ParentId = @ParentId
                );

                INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
                VALUES (N'EOBI Settings', N'Settings', N'/pay-allowances/eobi-settings', @ParentId, @SortOrder, 1);

                SET @SettingsMenuId = SCOPE_IDENTITY();
            END
            ELSE
            BEGIN
                UPDATE dbo.Menus
                SET Title = N'EOBI Settings',
                    Icon = N'Settings',
                    ParentId = @ParentId,
                    IsActive = 1
                WHERE Id = @SettingsMenuId;
            END;

            INSERT INTO dbo.Features (FeatureKey, FeatureName, Module, Description, CreatedDate)
            SELECT CONCAT(N'MENU_', @SettingsMenuId, s.Suffix),
                   CONCAT(N'EOBI Settings', s.DisplayName),
                   N'Pay & Allowances',
                   CONCAT(s.ActionName, N' EOBI Settings'),
                   SYSUTCDATETIME()
            FROM (VALUES
                (N'', N'', N'Open'),
                (N'_VIEW', N' - View', N'View'),
                (N'_ADD', N' - Add', N'Add'),
                (N'_EDIT', N' - Edit', N'Edit'),
                (N'_DELETE', N' - Delete', N'Delete')
            ) s(Suffix, DisplayName, ActionName)
            WHERE NOT EXISTS (
                SELECT 1 FROM dbo.Features f
                WHERE f.FeatureKey = CONCAT(N'MENU_', @SettingsMenuId, s.Suffix)
            );

            INSERT INTO dbo.MenuPermissions (MenuId, PermissionId)
            SELECT @SettingsMenuId, f.PermissionId
            FROM dbo.Features f
            WHERE f.FeatureKey IN (
                CONCAT(N'MENU_', @SettingsMenuId),
                CONCAT(N'MENU_', @SettingsMenuId, N'_VIEW'),
                CONCAT(N'MENU_', @SettingsMenuId, N'_ADD'),
                CONCAT(N'MENU_', @SettingsMenuId, N'_EDIT'),
                CONCAT(N'MENU_', @SettingsMenuId, N'_DELETE')
            )
            AND NOT EXISTS (
                SELECT 1 FROM dbo.MenuPermissions mp
                WHERE mp.MenuId = @SettingsMenuId AND mp.PermissionId = f.PermissionId
            );

            DECLARE @EobiMenuId int =
            (
                SELECT TOP (1) Id FROM dbo.Menus WHERE Route = N'/pay-allowances/eobi' AND IsActive = 1 ORDER BY Id
            );

            IF @EobiMenuId IS NOT NULL
            BEGIN
                INSERT INTO dbo.TenantMenuPermissions
                    (TenantId, MenuId, IsAllow, CanView, CanAdd, CanEdit, CanDelete, GrantedOnUtc, GrantedByUserId)
                SELECT tmp.TenantId,
                       @SettingsMenuId,
                       tmp.IsAllow,
                       tmp.CanView,
                       tmp.CanAdd,
                       tmp.CanEdit,
                       tmp.CanDelete,
                       SYSUTCDATETIME(),
                       tmp.GrantedByUserId
                FROM dbo.TenantMenuPermissions tmp
                WHERE tmp.MenuId = @EobiMenuId
                  AND NOT EXISTS (
                      SELECT 1 FROM dbo.TenantMenuPermissions x
                      WHERE x.TenantId = tmp.TenantId AND x.MenuId = @SettingsMenuId
                  );
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @SettingsMenuId int =
            (
                SELECT TOP (1) Id FROM dbo.Menus WHERE Route = N'/pay-allowances/eobi-settings' ORDER BY Id
            );

            IF @SettingsMenuId IS NOT NULL
            BEGIN
                DELETE FROM dbo.TenantMenuPermissions WHERE MenuId = @SettingsMenuId;
                DELETE FROM dbo.MenuPermissions WHERE MenuId = @SettingsMenuId;
                DELETE FROM dbo.Features
                WHERE FeatureKey LIKE CONCAT(N'MENU_', @SettingsMenuId, N'%');
                DELETE FROM dbo.Menus WHERE Id = @SettingsMenuId;
            END;

            IF OBJECT_ID(N'dbo.StaffMonthlyEobis', N'U') IS NOT NULL
                DROP TABLE dbo.StaffMonthlyEobis;
            """);
    }
}
