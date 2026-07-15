namespace Accounts.Services.Interfaces
{
    public sealed record AccountScopeAccessResult(bool IsAllowed, string Code, string Message)
    {
        public static AccountScopeAccessResult Allowed() => new(true, "OK", string.Empty);
        public static AccountScopeAccessResult Denied(string message) =>
            new(false, "ACCOUNT_SCOPE_DISABLED", message);
    }

    public interface IAccountScopeAccessService
    {
        Task<AccountScopeAccessResult> ValidateAsync(string userId, CancellationToken cancellationToken = default);
    }
}
