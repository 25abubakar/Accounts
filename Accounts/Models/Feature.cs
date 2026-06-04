using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    /// <summary>
    /// Master Permissions/Features table with integer PK for optimized FK joins.
    /// FeatureKey remains unique for backward compatibility but is now indexed, not PK.
    /// </summary>
    [Table("Features")]
    public class Feature
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int PermissionId { get; set; }

        /// <summary>Unique string identifier (e.g., "EMPLOYEE_EDIT", "MENU_5_VIEW")</summary>
        [Required, MaxLength(100)]
        public string FeatureKey { get; set; } = string.Empty;

        [Required, MaxLength(150)]
        public string FeatureName { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Module { get; set; } = string.Empty;

        [MaxLength(250)]
        public string? Description { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<AccessGroupFeature>       AccessGroupFeatures       { get; set; } = new List<AccessGroupFeature>();
        public ICollection<DepartmentAccessMatrix>   DepartmentAccessMatrix    { get; set; } = new List<DepartmentAccessMatrix>();
        public ICollection<RolePermission>           RolePermissions           { get; set; } = new List<RolePermission>();
        public ICollection<UserPermissionOverride>   UserPermissionOverrides   { get; set; } = new List<UserPermissionOverride>();
    }
}
