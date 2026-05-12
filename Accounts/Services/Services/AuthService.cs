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

        private static readonly string[] AllowedRoles = ["Manager", "Developer", "AssistantManager"];

        public AuthService(
            UserManager<IdentityUser>  userManager,
            SignInManager<IdentityUser> signInManager,
            RoleManager<IdentityRole>  roleManager)
        {
            _userManager   = userManager;
            _signInManager = signInManager;
            _roleManager   = roleManager;
        }

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

            return (true, "User registered successfully.",
                new AuthResponseDto { Success = true, Message = "User registered successfully.", Email = user.Email, Roles = roles });
        }

        public async Task<(bool Success, int StatusCode, AuthResponseDto Response)> LoginAsync(LoginDto dto)
        {
            var result = await _signInManager.PasswordSignInAsync(
                dto.Email, dto.Password, dto.RememberMe, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                var user  = await _userManager.FindByEmailAsync(dto.Email);
                var roles = await _userManager.GetRolesAsync(user!);
                return (true, 200, new AuthResponseDto
                {
                    Success = true, Message = "Login successful.",
                    Email = user!.Email, Roles = roles
                });
            }

            if (result.IsLockedOut)
                return (false, 423, new AuthResponseDto { Success = false, Message = "Account is locked out." });

            return (false, 401, new AuthResponseDto { Success = false, Message = "Invalid email or password." });
        }

        public async Task LogoutAsync() =>
            await _signInManager.SignOutAsync();

        public async Task<(bool Success, string Message, AuthResponseDto Response)> AssignRoleAsync(AssignRoleDto dto)
        {
            if (!AllowedRoles.Contains(dto.Role))
                return (false, $"Invalid role. Allowed: {string.Join(", ", AllowedRoles)}",
                    new AuthResponseDto { Success = false });

            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null)
                return (false, "User not found.", new AuthResponseDto { Success = false });

            if (!await _roleManager.RoleExistsAsync(dto.Role))
                await _roleManager.CreateAsync(new IdentityRole(dto.Role));

            var current = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, current);
            await _userManager.AddToRoleAsync(user, dto.Role);

            var updated = await _userManager.GetRolesAsync(user);
            return (true, $"Role '{dto.Role}' assigned to {user.Email}.",
                new AuthResponseDto { Success = true, Message = $"Role '{dto.Role}' assigned.", Email = user.Email, Roles = updated });
        }

        public Task<IEnumerable<object>> GetUsersAsync()
        {
            var users = _userManager.Users.ToList();
            var result = users.Select(u => (object)new
            {
                u.Id, u.Email, u.UserName,
                Roles = _userManager.GetRolesAsync(u).Result
            });
            return Task.FromResult(result);
        }
    }
}
