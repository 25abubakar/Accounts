using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Services.Services
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<ApplicationUser>  _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole>      _roleManager;
        private readonly ApplicationDbContext           _db;
        private readonly IAccountScopeAccessService     _scopeAccess;
        private readonly IHttpContextAccessor           _httpContextAccessor;

        private static readonly string[] AllowedRoles =
            ["Manager", "Developer", "AssistantManager", "SuperAdmin", "Admin", "TenantAdmin"];

        public AuthService(
            UserManager<ApplicationUser>  userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleManager<IdentityRole>      roleManager,
            ApplicationDbContext           db,
            IAccountScopeAccessService     scopeAccess,
            IHttpContextAccessor           httpContextAccessor)
        {
            _userManager   = userManager;
            _signInManager = signInManager;
            _roleManager   = roleManager;
            _db            = db;
            _scopeAccess   = scopeAccess;
            _httpContextAccessor = httpContextAccessor;
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

            // Step 3: Sign in — cookie will carry the claims stamped above
            var result = await _signInManager.CheckPasswordSignInAsync(
                user, dto.Password, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                var access = await _scopeAccess.ValidateAsync(user.Id);
                if (!access.IsAllowed)
                    return (false, 403, new AuthResponseDto
                    {
                        Success = false,
                        Message = access.Message
                    });

                await StampTenantClaimsAsync(user);
                await _signInManager.SignInAsync(user, dto.RememberMe);
                await OpenApplicationLoginSessionAsync(user);
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

        public async Task LogoutAsync()
        {
            var identityUserId = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(identityUserId))
                await CloseApplicationLoginSessionAsync(identityUserId);

            await _signInManager.SignOutAsync();
        }

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

        public async Task<IEnumerable<object>> GetUsersAsync()
        {
            var users = await _userManager.Users.AsNoTracking().ToListAsync();
            var roleRows = await (
                from userRole in _db.UserRoles
                join role in _db.Roles on userRole.RoleId equals role.Id
                select new { userRole.UserId, Role = role.Name! })
                .AsNoTracking()
                .ToListAsync();
            var rolesByUser = roleRows
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => (IList<string>)g.Select(x => x.Role).ToList());

            return users.Select(u => (object)new
            {
                u.Id,
                u.UserName,
                u.Email,
                u.TenantId,
                u.IsSuperAdmin,
                u.IsTenantAdmin,
                Roles = rolesByUser.GetValueOrDefault(u.Id, Array.Empty<string>())
            });
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
                ITenantService.ClaimIsTenantAdmin,
                AccountClaimTypes.StaffId
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

            var staffId = await _db.Persons.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(person => person.IdentityUserId == user.Id)
                .Select(person => person.Staff != null ? (Guid?)person.Staff.StaffId : null)
                .FirstOrDefaultAsync();
            if (staffId.HasValue)
                fresh.Add(new Claim(AccountClaimTypes.StaffId, staffId.Value.ToString()));

            await _userManager.AddClaimsAsync(user, fresh);
        }

        private async Task OpenApplicationLoginSessionAsync(ApplicationUser user)
        {
            if (!user.TenantId.HasValue || user.IsSuperAdmin || user.IsTenantAdmin)
                return;

            await ApplicationLoginSessionSchema.EnsureCreatedAsync(_db);

            var staffInfo = await _db.Persons.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(person => person.IdentityUserId == user.Id && person.TenantId == user.TenantId.Value)
                .Select(person => new
                {
                    person.PersonId,
                    person.TimeZoneId,
                    StaffId = person.Staff != null ? (Guid?)person.Staff.StaffId : null
                })
                .FirstOrDefaultAsync();

            var nowUtc = DateTime.UtcNow;
            var zone = ResolveTimeZone(staffInfo?.TimeZoneId);
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
            var context = _httpContextAccessor.HttpContext;
            var userAgent = context?.Request.Headers.UserAgent.ToString();
            if (userAgent?.Length > 300) userAgent = userAgent[..300];

            _db.ApplicationLoginSessions.Add(new ApplicationLoginSession
            {
                TenantId = user.TenantId.Value,
                StaffId = staffInfo?.StaffId,
                PersonId = staffInfo?.PersonId,
                IdentityUserId = user.Id,
                SessionDate = DateOnly.FromDateTime(localNow),
                LoginUtc = nowUtc,
                IpAddress = context?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = userAgent,
                Source = "Software",
                CreatedDate = nowUtc,
            });

            await _db.SaveChangesAsync();
        }

        private async Task CloseApplicationLoginSessionAsync(string identityUserId)
        {
            var user = await _userManager.FindByIdAsync(identityUserId);
            if (user?.IsSuperAdmin == true || user?.IsTenantAdmin == true)
                return;

            await ApplicationLoginSessionSchema.EnsureCreatedAsync(_db);

            var session = await _db.ApplicationLoginSessions
                .IgnoreQueryFilters()
                .Where(item => item.IdentityUserId == identityUserId && item.LogoutUtc == null)
                .OrderByDescending(item => item.LoginUtc)
                .FirstOrDefaultAsync();

            if (session == null) return;

            var nowUtc = DateTime.UtcNow;
            session.LogoutUtc = nowUtc;
            session.WorkingMinutes = Math.Max(0, (int)Math.Floor((nowUtc - session.LoginUtc).TotalMinutes));
            session.ModifiedDate = nowUtc;
            await _db.SaveChangesAsync();
        }

        private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
        {
            if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Local;
            try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
            catch { return TimeZoneInfo.Local; }
        }
    }
}
