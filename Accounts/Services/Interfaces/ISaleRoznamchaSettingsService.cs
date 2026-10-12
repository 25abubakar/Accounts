using Accounts.DTOs;

namespace Accounts.Services.Interfaces;

public interface ISaleRoznamchaSettingsService
{
    Task<IReadOnlyList<SaleRoznamchaSettingRow>> ListAsync(string settingType, CancellationToken ct = default);
    Task<SaleRoznamchaSettingRow> SaveAsync(string settingType, int? id, SaveSaleRoznamchaSettingRequest request, CancellationToken ct = default);
    Task DeactivateAsync(string settingType, int id, CancellationToken ct = default);
}
