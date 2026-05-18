using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("AccessGroupFeatures")]
    public class AccessGroupFeature
    {
        public int GroupId { get; set; }

        [MaxLength(100)]
        public string FeatureKey { get; set; } = string.Empty;

        [ForeignKey("GroupId")]
        public AccessGroup? Group { get; set; }

        [ForeignKey("FeatureKey")]
        public Feature? Feature { get; set; }
    }
}
