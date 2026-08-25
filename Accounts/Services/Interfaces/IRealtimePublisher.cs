using Accounts.DTOs;

namespace Accounts.Services.Interfaces;

public interface IRealtimePublisher
{
    Task PublishEventToTenantAsync(int tenantId, RealtimeEventDto message);
    Task PublishEventToIdentityUserAsync(string identityUserId, RealtimeEventDto message);
    Task PublishEventToPersonAsync(Guid personId, RealtimeEventDto message);
    Task PublishEventToStaffAsync(Guid staffId, RealtimeEventDto message);
    Task PublishNotificationToIdentityUserAsync(
        string identityUserId,
        RealtimeNotificationDto notification);
    Task PublishNotificationToPersonAsync(
        Guid personId,
        RealtimeNotificationDto notification);
}
