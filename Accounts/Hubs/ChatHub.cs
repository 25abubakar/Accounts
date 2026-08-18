using System.Security.Claims;
using Accounts.Services;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Accounts.Hubs;

[Authorize]
public sealed class ChatHub(
    IChatService chatService,
    Accounts.Data.ApplicationDbContext db,
    ChatPresenceTracker presence) : Hub
{
    private const string PersonIdKey = "ChatPersonId";
    private const string TenantIdKey = "ChatTenantId";

    public static string TenantGroup(int tenantId) => $"chat:tenant:{tenantId}";
    public static string PersonGroup(Guid personId) => $"chat:person:{personId:N}";
    public static string ConversationGroup(long conversationId) => $"chat:conversation:{conversationId}";

    public override async Task OnConnectedAsync()
    {
        var identityUserId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(identityUserId))
        {
            Context.Abort();
            return;
        }

        try
        {
            var caller = await chatService.ResolveCallerAsync(identityUserId, Context.ConnectionAborted);
            Context.Items[PersonIdKey] = caller.PersonId;
            Context.Items[TenantIdKey] = caller.TenantId;
            await Groups.AddToGroupAsync(Context.ConnectionId, PersonGroup(caller.PersonId));
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(caller.TenantId));

            if (presence.Connect(caller.PersonId, Context.ConnectionId))
            {
                var person = await db.Persons.FindAsync(caller.PersonId);
                if (person != null && person.ShowLastSeen)
                {
                    await Clients.OthersInGroup(TenantGroup(caller.TenantId))
                        .SendAsync("presenceChanged", new { caller.PersonId, isOnline = true });
                }
            }

            await base.OnConnectedAsync();
        }
        catch (ChatForbiddenException)
        {
            Context.Abort();
        }
        catch (Exception ex) when (ChatExceptionHelper.IsCancellation(ex))
        {
            // Client disconnected or the request was cancelled while resolving the caller.
            Context.Abort();
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var (personId, isNowOffline) = presence.Disconnect(Context.ConnectionId);
        if (personId.HasValue &&
            isNowOffline &&
            Context.Items.TryGetValue(TenantIdKey, out var tenantValue) &&
            tenantValue is int tenantId)
        {
            var person = await db.Persons.FindAsync(personId.Value);
            if (person != null)
            {
                person.LastSeenUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();

                if (person.ShowLastSeen)
                {
                    await Clients.OthersInGroup(TenantGroup(tenantId))
                        .SendAsync("presenceChanged", new { PersonId = personId.Value, isOnline = false });
                }
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinConversation(long conversationId)
    {
        try
        {
            var personId = CurrentPersonId();
            if (!await chatService.IsConversationMemberAsync(personId, conversationId, Context.ConnectionAborted))
                return;
            await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));
        }
        catch (OperationCanceledException)
        {
            // Ignore cancelled operations
        }
    }

    public async Task LeaveConversation(long conversationId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));

    public async Task SetTyping(long conversationId, bool isTyping)
    {
        try
        {
            var personId = CurrentPersonId();
            if (!await chatService.IsConversationMemberAsync(personId, conversationId, Context.ConnectionAborted))
                return;
            await Clients.OthersInGroup(ConversationGroup(conversationId))
                .SendAsync("typingChanged", new { conversationId, personId, isTyping });
        }
        catch (OperationCanceledException)
        {
            // Ignore cancelled operations
        }
    }

    private Guid CurrentPersonId()
    {
        if (Context.Items.TryGetValue(PersonIdKey, out var value) &&
            value is Guid personId)
            return personId;
        throw new HubException("An active staff profile is required.");
    }

    public async Task Typing(long conversationId)
    {
        if (Context.Items.TryGetValue(PersonIdKey, out var pidObj) && pidObj is Guid personId)
        {
            // Broadcast "userTyping" to all members of the conversation EXCEPT the sender.
            await Clients.OthersInGroup(ConversationGroup(conversationId))
                .SendAsync("userTyping", new { conversationId, personId });
        }
    }
}
