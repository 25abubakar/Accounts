using Accounts.DTOs;
using Accounts.Models;

namespace Accounts.Services.Services;

public static class ChatRulePolicy
{
    public const string DeletedPlaceholder = "This message was deleted";

    public static bool CanEdit(
        ChatMessage message,
        bool hasAttachments,
        Guid callerPersonId,
        DateTime nowUtc,
        ChatRuleSettingDto rules) =>
        rules.AllowMessageEditing &&
        message.SenderPersonId == callerPersonId &&
        !message.DeletedOnUtc.HasValue &&
        !hasAttachments &&
        nowUtc <= message.CreatedOnUtc.AddMinutes(rules.EditWindowMinutes);

    public static bool CanDeleteForEveryone(
        ChatMessage message,
        Guid callerPersonId,
        DateTime nowUtc,
        ChatRuleSettingDto rules) =>
        rules.AllowDeleteForEveryone &&
        message.SenderPersonId == callerPersonId &&
        !message.DeletedOnUtc.HasValue &&
        nowUtc <= message.CreatedOnUtc.AddMinutes(rules.DeleteForEveryoneWindowMinutes);

    public static bool CanViewMessageInfo(ChatMessage message, Guid callerPersonId) =>
        message.SenderPersonId == callerPersonId &&
        !message.DeletedOnUtc.HasValue &&
        !message.DeliveryTrackingClearedOnUtc.HasValue;

    public static bool IsViewOnceExpired(
        ChatAttachment attachment,
        DateTime nowUtc,
        ChatRuleSettingDto rules) =>
        attachment.IsViewOnce &&
        !attachment.ViewOnceConsumedOnUtc.HasValue &&
        !attachment.ViewOnceExpiredOnUtc.HasValue &&
        nowUtc >= attachment.CreatedOnUtc.AddHours(rules.ViewOnceUnopenedExpiryHours);

    public static string ViewOnceState(
        ChatAttachment attachment,
        Guid callerPersonId,
        Guid senderPersonId,
        DateTime nowUtc,
        ChatRuleSettingDto rules)
    {
        if (!attachment.IsViewOnce) return "NotViewOnce";
        if (attachment.ViewOnceExpiredOnUtc.HasValue || IsViewOnceExpired(attachment, nowUtc, rules))
            return "Expired";
        if (attachment.ViewOnceConsumedOnUtc.HasValue)
            return "Opened";
        return callerPersonId == senderPersonId ? "Sent" : "Available";
    }
}
