using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260911140000_AddMenuAuthorityActions")]
public sealed class AddMenuAuthorityActions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.MenuAuthorityActions', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.MenuAuthorityActions
                (
                    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MenuAuthorityActions PRIMARY KEY,
                    MenuId          INT NOT NULL,
                    ActionCode      NVARCHAR(30) NOT NULL,
                    DisplayName     NVARCHAR(80) NOT NULL,
                    RankOrder       INT NOT NULL CONSTRAINT DF_MenuAuthorityActions_RankOrder DEFAULT(1),
                    SupportsPin     BIT NOT NULL CONSTRAINT DF_MenuAuthorityActions_SupportsPin DEFAULT(0),
                    PinProcessName  NVARCHAR(80) NULL,
                    IsActive        BIT NOT NULL CONSTRAINT DF_MenuAuthorityActions_IsActive DEFAULT(1),
                    CONSTRAINT FK_MenuAuthorityActions_Menus FOREIGN KEY (MenuId) REFERENCES dbo.Menus(Id)
                );

                CREATE UNIQUE INDEX IX_MenuAuthorityActions_MenuId_ActionCode
                    ON dbo.MenuAuthorityActions (MenuId, ActionCode);
            END

            -- Payroll multi-stage pipeline
            INSERT INTO dbo.MenuAuthorityActions (MenuId, ActionCode, DisplayName, RankOrder, SupportsPin, PinProcessName, IsActive)
            SELECT m.Id, v.ActionCode, v.DisplayName, v.RankOrder, v.SupportsPin, v.PinProcessName, 1
            FROM dbo.Menus m
            CROSS APPLY (VALUES
                (N'CREATE',  N'Create payroll',   1, 0, CAST(NULL AS NVARCHAR(80))),
                (N'VERIFY',  N'Verify payroll',   2, 0, CAST(NULL AS NVARCHAR(80))),
                (N'APPROVE', N'Final approval',   3, 1, N'PayrollApproval'),
                (N'PAY',     N'Salary dispatch',  4, 0, CAST(NULL AS NVARCHAR(80)))
            ) v(ActionCode, DisplayName, RankOrder, SupportsPin, PinProcessName)
            WHERE m.IsActive = 1 AND m.Route = N'/pay-allowances/payroll'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.MenuAuthorityActions x
                  WHERE x.MenuId = m.Id AND x.ActionCode = v.ActionCode);

            -- Deduction approval + overtime
            INSERT INTO dbo.MenuAuthorityActions (MenuId, ActionCode, DisplayName, RankOrder, SupportsPin, PinProcessName, IsActive)
            SELECT m.Id, v.ActionCode, v.DisplayName, v.RankOrder, v.SupportsPin, v.PinProcessName, 1
            FROM dbo.Menus m
            CROSS APPLY (VALUES
                (N'APPROVE',  N'Approve adjustment', 1, 1, N'DeductionAdjustment'),
                (N'OVERTIME', N'Approve overtime',   2, 1, N'DeductionOvertime')
            ) v(ActionCode, DisplayName, RankOrder, SupportsPin, PinProcessName)
            WHERE m.IsActive = 1 AND m.Route = N'/attendance/deduction'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.MenuAuthorityActions x
                  WHERE x.MenuId = m.Id AND x.ActionCode = v.ActionCode);

            -- Camera attendance review
            INSERT INTO dbo.MenuAuthorityActions (MenuId, ActionCode, DisplayName, RankOrder, SupportsPin, PinProcessName, IsActive)
            SELECT m.Id, N'APPROVE', N'Camera attendance review', 1, 1, N'CameraAttendance', 1
            FROM dbo.Menus m
            WHERE m.IsActive = 1 AND m.Route = N'/attendance/types/camera'
              AND NOT EXISTS (
                  SELECT 1 FROM dbo.MenuAuthorityActions x
                  WHERE x.MenuId = m.Id AND x.ActionCode = N'APPROVE');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.MenuAuthorityActions', N'U') IS NOT NULL
                DROP TABLE dbo.MenuAuthorityActions;
            """);
    }
}
