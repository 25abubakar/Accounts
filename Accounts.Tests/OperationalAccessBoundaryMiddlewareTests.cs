using Accounts.Middleware;
using Accounts.Models;
using Accounts.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace Accounts.Tests;

public class OperationalAccessBoundaryMiddlewareTests
{
    [Fact]
    public async Task SuperAdmin_IsDeniedTenantOperationalApi()
    {
        var nextCalled = false;
        var middleware = new OperationalAccessBoundaryMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateAuthenticatedContext("/api/persons", HttpMethods.Get);
        await using var db = TestDbFactory.Create(new TestTenantService(null, isSuperAdmin: true));

        await middleware.InvokeAsync(
            context,
            new TestTenantService(null, isSuperAdmin: true),
            db);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task TenantAdmin_ViewOnlyCeiling_AllowsReadAndDeniesCreate()
    {
        var tenantService = new TestTenantService(1, isTenantAdmin: true);
        await using var db = TestDbFactory.Create(tenantService);
        var menu = new Menu { Title = "Staff", Route = "/hr/staff", IsActive = true };
        db.Menus.Add(menu);
        await db.SaveChangesAsync();
        db.TenantMenuPermissions.Add(new TenantMenuPermission
        {
            TenantId = 1,
            MenuId = menu.Id,
            IsAllow = true,
            CanView = true,
            CanAdd = false,
            CanEdit = false,
            CanDelete = false
        });
        await db.SaveChangesAsync();

        var readCalled = false;
        var middleware = new OperationalAccessBoundaryMiddleware(context =>
        {
            readCalled = true;
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        var readContext = CreateAuthenticatedContext("/api/persons", HttpMethods.Get);
        await middleware.InvokeAsync(readContext, tenantService, db);

        var writeContext = CreateAuthenticatedContext("/api/persons", HttpMethods.Post);
        await middleware.InvokeAsync(writeContext, tenantService, db);

        Assert.True(readCalled);
        Assert.Equal(StatusCodes.Status204NoContent, readContext.Response.StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, writeContext.Response.StatusCode);
    }

    private static DefaultHttpContext CreateAuthenticatedContext(string path, string method)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "test-user") },
                "Test"))
        };
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        return context;
    }
}
