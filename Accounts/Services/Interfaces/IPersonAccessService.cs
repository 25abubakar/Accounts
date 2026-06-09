namespace Accounts.Services.Interfaces
{
    public interface IPersonAccessService
    {
        Task<bool> HasPersonGrantsAsync(Guid personId, CancellationToken ct = default);

        Task<HashSet<int>> GetGrantedPermissionIdsAsync(Guid personId, CancellationToken ct = default);

        Task<IReadOnlyList<string>> GetGrantedFeatureKeysAsync(Guid personId, CancellationToken ct = default);

        Task<List<object>> GetGrantedSidebarAsync(Guid personId, CancellationToken ct = default);

        Task<(bool Success, string Message, IReadOnlyList<int> MenuIds, IReadOnlyList<string> FeatureKeys)>
            GrantMenuAsync(Guid personId, int menuId, string? grantedBy, string? reason, CancellationToken ct = default);

        Task<(bool Success, string Message)>
            RevokeMenuAsync(Guid personId, int menuId, CancellationToken ct = default);

        Task GrantFeatureAsync(Guid personId, int permissionId, string? grantedBy, CancellationToken ct = default);

        Task RevokeFeatureAsync(Guid personId, int permissionId, CancellationToken ct = default);

        Task<object> GetPersonAccessSummaryAsync(Guid personId, CancellationToken ct = default);
    }
}
