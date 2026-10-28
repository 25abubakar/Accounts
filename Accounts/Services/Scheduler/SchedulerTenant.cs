using System.Security.Claims;

namespace Accounts.Services.Scheduler;

public sealed class SchedulerTenant(IHttpContextAccessor accessor)
{
    public int Id => int.TryParse(accessor.HttpContext?.User.FindFirstValue("tenant_id"),out var value)?value:0;
    public string Actor => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "SCHEDULER";
}
