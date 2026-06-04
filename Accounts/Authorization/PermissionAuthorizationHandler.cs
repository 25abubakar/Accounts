using Accounts.Data;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Authorization
{
    /// <summary>
    /// Dynamic permission-based authorization handler.
    /// Allows protecting endpoints with [Authorize(Policy = "EMPLOYEE_EDIT")] etc.
    /// Uses optimized in-memory permission resolution (no N+1 queries).
    /// </summary>
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public string PermissionKey { get; }

        public PermissionRequirement(string permissionKey)
        {
            PermissionKey = permissionKey;
        }
    }

    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IServiceProvider _serviceProvider;

        public PermissionAuthorizationHandler(
            IHttpContextAccessor httpContextAccessor,
            IServiceProvider serviceProvider)
        {
            _httpContextAccessor = httpContextAccessor;
            _serviceProvider = serviceProvider;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            var user = context.User;
            if (user == null || !user.Identity?.IsAuthenticated == true)
            {
                context.Fail();
                return;
            }

            // SuperAdmin/Admin bypass
            if (user.IsInRole("SuperAdmin") || user.IsInRole("Admin"))
            {
                context.Succeed(requirement);
                return;
            }

            // Get identityUserId from claims
            var identityUserId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(identityUserId))
            {
                context.Fail();
                return;
            }

            // Resolve scoped services
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var menuService = scope.ServiceProvider.GetRequiredService<OptimizedMenuService>();

            // Look up staff record
            var person = await db.Persons
                .AsNoTracking()
                .Include(p => p.Staff)
                .Where(p => p.IdentityUserId == identityUserId)
                .Select(p => new { StaffId = p.Staff != null ? p.Staff.StaffId : (Guid?)null })
                .FirstOrDefaultAsync();

            if (person?.StaffId == null)
            {
                context.Fail();
                return;
            }

            // Check permission using optimized service
            var hasAccess = await menuService.HasAccessByKeyAsync(
                person.StaffId.Value,
                requirement.PermissionKey);

            if (hasAccess)
            {
                context.Succeed(requirement);
            }
            else
            {
                context.Fail();
            }
        }
    }

    /// <summary>
    /// Custom authorization attribute for easier usage.
    /// Usage: [RequirePermission("EMPLOYEE_EDIT")]
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class RequirePermissionAttribute : AuthorizeAttribute
    {
        public RequirePermissionAttribute(string permissionKey)
        {
            Policy = $"Permission:{permissionKey}";
        }
    }
}
