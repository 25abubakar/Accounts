using System.Security.Claims;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Hubs;
using Accounts.Idempotency;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Controllers;

[ApiController]
[Route("api/chat/rules")]
[Authorize]
[AutoValidateAntiforgeryToken]
[Produces("application/json")]
public sealed class ChatRulesController(
    ApplicationDbContext db,
    ITenantService tenant,
    IChatRuleService rules,
    RbacService rbac,
    TenantPermissionService tenantPermissions,
    IHubContext<ChatHub> hub) : ControllerBase
{
    [HttpGet("effective")]
    public async Task<IActionResult> Effective(CancellationToken cancellationToken)
    {
        if (!tenant.TenantId.HasValue || tenant.IsSuperAdmin) return Forbid();
        return Ok(await rules.GetEffectiveAsync(tenant.TenantId.Value, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!tenant.TenantId.HasValue ||
            !await HasMenuActionAsync("VIEW", cancellationToken))
            return Forbid();
        return Ok(await rules.GetEffectiveAsync(tenant.TenantId.Value, cancellationToken));
    }

    [HttpPut]
    [Idempotent]
    public async Task<IActionResult> Save(
        [FromBody] SaveChatRuleSettingDto dto,
        CancellationToken cancellationToken)
    {
        if (!tenant.TenantId.HasValue ||
            !await HasMenuActionAsync("EDIT", cancellationToken))
            return Forbid();

        try
        {
            var saved = await rules.SaveAsync(
                tenant.TenantId.Value,
                UserId(),
                dto,
                cancellationToken);
            await hub.Clients.Group(ChatHub.TenantGroup(tenant.TenantId.Value))
                .SendAsync("chatRulesUpdated", saved, cancellationToken);
            return Ok(saved);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private async Task<bool> HasMenuActionAsync(string action, CancellationToken cancellationToken)
    {
        if (TenantPermissionService.IsSuperAdmin(User)) return true;
        var routes = new[] { "/chat/rules" };
        if (TenantPermissionService.IsTenantAdmin(User))
            return await tenantPermissions.HasMenuRouteAsync(User, routes, action, cancellationToken);

        var identityUserId = UserId();
        var staffId = await db.Persons.AsNoTracking()
            .Where(person => person.IdentityUserId == identityUserId && person.Staff != null)
            .Select(person => (Guid?)person.Staff!.StaffId)
            .FirstOrDefaultAsync(cancellationToken);
        if (!staffId.HasValue) return false;

        var menuId = await db.Menus.AsNoTracking()
            .Where(menu => menu.IsActive && menu.Route == "/chat/rules")
            .Select(menu => (int?)menu.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (!menuId.HasValue) return false;
        var normalizedAction = action.Trim().ToUpperInvariant();
        if (normalizedAction == "VIEW" &&
            await rbac.HasAccessAsync(staffId.Value, $"MENU_{menuId.Value}"))
            return true;
        return await rbac.HasAccessAsync(
            staffId.Value,
            $"MENU_{menuId.Value}_{normalizedAction}");
    }

    private string UserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("Authenticated user ID is missing.");
}
