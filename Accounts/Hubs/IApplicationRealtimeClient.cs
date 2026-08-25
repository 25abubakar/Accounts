using Accounts.DTOs;

namespace Accounts.Hubs;

public interface IApplicationRealtimeClient
{
    Task ReceiveEvent(RealtimeEventDto message);
    Task ReceiveNotification(RealtimeNotificationDto notification);
}
