namespace Accounts.Models.SpListRows;

/// <summary>
/// Projection for dbo.usp_Accounts_PaymentRoz_List — must match SP column aliases exactly.
/// </summary>
public sealed class PaymentRozListRow
{
    public long Id { get; set; }
    public int? SNo { get; set; }
    public string? Ref { get; set; }
    public string? OldRef { get; set; }
    public string? TypeName { get; set; }
    public string? Project { get; set; }
    public int? ProjectId { get; set; }
    public int? ToCategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? AccountName { get; set; }
    public int? ToAccountId { get; set; }
    public string? FromAccountNumber { get; set; }
    public string? ToCategoryName { get; set; }
    public string? ToAccountName { get; set; }
    public string? ToAccountNumber { get; set; }
    public string? TransTypeName { get; set; }
    public string? TransModeName { get; set; }
    public DateTime? CreatedDate { get; set; }
    public string? BankRef { get; set; }
    public string? InstrumentNo { get; set; }
    public string? Descriptions { get; set; }
    public DateOnly TransDate { get; set; }
    public decimal? Adjustment { get; set; }
    public decimal? Debit { get; set; }
    public decimal? UsdDebit { get; set; }
    public decimal? Qty { get; set; }
    public decimal? Rate { get; set; }
    public string? StatusName { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsApproved { get; set; }
    public bool IsLocked { get; set; }
    public string? Attachment { get; set; }
    public string? Image { get; set; }
    public string? LibRef { get; set; }
    public string? Remarks { get; set; }
}
