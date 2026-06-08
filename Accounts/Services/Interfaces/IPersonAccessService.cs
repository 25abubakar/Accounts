namespace Accounts.Services.Interfaces
{
    /// <summary>
    /// DEPRECATED — PersonMenus/PersonFeatures direct-grant model has been removed.
    /// All permission grants now flow through UserPermissionOverrides (3-layer RBAC).
    ///
    /// This interface is retained only to avoid breaking AuthController and UserSessionService
    /// during the transition. Its implementation returns empty/no-op results.
    /// Remove once callers have been fully migrated to RbacService.
    /// </summary>
    [Obsolete("PersonAccessService is deprecated. Use RbacService for all permission resolution.")]
    public interface IPersonAccessService
    {
        /// <summary>Always returns false — no person-level grants exist in the new model.</summary>
        Task<bool> HasPersonGrantsAsync(Guid personId, CancellationToken ct = default);

        Task<HashSet<int>> GetGrantedPermissionIdsAsync(Guid personId, CancellationToken ct = default);
        Task<IReadOnlyList<string>> GetGrantedFeatureKeysAsync(Guid personId, CancellationToken ct = default);
        Task<List<object>> GetGrantedSidebarAsync(Guid personId, CancellationToken ct = default);
    }
}
