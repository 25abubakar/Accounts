using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("AppNoteTargets")]
    public class AppNoteTarget
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int NoteTargetId { get; set; }

        public int NoteId { get; set; }

        [Required, MaxLength(100)]
        public string TargetTypeCode { get; set; } = string.Empty;

        [Required, MaxLength(150)]
        public string TargetValue { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;

        [ForeignKey("NoteId")]
        public AppNote? Note { get; set; }
    }
}
