using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261027070000_AddSchedulerWorkspace")]
public sealed class AddSchedulerWorkspace : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        SET NOCOUNT ON;
        DECLARE @parent int=(SELECT TOP(1) Id FROM dbo.Menus WHERE ParentId IS NULL AND Title=N'Scheduler');
        IF @parent IS NULL
        BEGIN
            INSERT dbo.Menus(Title,Icon,Route,ParentId,SortOrder,IsActive) VALUES(N'Scheduler',N'CalendarClock',NULL,NULL,90,1);
            SET @parent=SCOPE_IDENTITY();
        END;
        DECLARE @children table(Title nvarchar(100),Icon nvarchar(100),Route nvarchar(200),SortOrder int);
        INSERT @children VALUES(N'Jobs',N'CalendarClock',N'/scheduler/jobs',1),(N'Upcoming Executions',N'Clock3',N'/scheduler/upcoming',2),(N'Run History',N'History',N'/scheduler/history',3);
        INSERT dbo.Menus(Title,Icon,Route,ParentId,SortOrder,IsActive)
        SELECT value.Title,value.Icon,value.Route,@parent,value.SortOrder,1 FROM @children value
        WHERE NOT EXISTS(SELECT 1 FROM dbo.Menus existing WHERE existing.Route=value.Route);
        DECLARE @targets table(MenuId int,Title nvarchar(100),CanManage bit);
        INSERT @targets SELECT @parent,N'Scheduler',0 UNION ALL SELECT menu.Id,menu.Title,CASE WHEN menu.Route=N'/scheduler/jobs' THEN 1 ELSE 0 END FROM dbo.Menus menu JOIN @children value ON value.Route=menu.Route;
        INSERT dbo.Features(FeatureKey,FeatureName,Module,Description,CreatedDate)
        SELECT CONCAT(N'MENU_',target.MenuId,action.Suffix),CONCAT(target.Title,action.Label),N'Menu',CONCAT(N'Scheduler: ',target.Title,action.Label),SYSUTCDATETIME()
        FROM @targets target CROSS JOIN(VALUES(N'',N''),(N'_VIEW',N' - View'),(N'_ADD',N' - Add'),(N'_EDIT',N' - Edit'))action(Suffix,Label)
        WHERE (action.Suffix IN(N'',N'_VIEW') OR target.CanManage=1) AND NOT EXISTS(SELECT 1 FROM dbo.Features feature WHERE feature.FeatureKey=CONCAT(N'MENU_',target.MenuId,action.Suffix));
        INSERT dbo.MenuPermissions(MenuId,PermissionId)
        SELECT target.MenuId,feature.PermissionId FROM @targets target JOIN dbo.Features feature ON feature.FeatureKey IN(CONCAT(N'MENU_',target.MenuId),CONCAT(N'MENU_',target.MenuId,N'_VIEW'),CONCAT(N'MENU_',target.MenuId,N'_ADD'),CONCAT(N'MENU_',target.MenuId,N'_EDIT'))
        WHERE NOT EXISTS(SELECT 1 FROM dbo.MenuPermissions existing WHERE existing.MenuId=target.MenuId AND existing.PermissionId=feature.PermissionId);
        INSERT dbo.TenantMenuPermissions(TenantId,MenuId,IsAllow,CanView,CanAdd,CanEdit,CanDelete,GrantedOnUtc,GrantedByUserId)
        SELECT tenant.Id,target.MenuId,1,1,target.CanManage,target.CanManage,0,SYSUTCDATETIME(),N'System: Scheduler workspace'
        FROM dbo.Tenants tenant CROSS JOIN @targets target
        WHERE NOT EXISTS(SELECT 1 FROM dbo.TenantMenuPermissions existing WHERE existing.TenantId=tenant.Id AND existing.MenuId=target.MenuId);
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        UPDATE dbo.Menus SET IsActive=0 WHERE Route IN(N'/scheduler/jobs',N'/scheduler/upcoming',N'/scheduler/history') OR (ParentId IS NULL AND Title=N'Scheduler');
        """);
}
