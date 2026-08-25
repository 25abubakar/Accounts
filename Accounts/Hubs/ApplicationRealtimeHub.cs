using Accounts.Data;
using Accounts.DTOs;
using Accounts.Authorization;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounts.Hubs;

[Authorize]
public sealed class ApplicationRealtimeHub(ApplicationDbContext db)
    : Hub<IApplicationRealtimeClient>
{
    // Server-assigned realtime groups prevent clients from changing audience scope.
    private const string TenantItem = "RealtimeTenantId";
    private const string PersonItem = "RealtimePersonId";
    private const string StaffItem = "RealtimeStaffId";

    public override async Task OnConnectedAsync()
    {
        var identityUserId = Context.UserIdentifier;
        if (string.IsNullOrWhiteSpace(identityUserId))
        {
            Context.Abort();
            return;
        }

        try
        {
            if (int.TryParse(Context.User?.FindFirstValue(ITenantService.ClaimTenantId), out var tenantId)
                && tenantId > 0)
            {
                Context.Items[TenantItem] = tenantId;
                await Groups.AddToGroupAsync(
                    Context.ConnectionId,
                    RealtimeGroups.Tenant(tenantId),
                    Context.ConnectionAborted);

                var personId = await db.Persons.AsNoTracking()
                    .Where(person =>
                        person.TenantId == tenantId &&
                        person.IdentityUserId == identityUserId &&
                        person.IsActive)
                    .Select(person => (Guid?)person.PersonId)
                    .FirstOrDefaultAsync(Context.ConnectionAborted);

                if (personId.HasValue)
                {
                    Context.Items[PersonItem] = personId.Value;
                    await Groups.AddToGroupAsync(
                        Context.ConnectionId,
                        RealtimeGroups.Person(personId.Value),
                        Context.ConnectionAborted);
                }

                if (Guid.TryParse(Context.User?.FindFirstValue(AccountClaimTypes.StaffId), out var staffId))
                {
                    Context.Items[StaffItem] = staffId;
                    await Groups.AddToGroupAsync(
                        Context.ConnectionId,
                        RealtimeGroups.Staff(staffId),
                        Context.ConnectionAborted);
                }
            }

            await base.OnConnectedAsync();
        }
        catch (OperationCanceledException)
        {
            Context.Abort();
        }
    }

    public Task<RealtimeConnectionInfoDto> GetConnectionInfo()
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrWhiteSpace(userId))
            throw new HubException("The authenticated user could not be resolved.");

        var tenantId = Context.Items.TryGetValue(TenantItem, out var tenant) ? tenant as int? : null;
        var personId = Context.Items.TryGetValue(PersonItem, out var person) ? person as Guid? : null;
        var staffId = Context.Items.TryGetValue(StaffItem, out var staff) ? staff as Guid? : null;
        return Task.FromResult(new RealtimeConnectionInfoDto(
            Context.ConnectionId, userId, tenantId, personId, staffId));
    }
}
