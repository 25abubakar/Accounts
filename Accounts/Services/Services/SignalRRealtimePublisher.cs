using Accounts.DTOs;
using Accounts.Hubs;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace Accounts.Services.Services;

// Realtime failures are logged and do not roll back committed database work.
public sealed partial class SignalRRealtimePublisher(
    IHubContext<ApplicationRealtimeHub, IApplicationRealtimeClient> hub,
    ILogger<SignalRRealtimePublisher> logger) : IRealtimePublisher
{
    public Task PublishEventToTenantAsync(int tenantId, RealtimeEventDto message) =>
        SafeSendAsync(
            () => hub.Clients.Group(RealtimeGroups.Tenant(tenantId)).ReceiveEvent(message),
            message.Type,
            $"tenant:{tenantId}");

    public Task PublishEventToIdentityUserAsync(string identityUserId, RealtimeEventDto message) =>
        SafeSendAsync(
            () => hub.Clients.User(identityUserId).ReceiveEvent(message),
            message.Type,
            "identity-user");

    public Task PublishEventToPersonAsync(Guid personId, RealtimeEventDto message) =>
        SafeSendAsync(
            () => hub.Clients.Group(RealtimeGroups.Person(personId)).ReceiveEvent(message),
            message.Type,
            $"person:{personId:N}");

    public Task PublishEventToStaffAsync(Guid staffId, RealtimeEventDto message) =>
        SafeSendAsync(
            () => hub.Clients.Group(RealtimeGroups.Staff(staffId)).ReceiveEvent(message),
            message.Type,
            $"staff:{staffId:N}");

    public Task PublishNotificationToIdentityUserAsync(
        string identityUserId,
        RealtimeNotificationDto notification) =>
        SafeSendAsync(
            () => hub.Clients.User(identityUserId).ReceiveNotification(notification),
            notification.Category,
            "identity-user");

    public Task PublishNotificationToPersonAsync(
        Guid personId,
        RealtimeNotificationDto notification) =>
        SafeSendAsync(
            () => hub.Clients.Group(RealtimeGroups.Person(personId)).ReceiveNotification(notification),
            notification.Category,
            $"person:{personId:N}");
}
