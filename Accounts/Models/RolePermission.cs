using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    /// <summary>
    /// Role-based permission defaults for a given JobTitle + DeptId + Permission.
    /// Now uses PermissionId (int FK) instead of FeatureKey (string) for optimized joins.
    /// </summary>
    [Table("RolePermissions")]
    public class RolePermission
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>Job title string — Agent, Supervisor, Manager, CEO, etc.</summary>
        [Required, MaxLength(100)]
        public string JobTitle { get; set; } = string.Empty;

        /// <summary>Optional department scope (NULL = global role permission)</summary>
        public int? DeptId { get; set; }

        /// <summary>FK to Features table (PermissionId)</summary>
        public int PermissionId { get; set; }

        public bool IsAllowed { get; set; } = false;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("DeptId")]
        public OrganizationTree? Department { get; set; }

        [ForeignKey("PermissionId")]
        public Feature? Feature { get; set; }
    }
}
