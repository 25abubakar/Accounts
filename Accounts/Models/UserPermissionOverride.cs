using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    /// <summary>
    /// Three-state permission status.
    /// DENY short-circuits ALL other rules — even if role says ALLOW.
    /// </summary>
    public enum PermissionStatus
    {
        INHERIT = 0,  // fall through to next level (role default)
        ALLOW   = 1,  // explicitly granted
        DENY    = 2   // explicitly denied — short-circuits everything
    }

    /// <summary>
    /// User-specific permission override for ONE staff member.
    /// Now uses PermissionId (int FK) instead of FeatureKey (string) for optimized joins.
    ///
    /// Resolution priority (highest first):
    ///   DENY   → immediately return false, no further checks
    ///   ALLOW  → immediately return true
    ///   INHERIT → fall through to RolePermission
    /// </summary>
    [Table("UserPermissionOverrides")]
    public class UserPermissionOverride
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public Guid StaffId { get; set; }

        /// <summary>FK to Features table (PermissionId)</summary>
        public int PermissionId { get; set; }

        /// <summary>
        /// ALLOW = explicitly granted
        /// DENY  = explicitly denied (short-circuits everything)
        /// INHERIT = fall through to role default
        /// </summary>
        [Required]
        [MaxLength(10)]
        public string Status { get; set; } = nameof(PermissionStatus.INHERIT);

        [MaxLength(450)]
        public string? SetBy { get; set; }

        public DateTime SetDate { get; set; } = DateTime.UtcNow;

        [MaxLength(250)]
        public string? Reason { get; set; }

        // Navigation properties
        [ForeignKey("StaffId")]
        public StaffVacancy? Staff { get; set; }

        [ForeignKey("PermissionId")]
        public Feature? Feature { get; set; }
    }
}
