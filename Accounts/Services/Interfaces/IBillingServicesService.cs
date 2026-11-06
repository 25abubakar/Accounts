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
}

