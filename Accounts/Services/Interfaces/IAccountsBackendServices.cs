using Accounts.DTOs;
using Accounts.Models;
using Microsoft.AspNetCore.Http;

namespace Accounts.Services.Interfaces;

public interface ICurrentUserService
{
    int TenantId { get; }
    string? UserId { get; }
}

public interface IReferenceGeneratorService
{
    Task<string> AccountReferenceAsync(int tenantId, CancellationToken ct = default);
    Task<string> MainAccountCodeAsync(int tenantId, string categoryCode, CancellationToken ct = default);
    Task<string> SubAccountCodeAsync(int tenantId, int parentId, string parentCode, CancellationToken ct = default);
    Task<string> AccountNumberAsync(int tenantId, string accountCode, CancellationToken ct = default);
    Task<string> PaymentReferenceAsync(int tenantId, CancellationToken ct = default);
    Task<string> ReceiptReferenceAsync(int tenantId, CancellationToken ct = default);
    Task<string> DocumentReferenceAsync(int tenantId, CancellationToken ct = default);
}

public interface IFileStorageService
{
    Task<StoredFileDto> SaveAccountsFileAsync(int tenantId, IFormFile file, CancellationToken ct = default);
    Task DeleteAccountsFileAsync(string storedPath, CancellationToken ct = default);
    Task<(Stream Stream, string ContentType, string FileName)?> OpenAccountsFileAsync(string storedPath, CancellationToken ct = default);
}

public interface IAccountCategoryService
{
    Task<IReadOnlyList<AccountCategoryTypeDto>> ListTypesAsync(CancellationToken ct = default);
    Task<AccountCategoryTypeDto> SaveTypeAsync(int? id, SaveAccountCategoryTypeRequest request, CancellationToken ct = default);
    Task DeleteTypeAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<AccountCategoryDto>> ListAsync(CancellationToken ct = default);
    Task<AccountCategoryDto?> GetAsync(int id, CancellationToken ct = default);
    Task<AccountCategoryDto> SaveAsync(int? id, SaveAccountCategoryRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

/// <summary>Account Type workspace: Roznamcha Type, Transaction Type/Mode, Date Label, Currency.</summary>
public interface IAccountTypeMasterService
{
    Task<IReadOnlyList<AccountTypeLookupDto>> ListAsync(string kind, CancellationToken ct = default);
    Task<AccountTypeLookupDto> SaveAsync(string kind, int? id, SaveAccountTypeLookupRequest request, CancellationToken ct = default);
    Task DeleteAsync(string kind, int id, CancellationToken ct = default);
}

public interface IAccountService
{
    Task<IReadOnlyList<AccountDto>> ListAsync(int? categoryId, int? parentId, bool activeOnly, CancellationToken ct = default);
    Task<IReadOnlyList<AccountDto>> ListMainAsync(int? categoryId, CancellationToken ct = default);
    Task<AccountDto?> GetAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<AccountDto>> ListSubAccountsAsync(int id, CancellationToken ct = default);
    Task<AccountDto> CreateAsync(SaveAccountRequest request, CancellationToken ct = default);
    Task<AccountDto> UpdateAsync(int id, SaveAccountRequest request, CancellationToken ct = default);
    Task<AccountDto> SaveFilesAsync(int id, IFormFile? photo, IFormFile? attachment, CancellationToken ct = default);
    Task<(Stream Stream, string ContentType, string FileName)?> OpenFileAsync(int id, string kind, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<AccountDto> UpdateBudgetAsync(int id, UpdateAccountBudgetRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AccountLedgerDto>> LedgerAsync(int id, DateOnly? from, DateOnly? to, bool hidden, CancellationToken ct = default);
    Task TransferLedgerAsync(LedgerTransferRequest request, CancellationToken ct = default);
}

public interface IRoznamchaService
{
    Task<IReadOnlyList<RoznamchaEntryDto>> ListAsync(int entryTypeId, DateOnly? from, DateOnly? to, CancellationToken ct = default);
    Task<RoznamchaEntryDto?> GetAsync(long id, CancellationToken ct = default);
    Task<RoznamchaEntryDto> CreateAsync(int entryTypeId, SaveRoznamchaEntryRequest request, CancellationToken ct = default);
    Task<RoznamchaEntryDto> UpdateAsync(long id, int entryTypeId, SaveRoznamchaEntryRequest request, CancellationToken ct = default);
    Task SoftDeleteAsync(long id, CancellationToken ct = default);
    Task<RoznamchaEntryDto> UpdateStatusAsync(long id, UpdateRoznamchaStatusRequest request, CancellationToken ct = default);
    Task<RoznamchaEntryDto> ApproveAsync(long id, CancellationToken ct = default);
    Task<EntryAttachmentDto> AddAttachmentAsync(long id, IFormFile file, string? remarks, CancellationToken ct = default);
    Task<IReadOnlyList<EntryAttachmentDto>> ListAttachmentsAsync(long id, CancellationToken ct = default);
    Task DeleteAttachmentAsync(long attachmentId, CancellationToken ct = default);
    Task<(Stream Stream, string ContentType, string FileName)?> OpenAttachmentAsync(long attachmentId, CancellationToken ct = default);
}

public interface IPayableReceivableService
{
    Task<IReadOnlyList<RecurringTransactionDto>> ListAsync(string? kind, CancellationToken ct = default);
    Task<RecurringTransactionDto?> GetAsync(int id, string kind, CancellationToken ct = default);
    Task<RecurringTransactionDto> CreateAsync(CreateRecurringTransactionRequest request, CancellationToken ct = default);
    Task<RecurringTransactionDto> UpdateAsync(int id, CreateRecurringTransactionRequest request, CancellationToken ct = default);
    Task<RecurringTransactionDto> UpdateStatusAsync(int id, string kind, bool isInactive, CancellationToken ct = default);
    Task<RecurringTransactionDto> SettleAsync(int id, string kind, SettleRecurringTransactionRequest request, CancellationToken ct = default);
    Task<RecurringTransactionDto> GenerateNextAsync(int id, string kind, CancellationToken ct = default);
}

public interface IBankStatementService
{
    Task<IReadOnlyList<BankStatementDto>> ListAsync(int? accountId, DateOnly? dateFrom, DateOnly? dateTo, CancellationToken ct = default);
    Task<BankStatementDto?> GetAsync(long id, CancellationToken ct = default);
    Task<BankStatementDto> SaveAsync(long? id, SaveBankStatementRequest request, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
    Task<BankStatementPreviewResultDto> PreviewExcelAsync(int accountId, string dateFormat, int? year, IFormFile excelFile, CancellationToken ct = default);
    Task<IReadOnlyList<BankStatementDto>> SaveUploadAsync(BankStatementUploadSaveRequest request, IFormFile? attachment, CancellationToken ct = default);
    Task<BankStatementTransferSettingsDto> GetTransferSettingsAsync(CancellationToken ct = default);
    Task<BankStatementTransferSettingsDto> SaveTransferSettingsAsync(SaveBankStatementTransferSettingsRequest request, CancellationToken ct = default);
    Task<BankStatementTransferResultDto> TransferToRoznamchaAsync(BankStatementTransferRequest request, CancellationToken ct = default);
}

public interface IEMarketingService
{
    Task<IReadOnlyList<EMarketingStockInfoDto>> ListStockInfoAsync(int accountId, DateOnly? dateFrom, DateOnly? dateTo, CancellationToken ct = default);
    Task<IReadOnlyList<EMarketingSalesRozDto>> ListSalesRozAsync(int? accountId, DateOnly? dateFrom, DateOnly? dateTo, CancellationToken ct = default);
    Task<EMarketingSalesRozDto?> GetSalesRozAsync(long id, CancellationToken ct = default);
    Task<EMarketingSalesRozDto> SaveSalesRozAsync(long? id, SaveEMarketingSalesRozRequest request, CancellationToken ct = default);
    Task DeleteSalesRozAsync(long id, CancellationToken ct = default);
}

public interface IReportService
{
    Task<IReadOnlyList<ReportRowDto>> MonthlyAsync(string? name, DateOnly from, DateOnly to, int? typeId, int? projectId, IReadOnlyList<int>? categoryIds, IReadOnlyList<int>? accountIds, CancellationToken ct = default);
    Task<IReadOnlyList<ReportRowDto>> DailyAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<ReportFilterDto>> ListFiltersAsync(CancellationToken ct = default);
    Task<ReportFilterDto> SaveFilterAsync(int? id, ReportFilterRequest request, CancellationToken ct = default);
    Task DeleteFilterAsync(int id, CancellationToken ct = default);
}

public interface IProjectCostService
{
    Task<IReadOnlyList<ProjectCostRuleDto>> ListRulesAsync(CancellationToken ct = default);
    Task<ProjectCostRuleDto> SaveRuleAsync(int? id, ProjectCostRuleRequest request, CancellationToken ct = default);
    Task DeleteRuleAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ReportRowDto>> ReportAsync(string? name, DateOnly from, DateOnly to, CancellationToken ct = default);
}
