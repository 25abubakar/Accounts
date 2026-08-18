using System.Data;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Hosting;

namespace Accounts.Services.Services;

public sealed class ChatService(
    ApplicationDbContext db,
    ITenantService tenant,
    ChatPresenceTracker presence,
    IMemoryCache cache,
    IWebHostEnvironment env) : IChatService
{
    private static readonly HashSet<string> AllowedAttachmentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp",
        "application/pdf", "text/plain",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/zip",
    };

    private (Guid PersonId, int TenantId, long WorkspaceId, int TenantOrganizationTreeId)? _callerContext;

    public async Task<(Guid PersonId, int TenantId, long WorkspaceId, int TenantOrganizationTreeId)> ResolveCallerAsync(
        string identityUserId,
        CancellationToken cancellationToken = default)
    {
        if (_callerContext.HasValue)
            return _callerContext.Value;

        // FIX #6: Try IMemoryCache before hitting the DB (5-min sliding window)
        var cacheKey = $"chat:caller:{identityUserId}";
        if (cache.TryGetValue(cacheKey, out (Guid PersonId, int TenantId, long WorkspaceId, int TenantOrganizationTreeId) cached))
        {
            _callerContext = cached;
            return cached;
        }

        if (tenant.IsSuperAdmin || !tenant.TenantId.HasValue)
            throw new ChatForbiddenException("Chat is available only to active tenant staff.");

        var tenantId = tenant.TenantId.Value;
        var person = await db.Persons.AsNoTracking()
            .Where(item =>
                item.TenantId == tenantId &&
                item.IdentityUserId == identityUserId &&
                item.IsActive)
            .Select(item => new
            {
                item.PersonId,
                item.EmploymentStatus,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (person == null ||
            string.Equals(person.EmploymentStatus, "Fired", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(person.EmploymentStatus, "Retired", StringComparison.OrdinalIgnoreCase))
            throw new ChatForbiddenException("An active staff profile is required to use chat.");

        var hasActiveAssignment = await db.StaffVacancies.AsNoTracking()
            .Where(staff =>
                staff.TenantId == tenantId &&
                staff.PersonId == person.PersonId &&
                staff.VacancyId != null)
            .Join(
                db.Vacancies.AsNoTracking().Where(vacancy => vacancy.TenantId == tenantId && vacancy.IsFilled),
                staff => staff.VacancyId,
                vacancy => vacancy.VacancyId,
                (_, vacancy) => vacancy.VacancyId)
            .AnyAsync(cancellationToken);

        if (!hasActiveAssignment)
            throw new ChatForbiddenException("An active staff profile is required to use chat.");

        var tenantRecord = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == tenantId && item.IsActive, cancellationToken);
        if (tenantRecord == null)
            throw new ChatForbiddenException("The chat workspace is not available for this tenant.");

        var workspaceId = await db.ChatWorkspaces
            .Where(workspace => workspace.TenantId == tenantId && workspace.IsActive)
            .OrderByDescending(workspace => workspace.OrganizationTreeId == tenantRecord.OrganizationTreeId ? 1 : 0)
            .Select(workspace => (long?)workspace.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!workspaceId.HasValue)
        {
            var workspace = new ChatWorkspace
            {
                TenantId = tenantId,
                OrganizationTreeId = tenantRecord.OrganizationTreeId,
            };
            db.ChatWorkspaces.Add(workspace);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                workspaceId = workspace.Id;
            }
            catch (DbUpdateException)
            {
                db.Entry(workspace).State = EntityState.Detached;
                workspaceId = await db.ChatWorkspaces
                    .Where(item => item.TenantId == tenantId && item.IsActive)
                    .Select(item => (long?)item.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        if (!workspaceId.HasValue)
            throw new ChatConflictException("The chat workspace could not be initialized.");

        var result = (person.PersonId, tenantId, workspaceId.Value, tenantRecord.OrganizationTreeId);
        _callerContext = result;

        // Store in cache for 5 minutes sliding — safe because workspace/person rarely changes
        cache.Set(cacheKey, result, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(5),
            Size = 1,
        });

        return result;
    }

    public async Task<ChatBootstrapDto> GetBootstrapAsync(string identityUserId, CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);

        var me = await db.Persons.AsNoTracking()
            .Where(p => p.PersonId == caller.PersonId && p.TenantId == caller.TenantId)
            .Select(p => new ChatPersonDto(
                p.PersonId,
                p.FirstName + " " + p.LastName,
                p.ProfilePhotoUrl,
                p.Staff != null && p.Staff.Vacancy != null ? p.Staff.Vacancy.Organization.Name : "",
                p.Staff != null && p.Staff.Vacancy != null ? (p.Staff.Vacancy.DesignationNav != null ? p.Staff.Vacancy.DesignationNav.Name : (p.Staff.Vacancy.JobTitle ?? "")) : "",
                false,
                false,
                null
            ))
            .SingleOrDefaultAsync(cancellationToken);

        if (me == null)
            throw new ChatConflictException("Could not resolve current user profile.");

        var conversations = await GetConversationsAsync(identityUserId, cancellationToken);

        var pendingCount = await db.ChatContactRequests
            .Where(r => r.TenantId == caller.TenantId && r.ReceiverPersonId == caller.PersonId && r.Status == "Pending")
            .CountAsync(cancellationToken);

        return new ChatBootstrapDto(me, conversations, pendingCount);
    }

    public async Task<IReadOnlyList<ChatPersonDto>> GetDirectoryAsync(
        string identityUserId,
        string? search,
        int take,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        take = Math.Clamp(take, 1, 100);
        var blockedIds = await GetBlockedPersonIdsAsync(caller.TenantId, caller.PersonId, cancellationToken);
        var normalized = search?.Trim();

        var query = db.StaffDirectoryRows.AsNoTracking()
            .Where(row =>
                row.TenantId == caller.TenantId &&
                row.IsPersonActive &&
                row.PersonId != caller.PersonId &&
                !blockedIds.Contains(row.PersonId));

        if (!string.IsNullOrWhiteSpace(normalized))
            query = query.Where(row =>
                row.FullName.Contains(normalized) ||
                row.Department.Contains(normalized) ||
                row.Designation.Contains(normalized));

        var joinedQuery =
            from row in query
            join person in db.Persons.AsNoTracking() on row.PersonId equals person.PersonId
            select new
            {
                row.PersonId,
                row.FullName,
                row.PhotoUrl,
                row.Department,
                row.Designation,
                row.OrganizationId,
                person.ShowLastSeen,
                person.LastSeenUtc
            };

        var rows = await joinedQuery
            .OrderBy(row => row.FullName)
            .Take(500)
            .ToListAsync(cancellationToken);

        return rows
            .Take(take)
            .Select(row => new ChatPersonDto(
                row.PersonId,
                row.FullName,
                row.PhotoUrl,
                row.Department,
                row.Designation,
                presence.IsOnline(row.PersonId),
                row.ShowLastSeen,
                row.LastSeenUtc))
            .ToList();
    }

    public async Task<IReadOnlyList<ChatContactRequestDto>> GetRequestsAsync(
        string identityUserId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        var requests = await db.ChatContactRequests.AsNoTracking()
            .Where(request =>
                request.WorkspaceId == caller.WorkspaceId &&
                request.Status == "Pending" &&
                (request.SenderPersonId == caller.PersonId || request.ReceiverPersonId == caller.PersonId))
            .OrderByDescending(request => request.CreatedOnUtc)
            .ToListAsync(cancellationToken);

        var personIds = requests.Select(request =>
                request.SenderPersonId == caller.PersonId ? request.ReceiverPersonId : request.SenderPersonId)
            .Distinct()
            .ToArray();
        var people = await LoadPeopleAsync(personIds, cancellationToken);

        return requests.Select(request =>
        {
            var otherId = request.SenderPersonId == caller.PersonId
                ? request.ReceiverPersonId
                : request.SenderPersonId;
            return ToRequestDto(request, caller.PersonId, people[otherId]);
        }).ToList();
    }

    public async Task<ChatContactRequestDto> CreateRequestAsync(
        string identityUserId,
        CreateChatRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        if (dto.ReceiverPersonId == caller.PersonId)
            throw new ChatValidationException("You cannot send a chat request to yourself.");

        var receiver = await db.Persons.AsNoTracking()
            .Where(person =>
                person.PersonId == dto.ReceiverPersonId &&
                person.TenantId == caller.TenantId &&
                person.IsActive &&
                person.Staff != null &&
                person.Staff.VacancyId != null &&
                person.Staff.Vacancy != null &&
                person.Staff.Vacancy.IsFilled)
            .Select(person => new
            {
                person.PersonId,
                person.EmploymentStatus,
                OrganizationId = person.Staff!.Vacancy!.OrganizationId,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (receiver == null ||
            string.Equals(receiver.EmploymentStatus, "Fired", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(receiver.EmploymentStatus, "Retired", StringComparison.OrdinalIgnoreCase))
            throw new ChatNotFoundException("The selected staff member is not available in this tenant.");

        if (await IsBlockedAsync(caller.TenantId, caller.PersonId, receiver.PersonId, cancellationToken))
            throw new ChatForbiddenException("A chat request cannot be sent to this staff member.");

        var pairKey = PairKey(caller.PersonId, receiver.PersonId);
        var existingConversation = await db.ChatConversations.AsNoTracking()
            .AnyAsync(conversation =>
                conversation.WorkspaceId == caller.WorkspaceId &&
                conversation.DirectPairKey == pairKey &&
                conversation.IsActive, cancellationToken);
        if (existingConversation)
            throw new ChatConflictException("A direct conversation already exists with this staff member.");

        var existingRequest = await db.ChatContactRequests.AsNoTracking()
            .AnyAsync(request =>
                request.WorkspaceId == caller.WorkspaceId &&
                request.PairKey == pairKey &&
                request.Status == "Pending", cancellationToken);
        if (existingRequest)
            throw new ChatConflictException("A chat request is already pending between these staff members.");

        var request = new ChatContactRequest
        {
            TenantId = caller.TenantId,
            WorkspaceId = caller.WorkspaceId,
            SenderPersonId = caller.PersonId,
            ReceiverPersonId = receiver.PersonId,
            PairKey = pairKey,
            Message = TrimOrNull(dto.Message, 500),
        };
        db.ChatContactRequests.Add(request);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.Entry(request).State = EntityState.Detached;
            var existingReq = await db.ChatContactRequests
                .SingleOrDefaultAsync(item => item.PairKey == pairKey, cancellationToken);
            if (existingReq != null)
            {
                var loadedPeople = await LoadPeopleAsync([receiver.PersonId], cancellationToken);
                return ToRequestDto(existingReq, caller.PersonId, loadedPeople[receiver.PersonId]);
            }
            throw new ChatConflictException("A chat request is already pending between these staff members.");
        }

        var people = await LoadPeopleAsync([receiver.PersonId], cancellationToken);
        return ToRequestDto(request, caller.PersonId, people[receiver.PersonId]);
    }

    public async Task<(ChatContactRequestDto Request, ChatConversationDto? Conversation)> DecideRequestAsync(
        string identityUserId,
        long requestId,
        DecideChatRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        ChatContactRequest? decidedRequest = null;
        long? conversationId = null;

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var request = await db.ChatContactRequests
                .SingleOrDefaultAsync(item =>
                    item.Id == requestId &&
                    item.WorkspaceId == caller.WorkspaceId, cancellationToken);
            if (request == null) throw new ChatNotFoundException("The chat request was not found.");
            if (request.ReceiverPersonId != caller.PersonId)
                throw new ChatForbiddenException("Only the recipient can respond to this chat request.");
            if (request.Status != "Pending")
            {
                decidedRequest = request;
                if (request.Status == "Accepted")
                {
                    conversationId = await db.ChatConversations
                        .Where(item => item.DirectPairKey == request.PairKey)
                        .Select(item => item.Id)
                        .SingleOrDefaultAsync(cancellationToken);
                }
                return;
            }
            if (await IsBlockedAsync(caller.TenantId, request.SenderPersonId, request.ReceiverPersonId, cancellationToken))
                throw new ChatForbiddenException("This chat request can no longer be accepted.");

            request.Status = dto.Accept ? "Accepted" : "Rejected";
            request.RespondedOnUtc = DateTime.UtcNow;

            if (dto.Accept)
            {
                var conversation = await db.ChatConversations
                    .SingleOrDefaultAsync(item =>
                        item.WorkspaceId == caller.WorkspaceId &&
                        item.DirectPairKey == request.PairKey, cancellationToken);
                if (conversation == null)
                {
                    conversation = new ChatConversation
                    {
                        TenantId = caller.TenantId,
                        WorkspaceId = caller.WorkspaceId,
                        ConversationType = "Direct",
                        DirectPairKey = request.PairKey,
                        CreatedByPersonId = request.SenderPersonId,
                    };
                    db.ChatConversations.Add(conversation);
                    await db.SaveChangesAsync(cancellationToken);
                    db.ChatConversationMembers.AddRange(
                        new ChatConversationMember
                        {
                            TenantId = caller.TenantId,
                            ConversationId = conversation.Id,
                            PersonId = request.SenderPersonId,
                        },
                        new ChatConversationMember
                        {
                            TenantId = caller.TenantId,
                            ConversationId = conversation.Id,
                            PersonId = request.ReceiverPersonId,
                        });
                }
                conversationId = conversation.Id;
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            decidedRequest = request;
        });

        var otherPersonId = decidedRequest!.SenderPersonId == caller.PersonId
            ? decidedRequest.ReceiverPersonId
            : decidedRequest.SenderPersonId;
        var people = await LoadPeopleAsync([otherPersonId], cancellationToken);
        ChatConversationDto? conversationDto = null;
        if (conversationId.HasValue)
            conversationDto = (await GetConversationsAsync(identityUserId, cancellationToken))
                .Single(item => item.Id == conversationId.Value);

        return (ToRequestDto(decidedRequest, caller.PersonId, people[otherPersonId]), conversationDto);
    }

    public async Task<IReadOnlyList<ChatConversationDto>> GetConversationsAsync(
        string identityUserId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        
        var results = await db.Database.SqlQueryRaw<ChatConversationResult>(
            "EXEC usp_Chat_GetConversations @TenantId = {0}, @PersonId = {1}", 
            caller.TenantId, caller.PersonId
        ).ToListAsync(cancellationToken);

        if (results.Count == 0) return [];

        return results.Select(r => new ChatConversationDto(
            r.Id,
            r.ConversationType,
            r.DisplayName,
            r.PhotoUrl,
            r.OtherPersonId,
            r.LastMessage,
            r.LastMessageOnUtc,
            r.UnreadCount,
            r.IsMuted,
            r.IsPinned,
            r.OtherPersonId.HasValue && presence.IsOnline(r.OtherPersonId.Value),
            r.LastSeenUtc,
            r.HasUnreadMention
        ))
        .OrderByDescending(item => item.IsPinned)
        .ThenByDescending(item => item.LastMessageOnUtc ?? DateTime.MinValue)
        .ToList();
    }

    public async Task<ChatConversationDto> CreateGroupAsync(
        string identityUserId,
        CreateChatGroupDto dto,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        var title = dto.Title?.Trim() ?? string.Empty;
        if (title.Length < 2 || title.Length > 200)
            throw new ChatValidationException("Group names must contain 2 to 200 characters.");

        var memberIds = dto.MemberPersonIds
            .Where(personId => personId != caller.PersonId)
            .Distinct()
            .ToArray();
        if (memberIds.Length < 2)
            throw new ChatValidationException("Select at least two other staff members for a group.");
        if (memberIds.Length > 99)
            throw new ChatValidationException("A group can contain at most 100 members.");

        var eligiblePeople = await db.Persons.AsNoTracking()
            .Where(person =>
                memberIds.Contains(person.PersonId) &&
                person.TenantId == caller.TenantId &&
                person.IsActive &&
                person.Staff != null &&
                person.Staff.VacancyId != null &&
                person.Staff.Vacancy != null &&
                person.Staff.Vacancy.IsFilled)
            .Select(person => new
            {
                person.PersonId,
                person.EmploymentStatus,
                OrganizationId = person.Staff!.Vacancy!.OrganizationId,
            })
            .ToListAsync(cancellationToken);
        var people = eligiblePeople.ToDictionary(p => p.PersonId);
        foreach (var personId in dto.MemberPersonIds.Distinct().Where(id => id != caller.PersonId))
        {
            if (!people.TryGetValue(personId, out var person) ||
                string.Equals(person.EmploymentStatus, "Fired", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(person.EmploymentStatus, "Retired", StringComparison.OrdinalIgnoreCase))
                throw new ChatValidationException("One or more selected staff members are not available.");
        }
        var eligibleIds = eligiblePeople
            .Where(person =>
                !string.Equals(person.EmploymentStatus, "Fired", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(person.EmploymentStatus, "Retired", StringComparison.OrdinalIgnoreCase))
            .Select(person => person.PersonId)
            .ToArray();
        if (eligibleIds.Length != memberIds.Length)
            throw new ChatValidationException("One or more selected staff members are not eligible for this company group.");

        var blocked = await db.ChatBlocks.AsNoTracking().AnyAsync(block =>
            (block.BlockerPersonId == caller.PersonId && memberIds.Contains(block.BlockedPersonId)) ||
            (block.BlockedPersonId == caller.PersonId && memberIds.Contains(block.BlockerPersonId)),
            cancellationToken);
        if (blocked)
            throw new ChatForbiddenException("A group cannot include a blocked staff member.");

        var strategy = db.Database.CreateExecutionStrategy();
        long conversationId = 0;
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var conversation = new ChatConversation
            {
                TenantId = caller.TenantId,
                WorkspaceId = caller.WorkspaceId,
                ConversationType = "Group",
                Title = title,
                CreatedByPersonId = caller.PersonId,
            };
            db.ChatConversations.Add(conversation);
            await db.SaveChangesAsync(cancellationToken);
            conversationId = conversation.Id;
            db.ChatConversationMembers.Add(new ChatConversationMember
            {
                TenantId = caller.TenantId,
                ConversationId = conversation.Id,
                PersonId = caller.PersonId,
                MemberRole = "Admin",
            });
            db.ChatConversationMembers.AddRange(memberIds.Select(personId => new ChatConversationMember
            {
                TenantId = caller.TenantId,
                ConversationId = conversation.Id,
                PersonId = personId,
            }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        return (await GetConversationsAsync(identityUserId, cancellationToken))
            .Single(conversation => conversation.Id == conversationId);
    }

    public async Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(
        string identityUserId,
        long conversationId,
        long? beforeId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        var membership = await RequireMembershipAsync(caller.PersonId, conversationId, cancellationToken);
        take = Math.Clamp(take, 1, 100);

        var query = db.ChatMessages.AsNoTracking()
            .Where(message =>
                message.ConversationId == conversationId &&
                (!membership.ClearedBeforeUtc.HasValue || message.CreatedOnUtc > membership.ClearedBeforeUtc.Value) &&
                !db.ChatMessageDeletions.Any(d => d.MessageId == message.Id && d.PersonId == caller.PersonId));
        if (beforeId.HasValue) query = query.Where(message => message.Id < beforeId.Value);

        var messages = await query.OrderByDescending(message => message.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
        messages.Reverse();
        return await MapMessagesAsync(messages, caller.PersonId, cancellationToken);
    }

    public async Task<ChatMessageDto> SendMessageAsync(
        string identityUserId,
        long conversationId,
        CreateChatMessageDto dto,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        await RequireMembershipAsync(caller.PersonId, conversationId, cancellationToken);
        var body = dto.Body?.Trim() ?? string.Empty;
        if (body.Length == 0) throw new ChatValidationException("Enter a message.");
        if (body.Length > 4000) throw new ChatValidationException("Messages cannot exceed 4,000 characters.");
        if (dto.ClientMessageId == Guid.Empty) throw new ChatValidationException("A client message ID is required.");

        await EnsureCanSendAsync(caller.TenantId, caller.PersonId, conversationId, cancellationToken);

        if (dto.ReplyToMessageId.HasValue)
        {
            var validReply = await db.ChatMessages.AsNoTracking()
                .AnyAsync(message =>
                    message.Id == dto.ReplyToMessageId.Value &&
                    message.ConversationId == conversationId, cancellationToken);
            if (!validReply) throw new ChatValidationException("The replied message is not in this conversation.");
        }

        var existing = await db.ChatMessages.AsNoTracking()
            .SingleOrDefaultAsync(message =>
                message.SenderPersonId == caller.PersonId &&
                message.ClientMessageId == dto.ClientMessageId, cancellationToken);
        if (existing != null)
            return (await MapMessagesAsync([existing], caller.PersonId, cancellationToken))[0];

        var message = new ChatMessage
        {
            TenantId = caller.TenantId,
            ConversationId = conversationId,
            SenderPersonId = caller.PersonId,
            ClientMessageId = dto.ClientMessageId,
            Body = body,
            ReplyToMessageId = dto.ReplyToMessageId,
        };
        db.ChatMessages.Add(message);
        await TrackMentionsAsync(conversationId, body, caller.PersonId, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.Entry(message).State = EntityState.Detached;
            var duplicate = await db.ChatMessages.AsNoTracking()
                .SingleOrDefaultAsync(item =>
                    item.SenderPersonId == caller.PersonId &&
                    item.ClientMessageId == dto.ClientMessageId, cancellationToken);
            if (duplicate == null) throw;
            return (await MapMessagesAsync([duplicate], caller.PersonId, cancellationToken))[0];
        }
        return (await MapMessagesAsync([message], caller.PersonId, cancellationToken))[0];
    }

    public async Task<ChatMessageDto> SendAttachmentAsync(
        string identityUserId,
        long conversationId,
        Guid clientMessageId,
        string? caption,
        string fileName,
        string contentType,
        byte[] content,
        long? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        await RequireMembershipAsync(caller.PersonId, conversationId, cancellationToken);
        await EnsureCanSendAsync(caller.TenantId, caller.PersonId, conversationId, cancellationToken);
        if (clientMessageId == Guid.Empty) throw new ChatValidationException("A client message ID is required.");
        if (content.Length == 0 || content.Length > 10 * 1024 * 1024)
            throw new ChatValidationException("Attachments must be between 1 byte and 10 MB.");
        if (!AllowedAttachmentTypes.Contains(contentType))
            throw new ChatValidationException("This attachment type is not allowed.");

        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
            throw new ChatValidationException("A valid attachment file name is required.");
        if (safeFileName.Length > 255) safeFileName = safeFileName[..255];
        var body = caption?.Trim() ?? string.Empty;
        if (body.Length > 4000) throw new ChatValidationException("The attachment caption is too long.");

        var existing = await db.ChatMessages.AsNoTracking()
            .SingleOrDefaultAsync(message =>
                message.SenderPersonId == caller.PersonId &&
                message.ClientMessageId == clientMessageId, cancellationToken);
        if (existing != null)
            return (await MapMessagesAsync([existing], caller.PersonId, cancellationToken))[0];

        var strategy = db.Database.CreateExecutionStrategy();
        ChatMessage? savedMessage = null;
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var message = new ChatMessage
            {
                TenantId = caller.TenantId,
                ConversationId = conversationId,
                SenderPersonId = caller.PersonId,
                ClientMessageId = clientMessageId,
                Body = body,
                ReplyToMessageId = replyToMessageId,
            };
            db.ChatMessages.Add(message);
            await TrackMentionsAsync(conversationId, body, caller.PersonId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            var uploadDir = Path.Combine(env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "chat-uploads");
            Directory.CreateDirectory(uploadDir);
            var extension = Path.GetExtension(safeFileName);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".dat";
            var relativePath = $"{caller.TenantId}/{conversationId}/{message.Id}{extension}";
            var fullPath = Path.Combine(uploadDir, caller.TenantId.ToString(), conversationId.ToString());
            Directory.CreateDirectory(fullPath);
            var finalFilePath = Path.Combine(fullPath, $"{message.Id}{extension}");
            await File.WriteAllBytesAsync(finalFilePath, content, cancellationToken);

            db.ChatAttachments.Add(new ChatAttachment
            {
                TenantId = caller.TenantId,
                MessageId = message.Id,
                FileName = safeFileName,
                ContentType = contentType,
                FileSize = content.LongLength,
                FilePath = relativePath,
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            savedMessage = message;
        });

        return (await MapMessagesAsync([savedMessage!], caller.PersonId, cancellationToken))[0];
    }

    public async Task<ChatMessageDto> EditMessageAsync(
        string identityUserId,
        long messageId,
        EditChatMessageDto dto,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        var message = await db.ChatMessages
            .Where(m => m.Id == messageId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ChatNotFoundException("The message could not be found.");

        if (message.SenderPersonId != caller.PersonId)
            throw new ChatForbiddenException("You can only edit your own messages.");
            
        if (message.DeletedOnUtc.HasValue)
            throw new ChatValidationException("Cannot edit a deleted message.");

        message.Body = dto.Body;
        message.EditedOnUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return (await MapMessagesAsync([message], caller.PersonId, cancellationToken)).Single();
    }

    public async Task<long> DeleteMessageAsync(
        string identityUserId,
        long messageId,
        bool forEveryone,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        var message = await db.ChatMessages
            .Where(m => m.Id == messageId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ChatNotFoundException("The message could not be found.");

        if (forEveryone)
        {
            if (message.SenderPersonId != caller.PersonId)
                throw new ChatForbiddenException("You can only delete your own messages for everyone.");
            
            message.DeletedOnUtc = DateTime.UtcNow;
        }
        else
        {
            var deletion = new ChatMessageDeletion
            {
                TenantId = caller.TenantId,
                MessageId = messageId,
                PersonId = caller.PersonId,
                DeletedOnUtc = DateTime.UtcNow
            };
            db.ChatMessageDeletions.Add(deletion);
        }

        await db.SaveChangesAsync(cancellationToken);
        return message.ConversationId;
    }

    public async Task<ChatMessageDto> ForwardMessageAsync(
        string identityUserId,
        long sourceMessageId,
        long targetConversationId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        var sourceMessage = await db.ChatMessages.AsNoTracking()
            .Where(m => m.Id == sourceMessageId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new ChatNotFoundException("The source message could not be found.");

        await RequireMembershipAsync(caller.PersonId, targetConversationId, cancellationToken);

        var attachments = await db.ChatAttachments.AsNoTracking()
            .Where(a => a.MessageId == sourceMessageId)
            .ToListAsync(cancellationToken);

        var forwardedMessage = new ChatMessage
        {
            TenantId = caller.TenantId,
            ConversationId = targetConversationId,
            SenderPersonId = caller.PersonId,
            ClientMessageId = Guid.NewGuid(),
            Body = sourceMessage.Body,
            CreatedOnUtc = DateTime.UtcNow
        };

        db.ChatMessages.Add(forwardedMessage);
        await db.SaveChangesAsync(cancellationToken);

        if (attachments.Count > 0)
        {
            foreach (var att in attachments)
            {
                db.ChatAttachments.Add(new ChatAttachment
                {
                    TenantId = caller.TenantId,
                    MessageId = forwardedMessage.Id,
                    FileName = att.FileName,
                    ContentType = att.ContentType,
                    FileSize = att.FileSize,
                    FilePath = att.FilePath,
                    CreatedOnUtc = DateTime.UtcNow
                });
            }
            await db.SaveChangesAsync(cancellationToken);
        }

        return (await MapMessagesAsync([forwardedMessage], caller.PersonId, cancellationToken)).Single();
    }

    public async Task<ChatAttachmentContentDto> GetAttachmentAsync(
        string identityUserId,
        long attachmentId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        var attachment = await db.ChatAttachments.AsNoTracking()
            .Where(item => item.Id == attachmentId)
            .Select(item => new
            {
                item.FileName,
                item.ContentType,
                item.FilePath,
                ConversationId = db.ChatMessages
                    .Where(message => message.Id == item.MessageId)
                    .Select(message => message.ConversationId)
                    .Single(),
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (attachment == null) throw new ChatNotFoundException("The attachment was not found.");
        await RequireMembershipAsync(caller.PersonId, attachment.ConversationId, cancellationToken);

        var uploadDir = Path.Combine(env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "chat-uploads");
        var fullPath = Path.Combine(uploadDir, attachment.FilePath.Replace("/", "\\"));
        if (!File.Exists(fullPath)) throw new ChatNotFoundException("The attachment file is missing from the server.");
        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);

        return new ChatAttachmentContentDto(attachment.FileName, attachment.ContentType, bytes);
    }

    public async Task<ChatMessageDto> SetReactionAsync(
        string identityUserId,
        long messageId,
        SetChatReactionDto dto,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        var emoji = dto.Emoji?.Trim() ?? string.Empty;
        if (emoji.Length == 0 || emoji.Length > 32)
            throw new ChatValidationException("Select a valid reaction.");

        var message = await db.ChatMessages
            .SingleOrDefaultAsync(item => item.Id == messageId, cancellationToken);
        if (message == null) throw new ChatNotFoundException("The message was not found.");
        await RequireMembershipAsync(caller.PersonId, message.ConversationId, cancellationToken);

        var existing = await db.ChatMessageReactions
            .SingleOrDefaultAsync(reaction =>
                reaction.MessageId == messageId &&
                reaction.PersonId == caller.PersonId &&
                reaction.Emoji == emoji, cancellationToken);
        if (existing == null)
            db.ChatMessageReactions.Add(new ChatMessageReaction
            {
                TenantId = caller.TenantId,
                MessageId = messageId,
                PersonId = caller.PersonId,
                Emoji = emoji,
            });
        else
            db.ChatMessageReactions.Remove(existing);

        await db.SaveChangesAsync(cancellationToken);
        return (await MapMessagesAsync([message], caller.PersonId, cancellationToken))[0];
    }

    public async Task MarkReadAsync(
        string identityUserId,
        long conversationId,
        MarkChatReadDto dto,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        var membership = await RequireMembershipAsync(caller.PersonId, conversationId, cancellationToken);
        var messageExists = await db.ChatMessages.AsNoTracking()
            .AnyAsync(message => message.Id == dto.MessageId && message.ConversationId == conversationId, cancellationToken);
        if (!messageExists) throw new ChatNotFoundException("The message was not found.");
        if (!membership.LastReadMessageId.HasValue || dto.MessageId > membership.LastReadMessageId.Value)
        {
            membership.LastReadMessageId = dto.MessageId;
            membership.HasUnreadMention = false;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdatePreferenceAsync(
        string identityUserId,
        long conversationId,
        UpdateChatPreferenceDto dto,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        var membership = await RequireMembershipAsync(caller.PersonId, conversationId, cancellationToken);
        membership.IsMuted = dto.IsMuted;
        membership.IsPinned = dto.IsPinned;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task TrackMentionsAsync(long conversationId, string body, Guid senderPersonId, CancellationToken cancellationToken)
    {
        if (!body.Contains("@")) return;
        var members = await db.ChatConversationMembers
            .Where(m => m.ConversationId == conversationId && m.PersonId != senderPersonId && m.LeftOnUtc == null)
            .ToListAsync(cancellationToken);
            
        var memberPersonIds = members.Select(m => m.PersonId).ToArray();
        if (memberPersonIds.Length == 0) return;
        
        var people = await db.Persons.AsNoTracking()
            .Where(p => memberPersonIds.Contains(p.PersonId))
            .Select(p => new { p.PersonId, p.FullName })
            .ToDictionaryAsync(p => p.PersonId, p => p.FullName, cancellationToken);
            
        foreach (var member in members)
        {
            if (people.TryGetValue(member.PersonId, out var fullName) && body.Contains("@" + fullName, StringComparison.OrdinalIgnoreCase))
            {
                member.HasUnreadMention = true;
            }
        }
    }

    public async Task ClearConversationAsync(
        string identityUserId,
        long conversationId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        var membership = await RequireMembershipAsync(caller.PersonId, conversationId, cancellationToken);
        membership.ClearedBeforeUtc = DateTime.UtcNow;
        membership.LastReadMessageId = await db.ChatMessages.AsNoTracking()
            .Where(message => message.ConversationId == conversationId)
            .MaxAsync(message => (long?)message.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> UpdateGroupPhotoAsync(
        string identityUserId,
        long conversationId,
        IFormFile photo,
        IWebHostEnvironment env,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        var membership = await RequireMembershipAsync(caller.PersonId, conversationId, cancellationToken);
        if (membership.MemberRole != "Admin")
            throw new ChatForbiddenException("Only admins can change the group photo.");

        var conversation = await db.ChatConversations
            .Where(c => c.TenantId == caller.TenantId && c.Id == conversationId && c.ConversationType == "Group")
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ChatNotFoundException("Group not found.");

        // Delete old photo if present
        if (!string.IsNullOrWhiteSpace(conversation.PhotoUrl))
        {
            var oldPath = Path.Combine(env.WebRootPath, conversation.PhotoUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(oldPath)) File.Delete(oldPath);
        }

        // Save new photo
        var uploadDir = Path.Combine(env.WebRootPath, "chat-group-photos");
        Directory.CreateDirectory(uploadDir);
        var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
        var allowedExts = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        if (!allowedExts.Contains(ext)) ext = ".jpg";
        var fileName = $"group_{conversationId}_{Guid.NewGuid():N}{ext}";
        var filePath = Path.Combine(uploadDir, fileName);
        using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            await photo.CopyToAsync(stream, cancellationToken);

        conversation.PhotoUrl = $"/chat-group-photos/{fileName}";
        await db.SaveChangesAsync(cancellationToken);
        return conversation.PhotoUrl;
    }

    public async Task UpdateGroupNameAsync(
        string identityUserId,
        long conversationId,
        string title,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ChatConflictException("Group name cannot be empty.");
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        var membership = await RequireMembershipAsync(caller.PersonId, conversationId, cancellationToken);
        if (membership.MemberRole != "Admin")
            throw new ChatForbiddenException("Only admins can rename the group.");
        var conversation = await db.ChatConversations
            .Where(c => c.TenantId == caller.TenantId && c.Id == conversationId && c.ConversationType == "Group")
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ChatNotFoundException("Group not found.");
        conversation.Title = title.Trim()[..Math.Min(title.Trim().Length, 200)];
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task BlockAsync(
        string identityUserId,
        Guid blockedPersonId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        if (blockedPersonId == caller.PersonId)
            throw new ChatValidationException("You cannot block yourself.");

        var personExists = await db.Persons.AsNoTracking()
            .AnyAsync(person =>
                person.PersonId == blockedPersonId &&
                person.TenantId == caller.TenantId, cancellationToken);
        if (!personExists) throw new ChatNotFoundException("The staff member was not found.");

        var exists = await db.ChatBlocks
            .AnyAsync(blocked =>
                blocked.BlockerPersonId == caller.PersonId &&
                blocked.BlockedPersonId == blockedPersonId, cancellationToken);
        if (!exists)
            db.ChatBlocks.Add(new ChatBlock
            {
                TenantId = caller.TenantId,
                BlockerPersonId = caller.PersonId,
                BlockedPersonId = blockedPersonId,
            });

        var pairKey = PairKey(caller.PersonId, blockedPersonId);
        var pending = await db.ChatContactRequests
            .Where(request => request.PairKey == pairKey && request.Status == "Pending")
            .ToListAsync(cancellationToken);
        foreach (var request in pending)
        {
            request.Status = "Cancelled";
            request.CancelledOnUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UnblockAsync(
        string identityUserId,
        Guid blockedPersonId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        var block = await db.ChatBlocks
            .FirstOrDefaultAsync(blocked =>
                blocked.BlockerPersonId == caller.PersonId &&
                blocked.BlockedPersonId == blockedPersonId, cancellationToken);
                
        if (block != null)
        {
            db.ChatBlocks.Remove(block);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<List<ChatPersonDto>> GetBlockedPersonsAsync(
        string identityUserId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        
        var query = from block in db.ChatBlocks
                    join staff in db.StaffDirectoryRows on block.BlockedPersonId equals staff.PersonId
                    join person in db.Persons on block.BlockedPersonId equals person.PersonId into personGroup
                    from person in personGroup.DefaultIfEmpty()
                    where block.TenantId == caller.TenantId &&
                          block.BlockerPersonId == caller.PersonId &&
                          staff.TenantId == caller.TenantId
                    select new { Staff = staff, Person = person };
                    
        var results = await query.ToListAsync(cancellationToken);
        
        return results.Select(r => new ChatPersonDto(
            r.Staff.PersonId,
            r.Staff.FullName,
            r.Staff.PhotoUrl,
            r.Staff.Department,
            r.Staff.Designation,
            presence.IsOnline(r.Staff.PersonId),
            r.Person != null ? r.Person.ShowLastSeen : true,
            r.Person != null ? r.Person.LastSeenUtc : null
        )).ToList();
    }

    public Task<bool> IsConversationMemberAsync(
        Guid personId,
        long conversationId,
        CancellationToken cancellationToken = default) =>
        db.ChatConversationMembers.AsNoTracking().AnyAsync(member =>
            member.ConversationId == conversationId &&
            member.PersonId == personId &&
            member.LeftOnUtc == null, cancellationToken);

    private async Task<ChatConversationMember> RequireMembershipAsync(
        Guid personId,
        long conversationId,
        CancellationToken cancellationToken)
    {
        var membership = await db.ChatConversationMembers
            .SingleOrDefaultAsync(member =>
                member.ConversationId == conversationId &&
                member.PersonId == personId &&
                member.LeftOnUtc == null, cancellationToken);
        return membership ?? throw new ChatForbiddenException("You are not a member of this conversation.");
    }

    private async Task<HashSet<Guid>> GetBlockedPersonIdsAsync(int tenantId, Guid personId, CancellationToken cancellationToken)
    {
        var rows = await db.ChatBlocks.AsNoTracking()
            .Where(block => block.TenantId == tenantId && 
                            (block.BlockerPersonId == personId || block.BlockedPersonId == personId))
            .Select(block => new { block.BlockerPersonId, block.BlockedPersonId })
            .ToListAsync(cancellationToken);
        return rows.Select(block =>
                block.BlockerPersonId == personId ? block.BlockedPersonId : block.BlockerPersonId)
            .ToHashSet();
    }

    private Task<bool> IsBlockedAsync(int tenantId, Guid firstPersonId, Guid secondPersonId, CancellationToken cancellationToken) =>
        db.ChatBlocks.AsNoTracking().AnyAsync(block =>
            block.TenantId == tenantId &&
            ((block.BlockerPersonId == firstPersonId && block.BlockedPersonId == secondPersonId) ||
            (block.BlockerPersonId == secondPersonId && block.BlockedPersonId == firstPersonId)),
            cancellationToken);

    private async Task EnsureCanSendAsync(
        int tenantId,
        Guid personId,
        long conversationId,
        CancellationToken cancellationToken)
    {
        var otherPersonIds = await db.ChatConversationMembers.AsNoTracking()
            .Where(member =>
                member.TenantId == tenantId &&
                member.ConversationId == conversationId &&
                member.PersonId != personId &&
                member.LeftOnUtc == null)
            .Select(member => member.PersonId)
            .ToListAsync(cancellationToken);

        var blockedPersonIds = await GetBlockedPersonIdsAsync(tenantId, personId, cancellationToken);
        if (otherPersonIds.Intersect(blockedPersonIds).Any())
            throw new ChatForbiddenException("Messages cannot be sent in this conversation.");
    }

    private async Task<Dictionary<Guid, ChatPersonDto>> LoadPeopleAsync(
        IReadOnlyCollection<Guid> personIds,
        CancellationToken cancellationToken)
    {
        if (personIds.Count == 0) return [];
        var rows = await (
            from row in db.StaffDirectoryRows.AsNoTracking()
            join person in db.Persons.AsNoTracking() on row.PersonId equals person.PersonId
            where personIds.Contains(row.PersonId)
            select new
            {
                row.PersonId,
                row.FullName,
                row.PhotoUrl,
                row.Department,
                row.Designation,
                person.ShowLastSeen,
                person.LastSeenUtc
            })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(
            row => row.PersonId,
            row => new ChatPersonDto(
                row.PersonId,
                row.FullName,
                row.PhotoUrl,
                row.Department,
                row.Designation,
                presence.IsOnline(row.PersonId),
                row.ShowLastSeen,
                row.LastSeenUtc));
    }

    private async Task<IReadOnlyList<ChatMessageDto>> MapMessagesAsync(
        IReadOnlyList<ChatMessage> messages,
        Guid callerPersonId,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0) return [];
        var messageIds = messages.Select(message => message.Id).ToArray();
        var senderIds = messages.Select(message => message.SenderPersonId).Distinct().ToArray();
        var people = await LoadPeopleAsync(senderIds, cancellationToken);
        var reactions = await db.ChatMessageReactions.AsNoTracking()
            .Where(reaction => messageIds.Contains(reaction.MessageId))
            .ToListAsync(cancellationToken);
        var attachments = await db.ChatAttachments.AsNoTracking()
            .Where(attachment => messageIds.Contains(attachment.MessageId))
            .Select(attachment => new
            {
                attachment.Id,
                attachment.MessageId,
                attachment.FileName,
                attachment.ContentType,
                attachment.FileSize,
            })
            .ToListAsync(cancellationToken);

        return messages.Select(message =>
        {
            people.TryGetValue(message.SenderPersonId, out var sender);
            var messageReactions = reactions
                .Where(reaction => reaction.MessageId == message.Id)
                .GroupBy(reaction => reaction.Emoji)
                .Select(group => new ChatReactionDto(
                    group.Key,
                    group.Count(),
                    group.Any(reaction => reaction.PersonId == callerPersonId)))
                .ToList();
            var messageAttachments = attachments
                .Where(attachment => attachment.MessageId == message.Id)
                .Select(attachment => new ChatAttachmentDto(
                    attachment.Id,
                    attachment.FileName,
                    attachment.ContentType,
                    attachment.FileSize,
                    $"/api/chat/attachments/{attachment.Id}"))
                .ToList();
            return new ChatMessageDto(
                message.Id,
                message.ConversationId,
                message.SenderPersonId,
                sender?.FullName ?? "Former staff",
                sender?.PhotoUrl,
                message.DeletedOnUtc.HasValue ? string.Empty : message.Body,
                message.ReplyToMessageId,
                message.CreatedOnUtc,
                message.EditedOnUtc,
                message.DeletedOnUtc.HasValue,
                messageReactions,
                messageAttachments);
        }).ToList();
    }

    private static ChatContactRequestDto ToRequestDto(
        ChatContactRequest request,
        Guid callerPersonId,
        ChatPersonDto otherPerson) =>
        new(
            request.Id,
            request.SenderPersonId,
            request.ReceiverPersonId,
            otherPerson,
            request.SenderPersonId == callerPersonId ? "Outgoing" : "Incoming",
            request.Status,
            request.Message,
            request.CreatedOnUtc);



    private static string PairKey(Guid first, Guid second)
    {
        var firstValue = first.ToString("N");
        var secondValue = second.ToString("N");
        return string.CompareOrdinal(firstValue, secondValue) < 0
            ? $"{firstValue}:{secondValue}"
            : $"{secondValue}:{firstValue}";
    }

    public async Task<IReadOnlyList<ChatGroupMemberDto>> GetGroupMembersAsync(string identityUserId, long conversationId, CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        if (!await IsConversationMemberAsync(caller.PersonId, conversationId, cancellationToken)) throw new ChatForbiddenException("Not a member.");

        var members = await (from m in db.ChatConversationMembers
                             join p in db.Persons on m.PersonId equals p.PersonId
                             where m.TenantId == caller.TenantId && m.ConversationId == conversationId && m.LeftOnUtc == null
                             select new ChatGroupMemberDto(p.PersonId, p.FullName, p.ProfilePhotoUrl, m.MemberRole, m.JoinedOnUtc))
                            .ToListAsync(cancellationToken);
        return members;
    }

    public async Task AddGroupMembersAsync(string identityUserId, long conversationId, AddChatGroupMembersDto dto, CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        var adminCheck = await db.ChatConversationMembers.FirstOrDefaultAsync(m => m.TenantId == caller.TenantId && m.ConversationId == conversationId && m.PersonId == caller.PersonId && m.LeftOnUtc == null, cancellationToken);
        if (adminCheck == null || adminCheck.MemberRole != "Admin") throw new ChatForbiddenException("Only admins can add members.");
        
        var personIds = dto.MemberPersonIds.Distinct().ToArray();
        var eligibleCount = await db.StaffDirectoryRows.AsNoTracking().CountAsync(r => r.TenantId == caller.TenantId && personIds.Contains(r.PersonId) && r.IsPersonActive, cancellationToken);
        if (eligibleCount != personIds.Length) throw new ChatValidationException("One or more selected staff members are not eligible.");
        
        var existingMembers = await db.ChatConversationMembers.AsNoTracking().Where(m => m.TenantId == caller.TenantId && m.ConversationId == conversationId && personIds.Contains(m.PersonId) && m.LeftOnUtc == null).Select(m => m.PersonId).ToListAsync(cancellationToken);
        
        foreach (var personId in personIds.Except(existingMembers))
        {
            db.ChatConversationMembers.Add(new Accounts.Models.ChatConversationMember
            {
                TenantId = caller.TenantId,
                ConversationId = conversationId,
                PersonId = personId,
                MemberRole = "Member",
                JoinedOnUtc = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveGroupMemberAsync(string identityUserId, long conversationId, Guid memberPersonId, CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        var membership = await db.ChatConversationMembers.FirstOrDefaultAsync(m => m.TenantId == caller.TenantId && m.ConversationId == conversationId && m.PersonId == memberPersonId && m.LeftOnUtc == null, cancellationToken);
        if (membership == null) return;

        if (caller.PersonId != memberPersonId)
        {
            var adminCheck = await db.ChatConversationMembers.FirstOrDefaultAsync(m => m.TenantId == caller.TenantId && m.ConversationId == conversationId && m.PersonId == caller.PersonId && m.LeftOnUtc == null, cancellationToken);
            if (adminCheck == null || adminCheck.MemberRole != "Admin") throw new ChatForbiddenException("Only admins can remove members.");
        }

        membership.LeftOnUtc = DateTime.UtcNow;
        
        // Admin transfer logic if the only admin leaves
        if (membership.MemberRole == "Admin")
        {
            var otherAdmins = await db.ChatConversationMembers.AnyAsync(m => m.TenantId == caller.TenantId && m.ConversationId == conversationId && m.LeftOnUtc == null && m.MemberRole == "Admin" && m.PersonId != memberPersonId, cancellationToken);
            if (!otherAdmins)
            {
                var oldestMember = await db.ChatConversationMembers.Where(m => m.TenantId == caller.TenantId && m.ConversationId == conversationId && m.LeftOnUtc == null && m.PersonId != memberPersonId).OrderBy(m => m.JoinedOnUtc).FirstOrDefaultAsync(cancellationToken);
                if (oldestMember != null) oldestMember.MemberRole = "Admin";
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateMemberRoleAsync(string identityUserId, long conversationId, Guid memberPersonId, UpdateChatMemberRoleDto dto, CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        var adminCheck = await db.ChatConversationMembers.FirstOrDefaultAsync(m => m.TenantId == caller.TenantId && m.ConversationId == conversationId && m.PersonId == caller.PersonId && m.LeftOnUtc == null, cancellationToken);
        if (adminCheck == null || adminCheck.MemberRole != "Admin") throw new ChatForbiddenException("Only admins can change roles.");

        var membership = await db.ChatConversationMembers.FirstOrDefaultAsync(m => m.TenantId == caller.TenantId && m.ConversationId == conversationId && m.PersonId == memberPersonId && m.LeftOnUtc == null, cancellationToken);
        if (membership != null)
        {
            membership.MemberRole = dto.MemberRole;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdatePrivacySettingsAsync(string identityUserId, UpdatePrivacySettingsDto dto, CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(identityUserId, cancellationToken);
        var person = await db.Persons.FindAsync(new object[] { caller.PersonId }, cancellationToken);
        if (person != null)
        {
            person.ShowLastSeen = dto.ShowLastSeen;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static string? TrimOrNull(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private sealed record OrganizationNodeInfo(int Id, int? ParentId, string Label, bool IsActive);
}
