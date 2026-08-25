using Accounts.DTOs;

namespace Accounts.Services.Interfaces;

public interface IChatRuleService
{
    Task<ChatRuleSettingDto> GetEffectiveAsync(
        int tenantId,
        CancellationToken cancellationToken = default);

    Task<ChatRuleSettingDto> SaveAsync(
        int tenantId,
        string identityUserId,
        SaveChatRuleSettingDto dto,
        CancellationToken cancellationToken = default);
}
