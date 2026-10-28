using Accounts.Models.Scheduler;
using Accounts.Services.Scheduler;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace Accounts.Authorization;

public sealed class SchedulerAccessFilter(SchedulerStorage storage,SchedulerTenant tenant) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context,ActionExecutionDelegate next)
    {
        if(tenant.Id<=0){context.Result=new ForbidResult();return;}
        var action=(context.ActionDescriptor as ControllerActionDescriptor)?.ActionName;
        var route=action=="Upcoming"?"/scheduler/upcoming":action=="Runs"?"/scheduler/history":"/scheduler/jobs";
        var permission=action=="Create"?"ADD":action is "Update" or "SetEnabled" or "Run"?"EDIT":"VIEW";
        await using var connection=new SqlConnection(storage.BusinessConnection);await connection.OpenAsync(context.HttpContext.RequestAborted);
        await using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT COUNT(*) FROM dbo.AspNetUsers account
            JOIN dbo.Tenants tenant ON tenant.Id=account.TenantId AND tenant.IsActive=1
            JOIN dbo.TenantMenuPermissions grantRow ON grantRow.TenantId=tenant.Id AND grantRow.IsAllow=1 AND grantRow.CanView=1
            JOIN dbo.Menus menu ON menu.Id=grantRow.MenuId AND menu.IsActive=1 AND menu.Route=@route
            WHERE account.Id=@user AND account.TenantId=@tenant AND account.IsTenantAdmin=1 AND account.IsSuperAdmin=0
              AND (account.LockoutEnd IS NULL OR account.LockoutEnd<=SYSUTCDATETIME())
              AND (@action='VIEW' OR (@action='ADD' AND grantRow.CanAdd=1) OR (@action='EDIT' AND grantRow.CanEdit=1));
            """;
        command.Parameters.AddWithValue("@route",route);command.Parameters.AddWithValue("@action",permission);
        command.Parameters.AddWithValue("@tenant",tenant.Id);command.Parameters.AddWithValue("@user",context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)??"");
        if(Convert.ToInt32(await command.ExecuteScalarAsync(context.HttpContext.RequestAborted))==0){context.Result=new ForbidResult();return;}
        await next();
    }
}
