namespace Accounts.Models.SpListRows;

/// <summary>
/// Projection for dbo.usp_Reminders_ReceivableList — must match SP column aliases exactly.
/// </summary>
public sealed class ReminderReceivableListRow
{
    public int Id { get; set; }
    public string? Ref { get; set; }
    public int? AccountTypeId { get; set; }
    public string? AccountType { get; set; }
    public int? ReminderTypeId { get; set; }
    public string? ReminderType { get; set; }
    public int? InvoiceTypeId { get; set; }
    public int? CategoryId { get; set; }
    public string? Category { get; set; }
    public int? FromAccountId { get; set; }
    public string? FromAcctName { get; set; }
    public string? FromAcctNo { get; set; }
    public int? ToCategoryId { get; set; }
    public string? ToCategory { get; set; }
    public int? ToAccountId { get; set; }
    public string? ToAcctName { get; set; }
    public string? ToAcctNo { get; set; }
    public DateOnly? CreatedOn { get; set; }
    public DateOnly? DueDate { get; set; }
    public int? RemindDay { get; set; }
    public DateOnly? RemindDate { get; set; }
    public DateOnly? ReceivedOn { get; set; }
    public DateOnly? LastPaidDate { get; set; }
    public DateOnly? PaidOn { get; set; }
    public decimal Amount { get; set; }
    public int? FrequencyTypeId { get; set; }
    public string? Frequency { get; set; }
    public string? InvoiceType { get; set; }
    public int? TransTypeId { get; set; }
    public string? TransactionType { get; set; }
    public string? Remarks { get; set; }
    public bool IsActive { get; set; }
    public bool AlertRoznamcha { get; set; }
}
