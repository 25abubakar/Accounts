using Accounts.Models;

namespace Accounts.Services.Interfaces
{
    public interface IAuthService
    {
        Task<(bool Success, string Message, AuthResponseDto Response)> RegisterAsync(RegisterDto dto);
        Task<(bool Success, int StatusCode, AuthResponseDto Response)> LoginAsync(LoginDto dto);
        Task LogoutAsync();
        Task<(bool Success, string Message, AuthResponseDto Response)> AssignRoleAsync(AssignRoleDto dto);
        Task<IEnumerable<object>> GetUsersAsync();
    }
}
