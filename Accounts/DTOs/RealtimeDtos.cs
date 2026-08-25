namespace Accounts.DTOs;

public static class RealtimeEventTypes
{
    public const string AttendanceChanged = "attendance.changed";
    public const string DeductionChanged = "deduction.changed";
    public const string ProcessChanged = "process.changed";
    public const string StaffChanged = "staff.changed";
    public const string InstructionChanged = "instruction.changed";
    public const string DashboardChanged = "dashboard.changed";
}

public sealed record RealtimeEventDto(
    Guid EventId,
    int SchemaVersion,
    string Type,
    string Module,
    string Action,
    int? TenantId,
    string? EntityId,
    DateTime OccurredOnUtc,
    IReadOnlyDictionary<string, string>? Data = null)
{
    public static RealtimeEventDto Create(
        string type,
        string module,
        string action,
        int? tenantId,
        string? entityId = null,
        IReadOnlyDictionary<string, string>? data = null) =>
        new(Guid.NewGuid(), 1, type, module, action, tenantId, entityId, DateTime.UtcNow, data);
}

public sealed record RealtimeNotificationDto(
    Guid NotificationId,
    int SchemaVersion,
    string Category,
    string Severity,
    string Title,
    string Message,
    string? Route,
    DateTime OccurredOnUtc,
    bool AutoDismiss = true)
{
    public static RealtimeNotificationDto Create(
        string category,
        string severity,
        string title,
        string message,
        string? route = null,
        bool autoDismiss = true) =>
        new(Guid.NewGuid(), 1, category, severity, title, message, route, DateTime.UtcNow, autoDismiss);
}

public sealed record RealtimeConnectionInfoDto(
    string ConnectionId,
    string IdentityUserId,
    int? TenantId,
    Guid? PersonId,
    Guid? StaffId);
