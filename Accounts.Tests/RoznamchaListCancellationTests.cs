using System.Security.Claims;
using Accounts.Controllers;
using Accounts.Models;
using Accounts.Models.SpListRows;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Accounts.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Accounts.Tests;

public sealed class RoznamchaListCancellationTests
{
    [Theory]
    [InlineData("payment")]
    [InlineData("receipt")]
    [InlineData("update")]
    public async Task Abandoned_list_returns_499_and_next_date_request_still_loads(string kind)
    {
        var tenant = new TenantContext();
        await using var db = TestDbFactory.Create(tenant);
        foreach (var route in new[] { "/accounts/payment-roz", "/accounts/receipt-roz", "/accounts/roznamcha-update" })
        {
            var menu = new Menu { Title = route, Route = route, IsActive = true };
            db.Menus.Add(menu);
            await db.SaveChangesAsync();
            db.TenantMenuPermissions.Add(new TenantMenuPermission { TenantId = 2, MenuId = menu.Id, IsAllow = true, CanView = true });
        }
        await db.SaveChangesAsync();
        using var cancellation = new CancellationTokenSource();
        var reader = new ListReader(cancellation);
        var rbac = new RbacService(db);
        var controller = new AccountsRoznamchaController(db, tenant, new TenantPermissionService(db, rbac), rbac, reader, null!, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestAborted = cancellation.Token,
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimTypes.NameIdentifier, "tenant-admin"),
                        new Claim(ClaimTypes.Role, "TenantAdmin"),
                        new Claim(ITenantService.ClaimTenantId, "2")
                    ], "test"))
                }
            }
        };
        var from = new DateOnly(2022, 4, 1);
        var to = new DateOnly(2022, 4, 30);
        async Task<IActionResult> Read(CancellationToken ct) => kind switch
        {
            "payment" => await controller.ListPaymentRoz(from, to, ct),
            "receipt" => await controller.ListReceiptRoz(from, to, ct),
            _ => await controller.ListRoznamchaUpdate(from, to, null, null, ct)
        };

        Assert.Equal(499, Assert.IsType<StatusCodeResult>(await Read(cancellation.Token)).StatusCode);
        reader.CancelRead = false;
        controller.HttpContext.RequestAborted = CancellationToken.None;
        Assert.IsType<OkObjectResult>(await Read(CancellationToken.None));
        Assert.Equal(from, reader.From);
        Assert.Equal(to, reader.To);
        Assert.Equal(2, reader.TenantId);

        reader.UnexpectedCancellation = true;
        await Assert.ThrowsAsync<TaskCanceledException>(() => Read(CancellationToken.None));
    }

    private sealed class TenantContext : ITenantService
    {
        public int? TenantId => 2;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public int RequiredTenantId => 2;
    }

    private sealed class ListReader(CancellationTokenSource cancellation) : IAccountsRoznamchaService
    {
        public bool CancelRead { get; set; } = true;
        public bool UnexpectedCancellation { get; set; }
        public int TenantId { get; private set; }
        public DateOnly From { get; private set; }
        public DateOnly To { get; private set; }
        private Task<IReadOnlyList<T>> Read<T>(int tenantId, DateOnly from, DateOnly to, CancellationToken ct)
        {
            TenantId = tenantId; From = from; To = to;
            if (UnexpectedCancellation) return Task.FromException<IReadOnlyList<T>>(new TaskCanceledException("Not a request cancellation"));
            if (CancelRead)
            {
                cancellation.Cancel();
                return Task.FromCanceled<IReadOnlyList<T>>(ct);
            }
            return Task.FromResult<IReadOnlyList<T>>([]);
        }
        public Task<IReadOnlyList<PaymentRozListRow>> ListPaymentRozAsync(int tenantId, DateOnly from, DateOnly to, CancellationToken ct = default) => Read<PaymentRozListRow>(tenantId, from, to, ct);
        public Task<IReadOnlyList<PaymentRozListRow>> ListReceiptRozAsync(int tenantId, DateOnly from, DateOnly to, CancellationToken ct = default) => Read<PaymentRozListRow>(tenantId, from, to, ct);
        public Task<IReadOnlyList<RoznamchaListRow>> ListRoznamchaAsync(int tenantId, DateOnly from, DateOnly to, int? typeId = null, CancellationToken cancellationToken = default) => Read<RoznamchaListRow>(tenantId, from, to, cancellationToken);
        public Task<int?> ResolveRozTypeIdAsync(int tenantId, string typeCode, CancellationToken ct = default) => Task.FromResult<int?>(null);
    }
}
