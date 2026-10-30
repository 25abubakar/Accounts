using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("tblMessage")]
public class tblMessage
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public string? SenderEmail { get; set; }

    public string? ReceiverEmail { get; set; }

    public string? Subject { get; set; }

    public string? Body { get; set; }

    public DateTime? SentAt { get; set; }

    public int? OrganizationId { get; set; }

    public bool? isRead { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedDate { get; set; }

    public string? UpdatedBy { get; set; }

    public DateTime? UpdatedDate { get; set; }

    [NotMapped]
    public List<tblEmailDocuments> Attachments { get; set; } = new();
}

[Table("tblEmailDocuments")]
public class tblEmailDocuments
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int? EmailId { get; set; }

    public string? Attachment { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? CreatedDate { get; set; }

    public string? UpdatedBy { get; set; }

    public DateTime? UpdatedDate { get; set; }
}
