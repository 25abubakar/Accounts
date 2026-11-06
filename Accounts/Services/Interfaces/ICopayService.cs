using Accounts.DTOs;

namespace Accounts.Services.Interfaces;

public interface ICopayService
{
    // ── Child Module 1: Merchant To Bank ───────────────────────────────────────
    Task<PagedResult<MerchantToBankItemDto>> GetMerchantToBankListAsync(CopayFilterDto filter, CancellationToken ct = default);
    Task<SettlementResultDto> SettleMerchantToBankAutoAsync(DateRangeDto dateRange, CancellationToken ct = default);
    Task<CopayImportResultDto> ImportMerchantPayoutExcelAsync(UploadMerchantPayoutDto dto, CancellationToken ct = default);

    // ── Child Module 2: Copay IMS To Merchant Statement ────────────────────────
    Task<PagedResult<MerchantStatementItemDto>> GetMerchantStatementsAsync(CopayFilterDto filter, CancellationToken ct = default);
    Task<CopayImportResultDto> ImportMerchantStatementExcelAsync(UploadMerchantStatementDto dto, CancellationToken ct = default);

    // ── Child Module 3: Copay IMS - Excel ──────────────────────────────────────
    Task<PagedResult<ImsReceiptItemDto>> GetImsReceiptsAsync(CopayFilterDto filter, CancellationToken ct = default);
    Task<CopayImportResultDto> ImportImsDailySheetAsync(UploadImsSheetDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<ImsReceiptItemDto>> GetImsVerificationAsync(DateRangeDto dateRange, CancellationToken ct = default);
    Task<IReadOnlyList<CopayProcessLogDto>> GetProcessLogsAsync(long entryId, CancellationToken ct = default);
    Task<SettlementResultDto> SettleImsAutoAsync(DateRangeDto dateRange, CancellationToken ct = default);
    Task<SettlementResultDto> SettleImsWithBankAutoAsync(DateRangeDto dateRange, CancellationToken ct = default);

    // ── Manual Reconciliation & Un-settle ───────────────────────────────────────
    Task<SettlementResultDto> SettleImsManualAsync(ManualSettleRequestDto request, CancellationToken ct = default);
    Task<SettlementResultDto> SettleMerchantStatementsManualAsync(ManualSettleRequestDto request, CancellationToken ct = default);
    Task<bool> UnsettleEntryAsync(UnsettleRequestDto request, CancellationToken ct = default);

    // ── Lookups ────────────────────────────────────────────────────────────────
    Task<CopayLookupsDto> GetLookupsAsync(CancellationToken ct = default);
}
