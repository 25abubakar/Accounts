using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Services;

namespace Accounts.Tests;

public sealed class ChatRulePolicyTests
{
    private static readonly Guid SenderId = Guid.NewGuid();
    private static readonly DateTime CreatedUtc = new(2026, 8, 24, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EditWindow_IsInclusiveAtExactlyFifteenMinutes_AndBlocksAttachments()
    {
        var message = Message();
        var rules = Rules();

        Assert.True(ChatRulePolicy.CanEdit(message, false, SenderId, CreatedUtc.AddMinutes(15), rules));
        Assert.False(ChatRulePolicy.CanEdit(message, false, SenderId, CreatedUtc.AddMinutes(15).AddTicks(1), rules));
        Assert.False(ChatRulePolicy.CanEdit(message, true, SenderId, CreatedUtc.AddMinutes(1), rules));
    }

    [Fact]
    public void DeleteForEveryone_IsInclusiveAtExactlySixtyHours()
    {
        var message = Message();
        var rules = Rules();

        Assert.True(ChatRulePolicy.CanDeleteForEveryone(message, SenderId, CreatedUtc.AddHours(60), rules));
        Assert.False(ChatRulePolicy.CanDeleteForEveryone(message, SenderId, CreatedUtc.AddHours(60).AddTicks(1), rules));
        Assert.False(ChatRulePolicy.CanDeleteForEveryone(message, Guid.NewGuid(), CreatedUtc.AddHours(1), rules));
    }

    [Fact]
    public void ViewOnce_ExpiresAtExactlyFourteenDays_AndCannotBeReopened()
    {
        var attachment = new ChatAttachment
        {
            IsViewOnce = true,
            CreatedOnUtc = CreatedUtc,
        };
        var rules = Rules();

        Assert.False(ChatRulePolicy.IsViewOnceExpired(attachment, CreatedUtc.AddDays(14).AddTicks(-1), rules));
        Assert.True(ChatRulePolicy.IsViewOnceExpired(attachment, CreatedUtc.AddDays(14), rules));
        attachment.ViewOnceConsumedOnUtc = CreatedUtc.AddHours(1);
        Assert.False(ChatRulePolicy.IsViewOnceExpired(attachment, CreatedUtc.AddDays(20), rules));
        Assert.Equal("Opened", ChatRulePolicy.ViewOnceState(attachment, Guid.NewGuid(), SenderId, CreatedUtc.AddDays(20), rules));
    }

    [Fact]
    public void DeletedPlaceholder_MatchesComplianceCopyExactly() =>
        Assert.Equal("This message was deleted", ChatRulePolicy.DeletedPlaceholder);

    [Fact]
    public void MessageInfo_IsPermanentlyBlockedAfterDeleteForEveryoneClearsTracking()
    {
        var message = Message();
        Assert.True(ChatRulePolicy.CanViewMessageInfo(message, SenderId));
        Assert.False(ChatRulePolicy.CanViewMessageInfo(message, Guid.NewGuid()));

        message.DeletedOnUtc = CreatedUtc.AddMinutes(1);
        message.DeliveryTrackingClearedOnUtc = message.DeletedOnUtc;

        Assert.False(ChatRulePolicy.CanViewMessageInfo(message, SenderId));
    }

    private static ChatMessage Message() => new()
    {
        SenderPersonId = SenderId,
        CreatedOnUtc = CreatedUtc,
    };

    private static ChatRuleSettingDto Rules() => new(
        true,
        15,
        true,
        60 * 60,
        true,
        14 * 24,
        true,
        true,
        true,
        true,
        true,
        true,
        null);
}
