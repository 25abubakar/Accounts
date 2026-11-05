using Accounts.DTOs;
using Accounts.Models;

namespace Accounts.Services.Interfaces;

public interface IBillingClaimsService
{
    Task<IReadOnlyList<BillingPaymentRowDto>> ListClaimsAsync(
        int tenantId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string tab = "claims",
        int? statusId = null,
        int? carrierId = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<BillingEobRowDto>> ListEobsAsync(int tenantId, DateOnly? dateFrom, DateOnly? dateTo, CancellationToken ct = default);

    Task<BillingPaymentRowDto?> GetClaimByIdAsync(int tenantId, int claimId, CancellationToken ct = default);

    Task<IReadOnlyList<BillingClaimCodeDetailDto>> ListClaimCodeDetailsAsync(int tenantId, int claimId, int? appointmentId, CancellationToken ct = default);

    Task<int> CreateClaimAsync(int tenantId, CreateBillingClaimRequest request, string? userId, CancellationToken ct = default);

    Task<bool> UpdateClaimAsync(int tenantId, int claimId, CreateBillingClaimRequest request, string? userId, CancellationToken ct = default);

    Task<bool> UpdateClaimStatusAsync(int tenantId, int claimId, int statusId, string? remarks, string? userId, string? policyNumber = null, CancellationToken ct = default);

    Task<bool> PostClaimPaymentAsync(int tenantId, int claimId, PostClaimPaymentRequest request, string? userId, CancellationToken ct = default);

    Task<BillingClaimLookupsDto> GetClaimLookupsAsync(int tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<BillingClaimLogRowDto>> GetClaimLogsAsync(int tenantId, int claimId, CancellationToken ct = default);

    Task<IReadOnlyList<BillingClaimDocumentRowDto>> GetClaimDocumentsAsync(int tenantId, int claimId, CancellationToken ct = default);

    Task<BillingBatchActionResultDto> CorrectReprocessClaimsAsync(int tenantId, DateOnly? dateFrom, DateOnly? dateTo, CancellationToken ct = default);

    Task<BillingBatchActionResultDto> SettledClaimsPaymentAsync(int tenantId, DateOnly? dateFrom, DateOnly? dateTo, CancellationToken ct = default);

    Task<BillingBatchActionResultDto> BatchUpdateStatusAsync(int tenantId, IReadOnlyList<int> claimIds, int statusId, string? remarks, string? userId, DateTime? paymentDate = null, CancellationToken ct = default);

    Task<BillingBatchActionResultDto> BatchReprocessClaimsAsync(int tenantId, IReadOnlyList<int> claimIds, bool isReProcess, string? userId, CancellationToken ct = default);

    Task<BillingBatchActionResultDto> BatchArClaimsAsync(int tenantId, IReadOnlyList<int> claimIds, bool isAr, string? userId, CancellationToken ct = default);
}

