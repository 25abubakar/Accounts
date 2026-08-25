using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Services.Services;

public sealed class ChatRuleService(ApplicationDbContext db) : IChatRuleService
{
    public async Task<ChatRuleSettingDto> GetEffectiveAsync(
        int tenantId,
        CancellationToken cancellationToken = default)
    {
        var setting = await db.ChatRuleSettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken);
        return ToDto(setting);
    }

    public async Task<ChatRuleSettingDto> SaveAsync(
        int tenantId,
        string identityUserId,
        SaveChatRuleSettingDto dto,
        CancellationToken cancellationToken = default)
    {
        Validate(dto);
        var setting = await db.ChatRuleSettings
            .SingleOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken);
        var now = DateTime.UtcNow;
        if (setting == null)
        {
            setting = new ChatRuleSetting
            {
                TenantId = tenantId,
                CreatedOnUtc = now,
            };
            db.ChatRuleSettings.Add(setting);
        }

        setting.AllowMessageEditing = dto.AllowMessageEditing;
        setting.EditWindowMinutes = dto.EditWindowMinutes;
        setting.AllowDeleteForEveryone = dto.AllowDeleteForEveryone;
        setting.DeleteForEveryoneWindowMinutes = dto.DeleteForEveryoneWindowMinutes;
        setting.AllowViewOnceMedia = dto.AllowViewOnceMedia;
        setting.ViewOnceUnopenedExpiryHours = dto.ViewOnceUnopenedExpiryHours;
        setting.UpdatedByUserId = identityUserId;
        setting.UpdatedOnUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(setting);
    }

    private static void Validate(SaveChatRuleSettingDto dto)
    {
        if (dto.EditWindowMinutes is < 1 or > 1440)
            throw new ArgumentException("Edit duration must be between 1 and 1,440 minutes.");
        if (dto.DeleteForEveryoneWindowMinutes is < 1 or > 43200)
            throw new ArgumentException("Delete for Everyone duration must be between 1 and 43,200 minutes.");
        if (dto.ViewOnceUnopenedExpiryHours is < 1 or > 8760)
            throw new ArgumentException("View Once expiry must be between 1 and 8,760 hours.");
    }

    private static ChatRuleSettingDto ToDto(ChatRuleSetting? setting) => new(
        setting?.AllowMessageEditing ?? true,
        setting?.EditWindowMinutes ?? 15,
        setting?.AllowDeleteForEveryone ?? true,
        setting?.DeleteForEveryoneWindowMinutes ?? 60 * 60,
        setting?.AllowViewOnceMedia ?? true,
        setting?.ViewOnceUnopenedExpiryHours ?? 14 * 24,
        true,
        true,
        true,
        true,
        true,
        true,
        setting?.UpdatedOnUtc);
}
