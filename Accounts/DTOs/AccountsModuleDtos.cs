using Microsoft.AspNetCore.Http;

namespace Accounts.DTOs;

public sealed record ApiResponse<T>(bool Success, string Message, T? Data)
{
    public static ApiResponse<T> Ok(T data, string message = "Request completed successfully.") => new(true, message, data);
    public static ApiResponse<T> Fail(string message) => new(false, message, default);
}

public sealed class SaveAccountCategoryTypeRequest
{
    public string CategoryType { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class AccountCategoryTypeDto
{
    public int Id { get; set; }
    public string CategoryType { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

/// <summary>Account Type workspace lookup row (Roznamcha / Trans Type / Mode / Date / Currency).</summary>
public sealed class AccountTypeLookupDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsPlatform { get; set; }
}

public sealed class SaveAccountTypeLookupRequest
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class SaveAccountCategoryRequest
{
    public int? CategoryTypeId { get; set; }
    public string? ReferenceNo { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Number { get; set; }
    public string Code { get; set; } = string.Empty;
    public decimal BudgetAmount { get; set; }
    public decimal UsedAmount { get; set; }
    public bool IsNotInReport { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class AccountCategoryDto
{
    public int Id { get; set; }
    public int? CategoryTypeId { get; set; }
    public string? CategoryType { get; set; }
    public string? ReferenceNo { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Number { get; set; }
    public string Code { get; set; } = string.Empty;
    public decimal BudgetAmount { get; set; }
    public decimal UsedAmount { get; set; }
    public decimal BalanceAmount { get; set; }
    public bool IsNotInReport { get; set; }
    public bool IsActive { get; set; }
}

public sealed class SaveAccountRequest
{
    public int? CategoryId { get; set; }
    public int? ParentId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string? AccountCode { get; set; }
    public string? AccountNo { get; set; }
    public string? BankAccountNo { get; set; }
    public string? CnicNtn { get; set; }
    public string? Address { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int? DesignationId { get; set; }
    public Guid? PersonId { get; set; }
    public string? Description { get; set; }
    public string? Attachment { get; set; }
    /// <summary>Legacy Old Acct No — stored in AccountReference when provided.</summary>
    public string? OldAccountNo { get; set; }
    public decimal BudgetAmount { get; set; }
    public decimal AccountLimit { get; set; }
    public decimal Credit { get; set; }
    public decimal Debit { get; set; }
    public int StatusId { get; set; } = 11;
    public bool IsStatement { get; set; }
    public bool IsInventory { get; set; }
    public bool IsStaff { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AccountDto
{
    public int Id { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int? ParentId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string AccountNo { get; set; } = string.Empty;
    public string? AccountCode { get; set; }
    public string? AccountReference { get; set; }
    public string? BankAccountNo { get; set; }
    public string? CnicNtn { get; set; }
    public string? Address { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int? DesignationId { get; set; }
    public Guid? PersonId { get; set; }
    public string? Description { get; set; }
    public string? Photo { get; set; }
    public string? Attachment { get; set; }
    public decimal BudgetAmount { get; set; }
    public decimal UsedAmount { get; set; }
    public decimal BalanceAmount { get; set; }
    public decimal AccountLimit { get; set; }
    public decimal Credit { get; set; }
    public decimal Debit { get; set; }
    public int StatusId { get; set; }
    public bool IsStatement { get; set; }
    public bool IsInventory { get; set; }
    public bool IsStaff { get; set; }
    public bool IsActive { get; set; }
}

public sealed class AccountTreeDto : AccountDto
{
    public List<AccountTreeDto> Children { get; set; } = [];
}

public sealed class UpdateAccountBudgetRequest
{
    public decimal BudgetAmount { get; set; }
    public decimal AccountLimit { get; set; }
}

public sealed class LedgerTransferRequest
{
    public long EntryId { get; set; }
    public int TransferAccountId { get; set; }
    public string TransferSide { get; set; } = string.Empty;
    public bool IncludeHidden { get; set; }
}

public sealed class HiddenCategoryRowDto
{
    public int Id { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryCode { get; set; } = string.Empty;
    public int HiddenAccountsCount { get; set; }
    public int TotalAccountsCount { get; set; }

    // Legacy aliases matching Old Software frmCategoryList
    public string Cat_Name => CategoryName;
    public string Cat_Code => CategoryCode;
}

public sealed class HiddenAccountRowDto
{
    public int Id { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string? AccountCode { get; set; }
    public string? AccountReference { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int? ParentId { get; set; }
    public bool HasSubAccounts { get; set; }
    public int SubAccountsCount { get; set; }
    public decimal BalanceAmount { get; set; }
    public int StatusId { get; set; }
    public bool IsActive { get; set; }

    // Legacy aliases matching Old Software frmShowHiddenAccount
    public string AcctName => AccountName;
    public string AcctNo => AccountNumber;
    public string? Acct_Code => AccountCode;
    public string? Acc_Ref => AccountReference;
}

public sealed class HiddenLedgerTransferRequest
{
    public long EntryId { get; set; }
    public int TransferAccountId { get; set; }
    /// <summary>"to" (default) or "from".</summary>
    public string TransferSide { get; set; } = "to";
    /// <summary>Whether to unhide the voucher so it appears in standard roznamcha (default true, matching legacy behavior).</summary>
    public bool UnhideEntry { get; set; } = true;
}

public sealed class AccountLedgerDto
{
    public long EntryId { get; set; }
    public int? SNo { get; set; }
    public string? ReferenceNo { get; set; }
    public string? OldRef { get; set; }
    public string? Type { get; set; }
    public DateOnly TransactionDate { get; set; }
    public string? Category { get; set; }
    public string? FromAcc { get; set; }
    public string? FromAccNo { get; set; }
    public string? ToAcct { get; set; }
    public string? ToAcctNo { get; set; }
    public string? TransType { get; set; }
    public string? TransMode { get; set; }
    public DateOnly? InstrumentDate { get; set; }
    public string? InstrumentNo { get; set; }
    public string? Descriptions { get; set; }
    public string? OtherAccount { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal RunningBalance { get; set; }
    public bool IsHidden { get; set; }
    public string? Attachment { get; set; }
    public string? Remarks { get; set; }
    public string? StatusName { get; set; }
    public bool IsDeleted { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? Rate { get; set; }
    public string? TaxType { get; set; }
    public decimal? TaxRate { get; set; }
    public decimal? TaxAmt { get; set; }
    public decimal? Adjustment { get; set; }
    public DateTime? CreatedDate { get; set; }

    // Legacy aliases matching Old Software CustomerhiddenLedger
    public long Id => EntryId;
    public string? Ref => ReferenceNo;
    public string? Old_Ref => OldRef;
    public string? RoznamchaType => Type;
    public DateOnly TransDate => TransactionDate;
    public string? Cat_Name => Category;
    public string? AcctName => FromAcc;
    public string? AcctNo => FromAccNo;
    public string? ToAcctName => ToAcct;
    public string? RDiscriptions => Descriptions;
    public decimal Cr_DbAmt => Debit > 0 ? Debit : Credit;
    public decimal Balance => RunningBalance;
    public decimal? Qty => Quantity;
    public decimal? rate => Rate;
    public bool isDeleted => IsDeleted;
}

public class SaveRoznamchaEntryRequest
{
    public string? OldReferenceNo { get; set; }
    public int? CategoryId { get; set; }
    public int FromAccountId { get; set; }
    public int ToAccountId { get; set; }
    public int? CustomerId { get; set; }
    public Guid? StaffId { get; set; }
    public int? TransactionTypeId { get; set; }
    public int? TransactionModeId { get; set; }
    public DateOnly? InstrumentDate { get; set; }
    public string? InstrumentNo { get; set; }
    public string? Description { get; set; }
    public int? CurrencyId { get; set; }
    public int? TaxTypeId { get; set; }
    public decimal? TaxRate { get; set; }
    public decimal Adjustment { get; set; }
    public decimal Amount { get; set; }
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public int? EntryStatusId { get; set; }
    public string? Remarks { get; set; }
    public string? BankReference { get; set; }
    public string? BankLTReference { get; set; }
    public DateOnly TransactionDate { get; set; }
    public bool IsShownHidden { get; set; }
    public bool IsSettled { get; set; }
    public bool IsFromLibrary { get; set; }
    public string? LibraryReference { get; set; }
    public List<long>? LibraryDocumentIds { get; set; }
    public bool IsLedger { get; set; }
    public bool IsManual { get; set; }
    public int? ProjectId { get; set; }
    public long? ProductId { get; set; }
}

public sealed class CreateRoznamchaPaymentRequest : SaveRoznamchaEntryRequest { }
public sealed class CreateRoznamchaReceiptRequest : SaveRoznamchaEntryRequest { }

public sealed class PaymentRozExcelImportResult
{
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public int SkippedRows { get; set; }
    public int DuplicateRows { get; set; }
    public int InventoryLinksRestored { get; set; }
    public List<string> Errors { get; set; } = [];
}

public sealed class RoznamchaEntryDto
{
    public long Id { get; set; }
    public Guid? InternalTransferGroupId { get; set; }
    public int? SerialNo { get; set; }
    public string? ReferenceNo { get; set; }
    public string? OldReferenceNo { get; set; }
    public int EntryTypeId { get; set; }
    public int? CategoryId { get; set; }
    public int? FromAccountId { get; set; }
    public string? FromAccountName { get; set; }
    public int? ToAccountId { get; set; }
    public string? ToAccountName { get; set; }
    public int? TransactionTypeId { get; set; }
    public int? TransactionModeId { get; set; }
    public DateOnly? InstrumentDate { get; set; }
    public string? InstrumentNo { get; set; }
    public int? CurrencyId { get; set; }
    public int? TaxTypeId { get; set; }
    public decimal? TaxRate { get; set; }
    public int? EntryStatusId { get; set; }
    public int? ProjectId { get; set; }
    public decimal Amount { get; set; }
    public decimal UsdAmount { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Adjustment { get; set; }
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public DateOnly TransactionDate { get; set; }
    public string? Description { get; set; }
    public string? Remarks { get; set; }
    public string? BankReference { get; set; }
    public string? BankLTReference { get; set; }
    public bool IsFromLibrary { get; set; }
    public string? LibraryReference { get; set; }
    public IReadOnlyList<LibraryDocumentLinkDto> LibraryDocuments { get; set; } = [];
    public bool IsApproved { get; set; }
    public bool IsSettled { get; set; }
    public bool IsShownHidden { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsLocked { get; set; }
    public long? ProductId { get; set; }
}

public sealed class LibraryDocumentLinkDto
{
    public long Id { get; set; }
    public string DocumentReference { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
}

public sealed class UpdateRoznamchaStatusRequest
{
    public int? EntryStatusId { get; set; }
    public bool? IsSettled { get; set; }
    public bool? IsShownHidden { get; set; }
    public bool? IsLocked { get; set; }
    public string? Remarks { get; set; }
}

public sealed class EntryAttachmentDto
{
    public long Id { get; set; }
    public long? EntryId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime UploadedOnUtc { get; set; }
    public string? DocumentReference { get; set; }
    public string? Remarks { get; set; }
}

public sealed class RoznamchaProcessLogDto
{
    public long Id { get; set; }
    public long EntryId { get; set; }
    public string? ReferenceNo { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? ProcessedByUserId { get; set; }
    public DateTime ProcessedOnUtc { get; set; }
}

public sealed class DeleteRoznamchaRequest
{
    public string? Remarks { get; set; }
}

public sealed class BulkRoznamchaStatusRequest
{
    public List<long> Ids { get; set; } = [];
    public int EntryStatusId { get; set; }
    public string? Comments { get; set; }
    public bool LockWhenApproved { get; set; }
}

public sealed class RoznamchaUpdateListRow
{
    public long Id { get; set; }
    public int? SNo { get; set; }
    public string? Ref { get; set; }
    public string? OldRef { get; set; }
    public string? TypeName { get; set; }
    public int? FromAccountId { get; set; }
    public int? ToAccountId { get; set; }
    public string? CategoryName { get; set; }
    public string? ToCategoryName { get; set; }
    public string? AccountName { get; set; }
    public string? FromAccountNumber { get; set; }
    public string? ToAccountName { get; set; }
    public string? ToAccountNumber { get; set; }
    public string? TransTypeName { get; set; }
    public string? TransModeName { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateOnly TransDate { get; set; }
    public string? InstrumentNo { get; set; }
    public string? Description { get; set; }
    public decimal? Credit { get; set; }
    public decimal? UsdCredit { get; set; }
    public decimal? Adjustment { get; set; }
    public decimal? Qty { get; set; }
    public decimal? Rate { get; set; }
    public string? CheckNo { get; set; }
    public string? BankLtRef { get; set; }
    public string? BankRef { get; set; }
    public string? LibRef { get; set; }
    public string? Attachment { get; set; }
    public string? Remarks { get; set; }
    public int? StatusId { get; set; }
    public string? StatusName { get; set; }
    public string? StatusColorCode { get; set; }
    public string? StatusFontColor { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsSettled { get; set; }
    public DateTime? ProcessDate { get; set; }
    public DateOnly? RemindDate { get; set; }
}

public sealed class UpdateRoznamchaInlineRequest
{
    public string? InstrumentNo { get; set; }
    public string? Description { get; set; }
    public string? CheckNo { get; set; }
    public string? BankLtRef { get; set; }
}

public sealed class ChangeRozProcessLogRequest
{
    public int UpdatedStatusId { get; set; }
    public string? Remarks { get; set; }
    public DateOnly? RemindDate { get; set; }
    public string? CheckNo { get; set; }
    public string? SecurityCode { get; set; }
    public int? DepartmentId { get; set; }
    public List<Guid>? StaffIds { get; set; }
}

public sealed class RozEntryProcessLogDto
{
    public long Id { get; set; }
    public DateTime ProcessedOnUtc { get; set; }
    public int? StatusId { get; set; }
    public string? StatusName { get; set; }
    public string? StatusColorCode { get; set; }
    public string? StatusFontColor { get; set; }
    public DateOnly? RemindDate { get; set; }
    public string? Remarks { get; set; }
    public string? OldCheckNo { get; set; }
    public string? NewCheckNo { get; set; }
    public string? ProcessedBy { get; set; }
}

public sealed class UploadAccountsFileRequest
{
    public IFormFile File { get; set; } = null!;
    public string? Remarks { get; set; }
}

public sealed class CreateRecurringTransactionRequest
{
    public string Kind { get; set; } = "payable";
    public string? ReferenceNo { get; set; }
    public int? AccountTypeId { get; set; }
    public int? TypeId { get; set; }
    public int? InvoiceTypeId { get; set; }
    public int? CategoryId { get; set; }
    public int? FromAccountId { get; set; }
    public int? ToCategoryId { get; set; }
    public int? ToAccountId { get; set; }
    public int? FrequencyId { get; set; }
    public int? TransactionTypeId { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateOnly? LastPaidDate { get; set; }
    public DateOnly? PaidOn { get; set; }
    public int ReminderDays { get; set; }
    public int? StatusId { get; set; }
    public decimal Amount { get; set; }
    public int? CurrencyId { get; set; }
    public string? Attachment { get; set; }
    public string? Remarks { get; set; }
    public bool IsInactive { get; set; }
    public bool IsNotRoznamcha { get; set; }
}

public sealed class SettleRecurringTransactionRequest
{
    public DateOnly? PaidOn { get; set; }
    public int? TransactionModeId { get; set; }
    public string? Remarks { get; set; }
    public bool GenerateNext { get; set; } = true;
}

public sealed class UpdateRecurringStatusRequest
{
    public bool IsInactive { get; set; }
}

public sealed class RecurringTransactionDto
{
    public int Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ReferenceNo { get; set; }
    public int? AccountTypeId { get; set; }
    public int? TypeId { get; set; }
    public int? InvoiceTypeId { get; set; }
    public int? CategoryId { get; set; }
    public int? FromAccountId { get; set; }
    public int? ToCategoryId { get; set; }
    public int? ToAccountId { get; set; }
    public int? FrequencyId { get; set; }
    public string? Frequency { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateOnly? LastPaidDate { get; set; }
    public DateOnly? PaidOn { get; set; }
    public DateOnly? RemindDate { get; set; }
    public int ReminderDays { get; set; }
    public decimal Amount { get; set; }
    public int? CurrencyId { get; set; }
    public int? StatusId { get; set; }
    public string? Attachment { get; set; }
    public string? Remarks { get; set; }
    public bool IsInactive { get; set; }
    public bool IsNotRoznamcha { get; set; }
}

public class ReportFilterRequest
{
    public string Name { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public int? AccountId { get; set; }
    public int? TypeId { get; set; }
    public List<int> CategoryIds { get; set; } = [];
    public List<int> AccountIds { get; set; } = [];
    public List<int> SubAccountIds { get; set; } = [];
    public bool IsAllCategoryAccounts { get; set; }
}

public sealed class ReportFilterDto : ReportFilterRequest
{
    public int Id { get; set; }
}

public sealed class ReportFilterNameLookupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? TypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
}

public sealed class ReportFilterDetailRowDto
{
    public int FilterId { get; set; }
    public int AccountMappingId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public int? TypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public DateTime? CreatedDate { get; set; }
    public string? CreatedBy { get; set; }

    // Legacy Grid Aliases for 100% backward compatibility
    public int Id => AccountMappingId;
    public string Name => RuleName;
    public string Cat_Name => CategoryName;
    public int CatId => CategoryId;
    public string AcctName => AccountName;
    public string AcctNo => AccountNumber;
    public int AcctId => AccountId;
}

public sealed class AddAccountsToFilterRequest
{
    public int? FilterId { get; set; }
    public string? RuleName { get; set; }
    public string? Name { get; set; }
    public List<int> CategoryIds { get; set; } = [];
    public List<int> AccountIds { get; set; } = [];
    public List<int> SubAccountIds { get; set; } = [];
    public bool IsAllCategoryAccounts { get; set; }
}

public sealed class UpdateReportInlineRequest
{
    public decimal? Debit { get; set; }
    public decimal? Credit { get; set; }
    public string? Description { get; set; }
    public string? Remarks { get; set; }
    public int? StatusId { get; set; }
}

public sealed class MonthlyReportFilterPostRequest
{
    public string? Name { get; set; }
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public int? TypeId { get; set; }
    public int? ProjectId { get; set; }
    public List<int>? CategoryIds { get; set; }
    public List<int>? AccountIds { get; set; }
}

public sealed class ReportRowDto
{
    public long? Id { get; set; }
    public int? SNo { get; set; }
    public string? OldRef { get; set; }
    public string? FromAccountNumber { get; set; }
    public string? ToCategory { get; set; }
    public string? ToAccountNumber { get; set; }
    public string? TransType { get; set; }
    public string? TransMode { get; set; }
    public DateTime? CreatedOnUtc { get; set; }
    public string? CreatedBy { get; set; }
    public string? InstrumentNo { get; set; }
    public string? TaxType { get; set; }
    public decimal? TaxRate { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? Adjustment { get; set; }
    public decimal? Qty { get; set; }
    public decimal? Rate { get; set; }
    public string? Status { get; set; }
    public int? StatusId { get; set; }
    public int? EnterStatusId { get; set; }
    public string? StatusColorCode { get; set; }
    public string? StatusFontColor { get; set; }
    public string? Attachment { get; set; }
    public string? Remarks { get; set; }
    public DateOnly Date { get; set; }
    public int? CategoryId { get; set; }
    public string? Category { get; set; }
    public int? AccountId { get; set; }
    public string? Account { get; set; }
    public int? FromAccountId { get; set; }
    public int? ToAccountId { get; set; }
    public int? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public bool? IsDeleted { get; set; }
    public int EntryTypeId { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance { get; set; }
    public decimal UsdDebit { get; set; }
    public decimal UsdCredit { get; set; }
    public decimal UsdBalance { get; set; }
    public string? EntryType { get; set; }
    public string? FromAccount { get; set; }
    public string? ToAccount { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Description { get; set; }

    // Legacy Grid Aliases for 100% backward compatibility
    public string? Ref => ReferenceNo;
    public string? Old_Ref => OldRef;
    public string? Cat_Name => Category;
    public string? AcctName => FromAccount;
    public string? AcctNo => FromAccountNumber;
    public string? ToAcctName => ToAccount;
    public string? ToAcctNo => ToAccountNumber;
    public string? RDiscriptions => Description;
    public decimal? Bal_Amt => Balance;
    public string? StatusName => Status;
    public DateOnly TransDate => Date;
}

public sealed class AccountsDailyUpdateDto
{
    public long Id { get; set; }
    public int? SNo { get; set; }
    public string? ReferenceNo { get; set; }
    public string? OldRef { get; set; }
    public int? RoznamchaTypeId { get; set; }
    public string? EntryType { get; set; }          // "Payment" or "Receipt"
    public DateOnly TransactionDate { get; set; }   // Voucher TransDate
    public DateTime CreatedOnUtc { get; set; }      // System insertion timestamp
    public string? CreatedBy { get; set; }          // Operator user name / email
    public int? CategoryId { get; set; }
    public string? Category { get; set; }
    public int? ToCategoryId { get; set; }
    public string? ToCategory { get; set; }
    public int? FromAccountId { get; set; }
    public string? FromAccount { get; set; }
    public string? FromAccountNumber { get; set; }
    public int? ToAccountId { get; set; }
    public string? ToAccount { get; set; }
    public string? ToAccountNumber { get; set; }
    public decimal? Debit { get; set; }             // Amount if Payment
    public decimal? Credit { get; set; }            // Amount if Receipt
    public decimal? UsdDebit { get; set; }
    public decimal? UsdCredit { get; set; }
    public decimal? Balance { get; set; }
    public decimal? Adjustment { get; set; }
    public string? InstrumentNo { get; set; }
    public string? Description { get; set; }
    public string? Remarks { get; set; }
    public int? EnterStatusId { get; set; }
    public string? Status { get; set; }
    public string? StatusColorCode { get; set; }
    public string? StatusFontColor { get; set; }
    public string? Attachment { get; set; }
    public bool IsDeleted { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }

    // Backward compatibility aliases
    public long LogId => Id;
    public long EntryId => Id;
    public decimal Amount => (Debit ?? 0) + (Credit ?? 0);
    public decimal UsdAmount => (UsdDebit ?? 0) + (UsdCredit ?? 0);
    public string Action => "Create";
    public string? Notes => Remarks;
    public string? ChangedByUserId => CreatedBy;
    public DateTime ChangedOnUtc => CreatedOnUtc;

    // Legacy Grid Aliases for 100% Old Software Parity (AccountReports/DailyReports)
    public string? Ref => ReferenceNo;
    public string? Old_Ref => OldRef;
    public string? RoznamchaType => EntryType;
    public int? R_TypeId => RoznamchaTypeId;
    public string? Cat_Name => Category;
    public int? from_AccId => FromAccountId;
    public string? AcctName => FromAccount;
    public string? AcctNo => FromAccountNumber;
    public int? To_AcctId => ToAccountId;
    public string? ToAcctName => ToAccount;
    public string? ToAcctNo => ToAccountNumber;
    public string? RDiscriptions => Description;
    public decimal? Bal_Amt => Balance;
    public string? StatusName => Status;
    public string? ColorCode => StatusColorCode;
    public string? FontColor => StatusFontColor;
    public DateOnly TransDate => TransactionDate;
    public DateTime? CreatedDate => CreatedOnUtc;
    public DateTime? UpdatedDate => UpdatedOnUtc;
    public bool isDeleted => IsDeleted;
}

public sealed class AccountsVoucherChangeLogDto
{
    public long Id { get; set; }
    public long EntryId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? OldCheckNo { get; set; }
    public string? NewCheckNo { get; set; }
    public DateOnly? RemindDate { get; set; }
    public int? StatusId { get; set; }
    public string? ProcessedBy { get; set; }
    public DateTime ChangeDate { get; set; }
    public DateTime ProcessedOnUtc { get; set; }

    // Legacy tblChangeLog aliases
    public string TableName => "tblRoznamcha";
    public string? OperationType => Action;
}

public sealed class AccountsRecordRowDto
{
    public long Id { get; set; }
    public int? SerialNo { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Type { get; set; }
    public DateOnly TransactionDate { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public string? Category { get; set; }
    public string? FromAccount { get; set; }
    public string? ToAccount { get; set; }
    public decimal Amount { get; set; }
    public decimal UsdAmount { get; set; }
    public string? Status { get; set; }
    public string? Description { get; set; }
    public string? Remarks { get; set; }
    public bool IsHidden { get; set; }
    public bool IsDeleted { get; set; }
}

public class ProjectCostRuleRequest
{
    public string Name { get; set; } = string.Empty;
    public int? TypeId { get; set; }
    public int? CategoryId { get; set; }
    public int? AccountId { get; set; }
    public long? EntryId { get; set; }
    public decimal PercentageValue { get; set; }
}

public sealed class ProjectCostRuleDto : ProjectCostRuleRequest
{
    public int Id { get; set; }
}

public sealed class StoredFileDto
{
    public string FileName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSizeBytes { get; set; }
}

public sealed class AccountsProjectDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class SaveAccountsProjectRequest
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class BankStatementDto
{
    public long Id { get; set; }
    public int? AccountId { get; set; }
    public string? AccountName { get; set; }
    public string? AccountReference { get; set; }
    public DateOnly? ValueDate { get; set; }
    public DateOnly? PostingDate { get; set; }
    public string? InstrumentNo { get; set; }
    public string? TransactionDetails { get; set; }
    public string? TransactionReferenceNo { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance { get; set; }
    public string? Remarks { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Attachment { get; set; }
    public bool IsSettled { get; set; }
    public bool IsReversal { get; set; }
    public bool IsManual { get; set; }
    public bool IsMatched { get; set; }
    public int? StatusId { get; set; }
    public string? DateFormat { get; set; }
}

public sealed class SaveBankStatementRequest
{
    public int AccountId { get; set; }
    public DateOnly? ValueDate { get; set; }
    public DateOnly? PostingDate { get; set; }
    public string? InstrumentNo { get; set; }
    public string? TransactionDetails { get; set; }
    public string? TransactionReferenceNo { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance { get; set; }
    public string? Remarks { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Attachment { get; set; }
    public bool IsSettled { get; set; }
    public bool IsReversal { get; set; }
    public bool IsManual { get; set; }
    public int? StatusId { get; set; }
    public string? DateFormat { get; set; }
}

public sealed class BankStatementPreviewRowDto
{
    public int RowNumber { get; set; }
    public DateOnly? ValueDate { get; set; }
    public DateOnly? PostingDate { get; set; }
    public string? InstrumentNo { get; set; }
    public string? TransactionDetails { get; set; }
    public string? TransactionReferenceNo { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance { get; set; }
    public string? Remarks { get; set; }
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class BankStatementPreviewResultDto
{
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public List<BankStatementPreviewRowDto> Rows { get; set; } = [];
}

public sealed class BankStatementUploadSaveRequest
{
    public int AccountId { get; set; }
    public string? DateFormat { get; set; }
    public int? YearId { get; set; }
    public List<BankStatementPreviewRowDto> Rows { get; set; } = [];
}

public sealed class BankStatementTransferRequest
{
    public int AccountId { get; set; }
    public DateOnly DateFrom { get; set; }
    public DateOnly DateTo { get; set; }
}

public sealed class BankStatementTransferSettingsDto
{
    public int? DefaultFromAccountId { get; set; }
    public string? DefaultFromAccountName { get; set; }
    public string? DefaultFromAccountNumber { get; set; }
    public int? DefaultToAccountId { get; set; }
    public string? DefaultToAccountName { get; set; }
    public string? DefaultToAccountNumber { get; set; }
}

public sealed class SaveBankStatementTransferSettingsRequest
{
    public int DefaultFromAccountId { get; set; }
    public int DefaultToAccountId { get; set; }
}

public sealed class BankStatementTransferResultDto
{
    public int TransferredCount { get; set; }
    public int SkippedCount { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class VerifyAnnualReportCodeRequest
{
    public string Code { get; set; } = string.Empty;
}

/// <summary>LT ShowSalesStock / SalesStockInfo grid row.</summary>
public sealed class EMarketingStockInfoDto
{
    public long Id { get; set; }
    public string? Ref { get; set; }
    public string? AcctName { get; set; }
    public string? RDiscriptions { get; set; }
    public decimal PurchaseAmount { get; set; }
    public decimal TotProCharges { get; set; }
    public decimal SaleAmount { get; set; }
    public decimal QtyPurchase { get; set; }
    public decimal QtySale { get; set; }
    public decimal Balance { get; set; }
    public string? StatusName { get; set; }
    public DateOnly TransDate { get; set; }
}

/// <summary>LT Sales Roznamcha / tblSalesRoznamcha list row.</summary>
public sealed class EMarketingSalesRozDto
{
    public long Id { get; set; }
    public string? Ref { get; set; }
    public int AccountId { get; set; }
    public string? AcctName { get; set; }
    public string? PlatformName { get; set; }
    public int? TransTypeId { get; set; }
    public string? TransTypeName { get; set; }
    public int? SalesTypeId { get; set; }
    public string? SalesTypeName { get; set; }
    public decimal Qty { get; set; }
    public string? AmzProRef { get; set; }
    public decimal TotProCharges { get; set; }
    public decimal TotPromotion { get; set; }
    public decimal TotProRebate { get; set; }
    public decimal AmazonFee { get; set; }
    public string? Descriptions { get; set; }
    public string? OrderId { get; set; }
    public decimal Other { get; set; }
    public decimal Amount { get; set; }
    public decimal PurchaseAmount { get; set; }
    public decimal SaleAmount { get; set; }
    public decimal QtyPurchase { get; set; }
    public decimal QtySale { get; set; }
    public int? StatusId { get; set; }
    public string? StatusName { get; set; }
    public DateOnly TransDate { get; set; }
    public string? DocName { get; set; }
    public string? AttachmentPath { get; set; }
    public string? Remarks { get; set; }
}

public sealed class SaveEMarketingSalesRozRequest
{
    public int AccountId { get; set; }
    public string? Ref { get; set; }
    public string? PlatformName { get; set; }
    public int? TransTypeId { get; set; }
    public string? TransTypeName { get; set; }
    public int? SalesTypeId { get; set; }
    public string? SalesTypeName { get; set; }
    public decimal Qty { get; set; }
    public string? AmzProRef { get; set; }
    public decimal TotProCharges { get; set; }
    public decimal TotPromotion { get; set; }
    public decimal TotProRebate { get; set; }
    public decimal AmazonFee { get; set; }
    public string? Descriptions { get; set; }
    public string? OrderId { get; set; }
    public decimal Other { get; set; }
    public decimal Amount { get; set; }
    public decimal? PurchaseAmount { get; set; }
    public decimal? SaleAmount { get; set; }
    public decimal? QtyPurchase { get; set; }
    public decimal? QtySale { get; set; }
    public int? StatusId { get; set; }
    public string? StatusName { get; set; }
    public DateOnly TransDate { get; set; }
    public string? DocName { get; set; }
    public string? Remarks { get; set; }
}
