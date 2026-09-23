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
    public bool IsLedger { get; set; }
    public bool IsManual { get; set; }
    public int? ProjectId { get; set; }
}

public sealed class CreateRoznamchaPaymentRequest : SaveRoznamchaEntryRequest { }
public sealed class CreateRoznamchaReceiptRequest : SaveRoznamchaEntryRequest { }

public sealed class RoznamchaEntryDto
{
    public long Id { get; set; }
    public int? SerialNo { get; set; }
    public string? ReferenceNo { get; set; }
    public string? OldReferenceNo { get; set; }
    public int EntryTypeId { get; set; }
    public int? CategoryId { get; set; }
    public int? FromAccountId { get; set; }
    public string? FromAccountName { get; set; }
    public int? ToAccountId { get; set; }
    public string? ToAccountName { get; set; }
    public decimal Amount { get; set; }
    public decimal UsdAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Adjustment { get; set; }
    public DateOnly TransactionDate { get; set; }
    public string? Description { get; set; }
    public string? Remarks { get; set; }
    public bool IsApproved { get; set; }
    public bool IsSettled { get; set; }
    public bool IsShownHidden { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsLocked { get; set; }
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
}

public sealed class ReportFilterDto : ReportFilterRequest
{
    public int Id { get; set; }
}

public sealed class ReportRowDto
{
    public DateOnly Date { get; set; }
    public int? CategoryId { get; set; }
    public string? Category { get; set; }
    public int? AccountId { get; set; }
    public string? Account { get; set; }
    public int EntryTypeId { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Description { get; set; }
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

public sealed class VerifyAnnualReportCodeRequest
{
    public string Code { get; set; } = string.Empty;
}
