using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace Accounts.Services.Services
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<ApplicationUser>  _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole>      _roleManager;

        private static readonly string[] AllowedRoles =
            ["Manager", "Developer", "AssistantManager", "SuperAdmin", "Admin", "TenantAdmin"];

        public AuthService(
            UserManager<ApplicationUser>  userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleManager<IdentityRole>      roleManager)
        {
            _userManager   = userManager;
            _signInManager = signInManager;
            _roleManager   = roleManager;
        }

        // ── Register ──────────────────────────────────────────────────────────

        public async Task<(bool Success, string Message, AuthResponseDto Response)> RegisterAsync(RegisterDto dto)
        {
            if (!AllowedRoles.Contains(dto.Role))
                return (false, $"Invalid role. Allowed: {string.Join(", ", AllowedRoles)}",
                    new AuthResponseDto { Success = false });

            if (await _userManager.FindByEmailAsync(dto.Email) != null)
                return (false, "Email is already registered.",
                    new AuthResponseDto { Success = false });

            var user = new ApplicationUser
            {
                UserName       = dto.Email,
                Email          = dto.Email,
                EmailConfirmed = true,
                IsSuperAdmin   = dto.Role == "SuperAdmin"
            };

            var result = await _userManager.CreateAsync(user, dto.Password);
            if (!result.Succeeded)
                return (false, string.Join("; ", result.Errors.Select(e => e.Description)),
                    new AuthResponseDto { Success = false });

            if (!await _roleManager.RoleExistsAsync(dto.Role))
                await _roleManager.CreateAsync(new IdentityRole(dto.Role));

            await _userManager.AddToRoleAsync(user, dto.Role);

            // Stamp tenant claims as persistent user claims so they survive re-login
            await StampTenantClaimsAsync(user);

            var roles = await _userManager.GetRolesAsync(user);

            return (true, "User registered successfully.", new AuthResponseDto
            {
                Success  = true,
                Message  = "User registered successfully.",
                Username = user.UserName,
                Email    = user.Email,
                Roles    = roles
            });
        }

        // ── Login — accepts Username (LT10001) OR Email ───────────────────────

        public async Task<(bool Success, int StatusCode, AuthResponseDto Response)> LoginAsync(LoginDto dto)
        {
            // Step 1: Resolve the ApplicationUser by username or email
            ApplicationUser? user = null;

            user = await _userManager.FindByNameAsync(dto.Username);

            if (user == null && dto.Username.Contains('@'))
                user = await _userManager.FindByEmailAsync(dto.Username);

            if (user == null)
                return (false, 401, new AuthResponseDto
                {
                    Success = false,
                    Message = "Invalid username or password."
                });

            // Step 2: Ensure tenant claims are up-to-date before signing in
            await StampTenantClaimsAsync(user);

            // Step 3: Sign in — cookie will carry the claims stamped above
            var result = await _signInManager.PasswordSignInAsync(
                user.UserName!, dto.Password, dto.RememberMe, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                var roles = await _userManager.GetRolesAsync(user);
                return (true, 200, new AuthResponseDto
                {
                    Success      = true,
                    Message      = "Login successful.",
                    Username     = user.UserName,
                    Email        = user.Email,
                    Roles        = roles,
                    TenantId     = user.TenantId,
                    IsSuperAdmin = user.IsSuperAdmin,
                    IsTenantAdmin = user.IsTenantAdmin
                });
            }

            if (result.IsLockedOut)
                return (false, 423, new AuthResponseDto
                {
                    Success = false,
                    Message = "Account is locked out."
                });

            return (false, 401, new AuthResponseDto
            {
                Success = false,
                Message = "Invalid username or password."
            });
        }

        // ── Logout ────────────────────────────────────────────────────────────

        public async Task LogoutAsync() =>
            await _signInManager.SignOutAsync();

        // ── Assign Role ───────────────────────────────────────────────────────

        public async Task<(bool Success, string Message, AuthResponseDto Response)> AssignRoleAsync(AssignRoleDto dto)
        {
            if (!AllowedRoles.Contains(dto.Role))
                return (false, $"Invalid role. Allowed: {string.Join(", ", AllowedRoles)}",
                    new AuthResponseDto { Success = false });

            var user = await _userManager.FindByNameAsync(dto.Username)
                    ?? await _userManager.FindByEmailAsync(dto.Username);

            if (user == null)
                return (false, "User not found.", new AuthResponseDto { Success = false });

            if (!await _roleManager.RoleExistsAsync(dto.Role))
                await _roleManager.CreateAsync(new IdentityRole(dto.Role));

            var current = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, current);
            await _userManager.AddToRoleAsync(user, dto.Role);

            // Update IsSuperAdmin flag when role changes
            user.IsSuperAdmin = dto.Role == "SuperAdmin";
            await _userManager.UpdateAsync(user);
            await StampTenantClaimsAsync(user);

            var updated = await _userManager.GetRolesAsync(user);
            return (true, $"Role '{dto.Role}' assigned to {user.UserName}.", new AuthResponseDto
            {
                Success  = true,
                Message  = $"Role '{dto.Role}' assigned.",
                Username = user.UserName,
                Email    = user.Email,
                Roles    = updated
            });
        }

        // ── Get All Users ─────────────────────────────────────────────────────

        public Task<IEnumerable<object>> GetUsersAsync()
        {
            var users  = _userManager.Users.ToList();
            var result = users.Select(u => (object)new
            {
                u.Id,
                u.UserName,
                u.Email,
                u.TenantId,
                u.IsSuperAdmin,
                u.IsTenantAdmin,
                Roles = _userManager.GetRolesAsync(u).Result
            });
            return Task.FromResult(result);
        }

        // ── Private: stamp tenant claims as persistent user claims ────────────

        /// <summary>
        /// Writes tenant_id, is_super_admin, and is_tenant_admin as persistent
        /// user claims (stored in AspNetUserClaims) so they are automatically
        /// included in every cookie/session without a DB lookup on each request.
        ///
        /// Called on registration, login, and role assignment to keep claims
        /// in sync with the ApplicationUser flags.
        /// </summary>
        private async Task StampTenantClaimsAsync(ApplicationUser user)
        {
            // Remove stale tenant claims before re-stamping
            var existingClaims = await _userManager.GetClaimsAsync(user);
            var tenantClaimTypes = new[]
            {
                ITenantService.ClaimTenantId,
                ITenantService.ClaimIsSuperAdmin,
                ITenantService.ClaimIsTenantAdmin
            };

            var stale = existingClaims
                .Where(c => tenantClaimTypes.Contains(c.Type))
                .ToList();

            if (stale.Any())
                await _userManager.RemoveClaimsAsync(user, stale);

            // Write fresh claims
            var fresh = new List<Claim>
            {
                new(ITenantService.ClaimIsSuperAdmin,  user.IsSuperAdmin.ToString().ToLower()),
                new(ITenantService.ClaimIsTenantAdmin, user.IsTenantAdmin.ToString().ToLower()),
            };

            if (user.TenantId.HasValue)
                fresh.Add(new Claim(ITenantService.ClaimTenantId, user.TenantId.Value.ToString()));

            await _userManager.AddClaimsAsync(user, fresh);
        }
    }
}
