using Accounts.Services.Interfaces;
using System.Security.Claims;

namespace Accounts.Services.Services
{
    /// <summary>
    /// Reads tenant context from the current HTTP request's User claims.
    ///
    /// Registered as Scoped so each request gets its own instance with
    /// the correct HttpContext.User snapshot.
    ///
    /// Claim population: AuthService.LoginAsync writes the tenant claims
    /// into the Identity cookie when the user signs in.
    /// </summary>
    public class TenantService : ITenantService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public TenantService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

        /// <inheritdoc/>
        public int? TenantId
        {
            get
            {
                var claim = User?.FindFirstValue(ITenantService.ClaimTenantId);
                return int.TryParse(claim, out var id) ? id : null;
            }
        }

        /// <inheritdoc/>
        public bool IsSuperAdmin
        {
            get
            {
                var claim = User?.FindFirstValue(ITenantService.ClaimIsSuperAdmin);
                return string.Equals(claim, "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <inheritdoc/>
        public bool IsTenantAdmin
        {
            get
            {
                var claim = User?.FindFirstValue(ITenantService.ClaimIsTenantAdmin);
                return string.Equals(claim, "true", StringComparison.OrdinalIgnoreCase) ||
                    User?.IsInRole("TenantAdmin") == true;
            }
        }

        /// <inheritdoc/>
        public int RequiredTenantId =>
            TenantId ?? throw new InvalidOperationException(
                "No TenantId found in user claims. " +
                "Super Admin accounts cannot access tenant-scoped data.");
    }
}
