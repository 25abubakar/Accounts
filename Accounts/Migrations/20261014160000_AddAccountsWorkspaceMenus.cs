using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

/// <summary>
/// Persists the Accounts parent and its operational child routes.
/// Tenant access is deliberately not auto-granted; it remains managed by the existing RBAC screens.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261014160000_AddAccountsWorkspaceMenus")]
public sealed class AddAccountsWorkspaceMenus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        """
        SET NOCOUNT ON;
        DECLARE @ParentId int = (SELECT TOP (1) Id FROM dbo.Menus WHERE ParentId IS NULL AND Title = N'Accounts' ORDER BY Id);
        IF @ParentId IS NULL
        BEGIN
            INSERT dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
            VALUES (N'Accounts', N'ReceiptText', NULL, NULL, 81, 1);
            SET @ParentId = SCOPE_IDENTITY();
        END
        ELSE UPDATE dbo.Menus SET Icon=N'ReceiptText', Route=NULL, SortOrder=81, IsActive=1 WHERE Id=@ParentId;

        DECLARE @Seed table (Title nvarchar(100), Icon nvarchar(100), Route nvarchar(300), SortOrder int);
        INSERT @Seed VALUES
          (N'Payment (ROZ)', N'DollarSign', N'/accounts/payment-roz', 1),
          (N'Receipt (ROZ)', N'ReceiptText', N'/accounts/receipt-roz', 2),
          (N'Roznamcha Update', N'FileCog', N'/accounts/roznamcha-update', 3),
          (N'Reports', N'BarChart3', N'/accounts/reports', 4),
          (N'Report Filter', N'SlidersHorizontal', N'/accounts/report-filter', 5),
          (N'Daily Updates', N'CalendarRange', N'/accounts/daily-updates', 6),
          (N'Bank Account Report', N'Files', N'/accounts/bank-account-report', 7),
          (N'Show record', N'LayoutGrid', N'/accounts/show-record', 8);

        UPDATE menu SET menu.Title=seed.Title, menu.Icon=seed.Icon, menu.ParentId=@ParentId,
            menu.SortOrder=seed.SortOrder, menu.IsActive=1
        FROM dbo.Menus menu JOIN @Seed seed ON seed.Route=menu.Route;

        INSERT dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
        SELECT seed.Title, seed.Icon, seed.Route, @ParentId, seed.SortOrder, 1
        FROM @Seed seed
        WHERE NOT EXISTS (SELECT 1 FROM dbo.Menus menu WHERE menu.Route=seed.Route);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        """
        UPDATE dbo.Menus SET IsActive=0
        WHERE Route IN
          (N'/accounts/payment-roz',N'/accounts/receipt-roz',N'/accounts/roznamcha-update',N'/accounts/reports',
           N'/accounts/report-filter',N'/accounts/daily-updates',N'/accounts/bank-account-report',N'/accounts/show-record');
        """);
}
