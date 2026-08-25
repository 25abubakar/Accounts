namespace Accounts.Services.Services;

public sealed partial class SignalRRealtimePublisher
{
    private async Task SafeSendAsync(Func<Task> send, string eventType, string audience)
    {
        try { await send(); }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Realtime delivery failed for {EventType} to {Audience}",
                eventType,
                audience);
        }
    }
}
