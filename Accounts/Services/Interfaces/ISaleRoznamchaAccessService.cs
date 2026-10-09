using System.Security.Claims;

namespace Accounts.Services.Interfaces;

public interface ISaleRoznamchaAccessService
{
    Task<bool> CanAsync(
        ClaimsPrincipal user,
        string route,
        string action,
        CancellationToken ct = default);
}
