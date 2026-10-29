using Accounts.Services.Scheduler;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Accounts.Authorization;

public sealed class SchedulerAccessFilter(SchedulerSqlStore store,SchedulerTenant tenant) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context,ActionExecutionDelegate next)
    {
        if(tenant.Id<=0){context.Result=new ForbidResult();return;}
        var action=(context.ActionDescriptor as ControllerActionDescriptor)?.ActionName;
        var route=action=="Upcoming"?"/scheduler/upcoming":action=="Runs"?"/scheduler/history":"/scheduler/jobs";
        var permission=action switch
        {
            "Create"=>"ADD",
            "Delete"=>"DELETE",
            "Update" or "SetEnabled" or "Run"=>"EDIT",
            "Preview"=>context.ActionArguments.TryGetValue("scheduleId",out var id)&&id is Guid?"EDIT":"ADD",
            _=>"VIEW"
        };
        var allowed=action=="Session"
            ? await AnyViewAsync(context.HttpContext.RequestAborted)
            : await store.AuthorizedAsync(tenant.Id,tenant.Actor,route,permission,context.HttpContext.RequestAborted);
        if(!allowed)
        {context.Result=new ForbidResult();return;}
        await next();
    }

    private async Task<bool> AnyViewAsync(CancellationToken ct)
    {
        foreach(var route in new[]{"/scheduler/jobs","/scheduler/upcoming","/scheduler/history"})
            if(await store.AuthorizedAsync(tenant.Id,tenant.Actor,route,"VIEW",ct))return true;
        return false;
    }
}
