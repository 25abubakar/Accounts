using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations
{
    /// <inheritdoc />
    public partial class AddChatUnreadNavigationAndReceiptPrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryTrackingClearedOnUtc",
                table: "ChatMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE usp_Chat_GetConversations
    @TenantId INT,
    @PersonId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        c.Id,
        c.ConversationType,
        CASE
            WHEN c.ConversationType = 'Direct' THEN ISNULL(NULLIF(LTRIM(RTRIM(otherPerson.FullName)), ''), 'Unavailable staff')
            ELSE ISNULL(c.Title, 'Group conversation')
        END AS DisplayName,
        CASE WHEN c.ConversationType = 'Direct' THEN otherPerson.ProfilePhotoUrl ELSE c.PhotoUrl END AS PhotoUrl,
        CASE WHEN c.ConversationType = 'Direct' THEN otherMember.PersonId ELSE NULL END AS OtherPersonId,
        CASE WHEN lastMsg.DeletedOnUtc IS NULL THEN lastMsg.Body ELSE 'This message was deleted' END AS LastMessage,
        lastMsg.CreatedOnUtc AS LastMessageOnUtc,
        (
            SELECT COUNT(*)
            FROM ChatMessages msg
            WHERE msg.ConversationId = c.Id
              AND msg.Id > ISNULL(member.LastReadMessageId, 0)
              AND msg.SenderPersonId <> @PersonId
              AND msg.DeletedOnUtc IS NULL
              AND (member.ClearedBeforeUtc IS NULL OR msg.CreatedOnUtc > member.ClearedBeforeUtc)
              AND NOT EXISTS (SELECT 1 FROM ChatMessageDeletions d WHERE d.MessageId = msg.Id AND d.PersonId = @PersonId)
        ) AS UnreadCount,
        member.IsMuted,
        member.IsPinned,
        CAST(CASE WHEN firstMention.Id IS NULL THEN 0 ELSE 1 END AS BIT) AS HasUnreadMention,
        member.LastReadMessageId,
        firstUnread.Id AS FirstUnreadMessageId,
        firstMention.Id AS FirstUnreadMentionMessageId,
        CASE WHEN otherPerson.ShowLastSeen = 1 THEN otherPerson.LastSeenUtc ELSE NULL END AS LastSeenUtc,
        CAST(CASE WHEN c.ConversationType = 'Direct' AND EXISTS (
            SELECT 1 FROM ChatBlocks b WHERE b.BlockerPersonId = @PersonId AND b.BlockedPersonId = otherMember.PersonId
        ) THEN 1 ELSE 0 END AS BIT) AS IsBlockedByMe,
        CAST(CASE WHEN c.ConversationType = 'Direct' AND EXISTS (
            SELECT 1 FROM ChatBlocks b WHERE b.BlockerPersonId = otherMember.PersonId AND b.BlockedPersonId = @PersonId
        ) THEN 1 ELSE 0 END AS BIT) AS IsBlockedByOther
    FROM ChatConversationMembers member
    INNER JOIN ChatConversations c ON member.ConversationId = c.Id
    INNER JOIN Persons me ON me.PersonId = @PersonId AND me.TenantId = @TenantId
    OUTER APPLY (
        SELECT TOP 1 om.PersonId
        FROM ChatConversationMembers om
        WHERE om.ConversationId = c.Id AND om.PersonId <> @PersonId AND om.LeftOnUtc IS NULL
    ) otherMember
    LEFT JOIN Persons otherPerson ON otherMember.PersonId = otherPerson.PersonId AND otherPerson.TenantId = @TenantId
    OUTER APPLY (
        SELECT TOP 1 msg.Body, msg.CreatedOnUtc, msg.DeletedOnUtc
        FROM ChatMessages msg
        WHERE msg.ConversationId = c.Id
          AND (member.ClearedBeforeUtc IS NULL OR msg.CreatedOnUtc > member.ClearedBeforeUtc)
          AND NOT EXISTS (SELECT 1 FROM ChatMessageDeletions d WHERE d.MessageId = msg.Id AND d.PersonId = @PersonId)
        ORDER BY msg.Id DESC
    ) lastMsg
    OUTER APPLY (
        SELECT TOP 1 msg.Id
        FROM ChatMessages msg
        WHERE msg.ConversationId = c.Id
          AND msg.Id > ISNULL(member.LastReadMessageId, 0)
          AND msg.SenderPersonId <> @PersonId
          AND msg.DeletedOnUtc IS NULL
          AND (member.ClearedBeforeUtc IS NULL OR msg.CreatedOnUtc > member.ClearedBeforeUtc)
          AND NOT EXISTS (SELECT 1 FROM ChatMessageDeletions d WHERE d.MessageId = msg.Id AND d.PersonId = @PersonId)
        ORDER BY msg.Id
    ) firstUnread
    OUTER APPLY (
        SELECT TOP 1 msg.Id
        FROM ChatMessages msg
        WHERE msg.ConversationId = c.Id
          AND msg.Id > ISNULL(member.LastReadMessageId, 0)
          AND msg.SenderPersonId <> @PersonId
          AND msg.DeletedOnUtc IS NULL
          AND NULLIF(LTRIM(RTRIM(me.FullName)), '') IS NOT NULL
          AND CHARINDEX(N'@' + LTRIM(RTRIM(me.FullName)), msg.Body) > 0
          AND (member.ClearedBeforeUtc IS NULL OR msg.CreatedOnUtc > member.ClearedBeforeUtc)
          AND NOT EXISTS (SELECT 1 FROM ChatMessageDeletions d WHERE d.MessageId = msg.Id AND d.PersonId = @PersonId)
        ORDER BY msg.Id
    ) firstMention
    WHERE member.TenantId = @TenantId
      AND member.PersonId = @PersonId
      AND member.LeftOnUtc IS NULL
      AND c.IsActive = 1
    ORDER BY member.IsPinned DESC, ISNULL(lastMsg.CreatedOnUtc, c.CreatedOnUtc) DESC;
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE OR ALTER PROCEDURE usp_Chat_GetConversations
    @TenantId INT,
    @PersonId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        c.Id,
        c.ConversationType,
        CASE WHEN c.ConversationType = 'Direct' THEN ISNULL(otherPerson.FirstName + ' ' + otherPerson.LastName, 'Unavailable staff') ELSE ISNULL(c.Title, 'Group conversation') END AS DisplayName,
        CASE WHEN c.ConversationType = 'Direct' THEN otherPerson.ProfilePhotoUrl ELSE c.PhotoUrl END AS PhotoUrl,
        CASE WHEN c.ConversationType = 'Direct' THEN otherMember.PersonId ELSE NULL END AS OtherPersonId,
        CASE WHEN lastMsg.DeletedOnUtc IS NULL THEN lastMsg.Body ELSE 'Message deleted' END AS LastMessage,
        lastMsg.CreatedOnUtc AS LastMessageOnUtc,
        (SELECT COUNT(*) FROM ChatMessages msg WHERE msg.ConversationId = c.Id AND msg.Id > ISNULL(member.LastReadMessageId, 0) AND msg.SenderPersonId <> @PersonId) AS UnreadCount,
        member.IsMuted,
        member.IsPinned,
        member.HasUnreadMention,
        CASE WHEN otherPerson.ShowLastSeen = 1 THEN otherPerson.LastSeenUtc ELSE NULL END AS LastSeenUtc,
        CAST(CASE WHEN c.ConversationType = 'Direct' AND EXISTS (SELECT 1 FROM ChatBlocks b WHERE b.BlockerPersonId = @PersonId AND b.BlockedPersonId = otherMember.PersonId) THEN 1 ELSE 0 END AS BIT) AS IsBlockedByMe,
        CAST(CASE WHEN c.ConversationType = 'Direct' AND EXISTS (SELECT 1 FROM ChatBlocks b WHERE b.BlockerPersonId = otherMember.PersonId AND b.BlockedPersonId = @PersonId) THEN 1 ELSE 0 END AS BIT) AS IsBlockedByOther
    FROM ChatConversationMembers member
    INNER JOIN ChatConversations c ON member.ConversationId = c.Id
    OUTER APPLY (SELECT TOP 1 om.PersonId FROM ChatConversationMembers om WHERE om.ConversationId = c.Id AND om.PersonId <> @PersonId AND om.LeftOnUtc IS NULL) otherMember
    LEFT JOIN Persons otherPerson ON otherMember.PersonId = otherPerson.PersonId AND otherPerson.TenantId = @TenantId
    OUTER APPLY (
        SELECT TOP 1 msg.Body, msg.CreatedOnUtc, msg.DeletedOnUtc
        FROM ChatMessages msg
        WHERE msg.ConversationId = c.Id
          AND (member.ClearedBeforeUtc IS NULL OR msg.CreatedOnUtc > member.ClearedBeforeUtc)
          AND NOT EXISTS (SELECT 1 FROM ChatMessageDeletions d WHERE d.MessageId = msg.Id AND d.PersonId = @PersonId)
        ORDER BY msg.Id DESC
    ) lastMsg
    WHERE member.TenantId = @TenantId AND member.PersonId = @PersonId AND member.LeftOnUtc IS NULL AND c.IsActive = 1
    ORDER BY ISNULL(lastMsg.CreatedOnUtc, c.CreatedOnUtc) DESC;
END;");

            migrationBuilder.DropColumn(
                name: "DeliveryTrackingClearedOnUtc",
                table: "ChatMessages");
        }
    }
}
