using Accounts.Services.Interfaces;

namespace Accounts.Tests.Helpers;

public sealed class TestTenantService : ITenantService
{
    public TestTenantService(int? tenantId, bool isSuperAdmin = false, bool isTenantAdmin = false)
    {
        TenantId = tenantId;
        IsSuperAdmin = isSuperAdmin;
        IsTenantAdmin = isTenantAdmin;
    }

    public int? TenantId { get; }
    public bool IsSuperAdmin { get; }
    public bool IsTenantAdmin { get; }

    public int RequiredTenantId => TenantId
        ?? throw new InvalidOperationException("A tenant is required for this operation.");
}
