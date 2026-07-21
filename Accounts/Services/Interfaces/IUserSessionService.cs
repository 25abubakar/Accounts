using Accounts.Models;

namespace Accounts.Services.Interfaces
{
    public interface IUserSessionService
    {
        /// <summary>
        /// Builds post-login payload: filtered sidebar, permissions, and admin instructions.
        /// </summary>
        Task<UserSessionDto> GetSessionAsync(
            string identityUserId,
            bool isFullAccess,
            bool isOrganizationCeo,
            bool includeNavigation,
            CancellationToken cancellationToken = default);
    }
}
