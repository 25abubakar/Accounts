using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{

    [Table("RolePermissions")]
    public class RolePermission
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Job title string — Agent, Supervisor, Manager, CEO, etc.</summary>
        [Required, MaxLength(100)]
        public string JobTitle { get; set; } = string.Empty;

        public int? DeptId { get; set; }

        [Required, MaxLength(100)]
        public string FeatureKey { get; set; } = string.Empty;

        public bool IsAllowed { get; set; } = false;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        // Navigation
        [ForeignKey("DeptId")]
        public OrganizationTree? Department { get; set; }

        [ForeignKey("FeatureKey")]
        public Feature? Feature { get; set; }
    }
}
