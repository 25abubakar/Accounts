using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace Accounts.Services.Services
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<IdentityUser>  _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly RoleManager<IdentityRole>  _roleManager;

        private static readonly string[] AllowedRoles =
            ["Manager", "Developer", "AssistantManager", "SuperAdmin", "Admin"];

        public AuthService(
            UserManager<IdentityUser>  userManager,
            SignInManager<IdentityUser> signInManager,
            RoleManager<IdentityRole>  roleManager)
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

            var user = new IdentityUser
            {
                UserName       = dto.Email,
                Email          = dto.Email,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, dto.Password);
            if (!result.Succeeded)
                return (false, string.Join("; ", result.Errors.Select(e => e.Description)),
                    new AuthResponseDto { Success = false });

            if (!await _roleManager.RoleExistsAsync(dto.Role))
                await _roleManager.CreateAsync(new IdentityRole(dto.Role));

            await _userManager.AddToRoleAsync(user, dto.Role);
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
            // Step 1: Resolve the IdentityUser by username or email
            IdentityUser? user = null;

            // Try username first (e.g. LT10001, admin)
            user = await _userManager.FindByNameAsync(dto.Username);

            // Fallback: try email (e.g. abubakar@laltechnologies.com)
            if (user == null && dto.Username.Contains('@'))
                user = await _userManager.FindByEmailAsync(dto.Username);

            if (user == null)
                return (false, 401, new AuthResponseDto
                {
                    Success = false,
                    Message = "Invalid username or password."
                });

            // Step 2: Sign in using the resolved username
            var result = await _signInManager.PasswordSignInAsync(
                user.UserName!, dto.Password, dto.RememberMe, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                var roles = await _userManager.GetRolesAsync(user);
                return (true, 200, new AuthResponseDto
                {
                    Success  = true,
                    Message  = "Login successful.",
                    Username = user.UserName,
                    Email    = user.Email,
                    Roles    = roles
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

        // ── Assign Role — accepts Username OR Email ───────────────────────────

        public async Task<(bool Success, string Message, AuthResponseDto Response)> AssignRoleAsync(AssignRoleDto dto)
        {
            if (!AllowedRoles.Contains(dto.Role))
                return (false, $"Invalid role. Allowed: {string.Join(", ", AllowedRoles)}",
                    new AuthResponseDto { Success = false });

            // Try username first, then email
            var user = await _userManager.FindByNameAsync(dto.Username)
                    ?? await _userManager.FindByEmailAsync(dto.Username);

            if (user == null)
                return (false, "User not found.", new AuthResponseDto { Success = false });

            if (!await _roleManager.RoleExistsAsync(dto.Role))
                await _roleManager.CreateAsync(new IdentityRole(dto.Role));

            var current = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, current);
            await _userManager.AddToRoleAsync(user, dto.Role);

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
                Roles = _userManager.GetRolesAsync(u).Result
            });
            return Task.FromResult(result);
        }
    }
}
