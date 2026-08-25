namespace Accounts.Hubs;

public static class RealtimeGroups
{
    public static string Tenant(int tenantId) => $"app:tenant:{tenantId}";
    public static string Person(Guid personId) => $"app:person:{personId:N}";
    public static string Staff(Guid staffId) => $"app:staff:{staffId:N}";
}
