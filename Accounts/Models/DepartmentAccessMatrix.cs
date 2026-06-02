using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{

    [Table("DepartmentAccessMatrix")]
    public class DepartmentAccessMatrix
    {
        [Key]
        public int Id { get; set; }

        public Guid StaffId { get; set; }

        public int DeptId { get; set; }

        [MaxLength(100)]
        public string FeatureKey { get; set; } = string.Empty;

        public bool HasAccess { get; set; } = false;

        [MaxLength(450)]
        public string? GrantedBy { get; set; }

        public DateTime GrantedDate { get; set; } = DateTime.UtcNow;

        [ForeignKey("StaffId")]
        public StaffVacancy? Staff { get; set; }

        [ForeignKey("DeptId")]
        public OrganizationTree? Department { get; set; }

        [ForeignKey("FeatureKey")]
        public Feature? Feature { get; set; }
    }
}
