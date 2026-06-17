using System.ComponentModel.DataAnnotations;

namespace Accounts.Models
{
    public class RegisterDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = string.Empty;
    }

    /// <summary>
    /// Login using Username (e.g. LT10001) or Email — both accepted.
    /// Password is the same format: LT10001@
    /// </summary>
    public class LoginDto
    {
        /// <summary>
        /// Username (e.g. LT10001, admin) OR Email (e.g. abubakar@laltechnologies.com).
        /// Both work — backend tries username first, then email.
        /// </summary>
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; } = false;
    }

    public class AssignRoleDto
    {
        /// <summary>Username (LT10001) or Email</summary>
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = string.Empty;
    }

    public class AuthResponseDto
    {
        public bool    Success  { get; set; }
        public string  Message  { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string? Email    { get; set; }
        public IList<string>? Roles { get; set; }

        // ── Multi-Tenant SaaS fields ──────────────────────────────────────
        public int?  TenantId      { get; set; }
        public bool  IsSuperAdmin  { get; set; }
        public bool  IsTenantAdmin { get; set; }
    }
}
