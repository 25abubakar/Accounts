using Accounts.DTOs;
using Accounts.Hubs;

namespace Accounts.Tests;

public sealed class RealtimeContractTests
{
    [Fact]
    public void GroupNames_AreStableAndAudienceScoped()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        Assert.Equal("app:tenant:27", RealtimeGroups.Tenant(27));
        Assert.Equal("app:person:11111111222233334444555555555555", RealtimeGroups.Person(id));
        Assert.Equal("app:staff:11111111222233334444555555555555", RealtimeGroups.Staff(id));
    }

    [Fact]
    public void EventFactory_CreatesVersionedUtcEnvelope()
    {
        var message = RealtimeEventDto.Create(
            RealtimeEventTypes.DeductionChanged,
            "deduction",
            "adjustment-approved",
            27,
            "42");

        Assert.NotEqual(Guid.Empty, message.EventId);
        Assert.Equal(1, message.SchemaVersion);
        Assert.Equal(27, message.TenantId);
        Assert.Equal(DateTimeKind.Utc, message.OccurredOnUtc.Kind);
    }

    [Fact]
    public void NotificationFactory_DoesNotRequireProtectedBusinessData()
    {
        var notification = RealtimeNotificationDto.Create(
            "deduction",
            "success",
            "Deduction approved",
            "Your monthly deduction adjustment was approved.",
            "/attendance/deduction");

        Assert.NotEqual(Guid.Empty, notification.NotificationId);
        Assert.True(notification.AutoDismiss);
        Assert.Equal(1, notification.SchemaVersion);
    }
}
