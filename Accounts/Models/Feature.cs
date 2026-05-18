using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{

    [Table("Features")]
    public class Feature
    {
        [Key]
        [MaxLength(100)]
        public string FeatureKey { get; set; } = string.Empty;

        [Required, MaxLength(150)]
        public string FeatureName { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Module { get; set; } = string.Empty;

        [MaxLength(250)]
        public string? Description { get; set; }

        public ICollection<AccessGroupFeature>    AccessGroupFeatures    { get; set; } = new List<AccessGroupFeature>();
        public ICollection<DepartmentAccessMatrix> DepartmentAccessMatrix { get; set; } = new List<DepartmentAccessMatrix>();
    }
}
