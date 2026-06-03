using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Accounts.Controllers
{
    [ApiController]
    [Route("api/auth")]
    [Produces("application/json")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService       _service;
        private readonly IUserSessionService _session;

        public AuthController(IAuthService service, IUserSessionService session)
        {
            _service = service;
            _session = session;
        }

        /// <summary>Register a new user with a role (Manager / Developer / AssistantManager)</summary>
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (success, message, response) = await _service.RegisterAsync(dto);
            if (!success)
            {
                response.Message = message;
                return message.Contains("already") ? Conflict(response) : BadRequest(response);
            }
            return Ok(response);
        }

        /// <summary>Login with email and password</summary>
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (success, statusCode, response) = await _service.LoginAsync(dto);
            return StatusCode(statusCode, response);
        }

        /// <summary>Logout the current user</summary>
        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await _service.LogoutAsync();
            return Ok(new { success = true, message = "Logged out successfully." });
        }

        /// <summary>
        /// Post-login bootstrap: filtered sidebar, permissions, and admin instructions.
        /// Call immediately after successful login.
        /// </summary>
        [HttpGet("session")]
        [Authorize]
        public async Task<IActionResult> GetSession(CancellationToken ct)
        {
            var identityUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(identityUserId))
                return Unauthorized(new { success = false, message = "Not authenticated." });

            var isFullAccess = User.IsInRole("SuperAdmin") || User.IsInRole("Admin");
            var session = await _session.GetSessionAsync(identityUserId, isFullAccess, ct);
            return Ok(new { success = true, data = session });
        }

        /// <summary>Assign a role to an existing user</summary>
        [HttpPost("assign-role")]
        [AllowAnonymous]
        public async Task<IActionResult> AssignRole([FromBody] AssignRoleDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var (success, message, response) = await _service.AssignRoleAsync(dto);
            if (!success)
            {
                response.Message = message;
                return message.Contains("not found") ? NotFound(response) : BadRequest(response);
            }
            return Ok(response);
        }

        /// <summary>Get all system users with their roles</summary>
        [HttpGet("users")]
        [AllowAnonymous]
        public async Task<IActionResult> GetUsers() =>
            Ok(await _service.GetUsersAsync());
    }
}
