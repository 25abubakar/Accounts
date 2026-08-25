using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace Accounts.Hubs;

public sealed class NameIdentifierUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? connection.User.FindFirstValue("sub");
}
