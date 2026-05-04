using Accounts.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        // Predefined allowed roles
        private static readonly string[] AllowedRoles = ["Manager", "Developer", "AssistantManager"];

        public AuthController(
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
        }

        /// <summary>Register a new user and assign a role (Manager / Developer / AssistantManager)</summary>
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (!AllowedRoles.Contains(dto.Role))
                return BadRequest(new AuthResponseDto
                {
                    Success = false,
                    Message = $"Invalid role. Allowed roles: {string.Join(", ", AllowedRoles)}"
                });

            var existingUser = await _userManager.FindByEmailAsync(dto.Email);
            if (existingUser != null)
                return Conflict(new AuthResponseDto { Success = false, Message = "Email is already registered." });

            var user = new IdentityUser
            {
                UserName = dto.Email,
                Email = dto.Email,
                EmailConfirmed = true   // no email confirmation required
            };

            var result = await _userManager.CreateAsync(user, dto.Password);
            if (!result.Succeeded)
                return BadRequest(new AuthResponseDto
                {
                    Success = false,
                    Message = string.Join("; ", result.Errors.Select(e => e.Description))
                });

            // Ensure role exists then assign
            if (!await _roleManager.RoleExistsAsync(dto.Role))
                await _roleManager.CreateAsync(new IdentityRole(dto.Role));

            await _userManager.AddToRoleAsync(user, dto.Role);

            var roles = await _userManager.GetRolesAsync(user);
            return Ok(new AuthResponseDto
            {
                Success = true,
                Message = "User registered successfully.",
                Email = user.Email,
                Roles = roles
            });
        }

        /// <summary>Login with email and password</summary>
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _signInManager.PasswordSignInAsync(
                dto.Email, dto.Password, dto.RememberMe, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                var user = await _userManager.FindByEmailAsync(dto.Email);
                var roles = await _userManager.GetRolesAsync(user!);
                return Ok(new AuthResponseDto
                {
                    Success = true,
                    Message = "Login successful.",
                    Email = user!.Email,
                    Roles = roles
                });
            }

            if (result.IsLockedOut)
                return StatusCode(423, new AuthResponseDto { Success = false, Message = "Account is locked out." });

            return Unauthorized(new AuthResponseDto { Success = false, Message = "Invalid email or password." });
        }

        /// <summary>Logout the current user</summary>
        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return Ok(new AuthResponseDto { Success = true, Message = "Logged out successfully." });
        }

        /// <summary>Assign a role to an existing user (Manager / Developer / AssistantManager)</summary>
        [HttpPost("assign-role")]
        [AllowAnonymous]   // lock this down with [Authorize(Roles="Manager")] in production
        public async Task<IActionResult> AssignRole([FromBody] AssignRoleDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (!AllowedRoles.Contains(dto.Role))
                return BadRequest(new AuthResponseDto
                {
                    Success = false,
                    Message = $"Invalid role. Allowed roles: {string.Join(", ", AllowedRoles)}"
                });

            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null)
                return NotFound(new AuthResponseDto { Success = false, Message = "User not found." });

            // Ensure role exists
            if (!await _roleManager.RoleExistsAsync(dto.Role))
                await _roleManager.CreateAsync(new IdentityRole(dto.Role));

            // Remove existing roles first, then assign new one
            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, dto.Role);

            var updatedRoles = await _userManager.GetRolesAsync(user);
            return Ok(new AuthResponseDto
            {
                Success = true,
                Message = $"Role '{dto.Role}' assigned to {user.Email}.",
                Email = user.Email,
                Roles = updatedRoles
            });
        }

        /// <summary>Get all users with their roles</summary>
        [HttpGet("users")]
        [AllowAnonymous]
        public IActionResult GetUsers()
        {
            var users = _userManager.Users.ToList();
            var result = users.Select(u => new
            {
                u.Id,
                u.Email,
                u.UserName,
                Roles = _userManager.GetRolesAsync(u).Result
            });
            return Ok(result);
        }
    }
}
