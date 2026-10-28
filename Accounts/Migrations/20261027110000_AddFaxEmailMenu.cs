using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261027110000_AddFaxEmailMenu")]
public sealed class AddFaxEmailMenu : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        SET NOCOUNT ON;
        DECLARE @parent int=(SELECT TOP(1) Id FROM dbo.Menus
            WHERE ParentId IS NULL AND Title IN(N'E-mail',N'Email')
            ORDER BY CASE WHEN Title=N'E-mail' THEN 0 ELSE 1 END,Id);
        IF @parent IS NULL THROW 50000,'The E-mail parent menu must exist before adding Fax.',1;

        DECLARE @fax int=(SELECT TOP(1) Id FROM dbo.Menus WHERE Route=N'/email/fax'
            OR (ParentId=@parent AND Title=N'Fax') ORDER BY Id);
        IF @fax IS NULL
        BEGIN
            INSERT dbo.Menus(Title,Icon,Route,ParentId,SortOrder,IsActive)
            VALUES(N'Fax',N'Printer',N'/email/fax',@parent,2,1);
            SET @fax=SCOPE_IDENTITY();
        END
        ELSE
            UPDATE dbo.Menus SET Title=N'Fax',Icon=N'Printer',Route=N'/email/fax',ParentId=@parent,SortOrder=2,IsActive=1 WHERE Id=@fax;

        DECLARE @base nvarchar(100)=CONCAT(N'MENU_',@fax),@view nvarchar(100)=CONCAT(N'MENU_',@fax,N'_VIEW');
        INSERT dbo.Features(FeatureKey,FeatureName,Module,Description,CreatedDate)
        SELECT value.FeatureKey,value.FeatureName,N'Menu',N'Open the Fax screen.',SYSUTCDATETIME()
        FROM (VALUES(@base,N'Fax'),(@view,N'Fax - View'))value(FeatureKey,FeatureName)
        WHERE NOT EXISTS(SELECT 1 FROM dbo.Features existing WHERE existing.FeatureKey=value.FeatureKey);
        INSERT dbo.MenuPermissions(MenuId,PermissionId)
        SELECT @fax,feature.PermissionId FROM dbo.Features feature WHERE feature.FeatureKey IN(@base,@view)
            AND NOT EXISTS(SELECT 1 FROM dbo.MenuPermissions existing WHERE existing.MenuId=@fax AND existing.PermissionId=feature.PermissionId);

        -- Match the existing E-mail workspace's tenant ceiling. No role/person grants are added.
        INSERT dbo.TenantMenuPermissions(TenantId,MenuId,IsAllow,CanView,CanAdd,CanEdit,CanDelete,GrantedOnUtc,GrantedByUserId)
        SELECT tenant.Id,@fax,1,1,0,0,0,SYSUTCDATETIME(),N'System: Fax menu'
        FROM dbo.Tenants tenant
        WHERE EXISTS(SELECT 1 FROM dbo.TenantMenuPermissions access JOIN dbo.Menus menu ON menu.Id=access.MenuId
            WHERE access.TenantId=tenant.Id AND access.IsAllow=1 AND access.CanView=1
                AND (menu.Id=@parent OR menu.Route=N'/email/lt-email'))
          AND NOT EXISTS(SELECT 1 FROM dbo.TenantMenuPermissions existing WHERE existing.TenantId=tenant.Id AND existing.MenuId=@fax);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE dbo.Menus SET IsActive=0 WHERE Route=N'/email/fax';
        """);
}
