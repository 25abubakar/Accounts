namespace Accounts.DTOs;

public sealed record ChatPersonDto(
    Guid PersonId,
    string FullName,
    string? PhotoUrl,
    string Department,
    string Designation,
    bool IsOnline,
    bool ShowLastSeen,
    DateTime? LastSeenUtc);

public sealed record ChatContactRequestDto(
    long Id,
    Guid SenderPersonId,
    Guid ReceiverPersonId,
    ChatPersonDto OtherPerson,
    string Direction,
    string Status,
    string? Message,
    DateTime CreatedOnUtc);

public sealed record ChatReactionDto(string Emoji, int Count, bool ReactedByMe);

public sealed record ChatAttachmentDto(
    long Id,
    string FileName,
    string ContentType,
    long FileSize,
    string DownloadUrl);

public sealed record ChatAttachmentContentDto(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record ChatMessageDto(
    long Id,
    long ConversationId,
    Guid SenderPersonId,
    string SenderName,
    string? SenderPhotoUrl,
    string Body,
    long? ReplyToMessageId,
    DateTime CreatedOnUtc,
    DateTime? EditedOnUtc,
    bool IsDeleted,
    string Status,
    IReadOnlyList<ChatReactionDto> Reactions,
    IReadOnlyList<ChatAttachmentDto> Attachments);

public sealed record ChatConversationDto(
    long Id,
    string ConversationType,
    string DisplayName,
    string? PhotoUrl,
    Guid? OtherPersonId,
    string? LastMessage,
    DateTime? LastMessageOnUtc,
    int UnreadCount,
    bool IsMuted,
    bool IsPinned,
    bool IsOnline,
    DateTime? LastSeenUtc,
    bool HasUnreadMention, bool IsBlockedByMe = false, bool IsBlockedByOther = false);

public sealed record CreateChatRequestDto(Guid ReceiverPersonId, string? Message);
public sealed record CreateChatGroupDto(string Title, IReadOnlyList<Guid> MemberPersonIds);
public sealed record DecideChatRequestDto(bool Accept);
public sealed record CreateChatMessageDto(Guid ClientMessageId, string Body, long? ReplyToMessageId);
public sealed record SetChatReactionDto(string Emoji);
public sealed record MarkChatReadDto(long MessageId);
public sealed record UpdateChatPreferenceDto(bool IsMuted, bool IsPinned);
public sealed record EditChatMessageDto(string Body);

public sealed record ChatGroupMemberDto(
    Guid PersonId,
    string FullName,
    string? PhotoUrl,
    string MemberRole,
    DateTime JoinedOnUtc);

public sealed record AddChatGroupMembersDto(IReadOnlyList<Guid> MemberPersonIds);
public sealed record UpdateChatMemberRoleDto(string MemberRole);
public sealed record UpdatePrivacySettingsDto(bool ShowLastSeen);

public sealed record MessageDeliveryInfoDto(
    Guid PersonId,
    string FullName,
    string? PhotoUrl,
    string Status
);
public sealed record UpdateGroupNameDto(string Title);

public sealed record ChatBootstrapDto(
    ChatPersonDto CurrentUser,
    IReadOnlyList<ChatConversationDto> Conversations,
    int PendingRequestsCount
);

public class ChatConversationResult
{
    public long Id { get; set; }
    public string ConversationType { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? PhotoUrl { get; set; }
    public Guid? OtherPersonId { get; set; }
    public string? LastMessage { get; set; }
    public DateTime? LastMessageOnUtc { get; set; }
    public int UnreadCount { get; set; }
    public bool IsMuted { get; set; }
    public bool IsPinned { get; set; }
    public bool IsBlockedByMe { get; set; }
    public bool IsBlockedByOther { get; set; }
    public bool HasUnreadMention { get; set; }
    public DateTime? LastSeenUtc { get; set; }
}
