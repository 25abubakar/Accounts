using Microsoft.AspNetCore.Http;

namespace Accounts.DTOs;

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}

public sealed class CopayFilterDto
{
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public bool? IsSettled { get; set; }
    public int? StatusId { get; set; }
    public string? SearchTerm { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class DateRangeDto
{
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
    /// <summary>Configurable window in days to look ahead for settlement matching.</summary>
    public int? ToleranceDays { get; set; }
}

public sealed class MerchantToBankItemDto
{
    public long Id { get; set; }
    public string? Ref { get; set; }
    public DateOnly TransDate { get; set; }
    public decimal? UsdAmount { get; set; }
    public decimal? Amount { get; set; }
    public string? BankLtRef { get; set; }
    public string? BankRef { get; set; }
    public int? EnterStatusId { get; set; }
    public string? EnterStatusName { get; set; }
    public bool IsSettled { get; set; }
    public string? Remarks { get; set; }
    public long? MatchedBankStatementId { get; set; }
    public DateOnly? BankPostingDate { get; set; }
    public decimal? BankCredit { get; set; }
    public decimal? BankDebit { get; set; }
    public string? StatusColor { get; set; }
    public string? StatusFontColor { get; set; }
}

public sealed class MerchantStatementItemDto
{
    public long Id { get; set; }
    public string? Ref { get; set; }
    public int? AcctId { get; set; }
    public string? CardNumber { get; set; }
    public string? Currency { get; set; }
    public string? PaymentNo { get; set; }
    public DateOnly? PaymentDate { get; set; }
    public decimal? TransactionAmount { get; set; }
    public string? FundSource { get; set; }
    public string? BusinessName { get; set; }
    public string? MerchantID { get; set; }
    public string? PaymentType { get; set; }
    public string? AdjustmentDescription { get; set; }
    public string? GBOK_BatchID { get; set; }
    public string? InterchangeDescription { get; set; }
    public string? ARNNumber { get; set; }
    public string? CopayRef { get; set; }
    public string? Attachment { get; set; }
    public string? Remarks { get; set; }
    public bool IsSettled { get; set; }
    public int? StatusId { get; set; }
    public string? StatusName { get; set; }
    public bool IsManual { get; set; }
    public long? MatchedRoznamchaEntryId { get; set; }
    public string? AccountName { get; set; }
    public string? StatusColor { get; set; }
    public string? StatusFontColor { get; set; }
}

public sealed class ImsReceiptItemDto
{
    public long Id { get; set; }
    public int? SNo { get; set; }
    public string? Ref { get; set; }
    public string? RoznamchaTypeName { get; set; }
    public string? CategoryName { get; set; }
    public int? FromAccountId { get; set; }
    public string? FromAccountName { get; set; }
    public string? FromAccountNumber { get; set; }
    public string? ToCategoryName { get; set; }
    public int? ToAccountId { get; set; }
    public string? ToAccountName { get; set; }
    public string? ToAccountNumber { get; set; }
    public int? TransTypeId { get; set; }
    public string? TransTypeName { get; set; }
    public int? TransModeId { get; set; }
    public string? TransModeName { get; set; }
    public DateTime? CreatedDate { get; set; }
    public string? InstrumentNo { get; set; }
    public string? Description { get; set; }
    public string? BankLtRef { get; set; }
    public DateOnly TransDate { get; set; }
    public decimal? Adjustment { get; set; }
    public decimal? Amount { get; set; }
    public decimal? UsdAmount { get; set; }
    public decimal? PaidCopayAmount { get; set; }
    public string? PatientInfo { get; set; }
    public string? Reference { get; set; }
    public string? ReferralSource { get; set; }
    public decimal? Qty { get; set; }
    public decimal? Rate { get; set; }
    public int? EnterStatusId { get; set; }
    public string? EnterStatusName { get; set; }
    public bool IsDeleted { get; set; }
    public string? Attachment { get; set; }
    public string? Image { get; set; }
    public string? LibRef { get; set; }
    public string? Remarks { get; set; }
    public bool IsSettled { get; set; }
    public int? CurrencyId { get; set; }
    public string? CurrencyCode { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public bool IsManual { get; set; }
    public string? StatusColor { get; set; }
    public string? StatusFontColor { get; set; }
}

public sealed class UploadMerchantStatementDto
{
    public required IFormFile File { get; set; }
    public IFormFile? Attachment { get; set; }
    public int? AccountId { get; set; }
    public int? YearId { get; set; }
}

public sealed class UploadImsSheetDto
{
    public required IFormFile File { get; set; }
    public IFormFile? Attachment { get; set; }
    public int? YearId { get; set; }
}

public sealed class UploadMerchantPayoutDto
{
    public required IFormFile File { get; set; }
    public IFormFile? Attachment { get; set; }
    public int? YearId { get; set; }
    public bool AutoSettle { get; set; } = true;
}

public sealed class ManualSettleRequestDto
{
    public List<long> EntryIds { get; set; } = [];
    public string? TargetRef { get; set; }
    public string? Remarks { get; set; }
    public bool CreateSummaryBatch { get; set; } = true;
    public string? TargetType { get; set; }
    public long? TargetId { get; set; }
}

public sealed class UnsettleRequestDto
{
    public long? RoznamchaEntryId { get; set; }
    public long? StatementId { get; set; }
    public string? Remarks { get; set; }
}

public sealed class SettlementResultDto
{
    public int MatchedCount { get; set; }
    public int FailedCount { get; set; }
    public decimal TotalSettledAmount { get; set; }
    public List<string> Messages { get; set; } = [];
}

public sealed class CopayImportResultDto
{
    public long BatchId { get; set; }
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public int DuplicateRows { get; set; }
    public int InvalidRows { get; set; }
    public int AutoSettledRows { get; set; }
    public List<string> Errors { get; set; } = [];
}

public sealed class CopayProcessLogDto
{
    public long Id { get; set; }
    public long? RoznamchaEntryId { get; set; }
    public long? CopayStatementId { get; set; }
    public string? SourceReference { get; set; }
    public string? TargetReference { get; set; }
    public string? LinkType { get; set; }
    public bool IsActive { get; set; } = true;
    public string Action { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public int? StatusId { get; set; }
    public string? StatusName { get; set; }
    public string? ProcessedByUserId { get; set; }
    public DateTime ProcessedOnUtc { get; set; }
}

public sealed class LookupItemDto
{
    public int Id { get; set; }
    public string DisplayText { get; set; } = string.Empty;
}

public sealed class CopayLookupsDto
{
    public List<LookupItemDto> Accounts { get; set; } = [];
    public List<LookupItemDto> Statuses { get; set; } = [];
    public List<LookupItemDto> TransactionModes { get; set; } = [];
    public List<int> AvailableYears { get; set; } = [];
}
