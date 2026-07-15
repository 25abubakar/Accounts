using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantManagementMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DECLARE @ParentId INT = (
    SELECT TOP (1) Id FROM dbo.Menus
    WHERE Title = N'Accounts & Groups' AND ParentId IS NULL
    ORDER BY Id
);

IF EXISTS (SELECT 1 FROM dbo.Menus WHERE Route = N'/tenants')
BEGIN
    UPDATE dbo.Menus
    SET Title = N'Tenant Management', Icon = N'ShieldCheck',
        ParentId = @ParentId, SortOrder = 4, IsActive = 1
    WHERE Route = N'/tenants';
END
ELSE
BEGIN
    INSERT INTO dbo.Menus (Title, Icon, Route, ParentId, SortOrder, IsActive)
    VALUES (N'Tenant Management', N'ShieldCheck', N'/tenants', @ParentId, 4, 1);
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Keep the menu because an administrator may have customized it.
        }
    }
}
