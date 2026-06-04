using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    /// <summary>
    /// Access group feature mapping.
    /// Now uses PermissionId (int FK) instead of FeatureKey (string) for optimized joins.
    /// </summary>
    [Table("AccessGroupFeatures")]
    public class AccessGroupFeature
    {
        public int GroupId { get; set; }

        /// <summary>FK to Features table (PermissionId)</summary>
        public int PermissionId { get; set; }

        [ForeignKey("GroupId")]
        public AccessGroup? Group { get; set; }

        [ForeignKey("PermissionId")]
        public Feature? Feature { get; set; }
    }
}
