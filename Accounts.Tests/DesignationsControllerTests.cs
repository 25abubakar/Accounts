using System.Security.Claims;
using Accounts.Controllers;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Accounts.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Tests;

public sealed class DesignationsControllerTests
{
    [Fact]
    public async Task Upsert_AllowsTenantAdminWithLegacyStatusesRouteAddPermission()
    {
        const int tenantId = 41;
        var tenant = new TestTenantService(tenantId);
        await using var db = TestDbFactory.Create(tenant);
        var menu = new Menu
        {
            Title = "Types",
            Route = "/settings/statuses",
            IsActive = true
        };
        db.Menus.Add(menu);
        await db.SaveChangesAsync();
        db.TenantMenuPermissions.Add(new TenantMenuPermission
        {
            TenantId = tenantId,
            MenuId = menu.Id,
            IsAllow = true,
            CanView = true,
            CanAdd = true
        });
        await db.SaveChangesAsync();

        var rbac = new RbacService(db);
        var controller = new DesignationsController(
            new DesignationService(db, tenant),
            new PlatformSettingsProvisioningService(db),
            tenant,
            db,
            rbac,
            new TenantPermissionService(db, rbac))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "tenant-admin"),
                        new Claim(ClaimTypes.Role, "TenantAdmin"),
                        new Claim(ITenantService.ClaimTenantId, tenantId.ToString()),
                        new Claim(ITenantService.ClaimIsTenantAdmin, "true")
                    ], "test"))
                }
            }
        };

        var result = await controller.Upsert(new UpsertDesignationDto { Name = "CEO" });

        Assert.IsType<OkObjectResult>(result);
        var designation = await db.Designations.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(tenantId, designation.TenantId);
        Assert.Equal("CEO", designation.Name);
    }

    private sealed class TestTenantService(int tenantId) : ITenantService
    {
        public int? TenantId { get; } = tenantId;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public int RequiredTenantId => tenantId;
    }
}
