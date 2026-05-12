using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    /// <summary>
    /// Stores the last-used sequence number per vacancy code prefix.
    /// Used to guarantee unique, gap-free, incrementing vacancy codes
    /// even under concurrent requests.
    ///
    /// Example rows:
    ///   Prefix                      | LastNumber
    ///   Pakistan-LalGroup-LT-       | 3
    ///   Pakistan-LalGroup-NS-       | 1
    ///   Pakistan-TechGroup-TS-      | 7
    /// </summary>
    [Table("VacancyCounters")]
    public class VacancyCounter
    {
        [Key]
        [MaxLength(200)]
        public string Prefix { get; set; } = string.Empty;

        /// <summary>The last sequence number issued for this prefix. Starts at 0.</summary>
        public int LastNumber { get; set; } = 0;
    }
}
