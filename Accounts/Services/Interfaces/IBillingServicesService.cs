using Accounts.DTOs;

namespace Accounts.Services.Interfaces;

public interface IBillingServicesService
{
    Task<IReadOnlyList<BillingReProcessRowDto>> ListReProcessAsync(
        int tenantId,
        DateOnly? dateTo,
        CancellationToken ct = default);

    Task<IReadOnlyList<BillingCptsProcessedRowDto>> ListCptsProcessedAsync(
        int tenantId,
        DateOnly? dateTo,
        CancellationToken ct = default);

    Task<IReadOnlyList<BillingArReportRowDto>> ListArReportAsync(
        int tenantId,
        DateOnly? dateTo,
        CancellationToken ct = default);

    Task<IReadOnlyList<BillingStatusRuleDto>> ListStatusRulesAsync(
        CancellationToken ct = default);

    Task<BillingStatusRuleDto?> GetStatusRuleByIdAsync(
        int id,
        CancellationToken ct = default);

    Task<BillingServiceActionResultDto> SaveStatusRuleAsync(
        BillingStatusRuleSaveRequestDto request,
        string? userName,
        CancellationToken ct = default);

    Task<BillingServiceActionResultDto> DeleteStatusRuleAsync(
        int id,
        string? userName,
        CancellationToken ct = default);

    Task<IReadOnlyList<BillingStatusOptionDto>> ListStatusOptionsAsync(
        CancellationToken ct = default);

    // ── Child 4: Call List ──────────────────────────────────────────────────
    Task<IReadOnlyList<BillingCallListRowDto>> ListCallListAsync(
        int tenantId,
        DateOnly? dateTo,
        CancellationToken ct = default);

    // ── Child 5: Closed List ────────────────────────────────────────────────
    Task<IReadOnlyList<BillingClosedListRowDto>> ListClosedListAsync(
        int tenantId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken ct = default);

    // ── Child 6: Master List ────────────────────────────────────────────────
    Task<IReadOnlyList<BillingMasterListRowDto>> ListMasterListAsync(
        int tenantId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken ct = default);

    // ── Child 8: Mics Report ────────────────────────────────────────────────
    Task<IReadOnlyList<BillingMicsReportRowDto>> ListMicsReportAsync(
        int tenantId,
        DateOnly? dateTo,
        CancellationToken ct = default);

    // ── Child 9: Claim Master List ──────────────────────────────────────────
    Task<IReadOnlyList<BillingClaimMasterListRowDto>> ListClaimMasterListAsync(
        int tenantId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken ct = default);

    // ── Child 11: Code Usage ────────────────────────────────────────────────
    Task<IReadOnlyList<BillingCodeUsageRowDto>> ListCodeUsageAsync(
        int tenantId,
        string code,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken ct = default);
}

