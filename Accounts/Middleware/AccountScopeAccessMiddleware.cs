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
                    var decision = await accessService.ValidateAsync(userId, context.RequestAborted);
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
