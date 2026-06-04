using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Accounts.Authorization
{
    /// <summary>
    /// Dynamic policy provider that creates permission-based policies on-the-fly.
    /// Eliminates the need to manually register hundreds of policies in Program.cs.
    /// 
    /// Usage: [Authorize(Policy = "Permission:EMPLOYEE_EDIT")]
    /// or use [RequirePermission("EMPLOYEE_EDIT")] attribute.
    /// </summary>
    public class PermissionPolicyProvider : IAuthorizationPolicyProvider
    {
        private const string PermissionPolicyPrefix = "Permission:";
        private readonly DefaultAuthorizationPolicyProvider _fallbackPolicyProvider;

        public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
        {
            _fallbackPolicyProvider = new DefaultAuthorizationPolicyProvider(options);
        }

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
        {
            return _fallbackPolicyProvider.GetDefaultPolicyAsync();
        }

        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
        {
            return _fallbackPolicyProvider.GetFallbackPolicyAsync();
        }

        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            // Check if policy name matches our permission pattern
            if (policyName.StartsWith(PermissionPolicyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var permissionKey = policyName.Substring(PermissionPolicyPrefix.Length);

                var policy = new AuthorizationPolicyBuilder()
                    .AddRequirements(new PermissionRequirement(permissionKey))
                    .Build();

                return Task.FromResult<AuthorizationPolicy?>(policy);
            }

            // Fall back to default policy provider
            return _fallbackPolicyProvider.GetPolicyAsync(policyName);
        }
    }
}
