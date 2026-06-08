using Accounts.Data;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services
{
    /// <summary>
    /// DEPRECATED stub — PersonMenus/PersonFeatures tables no longer exist in the new RBAC model.
    ///
    /// This class satisfies the IPersonAccessService interface that AuthController and
    /// UserSessionService still depend on. All methods return empty/false so callers
    /// transparently fall through to the 3-layer RbacService resolution path.
    ///
    /// Remove this class (and IPersonAccessService) once callers have been migrated.
    /// </summary>
#pragma warning disable CS0618
    public class PersonAccessService : IPersonAccessService
#pragma warning restore CS0618
    {
        // No DB access needed — always returns empty results.
        public PersonAccessService() { }

        /// <summary>Always false — no person-level grants exist in the new model.</summary>
        public Task<bool> HasPersonGrantsAsync(Guid personId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<HashSet<int>> GetGrantedPermissionIdsAsync(Guid personId, CancellationToken ct = default)
            => Task.FromResult(new HashSet<int>());

        public Task<IReadOnlyList<string>> GetGrantedFeatureKeysAsync(Guid personId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<List<object>> GetGrantedSidebarAsync(Guid personId, CancellationToken ct = default)
            => Task.FromResult(new List<object>());
    }
}
