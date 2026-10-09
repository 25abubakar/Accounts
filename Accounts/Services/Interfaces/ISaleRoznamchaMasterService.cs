using Accounts.DTOs;

namespace Accounts.Services.Interfaces;

public interface ISaleRoznamchaMasterService
{
    Task<IReadOnlyList<SaleRoznamchaMasterRow>> ListAsync(
        string? entityType,
        int? parentId,
        bool activeOnly,
        CancellationToken ct = default);

    Task<SaleRoznamchaMasterRow> SaveAsync(
        string entityType,
        int? id,
        SaveSaleRoznamchaMasterRequest request,
        CancellationToken ct = default);

    Task DeleteAsync(string entityType, int id, CancellationToken ct = default);
}
