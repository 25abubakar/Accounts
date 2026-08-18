using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Accounts.Models;

[Table("ChatWorkspaces")]
public sealed class ChatWorkspace : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int OrganizationTreeId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
}

[Table("ChatContactRequests")]
public sealed class ChatContactRequest : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public long WorkspaceId { get; set; }
    public Guid SenderPersonId { get; set; }
    public Guid ReceiverPersonId { get; set; }
    [MaxLength(73)] public string PairKey { get; set; } = string.Empty;
    [MaxLength(20)] public string Status { get; set; } = "Pending";
    [MaxLength(500)] public string? Message { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedOnUtc { get; set; }
    public DateTime? CancelledOnUtc { get; set; }
}

[Table("ChatConversations")]
public sealed class ChatConversation : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public long WorkspaceId { get; set; }
    [MaxLength(20)] public string ConversationType { get; set; } = "Direct";
    [MaxLength(200)] public string? Title { get; set; }
    [MaxLength(73)] public string? DirectPairKey { get; set; }
    [MaxLength(500)] public string? PhotoUrl { get; set; }
    public Guid CreatedByPersonId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

[Table("ChatConversationMembers")]
public sealed class ChatConversationMember : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public long ConversationId { get; set; }
    public Guid PersonId { get; set; }
    [MaxLength(20)] public string MemberRole { get; set; } = "Member";
    public DateTime JoinedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LeftOnUtc { get; set; }
    public long? LastReadMessageId { get; set; }
    public DateTime? ClearedBeforeUtc { get; set; }
    public bool IsMuted { get; set; }
    public bool IsPinned { get; set; }
    public bool HasUnreadMention { get; set; }
}

[Table("ChatMessages")]
public sealed class ChatMessage : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public long ConversationId { get; set; }
    public Guid SenderPersonId { get; set; }
    public Guid ClientMessageId { get; set; }
    [MaxLength(4000)] public string Body { get; set; } = string.Empty;
    public long? ReplyToMessageId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EditedOnUtc { get; set; }
    public DateTime? DeletedOnUtc { get; set; }
}

[Table("ChatMessageDeletions")]
public sealed class ChatMessageDeletion : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public long MessageId { get; set; }
    public Guid PersonId { get; set; }
    public DateTime DeletedOnUtc { get; set; } = DateTime.UtcNow;
}

[Table("ChatMessageReactions")]
public sealed class ChatMessageReaction : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public long MessageId { get; set; }
    public Guid PersonId { get; set; }
    [MaxLength(32)] public string Emoji { get; set; } = string.Empty;
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
}

[Table("ChatAttachments")]
public sealed class ChatAttachment : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public long MessageId { get; set; }
    [MaxLength(255)] public string FileName { get; set; } = string.Empty;
    [MaxLength(100)] public string ContentType { get; set; } = "application/octet-stream";
    public long FileSize { get; set; }
    [MaxLength(500)] public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
}

[Table("ChatBlocks")]
public sealed class ChatBlock : ITenantEntity
{
    [Key]
    public long Id { get; set; }
    public int TenantId { get; set; }
    public Guid BlockerPersonId { get; set; }
    public Guid BlockedPersonId { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
}
