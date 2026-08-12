namespace Accounts.Services.Interfaces;

public sealed record OrganizationDataScope(
    bool IsTenantWide,
    int? RootOrganizationId,
    IReadOnlySet<int> OrganizationIds,
    IReadOnlySet<Guid> PersonIds,
    IReadOnlySet<Guid> StaffIds);

public interface IOrganizationDataScopeService
{
    Task<OrganizationDataScope> ResolveAsync(string identityUserId, CancellationToken cancellationToken = default);
}
