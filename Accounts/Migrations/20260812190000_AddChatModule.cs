using Accounts.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accounts.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260812190000_AddChatModule")]
public sealed class AddChatModule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        IF OBJECT_ID(N'dbo.ChatWorkspaces', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ChatWorkspaces
            (
                Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChatWorkspaces PRIMARY KEY,
                TenantId int NOT NULL,
                OrganizationTreeId int NOT NULL,
                IsActive bit NOT NULL CONSTRAINT DF_ChatWorkspaces_IsActive DEFAULT(1),
                CreatedOnUtc datetime2 NOT NULL CONSTRAINT DF_ChatWorkspaces_Created DEFAULT SYSUTCDATETIME(),
                CONSTRAINT FK_ChatWorkspaces_Tenants FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id) ON DELETE CASCADE,
                CONSTRAINT FK_ChatWorkspaces_OrganizationTree FOREIGN KEY(OrganizationTreeId) REFERENCES dbo.OrganizationTree(Id)
            );
            CREATE UNIQUE INDEX UX_ChatWorkspaces_TenantId ON dbo.ChatWorkspaces(TenantId);
        END;

        INSERT INTO dbo.ChatWorkspaces(TenantId, OrganizationTreeId, IsActive, CreatedOnUtc)
        SELECT tenant.Id, tenant.OrganizationTreeId, 1, SYSUTCDATETIME()
        FROM dbo.Tenants tenant
        WHERE NOT EXISTS (SELECT 1 FROM dbo.ChatWorkspaces workspace WHERE workspace.TenantId = tenant.Id);

        IF OBJECT_ID(N'dbo.ChatContactRequests', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ChatContactRequests
            (
                Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChatContactRequests PRIMARY KEY,
                TenantId int NOT NULL,
                WorkspaceId bigint NOT NULL,
                SenderPersonId uniqueidentifier NOT NULL,
                ReceiverPersonId uniqueidentifier NOT NULL,
                PairKey nvarchar(73) NOT NULL,
                Status nvarchar(20) NOT NULL CONSTRAINT DF_ChatContactRequests_Status DEFAULT(N'Pending'),
                Message nvarchar(500) NULL,
                CreatedOnUtc datetime2 NOT NULL CONSTRAINT DF_ChatContactRequests_Created DEFAULT SYSUTCDATETIME(),
                RespondedOnUtc datetime2 NULL,
                CancelledOnUtc datetime2 NULL,
                CONSTRAINT CK_ChatContactRequests_People CHECK(SenderPersonId <> ReceiverPersonId),
                CONSTRAINT CK_ChatContactRequests_Status CHECK(Status IN (N'Pending',N'Accepted',N'Rejected',N'Cancelled')),
                CONSTRAINT FK_ChatContactRequests_Workspace FOREIGN KEY(WorkspaceId) REFERENCES dbo.ChatWorkspaces(Id) ON DELETE CASCADE,
                CONSTRAINT FK_ChatContactRequests_Sender FOREIGN KEY(SenderPersonId) REFERENCES dbo.Persons(PersonId),
                CONSTRAINT FK_ChatContactRequests_Receiver FOREIGN KEY(ReceiverPersonId) REFERENCES dbo.Persons(PersonId)
            );
            CREATE UNIQUE INDEX UX_ChatContactRequests_PendingPair
                ON dbo.ChatContactRequests(WorkspaceId, PairKey) WHERE Status = N'Pending';
            CREATE INDEX IX_ChatContactRequests_Inbox
                ON dbo.ChatContactRequests(TenantId, ReceiverPersonId, Status, CreatedOnUtc DESC);
        END;

        IF OBJECT_ID(N'dbo.ChatConversations', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ChatConversations
            (
                Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChatConversations PRIMARY KEY,
                TenantId int NOT NULL,
                WorkspaceId bigint NOT NULL,
                ConversationType nvarchar(20) NOT NULL CONSTRAINT DF_ChatConversations_Type DEFAULT(N'Direct'),
                Title nvarchar(200) NULL,
                DirectPairKey nvarchar(73) NULL,
                CreatedByPersonId uniqueidentifier NOT NULL,
                CreatedOnUtc datetime2 NOT NULL CONSTRAINT DF_ChatConversations_Created DEFAULT SYSUTCDATETIME(),
                IsActive bit NOT NULL CONSTRAINT DF_ChatConversations_IsActive DEFAULT(1),
                CONSTRAINT CK_ChatConversations_Type CHECK(ConversationType IN (N'Direct',N'Group')),
                CONSTRAINT FK_ChatConversations_Workspace FOREIGN KEY(WorkspaceId) REFERENCES dbo.ChatWorkspaces(Id) ON DELETE CASCADE,
                CONSTRAINT FK_ChatConversations_Creator FOREIGN KEY(CreatedByPersonId) REFERENCES dbo.Persons(PersonId)
            );
            CREATE UNIQUE INDEX UX_ChatConversations_DirectPair
                ON dbo.ChatConversations(WorkspaceId, DirectPairKey) WHERE DirectPairKey IS NOT NULL;
            CREATE INDEX IX_ChatConversations_TenantActive
                ON dbo.ChatConversations(TenantId, IsActive, CreatedOnUtc DESC);
        END;

        IF OBJECT_ID(N'dbo.ChatConversationMembers', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ChatConversationMembers
            (
                Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChatConversationMembers PRIMARY KEY,
                TenantId int NOT NULL,
                ConversationId bigint NOT NULL,
                PersonId uniqueidentifier NOT NULL,
                MemberRole nvarchar(20) NOT NULL CONSTRAINT DF_ChatConversationMembers_Role DEFAULT(N'Member'),
                JoinedOnUtc datetime2 NOT NULL CONSTRAINT DF_ChatConversationMembers_Joined DEFAULT SYSUTCDATETIME(),
                LeftOnUtc datetime2 NULL,
                LastReadMessageId bigint NULL,
                ClearedBeforeUtc datetime2 NULL,
                IsMuted bit NOT NULL CONSTRAINT DF_ChatConversationMembers_Muted DEFAULT(0),
                IsPinned bit NOT NULL CONSTRAINT DF_ChatConversationMembers_Pinned DEFAULT(0),
                CONSTRAINT FK_ChatConversationMembers_Conversation FOREIGN KEY(ConversationId) REFERENCES dbo.ChatConversations(Id) ON DELETE CASCADE,
                CONSTRAINT FK_ChatConversationMembers_Person FOREIGN KEY(PersonId) REFERENCES dbo.Persons(PersonId)
            );
            CREATE UNIQUE INDEX UX_ChatConversationMembers_Person
                ON dbo.ChatConversationMembers(ConversationId, PersonId);
            CREATE INDEX IX_ChatConversationMembers_Inbox
                ON dbo.ChatConversationMembers(TenantId, PersonId, LeftOnUtc);
        END;

        IF OBJECT_ID(N'dbo.ChatMessages', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ChatMessages
            (
                Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChatMessages PRIMARY KEY,
                TenantId int NOT NULL,
                ConversationId bigint NOT NULL,
                SenderPersonId uniqueidentifier NOT NULL,
                ClientMessageId uniqueidentifier NOT NULL,
                Body nvarchar(4000) NOT NULL CONSTRAINT DF_ChatMessages_Body DEFAULT(N''),
                ReplyToMessageId bigint NULL,
                CreatedOnUtc datetime2 NOT NULL CONSTRAINT DF_ChatMessages_Created DEFAULT SYSUTCDATETIME(),
                EditedOnUtc datetime2 NULL,
                DeletedOnUtc datetime2 NULL,
                CONSTRAINT FK_ChatMessages_Conversation FOREIGN KEY(ConversationId) REFERENCES dbo.ChatConversations(Id) ON DELETE CASCADE,
                CONSTRAINT FK_ChatMessages_Sender FOREIGN KEY(SenderPersonId) REFERENCES dbo.Persons(PersonId),
                CONSTRAINT FK_ChatMessages_Reply FOREIGN KEY(ReplyToMessageId) REFERENCES dbo.ChatMessages(Id)
            );
            CREATE INDEX IX_ChatMessages_ConversationPage ON dbo.ChatMessages(ConversationId, Id DESC);
            CREATE UNIQUE INDEX UX_ChatMessages_ClientId
                ON dbo.ChatMessages(TenantId, SenderPersonId, ClientMessageId);
        END;

        IF OBJECT_ID(N'dbo.ChatMessageReactions', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ChatMessageReactions
            (
                Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChatMessageReactions PRIMARY KEY,
                TenantId int NOT NULL,
                MessageId bigint NOT NULL,
                PersonId uniqueidentifier NOT NULL,
                Emoji nvarchar(32) NOT NULL,
                CreatedOnUtc datetime2 NOT NULL CONSTRAINT DF_ChatMessageReactions_Created DEFAULT SYSUTCDATETIME(),
                CONSTRAINT FK_ChatMessageReactions_Message FOREIGN KEY(MessageId) REFERENCES dbo.ChatMessages(Id) ON DELETE CASCADE,
                CONSTRAINT FK_ChatMessageReactions_Person FOREIGN KEY(PersonId) REFERENCES dbo.Persons(PersonId)
            );
            CREATE UNIQUE INDEX UX_ChatMessageReactions_Unique
                ON dbo.ChatMessageReactions(MessageId, PersonId, Emoji);
        END;

        IF OBJECT_ID(N'dbo.ChatAttachments', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ChatAttachments
            (
                Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChatAttachments PRIMARY KEY,
                TenantId int NOT NULL,
                MessageId bigint NOT NULL,
                FileName nvarchar(255) NOT NULL,
                ContentType nvarchar(100) NOT NULL,
                FileSize bigint NOT NULL,
                Content varbinary(max) NOT NULL,
                CreatedOnUtc datetime2 NOT NULL CONSTRAINT DF_ChatAttachments_Created DEFAULT SYSUTCDATETIME(),
                CONSTRAINT CK_ChatAttachments_Size CHECK(FileSize >= 0 AND FileSize <= 10485760),
                CONSTRAINT FK_ChatAttachments_Message FOREIGN KEY(MessageId) REFERENCES dbo.ChatMessages(Id) ON DELETE CASCADE
            );
            CREATE INDEX IX_ChatAttachments_Message ON dbo.ChatAttachments(MessageId, Id);
        END;

        IF OBJECT_ID(N'dbo.ChatBlocks', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ChatBlocks
            (
                Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChatBlocks PRIMARY KEY,
                TenantId int NOT NULL,
                BlockerPersonId uniqueidentifier NOT NULL,
                BlockedPersonId uniqueidentifier NOT NULL,
                CreatedOnUtc datetime2 NOT NULL CONSTRAINT DF_ChatBlocks_Created DEFAULT SYSUTCDATETIME(),
                CONSTRAINT CK_ChatBlocks_People CHECK(BlockerPersonId <> BlockedPersonId),
                CONSTRAINT FK_ChatBlocks_Blocker FOREIGN KEY(BlockerPersonId) REFERENCES dbo.Persons(PersonId),
                CONSTRAINT FK_ChatBlocks_Blocked FOREIGN KEY(BlockedPersonId) REFERENCES dbo.Persons(PersonId)
            );
            CREATE UNIQUE INDEX UX_ChatBlocks_Unique
                ON dbo.ChatBlocks(TenantId, BlockerPersonId, BlockedPersonId);
        END;

        DECLARE @ChatParentId int = (
            SELECT TOP (1) Id FROM dbo.Menus WHERE ParentId IS NULL AND Title = N'Chat' ORDER BY Id
        );
        IF @ChatParentId IS NULL
        BEGIN
            INSERT INTO dbo.Menus(Title, Icon, Route, ParentId, SortOrder, IsActive)
            VALUES(N'Chat', N'MessageCircleMore', NULL, NULL, 75, 1);
            SET @ChatParentId = SCOPE_IDENTITY();
        END
        ELSE
            UPDATE dbo.Menus SET Icon=N'MessageCircleMore', IsActive=1 WHERE Id=@ChatParentId;

        DECLARE @ChatMenuId int = (SELECT TOP (1) Id FROM dbo.Menus WHERE Route=N'/chat');
        IF @ChatMenuId IS NULL
        BEGIN
            INSERT INTO dbo.Menus(Title, Icon, Route, ParentId, SortOrder, IsActive)
            VALUES(N'Messenger', N'MessageCircleMore', N'/chat', @ChatParentId, 1, 1);
            SET @ChatMenuId = SCOPE_IDENTITY();
        END
        ELSE
            UPDATE dbo.Menus SET Title=N'Messenger', Icon=N'MessageCircleMore',
                ParentId=@ChatParentId, SortOrder=1, IsActive=1 WHERE Id=@ChatMenuId;

        DECLARE @ChatMenus TABLE(MenuId int, Title nvarchar(100));
        INSERT INTO @ChatMenus VALUES(@ChatParentId,N'Chat'),(@ChatMenuId,N'Messenger');

        INSERT INTO dbo.Features(FeatureKey,FeatureName,Module,Description,CreatedDate)
        SELECT CONCAT(N'MENU_',menu.MenuId,suffix.Suffix),
               CONCAT(menu.Title,suffix.DisplayName),N'Chat',
               CONCAT(suffix.ActionName,N' ',menu.Title),SYSUTCDATETIME()
        FROM @ChatMenus menu
        CROSS JOIN (VALUES
            (N'',N'',N'Open'),(N'_VIEW',N' - View',N'View'),
            (N'_ADD',N' - Send',N'Send'),(N'_EDIT',N' - Manage',N'Manage'),
            (N'_DELETE',N' - Delete',N'Delete')
        ) suffix(Suffix,DisplayName,ActionName)
        WHERE NOT EXISTS (
            SELECT 1 FROM dbo.Features feature
            WHERE feature.FeatureKey=CONCAT(N'MENU_',menu.MenuId,suffix.Suffix));

        INSERT INTO dbo.MenuPermissions(MenuId,PermissionId)
        SELECT menu.MenuId,feature.PermissionId
        FROM @ChatMenus menu
        JOIN dbo.Features feature ON feature.FeatureKey IN (
            CONCAT(N'MENU_',menu.MenuId),CONCAT(N'MENU_',menu.MenuId,N'_VIEW'),
            CONCAT(N'MENU_',menu.MenuId,N'_ADD'),CONCAT(N'MENU_',menu.MenuId,N'_EDIT'),
            CONCAT(N'MENU_',menu.MenuId,N'_DELETE'))
        WHERE NOT EXISTS (
            SELECT 1 FROM dbo.MenuPermissions existing
            WHERE existing.MenuId=menu.MenuId AND existing.PermissionId=feature.PermissionId);

        INSERT INTO dbo.TenantMenuPermissions
            (TenantId,MenuId,IsAllow,CanView,CanAdd,CanEdit,CanDelete,GrantedOnUtc,GrantedByUserId)
        SELECT tenant.Id,menu.MenuId,1,1,1,1,0,SYSUTCDATETIME(),N'SYSTEM'
        FROM dbo.Tenants tenant CROSS JOIN @ChatMenus menu
        WHERE tenant.IsActive=1 AND NOT EXISTS (
            SELECT 1 FROM dbo.TenantMenuPermissions existing
            WHERE existing.TenantId=tenant.Id AND existing.MenuId=menu.MenuId);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE IF EXISTS dbo.ChatAttachments;
        DROP TABLE IF EXISTS dbo.ChatMessageReactions;
        DROP TABLE IF EXISTS dbo.ChatBlocks;
        DROP TABLE IF EXISTS dbo.ChatMessages;
        DROP TABLE IF EXISTS dbo.ChatConversationMembers;
        DROP TABLE IF EXISTS dbo.ChatContactRequests;
        DROP TABLE IF EXISTS dbo.ChatConversations;
        DROP TABLE IF EXISTS dbo.ChatWorkspaces;
        """);
}
