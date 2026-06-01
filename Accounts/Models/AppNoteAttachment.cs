using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models
{
    [Table("AppNoteAttachments")]
    public class AppNoteAttachment
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AttachmentId { get; set; }

        public int NoteId { get; set; }

        [MaxLength(250)]
        public string? FileName { get; set; }

        [MaxLength(500)]
        public string? FilePath { get; set; }

        [MaxLength(100)]
        public string? FileType { get; set; }

        public long? FileSizeBytes { get; set; }

        [MaxLength(500)]
        public string? ExternalUrl { get; set; }

        public bool IsActive { get; set; } = true;

        [MaxLength(100)]
        public string? CreatedBy { get; set; }

        public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;

        [ForeignKey("NoteId")]
        public AppNote? Note { get; set; }
    }
}
