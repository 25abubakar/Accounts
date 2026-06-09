using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    /// <summary>
    /// Legacy department-level access matrix (keep for backward compatibility).
    /// Now uses PermissionId (int FK) instead of FeatureKey (string) for optimized joins.
    /// </summary>
    [Table("DepartmentAccessMatrix")]
    public class DepartmentAccessMatrix
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public Guid StaffId { get; set; }

        public int DeptId { get; set; }

        /// <summary>FK to Features table (PermissionId)</summary>
        public int PermissionId { get; set; }

        public bool HasAccess { get; set; } = false;

        [MaxLength(450)]
        public string? GrantedBy { get; set; }

        public DateTime GrantedDate { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("StaffId")]
        public StaffVacancy? Staff { get; set; }

        [ForeignKey("DeptId")]
        public OrganizationTree? Department { get; set; }

        [ForeignKey("PermissionId")]
        public Feature? Feature { get; set; }
    }
}
