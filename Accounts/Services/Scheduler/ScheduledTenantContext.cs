using Accounts.Services.Interfaces;

namespace Accounts.Services.Scheduler;

// Scoped to one execution. It is never populated from an arbitrary browser payload.
public sealed class ScheduledTenantContext : ITenantService
{
    public int Id { get; set; }
    public int? TenantId => Id>0?Id:null;
    public int RequiredTenantId => TenantId??throw new InvalidOperationException("This job has no tenant scope.");
    public bool IsSuperAdmin => false;
    public bool IsTenantAdmin => false;
}
