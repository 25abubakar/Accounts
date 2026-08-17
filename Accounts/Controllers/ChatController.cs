using System.Security.Claims;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Hubs;
using Accounts.Services;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
[Produces("application/json")]
public sealed class ChatController(
    IChatService chat,
    ApplicationDbContext db,
    IHubContext<ChatHub> hub) : ControllerBase
{
    [HttpGet("bootstrap")]
    public Task<IActionResult> Bootstrap(CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () => Ok(await chat.GetBootstrapAsync(UserId(), cancellationToken)));

    [HttpGet("directory")]
    public Task<IActionResult> Directory(
        [FromQuery] string? search,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () => Ok(await chat.GetDirectoryAsync(
            UserId(), search, take, cancellationToken)));

    [HttpGet("requests")]
    public Task<IActionResult> Requests(CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await chat.GetRequestsAsync(UserId(), cancellationToken)));

    [HttpPost("requests")]
    public Task<IActionResult> CreateRequest(
        [FromBody] CreateChatRequestDto dto,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var request = await chat.CreateRequestAsync(UserId(), dto, cancellationToken);
            await hub.Clients.Group(ChatHub.PersonGroup(request.ReceiverPersonId))
                .SendAsync("chatRequestReceived", request, cancellationToken);
            return Ok(request);
        });

    [HttpPut("requests/{requestId:long}/decision")]
    public Task<IActionResult> DecideRequest(
        long requestId,
        [FromBody] DecideChatRequestDto dto,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var result = await chat.DecideRequestAsync(UserId(), requestId, dto, cancellationToken);
            await Task.WhenAll(
                hub.Clients.Group(ChatHub.PersonGroup(result.Request.SenderPersonId))
                    .SendAsync("chatRequestUpdated", result, cancellationToken),
                hub.Clients.Group(ChatHub.PersonGroup(result.Request.ReceiverPersonId))
                    .SendAsync("chatRequestUpdated", result, cancellationToken));
            return Ok(result);
        });

    [HttpGet("conversations")]
    public Task<IActionResult> Conversations(CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await chat.GetConversationsAsync(UserId(), cancellationToken)));

    [HttpPost("conversations/groups")]
    public Task<IActionResult> CreateGroup(
        [FromBody] CreateChatGroupDto dto,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var conversation = await chat.CreateGroupAsync(UserId(), dto, cancellationToken);
            await NotifyMembersAsync(
                conversation.Id,
                "conversationCreated",
                conversation,
                cancellationToken);
            return Ok(conversation);
        });

    [HttpGet("conversations/{conversationId:long}/messages")]
    public Task<IActionResult> Messages(
        long conversationId,
        [FromQuery] long? beforeId,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () => Ok(await chat.GetMessagesAsync(
            UserId(), conversationId, beforeId, take, cancellationToken)));

    [HttpPost("conversations/{conversationId:long}/messages")]
    public Task<IActionResult> SendMessage(
        long conversationId,
        [FromBody] CreateChatMessageDto dto,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var message = await chat.SendMessageAsync(UserId(), conversationId, dto, cancellationToken);
            await NotifyMembersAsync(conversationId, "messageReceived", message, cancellationToken);
            return Ok(message);
        });

    [HttpPost("conversations/{conversationId:long}/attachments")]
    [RequestSizeLimit(10_600_000)]
    public Task<IActionResult> SendAttachment(
        long conversationId,
        IFormFile file,
        [FromForm] Guid clientMessageId,
        [FromForm] string? caption,
        [FromForm] long? replyToMessageId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            if (file == null || file.Length == 0)
                throw new ChatValidationException("Select a file to upload.");
            if (file.Length > 10 * 1024 * 1024)
                throw new ChatValidationException("Attachments cannot exceed 10 MB.");

            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);
            var message = await chat.SendAttachmentAsync(
                UserId(),
                conversationId,
                clientMessageId,
                caption,
                file.FileName,
                file.ContentType,
                stream.ToArray(),
                replyToMessageId,
                cancellationToken);
            await NotifyMembersAsync(conversationId, "messageReceived", message, cancellationToken);
            return Ok(message);
        });

    [HttpPut("messages/{messageId:long}/edit")]
    public Task<IActionResult> EditMessage(
        long messageId,
        [FromBody] EditChatMessageDto dto,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var message = await chat.EditMessageAsync(UserId(), messageId, dto, cancellationToken);
            await NotifyMembersAsync(message.ConversationId, "messageEdited", message, cancellationToken);
            return Ok(message);
        });

    [HttpDelete("messages/{messageId:long}")]
    public Task<IActionResult> DeleteMessage(
        long messageId,
        [FromQuery] bool forEveryone,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var conversationId = await chat.DeleteMessageAsync(UserId(), messageId, forEveryone, cancellationToken);
            if (forEveryone)
            {
                await NotifyMembersAsync(conversationId, "messageDeleted", new { id = messageId, conversationId }, cancellationToken);
            }
            return NoContent();
        });

    [HttpPost("messages/{messageId:long}/forward")]
    public Task<IActionResult> ForwardMessage(
        long messageId,
        [FromQuery] long targetConversationId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var message = await chat.ForwardMessageAsync(UserId(), messageId, targetConversationId, cancellationToken);
            await NotifyMembersAsync(targetConversationId, "messageReceived", message, cancellationToken);
            return Ok(message);
        });

    [HttpGet("attachments/{attachmentId:long}")]
    public async Task<IActionResult> DownloadAttachment(
        long attachmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var attachment = await chat.GetAttachmentAsync(UserId(), attachmentId, cancellationToken);
            return File(attachment.Content, attachment.ContentType, attachment.FileName);
        }
        catch (Exception exception)
        {
            return MapException(exception);
        }
    }

    [HttpPut("messages/{messageId:long}/reaction")]
    public Task<IActionResult> SetReaction(
        long messageId,
        [FromBody] SetChatReactionDto dto,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var message = await chat.SetReactionAsync(UserId(), messageId, dto, cancellationToken);
            await NotifyMembersAsync(message.ConversationId, "reactionUpdated", message, cancellationToken);
            return Ok(message);
        });

    [HttpPut("conversations/{conversationId:long}/read")]
    public Task<IActionResult> MarkRead(
        long conversationId,
        [FromBody] MarkChatReadDto dto,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var caller = await chat.ResolveCallerAsync(UserId(), cancellationToken);
            await chat.MarkReadAsync(UserId(), conversationId, dto, cancellationToken);
            await hub.Clients.Group(ChatHub.ConversationGroup(conversationId))
                .SendAsync("messagesRead", new
                {
                    conversationId,
                    personId = caller.PersonId,
                    messageId = dto.MessageId,
                }, cancellationToken);
            return NoContent();
        });

    [HttpPut("conversations/{conversationId:long}/preferences")]
    public Task<IActionResult> UpdatePreference(
        long conversationId,
        [FromBody] UpdateChatPreferenceDto dto,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            await chat.UpdatePreferenceAsync(UserId(), conversationId, dto, cancellationToken);
            return NoContent();
        });

    [HttpDelete("conversations/{conversationId:long}/messages")]
    public Task<IActionResult> ClearConversation(
        long conversationId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            await chat.ClearConversationAsync(UserId(), conversationId, cancellationToken);
            return NoContent();
        });

    [HttpPost("blocks/{personId:guid}")]
    public Task<IActionResult> Block(
        Guid personId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            await chat.BlockAsync(UserId(), personId, cancellationToken);
            await hub.Clients.Group(ChatHub.PersonGroup(personId))
                .SendAsync("chatBlocked", new { personId }, cancellationToken);
            return NoContent();
        });

    [HttpDelete("blocks/{personId:guid}")]
    public Task<IActionResult> Unblock(
        Guid personId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            await chat.UnblockAsync(UserId(), personId, cancellationToken);
            return NoContent();
        });

    [HttpGet("blocks")]
    public Task<IActionResult> GetBlockedPersons(
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await chat.GetBlockedPersonsAsync(UserId(), cancellationToken)));

    private string UserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new ChatForbiddenException("The authenticated user could not be resolved.");

    private async Task NotifyMembersAsync(
        long conversationId,
        string eventName,
        object payload,
        CancellationToken cancellationToken)
    {
        var people = await db.ChatConversationMembers.AsNoTracking()
            .Where(member => member.ConversationId == conversationId && member.LeftOnUtc == null)
            .Select(member => member.PersonId)
            .ToListAsync(cancellationToken);
        await Task.WhenAll(people.Select(personId =>
            hub.Clients.Group(ChatHub.PersonGroup(personId))
                .SendAsync(eventName, payload, cancellationToken)));
    }

    [HttpGet("conversations/{conversationId:long}/members")]
    public Task<IActionResult> GetGroupMembers(
        long conversationId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Ok(await chat.GetGroupMembersAsync(UserId(), conversationId, cancellationToken)));

    [HttpPost("conversations/{conversationId:long}/members")]
    public Task<IActionResult> AddGroupMembers(
        long conversationId,
        [FromBody] AddChatGroupMembersDto dto,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            await chat.AddGroupMembersAsync(UserId(), conversationId, dto, cancellationToken);
            return Ok();
        });

    [HttpDelete("conversations/{conversationId:long}/members/{personId:guid}")]
    public Task<IActionResult> RemoveGroupMember(
        long conversationId,
        Guid personId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            await chat.RemoveGroupMemberAsync(UserId(), conversationId, personId, cancellationToken);
            return Ok();
        });

    [HttpPut("conversations/{conversationId:long}/members/{personId:guid}/role")]
    public Task<IActionResult> UpdateMemberRole(
        long conversationId,
        Guid personId,
        [FromBody] UpdateChatMemberRoleDto dto,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            await chat.UpdateMemberRoleAsync(UserId(), conversationId, personId, dto, cancellationToken);
            return Ok();
        });

    [HttpPut("privacy")]
    public Task<IActionResult> UpdatePrivacySettings(
        [FromBody] UpdatePrivacySettingsDto dto,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            await chat.UpdatePrivacySettingsAsync(UserId(), dto, cancellationToken);
            return Ok();
        });

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception exception)
        {
            return MapException(exception);
        }
    }

    private IActionResult MapException(Exception exception) => exception switch
    {
        ChatValidationException => BadRequest(new { message = exception.Message }),
        ChatForbiddenException => StatusCode(StatusCodes.Status403Forbidden, new { message = exception.Message }),
        ChatNotFoundException => NotFound(new { message = exception.Message }),
        ChatConflictException => Conflict(new { message = exception.Message }),
        OperationCanceledException => StatusCode(499),
        _ => Problem(
            title: "Chat operation failed",
            detail: HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment()
                ? exception.Message
                : "The chat operation could not be completed.",
            statusCode: StatusCodes.Status500InternalServerError),
    };
}
