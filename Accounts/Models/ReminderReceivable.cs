using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("ReminderReceivables")]
public sealed class ReminderReceivable : ITenantEntity
{
    [Key]
    public int Id { get; set; }
    public int TenantId { get; set; }
    [MaxLength(80)] public string? Ref { get; set; }
    public int? AccountTypeId { get; set; }
    public int? ReminderTypeId { get; set; }
    public int? InvoiceTypeId { get; set; }
    public int? CategoryId { get; set; }
    public int? FromAccountId { get; set; }
    public int? ToCategoryId { get; set; }
    public int? ToAccountId { get; set; }
    public DateOnly? CreatedOn { get; set; }
    public DateOnly? DueDate { get; set; }
    public int? RemindDay { get; set; }
    public DateOnly? RemindDate { get; set; }
    public DateOnly? ReceivedOn { get; set; }
    public DateOnly? LastPaidDate { get; set; }
    public DateOnly? PaidOn { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    public int? FrequencyTypeId { get; set; }
    [MaxLength(120)] public string? InvoiceType { get; set; }
    public int? TransTypeId { get; set; }
    [MaxLength(2000)] public string? Remarks { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AlertRoznamcha { get; set; }
    public int? CurrencyId { get; set; }
    public int? StatusId { get; set; }
    [MaxLength(500)] public string? Attachment { get; set; }
    public bool IsInactive { get; set; }
    public bool IsNotRoznamcha { get; set; }
    [MaxLength(450)] public string? CreatedByUserId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(450)] public string? UpdatedByUserId { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
}
