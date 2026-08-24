using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

namespace Accounts.Middleware
{
    public sealed class AccountScopeAccessMiddleware
    {
        private readonly RequestDelegate _next;

        public AccountScopeAccessMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context, IAccountScopeAccessService accessService)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    AccountScopeAccessResult decision;
                    try
                    {
                        decision = await accessService.ValidateAsync(userId, context.RequestAborted);
                    }
                    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                    {
                        // Browsers routinely cancel in-flight API calls during navigation,
                        // refresh, and component cleanup. The response is already abandoned,
                        // so SQL cancellation must not be treated as an application failure.
                        return;
                    }
                    if (!decision.IsAllowed)
                    {
                        await context.SignOutAsync();
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            code = decision.Code,
                            message = decision.Message
                        }, context.RequestAborted);
                        return;
                    }
                }
            }

            await _next(context);
        }
    }
}
