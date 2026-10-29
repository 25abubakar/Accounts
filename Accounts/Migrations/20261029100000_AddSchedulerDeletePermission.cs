using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261029100000_AddSchedulerDeletePermission")]
public sealed class AddSchedulerDeletePermission : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        SET NOCOUNT ON;
        DECLARE @menuId int=(SELECT TOP(1) Id FROM dbo.Menus WHERE Route=N'/scheduler/jobs' AND IsActive=1);
        IF @menuId IS NULL THROW 51000,'The Scheduler Jobs menu is missing.',1;

        DECLARE @featureKey nvarchar(100)=CONCAT(N'MENU_',@menuId,N'_DELETE');
        IF NOT EXISTS(SELECT 1 FROM dbo.Features WHERE FeatureKey=@featureKey)
            INSERT dbo.Features(FeatureKey,FeatureName,Module,Description,CreatedDate)
            VALUES(@featureKey,N'Jobs - Delete',N'Menu',N'Scheduler: Jobs - Delete',SYSUTCDATETIME());

        DECLARE @permissionId int=(SELECT PermissionId FROM dbo.Features WHERE FeatureKey=@featureKey);
        IF NOT EXISTS(SELECT 1 FROM dbo.MenuPermissions WHERE MenuId=@menuId AND PermissionId=@permissionId)
            INSERT dbo.MenuPermissions(MenuId,PermissionId) VALUES(@menuId,@permissionId);
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep the permission definition because assigned access rows may reference it.
    }
}
