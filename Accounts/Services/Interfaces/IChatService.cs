using Accounts.DTOs;

namespace Accounts.Services.Interfaces;

public interface IChatService
{
    Task<(Guid PersonId, int TenantId, long WorkspaceId, int CompanyOrganizationTreeId)> ResolveCallerAsync(
        string identityUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatPersonDto>> GetDirectoryAsync(
        string identityUserId,
        string? search,
        int take,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatContactRequestDto>> GetRequestsAsync(
        string identityUserId,
        CancellationToken cancellationToken = default);

    Task<ChatContactRequestDto> CreateRequestAsync(
        string identityUserId,
        CreateChatRequestDto dto,
        CancellationToken cancellationToken = default);

    Task<(ChatContactRequestDto Request, ChatConversationDto? Conversation)> DecideRequestAsync(
        string identityUserId,
        long requestId,
        DecideChatRequestDto dto,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatConversationDto>> GetConversationsAsync(
        string identityUserId,
        CancellationToken cancellationToken = default);

    Task<ChatConversationDto> CreateGroupAsync(
        string identityUserId,
        CreateChatGroupDto dto,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(
        string identityUserId,
        long conversationId,
        long? beforeId,
        int take,
        CancellationToken cancellationToken = default);

    Task<ChatMessageDto> SendMessageAsync(
        string identityUserId,
        long conversationId,
        CreateChatMessageDto dto,
        CancellationToken cancellationToken = default);

    Task<ChatMessageDto> SendAttachmentAsync(
        string identityUserId,
        long conversationId,
        Guid clientMessageId,
        string? caption,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken cancellationToken = default);

    Task<ChatMessageDto> EditMessageAsync(
        string identityUserId,
        long messageId,
        EditChatMessageDto dto,
        CancellationToken cancellationToken = default);

    Task<long> DeleteMessageAsync(
        string identityUserId,
        long messageId,
        bool forEveryone,
        CancellationToken cancellationToken = default);

    Task<ChatMessageDto> ForwardMessageAsync(
        string identityUserId,
        long sourceMessageId,
        long targetConversationId,
        CancellationToken cancellationToken = default);

    Task<ChatAttachmentContentDto> GetAttachmentAsync(
        string identityUserId,
        long attachmentId,
        CancellationToken cancellationToken = default);

    Task<ChatMessageDto> SetReactionAsync(
        string identityUserId,
        long messageId,
        SetChatReactionDto dto,
        CancellationToken cancellationToken = default);

    Task MarkReadAsync(
        string identityUserId,
        long conversationId,
        MarkChatReadDto dto,
        CancellationToken cancellationToken = default);

    Task UpdatePreferenceAsync(
        string identityUserId,
        long conversationId,
        UpdateChatPreferenceDto dto,
        CancellationToken cancellationToken = default);

    Task ClearConversationAsync(
        string identityUserId,
        long conversationId,
        CancellationToken cancellationToken = default);

    Task BlockAsync(
        string identityUserId,
        Guid blockedPersonId,
        CancellationToken cancellationToken = default);

    Task UnblockAsync(
        string identityUserId,
        Guid blockedPersonId,
        CancellationToken cancellationToken = default);

    Task<List<ChatPersonDto>> GetBlockedPersonsAsync(
        string identityUserId,
        CancellationToken cancellationToken = default);

    Task<bool> IsConversationMemberAsync(
        Guid personId,
        long conversationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatGroupMemberDto>> GetGroupMembersAsync(
        string identityUserId,
        long conversationId,
        CancellationToken cancellationToken = default);

    Task AddGroupMembersAsync(
        string identityUserId,
        long conversationId,
        AddChatGroupMembersDto dto,
        CancellationToken cancellationToken = default);

    Task RemoveGroupMemberAsync(
        string identityUserId,
        long conversationId,
        Guid memberPersonId,
        CancellationToken cancellationToken = default);

    Task UpdateMemberRoleAsync(
        string identityUserId,
        long conversationId,
        Guid memberPersonId,
        UpdateChatMemberRoleDto dto,
        CancellationToken cancellationToken = default);

    Task UpdatePrivacySettingsAsync(
        string identityUserId,
        UpdatePrivacySettingsDto dto,
        CancellationToken cancellationToken = default);
}
